namespace PilotTrade.Application.Services;

using PilotTrade.Application.Controls;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Risk;

// Known: a value the broker reported. Derived: computed here from known values only. Unknown: a needed value is missing, so no number is shown.
public enum Provenance { Known, Derived, Unknown }

public sealed record RiskField(string Name, decimal? Value, string Provenance, string Source, DateTimeOffset? AsOfUtc, string? Note);
public sealed record RiskMetric(string Key, string Label, decimal? Current, decimal? Limit, string Unit, string Provenance, decimal? UtilizationPercent, bool? Breached, string Note);
public sealed record RiskFlag(string Key, string Label, bool? Raised, string Detail);
public sealed record RiskRule(string Rule, string Result, string Reason, DateTimeOffset EvaluatedUtc, DateTimeOffset? InputAsOfUtc);
public sealed record RiskView(DateTimeOffset CheckedUtc, string PolicySource, bool PolicyEditable, string? AccountCurrency, string BrokerSource, string? SelectedEpic,
    IReadOnlyList<RiskField> AccountFields, IReadOnlyList<RiskMetric> Metrics, IReadOnlyList<RiskFlag> Flags, IReadOnlyList<RiskRule> Rules, IReadOnlyList<string> ReadErrors);

// Task 02-05: a read-only view of the account's risk against the built-in limits. It reads the broker and changes nothing.
// A missing broker field stays Unknown and is never filled in; an exposure total is shown only when every part of it is known,
// so a partial sum can never understate exposure.
public sealed class RiskMonitorService
{
    private readonly IBrokerAdapter _broker;
    private readonly ITradingControl _control;
    private readonly MarketWatchService _watch;
    private readonly MarketDataPolicy _marketPolicy;
    private readonly IClock _clock;

    public RiskMonitorService(IBrokerAdapter broker, ITradingControl control, MarketWatchService watch, MarketDataPolicy marketPolicy, IClock clock)
    { _broker = broker; _control = control; _watch = watch; _marketPolicy = marketPolicy; _clock = clock; }

