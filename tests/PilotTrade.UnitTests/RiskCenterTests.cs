namespace PilotTrade.UnitTests;

// Task 02-05: a broker whose reads are configurable. It never accepts an order.
internal sealed class RiskBroker : IBrokerAdapter
{
    public Func<BrokerAccount>? OnAccount { get; set; }
    public Func<IReadOnlyList<Position>>? OnPositions { get; set; } = () => Array.Empty<Position>();
    public Func<IReadOnlyList<WorkingOrder>>? OnOrders { get; set; } = () => Array.Empty<WorkingOrder>();
    public Func<IReadOnlyList<MarketQuote>>? OnQuotes { get; set; }
    public string QuoteCurrency { get; set; } = "USD";
    public int OrderCalls { get; private set; }
    public TradingEnvironment Environment => TradingEnvironment.Demo;
    public bool ExecutionEnabled => false;

    private static T Run<T>(Func<T>? f) => f is null ? throw new BrokerException(BrokerErrorKind.Unavailable, "unavailable") : f();

    public Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct) => Task.FromResult(Run(OnAccount));
    public Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct) => Task.FromResult(Run(OnPositions));
    public Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct) => Task.FromResult(Run(OnOrders));
    public Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct) => Task.FromResult(Run(OnQuotes));
    public Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct) => Task.FromResult<InstrumentMetadata?>(new(
        epic, epic, epic, InstrumentAssetClass.Fx, MarketStatus.Open, QuoteCurrency, null, 5, 0.0001m, 0.1m, 100m, 0.1m, null, null, null, null,
        true, true, true, Array.Empty<string>(), DateTimeOffset.UnixEpoch, accountId, TradingEnvironment.Demo));

    public Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct) { OrderCalls++; throw new InvalidOperationException("No orders in Phase 02"); }
    public Task<BrokerSubmissionResult?> GetConfirmationAsync(string reference, CancellationToken ct) { OrderCalls++; throw new InvalidOperationException("No orders in Phase 02"); }
    public Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct) { OrderCalls++; throw new InvalidOperationException("No orders in Phase 02"); }
    public Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct) { OrderCalls++; throw new InvalidOperationException("No orders in Phase 02"); }
    public Task<BrokerSession> CreateSessionAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<BrokerAccountSummary>> GetAccountsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<InstrumentSummary>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct) => throw new NotSupportedException();
}

internal sealed class RecordingAudit : IAuditLogger
{
    public bool Fail { get; set; }
    public List<AuditEvent> Events { get; } = new();
    public Task LogAsync(AuditEvent evt, CancellationToken ct) { if (Fail) throw new IOException("audit store down"); Events.Add(evt); return Task.CompletedTask; }
}

internal static class RiskData
{
    public static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    public static BrokerAccount Account(string id = "ACC-A", decimal? marginUsed = 200m, decimal? realized = -50m, decimal? freeMargin = 800m, TradingEnvironment env = TradingEnvironment.Demo) =>
        new(id, env, "USD", 1000m, 1000m, freeMargin, marginUsed, 0m, realized, Now);
    public static Position Pos(string epic, OrderSide side, decimal size, decimal? open, decimal? current, decimal? upnl = 0m, string? currency = "USD") =>
        new("p-" + epic + side, "ACC-A", TradingEnvironment.Demo, epic, side, size, open, current, upnl, null, null, Now, Now, currency);
    public static WorkingOrder Order(string epic, decimal size, decimal? level) =>
        new("o-" + epic, "ACC-A", TradingEnvironment.Demo, epic, OrderSide.Buy, OrderType.Limit, size, level, null, null, Now, Now);
    public static MarketQuote Quote(string epic, DateTimeOffset ts) => new(epic, 1.0000m, 1.0002m, null, 0.0002m, ts, ts, FeedHealth.Healthy);
    public static OrderJournalEntry Uncertain() =>
        new("cmd_u", "intent_u", TradingEnvironment.Demo, "ACC-A", "EURUSD", OrderState.Unknown, "d", "snap", null, "ref-u", null, Now, Now, null, null, Now, "c");
}

public class RiskMonitorTests
{
    private readonly FakeClock _clock = new(RiskData.Now);
    private readonly RiskBroker _broker = new() { OnAccount = () => RiskData.Account() };
    private readonly TestTradingControl _control = new();

    private Task<RiskView> Build(string? epic = null) =>
        new RiskMonitorService(_broker, _control, new MarketWatchService(_broker, _clock, MarketDataPolicy.Default), MarketDataPolicy.Default, _clock)
            .BuildAsync(TradingEnvironment.Demo, "ACC-A", epic, "Simulator", CancellationToken.None);

