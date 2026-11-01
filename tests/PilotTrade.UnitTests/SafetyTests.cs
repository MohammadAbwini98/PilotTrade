using PilotTrade.Infrastructure.Broker;

namespace PilotTrade.UnitTests;

public class SafetyTests
{
    [Fact]
    public async Task RiskEngine_RejectsStalePrice()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var broker = new SimulatedBrokerAdapter(TradingEnvironment.Demo, clock, new GuidIdGenerator());
        var meta = (await broker.GetInstrumentAsync("EURUSD", "SIM-ACC", CancellationToken.None))!;
        var quote = (await broker.GetQuotesAsync(new[]{"EURUSD"}, CancellationToken.None)).First();
        var staleQuote = quote with { SourceTimestampUtc = clock.UtcNow.AddMinutes(-10) };
        var account = await broker.GetAccountAsync("SIM-ACC", CancellationToken.None);
        var res = new RiskEngine().Evaluate(TestData.RiskRequest(clock, account, meta, staleQuote));
        res.Decision.Should().Be(RiskDecision.Rejected);
        res.Reasons.Should().Contain(r => r.Contains("Stale price"));
    }

    [Fact]
    public void DecisionCoordinator_StaleSnapshot_NoTrade()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var snap = TestData.Snapshot(clock, "snap2", 2);
        var claude = TestData.Claude(clock, "snap1", 1);
        var dec = new DecisionCoordinator(clock).Decide(new DecisionCoordinatorInput(snap, claude, null, "s", "1", TestData.Selection(clock, 2)));
        dec.Candidate.Should().Be(DecisionCandidate.NoTrade);
        dec.RejectionReason.Should().Contain("snapshot mismatch");
    }

    [Fact]
    public void DecisionCoordinator_MissingClaude_NoTrade()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var dec = new DecisionCoordinator(clock).Decide(new DecisionCoordinatorInput(TestData.Snapshot(clock), null, null, "s", "1", TestData.Selection(clock)));
        dec.Candidate.Should().Be(DecisionCandidate.NoTrade);
    }

    [Fact]
    public async Task ExecutionGateway_LiveNotArmed_Rejects()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var broker = new SimulatedBrokerAdapter(TradingEnvironment.Demo, clock, new GuidIdGenerator());
        // Use demo broker but force live mode? Gateway checks broker.Environment. Instead test demo mode disabled.
        var control = new TestTradingControl();
        control.State.Mode = TradingMode.MonitorOnly;
        var audit = new InMemoryAuditLogger();
        var gw = new ExecutionGateway(broker, control, audit, new InMemoryOrderJournal(), clock, new GuidIdGenerator());
        var intent = new OrderIntent("i2", "SIM-ACC", TradingEnvironment.Demo, "EURUSD", OrderSide.Buy, OrderType.Market, 1m, null, null, null, null, clock.UtcNow, "1", "snap", "strat1");
        var preview = new OrderPreview(intent, 1.1m, 10m, 5m, 0m, 2m, RiskDecision.Approved, Array.Empty<string>(), clock.UtcNow);
        var risk = new RiskEvaluationResult(RiskDecision.Approved, Array.Empty<string>(), 10m, 10m, clock.UtcNow);
        var req = new ExecutionRequest(intent, preview, risk, TradingMode.MonitorOnly, false);
        var res = await gw.ExecuteAsync(req, CancellationToken.None);
        res.FinalState.Should().Be(OrderState.Rejected);
    }

    [Fact]
    public async Task ExecutionGateway_LiveBroker_NeverArmed_Rejects()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var broker = new SimulatedBrokerAdapter(TradingEnvironment.Live, clock, new GuidIdGenerator());
        var control = new TestTradingControl();
        control.State.ReconciliationComplete = true;
        var audit = new InMemoryAuditLogger();
        var gw = new ExecutionGateway(broker, control, audit, new InMemoryOrderJournal(), clock, new GuidIdGenerator());
        var intent = new OrderIntent("i3", "SIM-ACC", TradingEnvironment.Live, "EURUSD", OrderSide.Buy, OrderType.Market, 1m, null, null, null, null, clock.UtcNow, "1", "snap", "strat1");
        var preview = new OrderPreview(intent, 1.1m, 10m, 5m, 0m, 2m, RiskDecision.Approved, Array.Empty<string>(), clock.UtcNow);
        var risk = new RiskEvaluationResult(RiskDecision.Approved, Array.Empty<string>(), 10m, 10m, clock.UtcNow);
        var req = new ExecutionRequest(intent, preview, risk, TradingMode.LiveArmed, true);
        var res = await gw.ExecuteAsync(req, CancellationToken.None);
        res.FinalState.Should().Be(OrderState.Rejected);
    }
}

public class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) { UtcNow = now; }
    public DateTimeOffset UtcNow { get; set; }
}

public class TestTradingControl : ITradingControl
{
    public TradingControlState State { get; } = new();
    public void EngageKillSwitch() => State.KillSwitchEngaged = true;
    public void DisarmLive() { State.LiveArmed = false; State.LiveArmedUntilUtc = null; }
}