    private async Task<T?> Read<T>(Func<Task<T>> read, string what, List<string> errors) where T : class
    {
        try { return await read(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(ex is BrokerException b ? $"{what} could not be read: {b.Kind}" : $"{what} could not be read");
            return null;
        }
    }

    public async Task<RiskView> BuildAsync(TradingEnvironment env, string accountId, string? selectedEpic, string brokerSource, CancellationToken ct)
    {
        var errors = new List<string>();
        var account = await Read(() => _broker.GetAccountAsync(accountId, ct), "Account", errors);
        var positions = await Read(() => _broker.GetPositionsAsync(accountId, ct), "Positions", errors);
        var orders = await Read(() => _broker.GetWorkingOrdersAsync(accountId, ct), "Working orders", errors);

        // A working order carries no currency, so its instrument's quote currency is looked up; an unknown currency makes the total Unknown.
        var orderCurrency = new Dictionary<string, string?>();
        foreach (var epic in (orders ?? Array.Empty<WorkingOrder>()).Select(o => o.Epic).Distinct())
            orderCurrency[epic] = (await Read(() => _broker.GetInstrumentAsync(epic, accountId, ct), $"Instrument {epic}", errors))?.QuoteCurrency;

        var watchRow = selectedEpic is null ? null
            : (await Read(() => _watch.BuildAsync(accountId, new[] { selectedEpic }, ct), "Quote", errors))?.Rows.FirstOrDefault();

        var now = _clock.UtcNow;
        var policy = RiskPolicyDefaults.Create(env, accountId, _marketPolicy.MaxQuoteAge, now);
        var mismatch = account is not null && (account.AccountId != accountId || account.Environment != env);
        // Figures from a different account are not this account's figures.
        var acct = mismatch ? null : account;
        var currency = acct?.Currency;

        var fields = AccountFields(acct, account is null ? "The account could not be read" : mismatch ? "The broker reported a different account" : null, brokerSource);

        // Exposure: a level is the larger of the open and current level, so a move against the open level is not missed.
        decimal? Notional(Position p) =>
            p.OpenLevel is null || currency is null || string.IsNullOrWhiteSpace(p.Currency) || !string.Equals(p.Currency, currency, StringComparison.OrdinalIgnoreCase)
                ? null : Math.Abs(p.Size) * Math.Max(p.OpenLevel.Value, p.CurrentLevel ?? p.OpenLevel.Value);
        decimal? OrderNotional(WorkingOrder o) =>
            o.Level is null || currency is null || !string.Equals(orderCurrency.GetValueOrDefault(o.Epic), currency, StringComparison.OrdinalIgnoreCase)
                ? null : Math.Abs(o.Size) * o.Level.Value;

        string? PositionGap() => positions is null ? "Positions could not be read"
            : currency is null ? "The account currency is unknown"
            : positions.Any(p => p.OpenLevel is null) ? "A position has no open level"
            : positions.Any(p => string.IsNullOrWhiteSpace(p.Currency)) ? "A position's currency is not reported"
            : positions.Any(p => Notional(p) is null) ? "A position is in another currency, and no conversion is available"
            : null;
        string? OrderGap() => orders is null ? "Working orders could not be read"
            : currency is null ? "The account currency is unknown"
            : orders.Any(o => o.Level is null) ? "A working order has no level"
            : orders.Any(o => OrderNotional(o) is null) ? "A working order's currency is unknown or differs, and no conversion is available"
            : null;

        var positionGap = PositionGap();
        var orderGap = OrderGap();
        decimal? positionGross = positionGap is null ? positions!.Sum(p => Notional(p)!.Value) : null;
        decimal? orderGross = orderGap is null ? orders!.Sum(o => OrderNotional(o)!.Value) : null;
        decimal? net = positionGap is null ? Math.Abs(positions!.Sum(p => (p.Direction == OrderSide.Buy ? 1m : -1m) * Notional(p)!.Value)) : null;
        decimal? largest = positionGap is null ? positions!.GroupBy(p => p.Epic).Select(g => g.Sum(p => Notional(p)!.Value)).DefaultIfEmpty(0m).Max() : null;

        string? lossGap = acct is null ? "The account could not be read"
            : acct.RealizedPnLToday is null ? "The broker did not report today's realized P/L; it is not estimated"
            : positions is null ? "Positions could not be read"
            : positions.Any(p => p.UnrealizedPnL is null) ? "A position's unrealized P/L is not reported"
            : null;
        decimal? dailyLoss = lossGap is null ? Math.Max(0m, -acct!.RealizedPnLToday!.Value) + positions!.Sum(p => Math.Max(0m, -p.UnrealizedPnL!.Value)) : null;

        string? marginGap = acct is null ? "The account could not be read"
            : acct.MarginUsed is null ? "The broker did not report margin used; it is not estimated"
            : acct.Equity is not > 0 ? "Equity is not reported or not positive"
            : null;
        decimal? marginPct = marginGap is null ? Math.Round(acct!.MarginUsed!.Value / acct.Equity!.Value * 100m, 2) : null;

        var quote = watchRow?.Quote;
        var metrics = new List<RiskMetric>
        {
            Count("openPositions", "Open positions", positions?.Count, policy.MaxConcurrentPositions, positions is null ? "Positions could not be read" : "Reported by the broker"),
            Count("workingOrders", "Working orders", orders?.Count, policy.MaxPendingOrders, orders is null ? "Working orders could not be read" : "Reported by the broker"),
            Money("largestInstrumentExposure", "Largest exposure in one instrument", largest, policy.MaxPerInstrumentExposureAccountCurrency, currency, positionGap, "Size × the larger of open and current level, per instrument"),
            Money("grossExposure", "Gross exposure of positions", positionGross, policy.MaxGrossExposureAccountCurrency, currency, positionGap, "Sum of size × the larger of open and current level"),
            Money("netExposure", "Net exposure of positions", net, policy.MaxNetExposureAccountCurrency, currency, positionGap, "Buys minus sells, as an absolute amount"),
            Money("workingOrderExposure", "Working-order exposure", orderGross, null, currency, orderGap, "Size × order level. No separate limit; it counts toward gross exposure if filled"),
            Money("grossWithOrders", "Gross exposure if all working orders fill", positionGross + orderGross, policy.MaxGrossExposureAccountCurrency, currency, positionGap ?? orderGap, "Positions plus working orders"),
            Money("dailyLoss", "Loss today", dailyLoss, policy.MaxDailyLossAccountCurrency, currency, lossGap, "Realized loss today plus open unrealized losses; gains do not offset it"),
            Metric("marginUtilization", "Margin used / equity", marginPct, policy.MaxMarginUtilizationPercent, "%", marginGap is null ? Provenance.Derived : Provenance.Unknown, marginGap ?? "Margin used divided by equity"),
            FreeMargin(acct, policy, currency),
            Metric("spread", "Spread of the selected instrument", quote?.SpreadPercent is { } sp ? Math.Round(sp, 4) : null, watchRow?.MaxSpreadPercent, "%",
                quote is null || watchRow?.MaxSpreadPercent is null ? Provenance.Unknown : Provenance.Derived,
                selectedEpic is null ? "No instrument is selected" : quote is null ? "No usable quote" : watchRow!.MaxSpreadPercent is null ? "No spread limit for this asset class" : $"{selectedEpic}: (ask − bid) / mid"),
            Metric("quoteAge", "Quote age of the selected instrument", quote is null ? null : Math.Round((decimal)quote.AgeSeconds, 1), (decimal)policy.MaxPriceAge.TotalSeconds, "s",
                quote is null ? Provenance.Unknown : Provenance.Derived, selectedEpic is null ? "No instrument is selected" : quote is null ? "No usable quote" : "Now minus the broker's quote timestamp"),
            new("slippage", "Slippage", null, null, "%", nameof(Provenance.Unknown), null, null, "No fills exist in this build. Slippage is measured from broker fills, so it is not shown."),
        };

        var stale = selectedEpic is null ? (bool?)null
            : quote is null ? null
            : quote.Health != nameof(FeedHealth.Healthy) || quote.AgeSeconds > policy.MaxPriceAge.TotalSeconds;
        var flags = new List<RiskFlag>
        {
            new("staleData", "Stale market data", stale, selectedEpic is null ? "No instrument is selected" : quote is null ? $"No usable quote for {selectedEpic}"
                : stale == true ? $"{selectedEpic} quote is {quote.AgeSeconds:F0} s old ({quote.Health})" : $"{selectedEpic} quote is {quote.AgeSeconds:F0} s old"),
            new("missingMargin", "Margin data missing", acct is null || acct.MarginUsed is null || acct.FreeMargin is null,
                acct is null ? "The account could not be read" : acct.MarginUsed is null && acct.FreeMargin is null ? "Margin used and free margin are not reported"
                : acct.MarginUsed is null ? "Margin used is not reported" : acct.FreeMargin is null ? "Free margin is not reported" : "Margin used and free margin are reported"),
            new("missingRealizedPnL", "Realized P/L today missing", acct is null || acct.RealizedPnLToday is null,
                acct is null ? "The account could not be read" : acct.RealizedPnLToday is null ? "Not reported by the broker" : "Reported by the broker"),
            new("openWorkingOrders", "Open working orders", orders is null ? null : orders.Count > 0,
                orders is null ? "Working orders could not be read" : $"{orders.Count} open at the broker"),
            new("accountMismatch", "Account mismatch", account is null ? null : mismatch,
                account is null ? "The account could not be read" : mismatch ? "The broker reported a different account or environment" : "The broker reports the configured account"),
        };

        var rules = Rules(now, account?.AsOfUtc, quote?.SourceTimestampUtc, account is null, mismatch, metrics, stale, selectedEpic);
        return new RiskView(now, "Built-in defaults", false, currency, brokerSource, selectedEpic, fields, metrics, flags, rules, errors);
    }

    private static IReadOnlyList<RiskField> AccountFields(BrokerAccount? a, string? unreadable, string source)
    {
        RiskField F(string name, decimal? v, Provenance p, string? note) =>
            new(name, unreadable is null ? v : null, (unreadable is null ? p : Provenance.Unknown).ToString(), source, a?.AsOfUtc, unreadable ?? note);
        RiskField Opt(string name, decimal? v, string missing) => F(name, v, v is null ? Provenance.Unknown : Provenance.Known, v is null ? missing : null);
        return new[]
        {
            F("Balance", a?.Balance, Provenance.Known, null),
            a?.Equity is null ? Opt("Equity", null, "Not reported by the broker")
                : F("Equity", a.Equity, a.EquityIsDerived ? Provenance.Derived : Provenance.Known, a.EquityIsDerived ? "Balance plus unrealized P/L, computed by the adapter" : null),
            Opt("Free margin", a?.FreeMargin, "Not reported by the broker"),
            Opt("Margin used", a?.MarginUsed, "Not reported by the broker. It is not estimated."),
            F("Unrealized P/L", a?.UnrealizedPnL, Provenance.Known, null),
            Opt("Realized P/L today", a?.RealizedPnLToday, "Not reported by the broker. It is not estimated."),
        };
    }

    private static decimal? Pct(decimal? current, decimal? limit) => current is { } c && limit is > 0 ? Math.Round(c / limit.Value * 100m, 1) : null;

    private static RiskMetric Metric(string key, string label, decimal? current, decimal? limit, string unit, Provenance p, string note) =>
        new(key, label, p == Provenance.Unknown ? null : current, limit, unit, p.ToString(), p == Provenance.Unknown ? null : Pct(current, limit),
            p == Provenance.Unknown || limit is null ? null : current > limit, note);

    private static RiskMetric Money(string key, string label, decimal? current, decimal? limit, string? currency, string? gap, string how) =>
        Metric(key, label, current, limit, currency ?? "account currency", gap is null && current is not null ? Provenance.Derived : Provenance.Unknown, gap ?? how);

    // A count at its limit already blocks one more, so it is reported as breached.
    private static RiskMetric Count(string key, string label, int? current, int limit, string note) =>
        current is null ? new(key, label, null, limit, "count", nameof(Provenance.Unknown), null, null, note)
            : new(key, label, current, limit, "count", nameof(Provenance.Known), Pct(current, limit), current >= limit, note);

    private static RiskMetric FreeMargin(BrokerAccount? a, RiskPolicy policy, string? currency) =>
        a?.FreeMargin is null
            ? new("freeMargin", "Free margin (minimum buffer)", null, policy.MinFreeMarginBufferAccountCurrency, currency ?? "account currency", nameof(Provenance.Unknown), null, null,
                a is null ? "The account could not be read" : "Not reported by the broker")
            : new("freeMargin", "Free margin (minimum buffer)", a.FreeMargin, policy.MinFreeMarginBufferAccountCurrency, currency ?? "account currency", nameof(Provenance.Known), null,
                a.FreeMargin < policy.MinFreeMarginBufferAccountCurrency, "Must stay at or above the buffer");

    private IReadOnlyList<RiskRule> Rules(DateTimeOffset now, DateTimeOffset? accountAsOf, DateTimeOffset? quoteAsOf, bool accountUnread, bool mismatch,
        IReadOnlyList<RiskMetric> metrics, bool? stale, string? selectedEpic)
    {
        var s = _control.State;
        var rules = new List<RiskRule>
        {
            new("Kill switch", s.KillSwitchEngaged ? "Veto" : "Pass", s.KillSwitchEngaged ? s.KillSwitchReason ?? "Engaged" : "Not engaged", now, s.KillSwitchChangedUtc),
            new("Startup reconciliation", s.ReconciliationComplete ? "Pass" : "Veto", s.ReconciliationComplete ? "Complete" : s.Reconciliation switch
            {
                ReconciliationOutcome.NotCompared => "Holdings are not compared while execution is disabled",
                ReconciliationOutcome.NotRun => "Has not run",
                ReconciliationOutcome.Uncertain => "Uncertain orders remain",
                ReconciliationOutcome.Mismatch => "The journal and the broker do not match",
                ReconciliationOutcome.CredentialsMissing => "Broker credentials are missing",
                _ => "Failed",
            }, now, null),
            new("Account binding", accountUnread ? "Unknown" : mismatch ? "Veto" : "Pass",
                accountUnread ? "The account could not be read" : mismatch ? "The broker reported a different account or environment" : "Configured account", now, accountAsOf),
            new("Market data freshness", stale is null ? "Unknown" : stale.Value ? "Veto" : "Pass",
                selectedEpic is null ? "No instrument is selected" : stale is null ? "No usable quote" : stale.Value ? "Quote is stale or the feed is unhealthy" : "Quote is fresh", now, quoteAsOf),
        };
        foreach (var (key, rule) in new[] { ("spread", "Spread"), ("dailyLoss", "Daily loss"), ("marginUtilization", "Margin utilization"), ("freeMargin", "Free margin buffer"),
            ("openPositions", "Concurrent positions"), ("workingOrders", "Pending orders"), ("grossWithOrders", "Gross exposure"), ("netExposure", "Net exposure"),
            ("largestInstrumentExposure", "Per-instrument exposure") })
        {
            var m = metrics.Single(x => x.Key == key);
            var asOf = key is "spread" ? quoteAsOf : accountAsOf;
            rules.Add(m.Provenance == nameof(Provenance.Unknown) || m.Breached is null
                ? new(rule, "Unknown", m.Note, now, asOf)
                : new(rule, m.Breached.Value ? "Veto" : "Pass", m.Breached.Value ? $"{m.Current} {m.Unit} against a limit of {m.Limit}" : $"{m.Current} {m.Unit} within {m.Limit}", now, asOf));
        }
        return rules;
    }
}