    private static RiskMetric M(RiskView v, string key) => v.Metrics.Single(m => m.Key == key);
    private static RiskRule R(RiskView v, string rule) => v.Rules.Single(r => r.Rule == rule);
    private static RiskField F(RiskView v, string name) => v.AccountFields.Single(f => f.Name == name);

    [Fact]
    public async Task MissingMarginAndRealizedPnL_StayUnknown_AndAreNeverSynthesized()
    {
        _broker.OnAccount = () => RiskData.Account(marginUsed: null, realized: null);
        var v = await Build();
        F(v, "Margin used").Value.Should().BeNull();
        F(v, "Margin used").Provenance.Should().Be("Unknown");
        F(v, "Realized P/L today").Value.Should().BeNull();
        F(v, "Realized P/L today").Provenance.Should().Be("Unknown");
        M(v, "marginUtilization").Current.Should().BeNull();
        M(v, "dailyLoss").Current.Should().BeNull();
        R(v, "Daily loss").Result.Should().Be("Unknown");
        R(v, "Margin utilization").Result.Should().Be("Unknown");
        v.Flags.Single(f => f.Key == "missingMargin").Raised.Should().BeTrue();
        v.Flags.Single(f => f.Key == "missingRealizedPnL").Raised.Should().BeTrue();
    }

    [Fact]
    public async Task KnownFields_CarryTheirSourceAndTimestamp()
    {
        var v = await Build();
        F(v, "Balance").Should().Match<RiskField>(f => f.Provenance == "Known" && f.Source == "Simulator" && f.AsOfUtc == RiskData.Now && f.Value == 1000m);
        M(v, "marginUtilization").Should().Match<RiskMetric>(m => m.Current == 20m && m.Provenance == "Derived" && m.Breached == false);
    }

    [Fact]
    public async Task ExposureUsesTheLargerLevel_SoItIsNotUnderstated()
    {
        _broker.OnPositions = () => new[] { RiskData.Pos("EURUSD", OrderSide.Buy, 1000m, 1.10m, 1.00m), RiskData.Pos("GBPUSD", OrderSide.Sell, 1000m, 1.20m, 1.30m) };
        var v = await Build();
        M(v, "grossExposure").Current.Should().Be(1100m + 1300m);
        M(v, "netExposure").Current.Should().Be(200m);
        M(v, "largestInstrumentExposure").Current.Should().Be(1300m);
    }

    [Fact]
    public async Task PositionWithoutLevelOrInAnotherCurrency_MakesExposureUnknown_NotAPartialSum()
    {
        _broker.OnPositions = () => new[] { RiskData.Pos("EURUSD", OrderSide.Buy, 1000m, 1.10m, 1.10m), RiskData.Pos("DE40", OrderSide.Buy, 1m, 18000m, 18000m, currency: "EUR") };
        var v = await Build();
        M(v, "grossExposure").Current.Should().BeNull();
        M(v, "grossExposure").Provenance.Should().Be("Unknown");
        R(v, "Gross exposure").Result.Should().Be("Unknown");

        _broker.OnPositions = () => new[] { RiskData.Pos("EURUSD", OrderSide.Buy, 1000m, null, 1.10m) };
        (await Build()).Metrics.Single(m => m.Key == "grossExposure").Current.Should().BeNull();
    }

    [Fact]
    public async Task OpenWorkingOrders_AreFlaggedAndCountedInExposure()
    {
        _broker.OnOrders = () => new[] { RiskData.Order("EURUSD", 1000m, 1.05m) };
        var v = await Build();
        v.Flags.Single(f => f.Key == "openWorkingOrders").Raised.Should().BeTrue();
        M(v, "workingOrderExposure").Current.Should().Be(1050m);
        M(v, "grossWithOrders").Current.Should().Be(1050m);

        _broker.QuoteCurrency = "EUR";
        M(await Build(), "workingOrderExposure").Current.Should().BeNull();
    }

    [Fact]
    public async Task MismatchedAccount_DropsItsFiguresAndVetoes()
    {
        _broker.OnAccount = () => RiskData.Account(id: "ACC-B");
        var v = await Build();
        v.Flags.Single(f => f.Key == "accountMismatch").Raised.Should().BeTrue();
        R(v, "Account binding").Result.Should().Be("Veto");
        F(v, "Balance").Value.Should().BeNull();
        M(v, "grossExposure").Current.Should().BeNull();
    }

