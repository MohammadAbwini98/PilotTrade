namespace PilotTrade.Infrastructure.Broker;

using System.Net;
using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

// Request building and response parsing for the documented order endpoints.
// Nothing in this phase sends these requests: CapitalComBrokerAdapter.SubmitOrderAsync never reaches the network.
public static class CapitalComOrderContract
{
    public const string PositionsPath = "/api/v1/positions";
    public const string WorkingOrdersPath = "/api/v1/workingorders";

    public static (string Path, string Json) BuildRequest(OrderIntent intent)
    {
        var direction = intent.Side == OrderSide.Buy ? "BUY" : "SELL";
        if (intent.Size <= 0) throw new BrokerException(BrokerErrorKind.InvalidRequest, "Order size must be positive");
        switch (intent.Type)
        {
            case OrderType.Market:
                return (PositionsPath, Serialize(new Dictionary<string, object?>
                {
                    ["epic"] = intent.Epic, ["direction"] = direction, ["size"] = intent.Size,
                    ["guaranteedStop"] = false, ["stopLevel"] = intent.StopLevel, ["profitLevel"] = intent.TakeProfitLevel
                }));
            case OrderType.Limit:
            case OrderType.Stop:
                if (intent.LimitLevel is null) throw new BrokerException(BrokerErrorKind.InvalidRequest, "Working order requires a level");
                return (WorkingOrdersPath, Serialize(new Dictionary<string, object?>
                {
                    ["epic"] = intent.Epic, ["direction"] = direction, ["size"] = intent.Size,
                    ["level"] = intent.LimitLevel, ["type"] = intent.Type == OrderType.Limit ? "LIMIT" : "STOP",
                    ["guaranteedStop"] = false, ["stopLevel"] = intent.StopLevel, ["profitLevel"] = intent.TakeProfitLevel
                }));
            default:
                throw new BrokerException(BrokerErrorKind.InvalidRequest, "Order type is not supported by the broker API");
        }
    }

    private static string Serialize(Dictionary<string, object?> fields) =>
        JsonSerializer.Serialize(fields.Where(f => f.Value is not null).ToDictionary(f => f.Key, f => f.Value));

    // Only an explicit 4xx refusal means the order was not placed. Anything else without a reference stays Unknown,
    // and an Unknown order is reconciled, never resubmitted.
    public static BrokerSubmissionResult ParseSubmission(HttpStatusCode status, string body, OrderIntent intent, DateTimeOffset now)
    {
        var code = (int)status;
        if (code is >= 400 and < 500 && status != HttpStatusCode.RequestTimeout && status != HttpStatusCode.TooManyRequests)
            return new BrokerSubmissionResult(intent.IntentId, null, null, OrderState.Rejected, $"Broker rejected the request (HTTP {code})", now, null);
        if (code is < 200 or >= 300)
            return new BrokerSubmissionResult(intent.IntentId, null, null, OrderState.Unknown, $"Broker outcome uncertain (HTTP {code})", now, null);

        string? reference = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            reference = CapitalComJson.OptStr(doc.RootElement, "dealReference");
        }
        catch (JsonException) { }
        return string.IsNullOrWhiteSpace(reference)
            ? new BrokerSubmissionResult(intent.IntentId, null, null, OrderState.Unknown, "Broker outcome uncertain (no deal reference)", now, null)
            : new BrokerSubmissionResult(intent.IntentId, reference, null, OrderState.BrokerPending, null, now, null);
    }

    public static BrokerSubmissionResult ParseConfirmation(string body, string dealReference, DateTimeOffset now)
    {
        using var doc = CapitalComJson.Parse(body);
        var root = doc.RootElement;
        if (CapitalComJson.Str(root, "dealReference") != dealReference) throw CapitalComJson.Malformed("dealReference");

        var state = CapitalComJson.Str(root, "dealStatus") switch
        {
            "ACCEPTED" => OrderState.Confirmed,
            "REJECTED" => OrderState.Rejected,
            _ => OrderState.Unknown
        };
        var affected = new List<AffectedDeal>();
        if (root.TryGetProperty("affectedDeals", out var deals) && deals.ValueKind != JsonValueKind.Null)
        {
            if (deals.ValueKind != JsonValueKind.Array) throw CapitalComJson.Malformed("affectedDeals");
            foreach (var d in deals.EnumerateArray())
                affected.Add(new AffectedDeal(CapitalComJson.Str(d, "dealId"), CapitalComJson.OptStr(d, "status")));
        }
        var dealId = CapitalComJson.OptStr(root, "dealId") ?? affected.FirstOrDefault()?.DealId;
        var reason = state == OrderState.Rejected ? "Broker rejected the deal" : state == OrderState.Unknown ? "Broker deal status not recognized" : null;

        // Contradictory answers stay uncertain: an acceptance must name a deal, and a rejection must not have touched one.
        if (state == OrderState.Confirmed && string.IsNullOrWhiteSpace(dealId))
            (state, reason) = (OrderState.Unknown, "Broker accepted the deal without a deal ID");
        else if (state == OrderState.Rejected && affected.Count > 0)
            (state, reason) = (OrderState.Unknown, "Broker rejected the deal but reported affected deals");

        return new BrokerSubmissionResult("", dealReference, dealId, state, reason, now, state == OrderState.Confirmed ? now : null, affected);
    }
}
