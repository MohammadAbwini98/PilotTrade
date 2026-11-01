namespace PilotTrade.Infrastructure.Broker;

using System.Reflection;
using PilotTrade.Application.Services;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

public sealed record DemoProbeStep(string Name, bool Passed, string Detail);

public sealed record DemoProbeEvidence(
    DateTimeOffset RunUtc,
    string Environment,
    string Adapter,
    string ApiHost,
    string? BuildVersion,
    string? GitSha,
    string? WorkingTree,
    string AccountIdMasked,
    string AccountSource,
    string RequestedEpic,
    string? InstrumentEpic,
    bool OrderEndpointsCalled,
    bool AllPassed,
    IReadOnlyList<DemoProbeStep> Steps);

// Exercises the read-only gate with real requests and records sanitized results only:
// presence, counts, ages, field names and the public instrument name, never balances, prices, tokens, full account IDs or broker bodies.
// A mismatched account or epic is refused inside the adapter, so a step only reports what the broker's answer can contradict.
public sealed class DemoReadOnlyProbe
{
    private const string CandleTimeframe = "5m";
    private readonly IBrokerAdapter _broker;
    private readonly IClock _clock;
    private readonly MarketDataPolicy _policy = MarketDataPolicy.Default;

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
                steps.Add(new DemoProbeStep(name, false, FailureDetail(ex)));
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // An unexpected type only: the exception message could quote broker data.
                steps.Add(new DemoProbeStep(name, false, $"Unexpected: {ex.GetType().Name}"));
                return null;
            }
        }

        var accounts = await Step("accounts", () => _broker.GetAccountsAsync(ct), a => (a.Count > 0, $"{a.Count} account(s)"));
        var target = accountId;
        var accountSource = "configured";
        if (string.IsNullOrWhiteSpace(target))
        {
            target = accounts?.FirstOrDefault(a => a.Preferred)?.AccountId ?? accounts?.FirstOrDefault()?.AccountId;
            accountSource = string.IsNullOrWhiteSpace(target) ? "none" : "autoSelected";
        }

        var status = MarketStatus.Unknown;
        string? instrumentEpic = null;
        if (!string.IsNullOrWhiteSpace(target))
        {
            await Step("accountIdentityAndFinancials", () => _broker.GetAccountAsync(target, ct), a =>
                (a.FreeMargin is not null,
                 $"currencyPresent={a.Currency.Length > 0}; availablePresent={a.FreeMargin is not null}; equityProven={a.Equity is not null}; marginUsedPresent={a.MarginUsed is not null}; realizedPnLPresent={a.RealizedPnLToday is not null}"));
            if (_broker is CapitalComBrokerAdapter capital)
                await Step("activeSessionAccount", () => capital.GetActiveAccountAsync(ct), active => (active == target, $"activeAccountMatched={active == target}"));
            else steps.Add(new DemoProbeStep("activeSessionAccount", false, "Not measured"));
            await Step("instrumentMetadata",
                async () => await _broker.GetInstrumentAsync(epic, target, ct) ?? throw new BrokerException(BrokerErrorKind.NotFound, "Instrument not found"),
                m =>
                {
                    status = m.Status;
                    instrumentEpic = m.Epic;
                    var rulesValid = m.MinDealSize > 0 && m.MaxDealSize >= m.MinDealSize && m.DealSizeStep > 0 && m.TickSize > 0 && m.MinStopDistance is not null && m.MaxStopDistance is not null;
                    return (m.Status != MarketStatus.Unknown && m.QuoteCurrency.Length > 0 && rulesValid,
                        $"instrumentName={m.Name}; assetClass={m.AssetClass}; status={m.Status}; precision={m.PricePrecision}; currencyPresent={m.QuoteCurrency.Length > 0}; dealingRulesValid={rulesValid}");
                });
            await Step("positions", () => _broker.GetPositionsAsync(target, ct), p => (true, $"{p.Count} position(s)"));
            await Step("workingOrders", () => _broker.GetWorkingOrdersAsync(target, ct), o => (true, $"{o.Count} working order(s)"));
        }
        else steps.Add(new DemoProbeStep("accountSelection", false, "No account available"));

        // Freshness can only be proven while the market is open, so a closed market never passes these two steps.
        var marketOpen = status == MarketStatus.Open;
        var now = _clock.UtcNow;
        await Step("quote", () => _broker.GetQuotesAsync(new[] { epic }, ct), q => DescribeQuote(q.FirstOrDefault(x => x.Epic == epic), now, marketOpen, status));
        await Step("candles", () => _broker.GetHistoryAsync(epic, CandleTimeframe, 50, ct), c => DescribeCandles(c, epic, now, marketOpen, status));

        var assembly = typeof(DemoReadOnlyProbe).Assembly;
        return new DemoProbeEvidence(now, _broker.Environment.ToString(), _broker.GetType().Name,
            (_broker as CapitalComBrokerAdapter)?.ApiHost ?? "(none)",
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            BuildMetadata(assembly, "GitSha"), BuildMetadata(assembly, "WorkingTree"),
            Mask(target), accountSource, epic, instrumentEpic, (_broker as CapitalComBrokerAdapter)?.NonSessionWriteRequests > 0, steps.All(s => s.Passed), steps);
    }

    private static string? BuildMetadata(Assembly assembly, string key) =>
        assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value;

    private static string FailureDetail(BrokerException ex) =>
        ex.Kind == BrokerErrorKind.MalformedResponse && ex.Message.StartsWith(CapitalComJson.MalformedPrefix, StringComparison.Ordinal)
            ? $"{ex.Kind}: {ex.Message[CapitalComJson.MalformedPrefix.Length..]}"
            : ex.Kind.ToString();

    private (bool, string) DescribeQuote(MarketQuote? quote, DateTimeOffset now, bool marketOpen, MarketStatus status)
    {
        if (quote is null) return (false, "no quote");
        var age = now - quote.SourceTimestampUtc;
        var fresh = age <= _policy.MaxQuoteAge && age >= -_policy.MaxQuoteAge;
        var valid = quote.Bid > 0 && quote.Ask >= quote.Bid;
        var spreadOk = valid && (quote.Ask - quote.Bid) / ((quote.Bid + quote.Ask) / 2m) * 100m <= _policy.MaxSpreadPercent;
        return (marketOpen && valid && spreadOk && fresh && quote.Health == FeedHealth.Healthy,
            $"marketStatus={status}; ageSeconds={(int)age.TotalSeconds}; fresh={fresh}; pricesValid={valid}; spreadWithinPolicy={spreadOk}; health={quote.Health}");
    }

    private (bool, string) DescribeCandles(IReadOnlyList<Candle> candles, string epic, DateTimeOffset now, bool marketOpen, MarketStatus status)
    {
        var closed = candles.Where(c => c.IsClosed).ToList();
        if (closed.Count == 0) return (false, $"marketStatus={status}; {candles.Count} candle(s); none closed");
        var ordered = candles.Zip(candles.Skip(1)).All(p => p.Second.OpenTimeUtc > p.First.OpenTimeUtc);
        var valid = candles.All(c => c.Epic == epic && c.Low > 0 && c.High >= Math.Max(c.Open, c.Close) && c.Low <= Math.Min(c.Open, c.Close) && c.OpenTimeUtc <= now);
        var last = closed[^1];
        var age = now - last.CloseTimeUtc;
        var fresh = age <= (last.CloseTimeUtc - last.OpenTimeUtc) * _policy.MaxCandleAgeInBars;
        var enough = closed.Count >= _policy.MinClosedCandles;
        return (marketOpen && ordered && valid && fresh && enough,
            $"marketStatus={status}; {candles.Count} candle(s); latestCloseAgeSeconds={(int)age.TotalSeconds}; fresh={fresh}; ordered={ordered}; valuesValid={valid}; enoughHistory={enough}");
    }
}
