using PilotTrade.Application.Security;
using PilotTrade.Infrastructure.Broker;

namespace PilotTrade.UnitTests;

// A broker whose submission behavior is scripted; reads are not used by these tests.
internal sealed class ScriptedBroker : IBrokerAdapter
{
    public int Submissions { get; private set; }
    public bool ExecutionEnabled { get; init; } = true;
    public TradingEnvironment Environment => TradingEnvironment.Demo;
    public Func<OrderIntent, BrokerSubmissionResult>? OnSubmit { get; init; }
    public Func<string, BrokerSubmissionResult?> OnConfirm { get; init; } = _ => null;

    public Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct)
    {
        Submissions++;
        return Task.FromResult(OnSubmit!(intent));
    }
    public Task<BrokerSubmissionResult?> GetConfirmationAsync(string reference, CancellationToken ct) => Task.FromResult(OnConfirm(reference));

    public Task<BrokerSession> CreateSessionAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<BrokerAccountSummary>> GetAccountsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<InstrumentSummary>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct) => throw new NotSupportedException();
}

public class DecisionInvalidationTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    private StrategyDecision Decide(InstrumentSelection? selection, ClaudeAnalysisResult? claude, JevResult? jev) =>
        new DecisionCoordinator(_clock).Decide(new DecisionCoordinatorInput(TestData.Snapshot(_clock), claude, jev, "s", "1", selection));

    [Fact]
    public void AlignedResultsForCurrentSelection_ProduceCandidate()
    {
        var dec = Decide(TestData.Selection(_clock), TestData.Claude(_clock), TestData.Jev(_clock));
        dec.Candidate.Should().Be(DecisionCandidate.Buy);
        dec.RejectionReason.Should().BeNull();
    }

    [Fact]
    public void MissingSelection_NoTrade() =>
        Decide(null, TestData.Claude(_clock), TestData.Jev(_clock)).RejectionReason.Should().Contain("selection missing");

    [Fact]
    public void InstrumentSwitched_InvalidatesSnapshot()
    {
        var dec = Decide(TestData.Selection(_clock, 2, "GOLD"), TestData.Claude(_clock), TestData.Jev(_clock));
        dec.Candidate.Should().Be(DecisionCandidate.NoTrade);
        dec.RejectionReason.Should().Contain("Selection changed");
    }

    [Fact]
    public void AccountSwitched_InvalidatesSnapshot() =>
        Decide(TestData.Selection(_clock, account: "OTHER"), TestData.Claude(_clock), TestData.Jev(_clock)).RejectionReason.Should().Contain("Selection changed");

    [Fact]
    public void ClaudeForOtherInstrument_NoTrade() =>
        Decide(TestData.Selection(_clock), TestData.Claude(_clock, epic: "GOLD"), TestData.Jev(_clock)).RejectionReason.Should().Contain("Claude instrument mismatch");

    [Fact]
    public void ClaudeForOtherVersion_NoTrade() =>
        Decide(TestData.Selection(_clock), TestData.Claude(_clock, version: 9), TestData.Jev(_clock)).RejectionReason.Should().Contain("Claude selection version");

    [Fact]
    public void ClaudeForOtherAccount_NoTrade() =>
        Decide(TestData.Selection(_clock), TestData.Claude(_clock, account: "OTHER"), TestData.Jev(_clock)).RejectionReason.Should().Contain("Claude account");

    [Fact]
    public void JevForOtherInstrument_NoTrade() =>
        Decide(TestData.Selection(_clock), TestData.Claude(_clock), TestData.Jev(_clock, epic: "GOLD")).RejectionReason.Should().Contain("JEV instrument mismatch");

    [Fact]
    public void JevForOtherVersion_NoTrade() =>
        Decide(TestData.Selection(_clock), TestData.Claude(_clock), TestData.Jev(_clock, version: 9)).RejectionReason.Should().Contain("JEV selection version");

    [Fact]
    public void JevForOtherAccount_NoTrade() =>
        Decide(TestData.Selection(_clock), TestData.Claude(_clock), TestData.Jev(_clock, account: "OTHER")).RejectionReason.Should().Contain("JEV account");

    [Fact]
    public void AiDisagreement_NoTrade() =>
        Decide(TestData.Selection(_clock), TestData.Claude(_clock), TestData.Jev(_clock, direction: "Sell")).Candidate.Should().Be(DecisionCandidate.NoTrade);

    [Fact]
    public void SelectionService_EveryChangeRaisesTheVersion()
    {
        var service = new InstrumentSelectionService(_clock, new GuidIdGenerator());
        var first = service.Select("A", TradingEnvironment.Demo, "EURUSD", "owner");
        var second = service.Select("A", TradingEnvironment.Demo, "EURUSD", "owner");
        var other = service.Select("B", TradingEnvironment.Demo, "GOLD", "owner");
        second.SelectionVersion.Should().BeGreaterThan(first.SelectionVersion);
        other.SelectionVersion.Should().BeGreaterThan(second.SelectionVersion);
        service.Current("A", TradingEnvironment.Demo).Should().Be(second);
        service.Current("C", TradingEnvironment.Demo).Should().BeNull();
    }
}