    [Fact]
    public async Task StaleQuote_IsFlaggedAndVetoed()
    {
        _broker.OnQuotes = () => new[] { RiskData.Quote("EURUSD", RiskData.Now.AddMinutes(-5)) };
        var v = await Build("EURUSD");
        v.Flags.Single(f => f.Key == "staleData").Raised.Should().BeTrue();
        R(v, "Market data freshness").Result.Should().Be("Veto");
        M(v, "quoteAge").Current.Should().BeGreaterThan(60m);
    }

    [Fact]
    public async Task FreshQuote_PassesFreshnessAndSpread()
    {
        _broker.OnQuotes = () => new[] { RiskData.Quote("EURUSD", RiskData.Now.AddSeconds(-2)) };
        var v = await Build("EURUSD");
        R(v, "Market data freshness").Result.Should().Be("Pass");
        R(v, "Spread").Result.Should().Be("Pass");
        R(v, "Market data freshness").InputAsOfUtc.Should().Be(RiskData.Now.AddSeconds(-2));
    }

    [Fact]
    public async Task BrokerReadFailures_BecomeUnknown_NotAnError()
    {
        _broker.OnAccount = null; _broker.OnPositions = null; _broker.OnOrders = null; _broker.OnQuotes = null;
        var v = await Build("EURUSD");
        v.ReadErrors.Should().NotBeEmpty();
        v.Metrics.Should().OnlyContain(m => m.Current == null);
        v.Rules.Where(r => r.Rule is not ("Kill switch" or "Startup reconciliation")).Should().OnlyContain(r => r.Result == "Unknown");
        v.AccountFields.Should().OnlyContain(f => f.Value == null && f.Provenance == "Unknown");
    }

    [Fact]
    public async Task SlippageIsAlwaysUnknown_AndNoOrderCallIsMade()
    {
        _broker.OnOrders = () => new[] { RiskData.Order("EURUSD", 1m, 1m) };
        var v = await Build("EURUSD");
        M(v, "slippage").Should().Match<RiskMetric>(m => m.Current == null && m.Provenance == "Unknown");
        _broker.OrderCalls.Should().Be(0);
        v.PolicyEditable.Should().BeFalse();
    }

    [Fact]
    public async Task CountAtItsLimit_IsReportedAsBreached()
    {
        _broker.OnPositions = () => Enumerable.Range(0, 5).Select(i => RiskData.Pos("E" + i, OrderSide.Buy, 1m, 1m, 1m)).ToList();
        R(await Build(), "Concurrent positions").Result.Should().Be("Veto");
    }

    [Fact]
    public async Task EngagedKillSwitch_IsAVetoWithItsReason()
    {
        _control.EngageKillSwitch("Owner: test stop", "owner");
        var r = R(await Build(), "Kill switch");
        r.Result.Should().Be("Veto");
        r.Reason.Should().Be("Owner: test stop");
    }
}

public class KillSwitchServiceTests
{
    private readonly FakeClock _clock = new(RiskData.Now);
    private readonly RiskBroker _broker = new() { OnAccount = () => RiskData.Account() };
    private readonly TestTradingControl _control = new();
    private readonly InMemoryOrderJournal _journal = new();
    private readonly RecordingAudit _audit = new();

    private KillSwitchService Service() => new(_control, _broker, _journal, _audit, _clock, new GuidIdGenerator());

    private void ReadyToClear()
    {
        _control.State.Reconciliation = ReconciliationOutcome.NotCompared;
        _control.EngageKillSwitch("Owner: stop", "owner");
    }

    [Fact]
    public async Task Engage_RecordsReasonAndAudits_AndKeepsTheFirstReason()
    {
        var ks = Service();
        (await ks.EngageAsync(TradingEnvironment.Demo, "ACC-A", "  maintenance ", CancellationToken.None)).Changed.Should().BeTrue();
        _control.State.KillSwitchReason.Should().Be("Owner: maintenance");
        _control.State.KillSwitchChangedBy.Should().Be("owner");
        (await ks.EngageAsync(TradingEnvironment.Demo, "ACC-A", "again", CancellationToken.None)).Changed.Should().BeFalse();
        _control.State.KillSwitchReason.Should().Be("Owner: maintenance");
        _audit.Events.Select(e => e.Action).Should().Equal("Engaged", "EngageRepeated");
        _audit.Events.Should().OnlyContain(e => e.Category == "KillSwitch");
    }

