namespace PilotTrade.Infrastructure.Broker;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

public sealed record DemoProbeStep(string Name, bool Passed, string Detail);

public sealed record DemoProbeEvidence(
    DateTimeOffset RunUtc,
    string Environment,
    string AccountIdMasked,
    bool OrderEndpointsCalled,
    bool AllPassed,
    IReadOnlyList<DemoProbeStep> Steps);

// Exercises the read-only gate with real requests and records sanitized results only:
// presence and counts, never balances, tokens, full account IDs or broker bodies.
public sealed class DemoReadOnlyProbe
{
    private readonly IBrokerAdapter _broker;
    private readonly IClock _clock;

    public DemoReadOnlyProbe(IBrokerAdapter broker, IClock clock) { _broker = broker; _clock = clock; }

    public static string Mask(string? accountId) =>
        string.IsNullOrEmpty(accountId) ? "(none)" : accountId.Length <= 4 ? "****" : "****" + accountId[^4..];

    public async Task<DemoProbeEvidence> RunAsync(string? accountId, string epic, CancellationToken ct)
    {
        var steps = new List<DemoProbeStep>();

        async Task<T?> Step<T>(string name, Func<Task<T>> run, Func<T, (bool Passed, string Detail)> describe) where T : class
        {
            try
            {
                var value = await run();
                var (passed, detail) = describe(value);
                steps.Add(new DemoProbeStep(name, passed, detail));
                return value;
            }
            catch (BrokerException ex)
            {
                steps.Add(new DemoProbeStep(name, false, ex.Kind.ToString()));
                return null;
            }
        }

        var accounts = await Step("accounts", () => _broker.GetAccountsAsync(ct), a => (a.Count > 0, $"{a.Count} account(s)"));
        var target = accountId;
        if (string.IsNullOrWhiteSpace(target)) target = accounts?.FirstOrDefault(a => a.Preferred)?.AccountId ?? accounts?.FirstOrDefault()?.AccountId;

        if (!string.IsNullOrWhiteSpace(target))
        {
            await Step("accountIdentityAndFinancials", () => _broker.GetAccountAsync(target, ct), a =>
                (a.AccountId == target && a.FreeMargin is not null,
                 $"accountMatched={a.AccountId == target}; currencyPresent={a.Currency.Length > 0}; availablePresent={a.FreeMargin is not null}; equityProven={a.Equity is not null}; marginUsedPresent={a.MarginUsed is not null}; realizedPnLPresent={a.RealizedPnLToday is not null}"));
            await Step("instrumentMetadata",
                async () => await _broker.GetInstrumentAsync(epic, target, ct) ?? throw new BrokerException(BrokerErrorKind.NotFound, "Instrument not found"),
                m => (true, $"status={m.Status}; precision={m.PricePrecision}; minSizePresent={m.MinDealSize > 0}; stopRulesPresent={m.MinStopDistance is not null}"));
            await Step("positions", () => _broker.GetPositionsAsync(target, ct), p => (true, $"{p.Count} position(s)"));
            await Step("workingOrders", () => _broker.GetWorkingOrdersAsync(target, ct), o => (true, $"{o.Count} working order(s)"));
        }
        else steps.Add(new DemoProbeStep("accountSelection", false, "No account available"));

        var now = _clock.UtcNow;
        await Step("quote", () => _broker.GetQuotesAsync(new[] { epic }, ct), q =>
            q.FirstOrDefault(x => x.Epic == epic) is { } quote
                ? (quote.Ask >= quote.Bid && quote.Health == FeedHealth.Healthy, $"ageSeconds={(int)(now - quote.SourceTimestampUtc).TotalSeconds}; health={quote.Health}")
                : (false, "no quote"));
        await Step("candles", () => _broker.GetHistoryAsync(epic, "5m", 50, ct), c => (c.Count > 0, $"{c.Count} candle(s)"));

        return new DemoProbeEvidence(now, _broker.Environment.ToString(), Mask(target), false, steps.All(s => s.Passed), steps);
    }
}