public class ExecutionIntegrityTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    private ExecutionGateway Gateway(IBrokerAdapter broker, IOrderJournal journal, bool killSwitch = false)
    {
        var control = new TestTradingControl();
        control.State.ReconciliationComplete = true;
        control.State.KillSwitchEngaged = killSwitch;
        return new ExecutionGateway(broker, control, new InMemoryAuditLogger(), journal, _clock, new GuidIdGenerator());
    }

    [Fact]
    public async Task BrokerExecutionDisabled_RejectsWithoutSubmittingOrJournaling()
    {
        var broker = new ScriptedBroker { ExecutionEnabled = false, OnSubmit = _ => throw new InvalidOperationException("must not run") };
        var journal = new InMemoryOrderJournal();
        var result = await Gateway(broker, journal).ExecuteAsync(TestData.Execution(_clock), CancellationToken.None);
        result.FinalState.Should().Be(OrderState.Rejected);
        broker.Submissions.Should().Be(0);
        (await journal.FindByIntentAsync("i1", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task KillSwitch_BlocksSubmission()
    {
        var broker = new ScriptedBroker { OnSubmit = _ => throw new InvalidOperationException("must not run") };
        (await Gateway(broker, new InMemoryOrderJournal(), killSwitch: true).ExecuteAsync(TestData.Execution(_clock), CancellationToken.None))
            .FinalState.Should().Be(OrderState.Rejected);
        broker.Submissions.Should().Be(0);
    }

    [Fact]
    public async Task Timeout_IsJournaledUnknown_AndNeverResubmitted()
    {
        var broker = new ScriptedBroker { OnSubmit = _ => throw new TaskCanceledException("secret detail") };
        var journal = new InMemoryOrderJournal();
        var gateway = Gateway(broker, journal);

        var first = await gateway.ExecuteAsync(TestData.Execution(_clock), CancellationToken.None);
        var second = await gateway.ExecuteAsync(TestData.Execution(_clock), CancellationToken.None);

        first.FinalState.Should().Be(OrderState.Unknown);
        first.Error.Should().NotContain("secret detail");
        second.FinalState.Should().Be(OrderState.Unknown);
        broker.Submissions.Should().Be(1);
        (await journal.ListUncertainAsync(TradingEnvironment.Demo, "SIM-ACC", CancellationToken.None)).Should().HaveCount(1);
    }

    private OrderJournalEntry Entry(string id, OrderState state, string? reference) =>
        new("cmd_" + id, id, TradingEnvironment.Demo, "SIM-ACC", "EURUSD", state, id, "snap", null, reference, null, _clock.UtcNow, _clock.UtcNow, null, null, _clock.UtcNow, id);

    [Fact]
    public async Task Reconciliation_RecordsEveryAffectedDeal_WithoutSubmitting()
    {
        var journal = new InMemoryOrderJournal();
        await journal.CreateAsync(Entry("a", OrderState.Unknown, "o_a"), CancellationToken.None);
        var broker = new ScriptedBroker
        {
            OnConfirm = r => new BrokerSubmissionResult("", r, "d1", OrderState.Confirmed, null, _clock.UtcNow, _clock.UtcNow, new[] { new AffectedDeal("d1", "OPENED"), new AffectedDeal("d2", "FULLY_CLOSED") })
        };
        var summary = await new OrderReconciliationService(broker, journal, _clock).ReconcileAsync(TradingEnvironment.Demo, "SIM-ACC", CancellationToken.None);

        summary.Should().Be(new ReconciliationSummary(1, 0));
        var entry = await journal.GetAsync("cmd_a", CancellationToken.None);
        entry!.State.Should().Be(OrderState.Reconciled);
        entry.BrokerDealId.Should().Be("d1,d2");
        broker.Submissions.Should().Be(0);
    }

    [Fact]
    public async Task Reconciliation_KeepsOrdersUncertain_WhenTheBrokerCannotConfirm()
    {
        var journal = new InMemoryOrderJournal();
        await journal.CreateAsync(Entry("noref", OrderState.Submitting, null), CancellationToken.None);
        await journal.CreateAsync(Entry("missing", OrderState.Unknown, "o_missing"), CancellationToken.None);
        await journal.CreateAsync(Entry("failed", OrderState.Unknown, "o_failed"), CancellationToken.None);
        var broker = new ScriptedBroker
        {
            OnConfirm = r => r == "o_failed" ? throw new BrokerException(BrokerErrorKind.Unavailable, "down") : null
        };
        var summary = await new OrderReconciliationService(broker, journal, _clock).ReconcileAsync(TradingEnvironment.Demo, "SIM-ACC", CancellationToken.None);

        summary.Should().Be(new ReconciliationSummary(0, 3));
        (await journal.ListUncertainAsync(TradingEnvironment.Demo, "SIM-ACC", CancellationToken.None)).Should().HaveCount(3);
    }
}

public class RiskFailClosedTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    private async Task<RiskEvaluationResult> Evaluate(Func<BrokerAccount, BrokerAccount>? account = null, Func<InstrumentMetadata, InstrumentMetadata>? meta = null)
    {
        var broker = new SimulatedBrokerAdapter(TradingEnvironment.Demo, _clock, new GuidIdGenerator());
        var a = await broker.GetAccountAsync("SIM-ACC", CancellationToken.None);
        var m = (await broker.GetInstrumentAsync("EURUSD", "SIM-ACC", CancellationToken.None))!;
        var q = (await broker.GetQuotesAsync(new[] { "EURUSD" }, CancellationToken.None)).First();
        return new RiskEngine().Evaluate(TestData.RiskRequest(_clock, account?.Invoke(a) ?? a, meta?.Invoke(m) ?? m, q));
    }

    [Fact]
    public async Task MissingEquity_Rejected() =>
        (await Evaluate(a => a with { Equity = null })).Reasons.Should().Contain("Equity unavailable");

    [Fact]
    public async Task MissingMarginUsed_Rejected() =>
        (await Evaluate(a => a with { MarginUsed = null })).Reasons.Should().Contain("Margin used unavailable");

    [Fact]
    public async Task MissingFreeMargin_Rejected() =>
        (await Evaluate(a => a with { FreeMargin = null })).Reasons.Should().Contain("Free margin unavailable");

    [Fact]
    public async Task MissingRealizedPnL_Rejected() =>
        (await Evaluate(a => a with { RealizedPnLToday = null })).Reasons.Should().Contain("Realized P/L unavailable");

    [Fact]
    public async Task UnknownMarketStatus_Rejected() =>
        (await Evaluate(meta: m => m with { Status = MarketStatus.Unknown })).Decision.Should().Be(RiskDecision.Rejected);

    [Fact]
    public async Task MissingStopRules_Rejected() =>
        (await Evaluate(meta: m => m with { MinStopDistance = null })).Reasons.Should().Contain("Stop rules missing");

    [Theory]
    [InlineData(InstrumentAssetClass.Fx, 0.01, false)]
    [InlineData(InstrumentAssetClass.Fx, 0.1, true)]
    [InlineData(InstrumentAssetClass.CryptoCfd, 0.5, false)]
    [InlineData(InstrumentAssetClass.CryptoCfd, 1.5, true)]
    [InlineData(InstrumentAssetClass.Commodity, 0.1, false)]
    [InlineData(InstrumentAssetClass.Commodity, 0.3, true)]
    public void SpreadLimit_DependsOnTheAssetClass(InstrumentAssetClass assetClass, double spreadPercent, bool rejected)
    {
        var reasons = EvaluateSpread(assetClass, (decimal)spreadPercent);
        reasons.Any(r => r.StartsWith("Spread ")).Should().Be(rejected);
    }

    [Theory]
    [InlineData(InstrumentAssetClass.Unknown)]
    [InlineData(InstrumentAssetClass.Other)]
    public void SpreadLimit_UnknownClass_IsRejected(InstrumentAssetClass assetClass) =>
        EvaluateSpread(assetClass, 0.001m).Should().Contain($"Spread limit unknown: {assetClass}");

    [Fact]
    public void Quote_FromTheFuture_IsRejected()
    {
        var request = SpreadRequest(InstrumentAssetClass.Fx, 0.001m);
        var future = request with { Quote = request.Quote with { SourceTimestampUtc = _clock.UtcNow.AddMinutes(5) } };
        new RiskEngine().Evaluate(future).Reasons.Should().Contain("Price timestamp in the future");
    }

    [Fact]
    public void Quote_Crossed_IsRejected()
    {
        var request = SpreadRequest(InstrumentAssetClass.Fx, 0.001m);
        var crossed = request with { Quote = request.Quote with { Bid = 1.2m, Ask = 1.1m } };
        new RiskEngine().Evaluate(crossed).Reasons.Should().Contain("Quote invalid");
    }

    private IReadOnlyList<string> EvaluateSpread(InstrumentAssetClass assetClass, decimal spreadPercent) =>
        new RiskEngine().Evaluate(SpreadRequest(assetClass, spreadPercent)).Reasons;

    private RiskEvaluationRequest SpreadRequest(InstrumentAssetClass assetClass, decimal spreadPercent)
    {
        var broker = new SimulatedBrokerAdapter(TradingEnvironment.Demo, _clock, new GuidIdGenerator());
        var account = broker.GetAccountAsync("SIM-ACC", CancellationToken.None).GetAwaiter().GetResult();
        var meta = broker.GetInstrumentAsync("EURUSD", "SIM-ACC", CancellationToken.None).GetAwaiter().GetResult()! with { AssetClass = assetClass };
        const decimal mid = 100m;
        var half = mid * spreadPercent / 200m;
        var quote = new MarketQuote("EURUSD", mid - half, mid + half, mid, half * 2m, _clock.UtcNow, _clock.UtcNow, FeedHealth.Healthy);
        return TestData.RiskRequest(_clock, account, meta, quote);
    }
}

public class MarketDataNoTradeTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));

    private sealed class Feed : IBrokerAdapter
    {
        public InstrumentMetadata? Meta;
        public MarketQuote? Quote;
        public IReadOnlyList<Candle> Candles = Array.Empty<Candle>();
        public Exception? Failure;
        public TradingEnvironment Environment => TradingEnvironment.Demo;
        public bool ExecutionEnabled => false;
        public Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct) => Failure is null ? Task.FromResult(Meta) : throw Failure;
        public Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct) => Task.FromResult<IReadOnlyList<MarketQuote>>(Quote is null ? Array.Empty<MarketQuote>() : new[] { Quote });
        public Dictionary<string, IReadOnlyList<Candle>>? CandlesByTimeframe;
        public Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct) => Task.FromResult(CandlesByTimeframe?[timeframe] ?? Candles);
        public Task<BrokerSession> CreateSessionAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BrokerAccountSummary>> GetAccountsAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<InstrumentSummary>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct) => throw new NotSupportedException();
        public Task<BrokerSubmissionResult?> GetConfirmationAsync(string reference, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct) => throw new NotSupportedException();
    }

    private Feed Healthy()
    {
        var bar = TimeSpan.FromMinutes(5);
        var start = _clock.UtcNow - bar * 30;
        return new Feed
        {
            Meta = new InstrumentMetadata("EURUSD", "EUR/USD", "EURUSD", InstrumentAssetClass.Fx, MarketStatus.Open, "USD", "USD", 5, 0.00001m, 0.01m, 100m, 0.01m, null, null, 1m, 100m, true, true, true, Array.Empty<string>(), _clock.UtcNow, "A", TradingEnvironment.Demo),
            Quote = new MarketQuote("EURUSD", 1.1000m, 1.1002m, 1.1001m, 0.0002m, _clock.UtcNow.AddSeconds(-5), _clock.UtcNow, FeedHealth.Healthy),
            Candles = Enumerable.Range(0, 30).Select(i => new Candle("EURUSD", "5m", start + bar * i, start + bar * (i + 1), true, 1.1m, 1.2m, 1.0m, 1.1m + i * 0.001m, 10m)).ToList()
        };
    }

    private Task<MarketSnapshotBundle> Build(Feed feed) =>
        new MarketDataService(feed, _clock).BuildSnapshotAsync("A", TradingEnvironment.Demo, "EURUSD", 1, new[] { "5m" }, CancellationToken.None);

    private async Task ExpectRejected(Feed feed, string reason) =>
        (await Assert.ThrowsAsync<MarketDataRejectedException>(() => Build(feed))).Message.Should().Contain(reason);

    [Fact]
    public async Task HealthyInputs_BuildSnapshot()
    {
        var bundle = await Build(Healthy());
        bundle.Epic.Should().Be("EURUSD");
        bundle.Indicators.Should().ContainKey("5m.ema20");
    }

    [Fact]
    public Task MissingMetadata() { var f = Healthy(); f.Meta = null; return ExpectRejected(f, "metadata missing"); }

    [Fact]
    public Task ClosedMarket() { var f = Healthy(); f.Meta = f.Meta! with { Status = MarketStatus.Closed }; return ExpectRejected(f, "not open"); }

    [Fact]
    public Task MissingQuote() { var f = Healthy(); f.Quote = null; return ExpectRejected(f, "No quote"); }

    [Fact]
    public Task StaleQuote() { var f = Healthy(); f.Quote = f.Quote! with { SourceTimestampUtc = _clock.UtcNow.AddMinutes(-5) }; return ExpectRejected(f, "Stale quote"); }

    [Fact]
    public Task FutureQuote() { var f = Healthy(); f.Quote = f.Quote! with { SourceTimestampUtc = _clock.UtcNow.AddMinutes(5) }; return ExpectRejected(f, "future"); }

    [Fact]
    public Task CrossedQuote() { var f = Healthy(); f.Quote = f.Quote! with { Bid = 1.2m, Ask = 1.1m }; return ExpectRejected(f, "crossed"); }

    [Fact]
    public Task WideSpread() { var f = Healthy(); f.Quote = f.Quote! with { Bid = 1.0m, Ask = 1.2m }; return ExpectRejected(f, "Spread too wide"); }

    [Fact]
    public Task DelayedFeed() { var f = Healthy(); f.Quote = f.Quote! with { Health = FeedHealth.Stale }; return ExpectRejected(f, "unhealthy"); }

    [Theory]
    [InlineData(-61, FeedHealth.Stale)]
    [InlineData(-60, FeedHealth.Healthy)]
    [InlineData(0, FeedHealth.Healthy)]
    [InlineData(60, FeedHealth.Healthy)]
    [InlineData(61, FeedHealth.Unknown)]
    public void SourceTimestamp_DecidesTheHealth(int offsetSeconds, FeedHealth expected) =>
        MarketDataPolicy.Default.FeedHealthAt(_clock.UtcNow.AddSeconds(offsetSeconds), _clock.UtcNow).Should().Be(expected);

    [Fact]
    public async Task StaleTimestamp_ReportsTheFeedAsStale_EvenWhenTheQuoteClaimsHealthy()
    {
        var f = Healthy(); f.Quote = f.Quote! with { SourceTimestampUtc = _clock.UtcNow.AddMinutes(-5), Health = FeedHealth.Healthy };
        var service = new MarketDataService(f, _clock);
        await Assert.ThrowsAsync<MarketDataRejectedException>(() => service.BuildSnapshotAsync("A", TradingEnvironment.Demo, "EURUSD", 1, new[] { "5m" }, CancellationToken.None));
        service.GetFeedHealth("EURUSD").Should().Be(FeedHealth.Stale);
    }

    [Fact]
    public async Task HealthyFeed_TurnsStale_AsTimePasses()
    {
        var f = Healthy();
        var service = new MarketDataService(f, _clock);
        await service.BuildSnapshotAsync("A", TradingEnvironment.Demo, "EURUSD", 1, new[] { "5m" }, CancellationToken.None);
        service.GetFeedHealth("EURUSD").Should().Be(FeedHealth.Healthy);
        _clock.UtcNow = _clock.UtcNow.AddSeconds(61);
        service.GetFeedHealth("EURUSD").Should().Be(FeedHealth.Stale);
    }

    [Fact]
    public void NoQuoteYet_IsUnknown_NotHealthy() =>
        new MarketDataService(Healthy(), _clock).GetFeedHealth("EURUSD").Should().Be(FeedHealth.Unknown);

    [Fact]
    public Task NoSpreadLimitForTheClass() { var f = Healthy(); f.Meta = f.Meta! with { AssetClass = InstrumentAssetClass.Unknown }; return ExpectRejected(f, "Spread limit unknown"); }

    [Fact]
    public async Task CryptoSpread_PassesWhereTheSameSpreadOnFxFails()
    {
        var f = Healthy();
        f.Quote = f.Quote! with { Bid = 100m, Ask = 100.5m };
        await ExpectRejected(f, "Spread too wide");
        f.Meta = f.Meta! with { AssetClass = InstrumentAssetClass.CryptoCfd };
        (await Build(f)).Epic.Should().Be("EURUSD");
    }

    [Fact]
    public async Task MultipleTimeframes_AreAllReturned()
    {
        var f = Healthy();
        var hour = TimeSpan.FromHours(1);
        var start = _clock.UtcNow - hour * 30;
        var fiveMinute = f.Candles;
        var hourly = Enumerable.Range(0, 30).Select(i => new Candle("EURUSD", "1h", start + hour * i, start + hour * (i + 1), true, 1.1m, 1.2m, 1.0m, 1.1m, 10m)).ToList();
        f.CandlesByTimeframe = new() { ["5m"] = fiveMinute, ["1h"] = hourly };
        var bundle = await new MarketDataService(f, _clock).BuildSnapshotAsync("A", TradingEnvironment.Demo, "EURUSD", 1, new[] { "5m", "1h" }, CancellationToken.None);
        bundle.CandlesByTimeframe.Keys.Should().BeEquivalentTo("5m", "1h");
        bundle.Indicators.Should().ContainKeys("5m.ema20", "1h.ema20");
    }

    [Fact]
    public async Task OneBadTimeframe_RejectsTheWholeSnapshot()
    {
        var f = Healthy();
        f.CandlesByTimeframe = new() { ["5m"] = f.Candles, ["1h"] = Array.Empty<Candle>() };
        var rejected = await Assert.ThrowsAsync<MarketDataRejectedException>(() =>
            new MarketDataService(f, _clock).BuildSnapshotAsync("A", TradingEnvironment.Demo, "EURUSD", 1, new[] { "5m", "1h" }, CancellationToken.None));
        rejected.Message.Should().Contain("1h candle history insufficient");
    }

    [Fact]
    public Task TooFewCandles() { var f = Healthy(); f.Candles = f.Candles.Take(5).ToList(); return ExpectRejected(f, "insufficient"); }

    [Fact]
    public Task StaleCandles() { var f = Healthy(); _clock.UtcNow = _clock.UtcNow.AddHours(3); f.Quote = f.Quote! with { SourceTimestampUtc = _clock.UtcNow }; return ExpectRejected(f, "candles stale"); }

    [Fact]
    public Task OutOfOrderCandles() { var f = Healthy(); f.Candles = f.Candles.Reverse().ToList(); return ExpectRejected(f, "out of order"); }

    [Fact]
    public Task InvalidCandleRange() { var f = Healthy(); f.Candles = f.Candles.Select((c, i) => i == 3 ? c with { High = 0.5m } : c).ToList(); return ExpectRejected(f, "range invalid"); }

    [Fact]
    public async Task OpenCandleIsExcluded()
    {
        var f = Healthy();
        var last = f.Candles[^1];
        var open = last with { OpenTimeUtc = last.CloseTimeUtc, CloseTimeUtc = last.CloseTimeUtc.AddMinutes(5), IsClosed = false, Close = 9m, High = 9m };
        f.Candles = f.Candles.Append(open).ToList();
        (await Build(f)).CandlesByTimeframe["5m"].Should().NotContain(c => !c.IsClosed);
    }

    [Fact]
    public async Task BrokerFailure_IsNoTrade_AndMarksFeedDisconnected()
    {
        var f = Healthy(); f.Failure = new BrokerException(BrokerErrorKind.Unavailable, "down");
        var service = new MarketDataService(f, _clock);
        await Assert.ThrowsAsync<MarketDataRejectedException>(() => service.BuildSnapshotAsync("A", TradingEnvironment.Demo, "EURUSD", 1, new[] { "5m" }, CancellationToken.None));
        service.GetFeedHealth("EURUSD").Should().Be(FeedHealth.Disconnected);
    }
}

public class CsrfTests
{
    [Fact]
    public void Token_IsSingleUse()
    {
        var csrf = new InMemoryCsrfTokenService(new FakeClock(DateTimeOffset.UtcNow));
        var token = csrf.IssueToken();
        csrf.Validate(token).Should().BeTrue();
        csrf.Validate(token).Should().BeFalse();
    }

    [Fact]
    public void Token_Expires()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var csrf = new InMemoryCsrfTokenService(clock);
        var token = csrf.IssueToken();
        clock.UtcNow = clock.UtcNow.AddMinutes(11);
        csrf.Validate(token).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown")]
    public void UnknownToken_Fails(string token) =>
        new InMemoryCsrfTokenService(new FakeClock(DateTimeOffset.UtcNow)).Validate(token).Should().BeFalse();
}