    [Fact]
    public async Task Engage_WorksWhenTheBrokerAndTheAuditLogFail()
    {
        _broker.OnAccount = null; _broker.OnOrders = null; _audit.Fail = true;
        var outcome = await Service().EngageAsync(TradingEnvironment.Demo, "ACC-A", "broker down", CancellationToken.None);
        outcome.Should().Be(new EngageOutcome(true, false));
        _control.State.KillSwitchEngaged.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("line\nbreak")]
    public void ReasonProblem_RejectsMissingOrControlCharacters(string? reason) => KillSwitchService.ReasonProblem(reason).Should().NotBeNull();

    [Fact]
    public void ReasonProblem_RejectsOverlongReasons() =>
        KillSwitchService.ReasonProblem(new string('x', KillSwitchService.MaxReasonLength + 1)).Should().NotBeNull();

    [Fact]
    public async Task Clear_NeedsTheExactPhrase()
    {
        ReadyToClear();
        var outcome = await Service().ClearAsync(TradingEnvironment.Demo, "ACC-A", "clear kill switch", CancellationToken.None);
        outcome.ConfirmationAccepted.Should().BeFalse();
        _control.State.KillSwitchEngaged.Should().BeTrue();
        _audit.Events.Single().Action.Should().Be("ClearRefused");
    }

    [Fact]
    public async Task Clear_PassesWhenEveryCheckPasses()
    {
        ReadyToClear();
        var outcome = await Service().ClearAsync(TradingEnvironment.Demo, "ACC-A", KillSwitchService.ClearPhrase, CancellationToken.None);
        outcome.Cleared.Should().BeTrue();
        outcome.Checks.Should().OnlyContain(c => c.Passed);
        _control.State.KillSwitchEngaged.Should().BeFalse();
        _audit.Events.Last().Action.Should().Be("Cleared");
        _broker.OrderCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(ReconciliationOutcome.NotRun)]
    [InlineData(ReconciliationOutcome.Failed)]
    [InlineData(ReconciliationOutcome.Uncertain)]
    [InlineData(ReconciliationOutcome.Mismatch)]
    [InlineData(ReconciliationOutcome.CredentialsMissing)]
    public async Task Clear_CannotBypassFailedReconciliation(ReconciliationOutcome outcome)
    {
        ReadyToClear();
        _control.State.Reconciliation = outcome;
        await RefusedBecause("Startup reconciliation");
    }

    [Fact]
    public async Task Clear_IsRefusedWithAnUncertainBrokerConfirmation()
    {
        ReadyToClear();
        await _journal.CreateAsync(RiskData.Uncertain(), CancellationToken.None);
        await RefusedBecause("No uncertain broker confirmation");
    }

    [Fact]
    public async Task Clear_IsRefusedWithOpenWorkingOrders()
    {
        ReadyToClear();
        _broker.OnOrders = () => new[] { RiskData.Order("EURUSD", 1m, 1m) };
        await RefusedBecause("No open working orders");
    }

    [Fact]
    public async Task Clear_IsRefusedForAMismatchedAccount()
    {
        ReadyToClear();
        _broker.OnAccount = () => RiskData.Account(id: "ACC-B");
        await RefusedBecause("Broker account matches");
    }

    [Fact]
    public async Task Clear_IsRefusedWhenRiskDataIsMissing()
    {
        ReadyToClear();
        _broker.OnAccount = null; _broker.OnOrders = null;
        var checks = await RefusedBecause("Broker account matches");
        checks.Single(c => c.Name == "No open working orders").Passed.Should().BeFalse();
    }

    [Fact]
    public async Task Clear_IsRefusedWhenNothingIsEngaged()
    {
        _control.State.Reconciliation = ReconciliationOutcome.Complete;
        await RefusedBecause("Kill switch engaged");
    }

    private async Task<IReadOnlyList<ClearCheck>> RefusedBecause(string check)
    {
        var wasEngaged = _control.State.KillSwitchEngaged;
        var outcome = await Service().ClearAsync(TradingEnvironment.Demo, "ACC-A", KillSwitchService.ClearPhrase, CancellationToken.None);
        outcome.Cleared.Should().BeFalse();
        outcome.ConfirmationAccepted.Should().BeTrue();
        outcome.Checks.Single(c => c.Name == check).Passed.Should().BeFalse();
        _control.State.KillSwitchEngaged.Should().Be(wasEngaged);
        _audit.Events.Last().Action.Should().Be("ClearRefused");
        _audit.Events.Last().DetailsJson.Should().Contain(check);
        return outcome.Checks;
    }
}
