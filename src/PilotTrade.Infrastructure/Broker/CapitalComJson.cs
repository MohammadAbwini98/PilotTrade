namespace PilotTrade.Infrastructure.Broker;

using System.Globalization;
using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

// Strict field readers: a missing or mistyped required field is a malformed response, never a default value.
// Exceptions name the field only, so broker values cannot reach logs or API responses.
internal static class CapitalComJson
{
    public const string MalformedPrefix = "Broker response is malformed: ";

    public static BrokerException Malformed(string field) =>
        new(BrokerErrorKind.MalformedResponse, MalformedPrefix + field);

    public static JsonDocument Parse(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException) { throw Malformed("not valid JSON"); }
    }

    public static JsonElement Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : throw Malformed(name);

    public static JsonElement Arr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array ? v : throw Malformed(name);

    public static string Str(JsonElement e, string name) =>
        OptStr(e, name) is { Length: > 0 } s ? s : throw Malformed(name);

    public static string? OptStr(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static decimal Dec(JsonElement e, string name) => OptDec(e, name) ?? throw Malformed(name);

    public static decimal? OptDec(JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null) return null;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : throw Malformed(name);
    }

    public static decimal Positive(JsonElement e, string name)
    {
        var d = Dec(e, name);
        return d > 0 ? d : throw Malformed(name);
    }

    public static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    public static DateTimeOffset Utc(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d)
            ? d : throw Malformed(name);

    public static OrderSide Side(JsonElement e, string name) => Str(e, name) switch
    {
        "BUY" => OrderSide.Buy,
        "SELL" => OrderSide.Sell,
        _ => throw Malformed(name)
    };

    // Only documented values map to Open; anything undocumented is non-tradeable.
    public static MarketStatus Status(JsonElement market)
    {
        var modes = market.TryGetProperty("marketModes", out var m) && m.ValueKind == JsonValueKind.Array
            ? m.EnumerateArray().Select(x => x.GetString()).ToList()
            : new List<string?>();
        if (modes.Contains("VIEW_ONLY")) return MarketStatus.Halted;
        return OptStr(market, "marketStatus") switch
        {
            "TRADEABLE" => MarketStatus.Open,
            "CLOSED" => MarketStatus.Closed,
            _ => MarketStatus.Unknown
        };
    }

    public static InstrumentAssetClass AssetClass(string? type) => type switch
    {
        null or "" => InstrumentAssetClass.Unknown,
        "CURRENCIES" => InstrumentAssetClass.Fx,
        "CRYPTOCURRENCIES" => InstrumentAssetClass.CryptoCfd,
        "COMMODITIES" => InstrumentAssetClass.Commodity,
        "INDICES" => InstrumentAssetClass.Index,
        "SHARES" => InstrumentAssetClass.Share,
        _ => InstrumentAssetClass.Other
    };

    public static (decimal Value, DistanceUnit Unit) Rule(JsonElement rules, string name)
    {
        var rule = Obj(rules, name);
        var unit = Str(rule, "unit") switch
        {
            "POINTS" => DistanceUnit.Points,
            "PERCENTAGE" => DistanceUnit.Percentage,
            _ => throw Malformed($"{name}.unit")
        };
        return (Positive(rule, "value"), unit);
    }
}