public class InMemoryOrderJournal : IOrderJournal
{
    private readonly List<OrderJournalEntry> _entries = new();
    public Task<OrderJournalEntry> CreateAsync(OrderJournalEntry entry, CancellationToken ct) { _entries.Add(entry); return Task.FromResult(entry); }
    public Task UpdateAsync(OrderJournalEntry entry, CancellationToken ct)
    {
        var idx = _entries.FindIndex(e => e.CommandId == entry.CommandId);
        if (idx >= 0) _entries[idx] = entry;
        return Task.CompletedTask;
    }
    public Task<OrderJournalEntry?> GetAsync(string commandId, CancellationToken ct) => Task.FromResult(_entries.FirstOrDefault(e => e.CommandId == commandId));
    public Task<OrderJournalEntry?> FindByIntentAsync(string intentId, CancellationToken ct) => Task.FromResult(_entries.FirstOrDefault(e => e.IntentId == intentId));
    public Task<IReadOnlyList<OrderJournalEntry>> ListUncertainAsync(TradingEnvironment env, string accountId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrderJournalEntry>>(_entries.Where(e => e.Environment == env && e.AccountId == accountId && (e.State == OrderState.Unknown || e.State == OrderState.Submitting || e.State == OrderState.BrokerPending)).ToList());
    public Task<IReadOnlyList<OrderJournalEntry>> ListAsync(TradingEnvironment env, string accountId, CancellationToken ct) => Task.FromResult<IReadOnlyList<OrderJournalEntry>>(_entries.Where(e => e.Environment == env && e.AccountId == accountId).ToList());
}

public class InMemoryAuditLogger : IAuditLogger
{
    public Task LogAsync(AuditEvent evt, CancellationToken ct) => Task.CompletedTask;
}

public static class TestData
{
    public static MarketSnapshotBundle Snapshot(IClock clock, string id = "snap1", int version = 1, string epic = "EURUSD") =>
        new(id, "SIM-ACC", TradingEnvironment.Demo, epic, version, clock.UtcNow, new MarketQuote(epic, 1m, 1.0002m, null, 0.0002m, clock.UtcNow, clock.UtcNow, FeedHealth.Healthy), new Dictionary<string, IReadOnlyList<Candle>>(), new Dictionary<string, decimal>());

    public static InstrumentSelection Selection(IClock clock, int version = 1, string epic = "EURUSD", string account = "SIM-ACC") =>
        new("sel", account, TradingEnvironment.Demo, epic, version, clock.UtcNow, "owner");

    public static ClaudeAnalysisResult Claude(IClock clock, string snapshotId = "snap1", int version = 1, string epic = "EURUSD", string account = "SIM-ACC", DecisionCandidate signal = DecisionCandidate.Buy) =>
        new("c1", snapshotId, epic, TradingEnvironment.Demo, version, "m", "m", signal, "trend", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), clock.UtcNow.AddMinutes(5), clock.UtcNow, 1, 1, 0m, TimeSpan.FromSeconds(1), account);

    public static JevResult Jev(IClock clock, string snapshotId = "snap1", int version = 1, string epic = "EURUSD", string account = "SIM-ACC", string direction = "Buy") =>
        new("j1", snapshotId, "m", "m", new[] { new JevAnswer("direction", direction, null, null) }, clock.UtcNow, TimeSpan.FromSeconds(1), account, epic, TradingEnvironment.Demo, version);

    public static OrderIntent Intent(IClock clock, string id = "i1", TradingEnvironment env = TradingEnvironment.Demo, decimal? stop = 1.0m) =>
        new(id, "SIM-ACC", env, "EURUSD", OrderSide.Buy, OrderType.Market, 1m, null, stop, null, null, clock.UtcNow, "1", "snap", "strat1");

    public static ExecutionRequest Execution(IClock clock, string id = "i1")
    {
        var intent = Intent(clock, id);
        return new ExecutionRequest(intent, new OrderPreview(intent, 1.1m, 10m, 5m, 0m, 2m, RiskDecision.Approved, Array.Empty<string>(), clock.UtcNow),
            new RiskEvaluationResult(RiskDecision.Approved, Array.Empty<string>(), 10m, 10m, clock.UtcNow), TradingMode.DemoAutomated, false);
    }

    public static RiskEvaluationRequest RiskRequest(IClock clock, BrokerAccount account, InstrumentMetadata meta, MarketQuote quote)
    {
        var policy = new RiskPolicy("p", TradingEnvironment.Demo, "SIM-ACC", 100m, 1m, 500m, 10000m, 10000m, 5000m, 5, 10, 80m, 100m, 10, TimeSpan.FromMinutes(5), SpreadLimits.DefaultPercent, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5), null, new[]{"EURUSD"}, Array.Empty<string>(), true, 200m, "UTC", clock.UtcNow);
        var intent = Intent(clock);
        var preview = new OrderPreview(intent, 1.1m, 10m, 5m, 0m, 2m, RiskDecision.Approved, Array.Empty<string>(), clock.UtcNow);
        return new RiskEvaluationRequest(intent, preview, policy, account, meta, quote, Array.Empty<Position>(), Array.Empty<WorkingOrder>(), clock.UtcNow);
    }
}
