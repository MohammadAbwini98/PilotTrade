namespace PilotTrade.UnitTests;

using System.Net;
using System.Text.Json;

// Task 02-04: every AI failure ends in a recorded NO_TRADE with a fixed, safe reason. Nothing is substituted.
public class AiDecisionPipelineTests
{
    // Split so the secret scan does not flag the source; the runtime value is a credential-shaped fake.
    private const string Secret = "sk-" + "ant-SECRET-should-never-appear";
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly RecordingStore _store = new();

    private sealed class RecordingStore : IDecisionEventStore
    {
        public List<DecisionEvent> Events { get; } = new();
        public void Record(DecisionEvent evt) => Events.Add(evt);
        public DecisionPage List(DecisionQuery query) => new(Events.Count, Events);
        public DecisionEvent? Get(string accountId, TradingEnvironment env, string decisionId) => Events.FirstOrDefault(e => e.DecisionId == decisionId);
    }

    private sealed class FakeClaude(Func<ClaudeAnalysisRequest, CancellationToken, Task<ClaudeAnalysisResult>> f) : IClaudeAnalyst
    {
        public int Calls { get; private set; }
        public Task<ClaudeAnalysisResult> AnalyzeAsync(ClaudeAnalysisRequest request, CancellationToken ct) { Calls++; return f(request, ct); }
    }

    private sealed class FakeJev(Func<JevRequest, CancellationToken, Task<JevResult>> f) : IJevEvaluator
    {
        public int Calls { get; private set; }
        public Task<JevResult> EvaluateAsync(JevRequest request, CancellationToken ct) { Calls++; return f(request, ct); }
    }

    private FakeClaude ClaudeSays(DecisionCandidate signal = DecisionCandidate.Buy) => new((_, _) => Task.FromResult(TestData.Claude(_clock, signal: signal)));
    private FakeJev JevSays(string direction = "Buy") => new((_, _) => Task.FromResult(TestData.Jev(_clock, direction: direction)));
    private static FakeClaude ClaudeThrows(Exception ex) => new((_, _) => Task.FromException<ClaudeAnalysisResult>(ex));

    private AiDecisionPipeline Pipeline(IClaudeAnalyst? claude, IJevEvaluator? jev, bool enabled = true, TimeSpan? timeout = null) =>
        new(claude, jev, new DecisionCoordinator(_clock), _store, _clock, new GuidIdGenerator(),
            AiPipelineOptions.Default with { Enabled = enabled, ClaudeTimeout = timeout ?? TimeSpan.FromSeconds(20), JevTimeout = timeout ?? TimeSpan.FromSeconds(10) });

    private Task<DecisionEvent> Run(AiDecisionPipeline p, CancellationToken ct = default) =>
        p.RunAsync(TestData.Snapshot(_clock), TestData.Selection(_clock), 100m, 0m, ct);

    private static void NoLeak(DecisionEvent e) => JsonSerializer.Serialize(e).Should().NotContain(Secret);

    [Fact]
    public async Task Agreeing_answers_produce_the_trade_candidate_with_metadata_only()
    {
        var e = await Run(Pipeline(ClaudeSays(), JevSays()));
        e.Decision.Should().Be(DecisionCandidate.Buy);
        e.NoTradeReason.Should().BeNull();
        e.Claude.Status.Should().Be(AiStepStatus.Succeeded);
        e.Claude.SchemaValid.Should().BeTrue();
        e.Claude.CostUsd.Should().BeNull("the client does not compute cost");
        e.Claude.Confidence.Should().BeNull("Claude does not return a confidence");
        e.Jev.Status.Should().Be(AiStepStatus.Succeeded);
        e.PromptSha256.Should().MatchRegex("^[0-9a-f]{64}$");
        e.PromptChars.Should().BeGreaterThan(0);
        e.RiskVeto.Should().BeNull();
        e.SnapshotId.Should().Be("snap1");
        e.SelectionVersion.Should().Be(1);
        _store.Events.Should().ContainSingle().Which.Should().Be(e);
        JsonSerializer.Serialize(e).Should().NotContain("You are a market analyst").And.NotContain("availableRiskBudget");
    }

    [Fact]
    public async Task Disabled_AI_asks_no_one_and_records_NO_TRADE()
    {
        var claude = ClaudeSays(); var jev = JevSays();
        var e = await Run(Pipeline(claude, jev, enabled: false));
        claude.Calls.Should().Be(0); jev.Calls.Should().Be(0);
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        e.Claude.Status.Should().Be(AiStepStatus.Disabled);
        e.Jev.Status.Should().Be(AiStepStatus.Disabled);
        e.NoTradeReason.Should().Be("AI is disabled");
        e.PromptSha256.Should().BeNull();
    }

    [Fact]
    public async Task Missing_clients_are_unavailable_not_mocked()
    {
        var e = await Run(Pipeline(null, null));
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        e.Claude.Status.Should().Be(AiStepStatus.Unavailable);
        e.Jev.Status.Should().Be(AiStepStatus.NotRun);
        e.NoTradeReason.Should().Contain("Claude API unavailable");
    }

    [Fact]
    public async Task Missing_JEV_client_blocks_a_Claude_buy()
    {
        var e = await Run(Pipeline(ClaudeSays(), null));
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        e.Claude.Output.Should().Be(DecisionCandidate.Buy);
        e.Jev.Status.Should().Be(AiStepStatus.Unavailable);
        e.NoTradeReason.Should().Contain("JEV/TypeSafe AI unavailable");
    }

    [Fact]
    public async Task Missing_key_is_unavailable()
    {
        var e = await Run(Pipeline(ClaudeThrows(new AiCredentialMissingException("Anthropic")), JevSays()));
        e.Claude.Status.Should().Be(AiStepStatus.Unavailable);
        e.Claude.Problem.Should().Be("API key missing");
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
    }

    [Fact]
    public async Task Timeout_is_TimedOut_and_NO_TRADE()
    {
        var slow = new FakeClaude(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });
        var jev = JevSays();
        var e = await Run(Pipeline(slow, jev, timeout: TimeSpan.FromMilliseconds(50)));
        e.Claude.Status.Should().Be(AiStepStatus.TimedOut);
        e.Jev.Status.Should().Be(AiStepStatus.NotRun);
        jev.Calls.Should().Be(0);
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        e.NoTradeReason.Should().StartWith("Claude API timed out");
    }

    [Fact]
    public async Task Invalid_schema_is_Rejected_and_the_message_is_not_kept()
    {
        var e = await Run(Pipeline(ClaudeThrows(new JsonException($"bad token near {Secret}")), JevSays()));
        e.Claude.Status.Should().Be(AiStepStatus.Rejected);
        e.Claude.SchemaValid.Should().BeFalse();
        e.Claude.Problem.Should().Be("Reply does not match the schema");
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        NoLeak(e);
    }

    [Fact]
    public async Task Provider_error_is_Unavailable_with_only_the_status_code()
    {
        var e = await Run(Pipeline(ClaudeThrows(new HttpRequestException($"x-api-key: {Secret}", null, HttpStatusCode.InternalServerError)), JevSays()));
        e.Claude.Status.Should().Be(AiStepStatus.Unavailable);
        e.Claude.Problem.Should().Be("Provider error (HTTP 500)");
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        NoLeak(e);
    }

    [Fact]
    public async Task Unexpected_exception_is_Unavailable_and_not_leaked()
    {
        var e = await Run(Pipeline(ClaudeThrows(new InvalidOperationException(Secret)), JevSays()));
        e.Claude.Status.Should().Be(AiStepStatus.Unavailable);
        e.Claude.Problem.Should().Be("Provider error");
        NoLeak(e);
    }

    [Theory]
    [InlineData("Maybe")]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("1")]
    public async Task JEV_without_a_valid_direction_is_Rejected(string direction)
    {
        var e = await Run(Pipeline(ClaudeSays(), JevSays(direction)));
        e.Jev.Status.Should().Be(AiStepStatus.Rejected);
        e.Jev.SchemaValid.Should().BeFalse();
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
    }

    [Fact]
    public async Task JEV_reply_for_another_snapshot_is_Rejected()
    {
        var jev = new FakeJev((_, _) => Task.FromResult(TestData.Jev(_clock, snapshotId: "other")));
        var e = await Run(Pipeline(ClaudeSays(), jev));
        e.Jev.Status.Should().Be(AiStepStatus.Rejected);
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
    }

    [Fact]
    public async Task Disagreement_is_NO_TRADE()
    {
        var e = await Run(Pipeline(ClaudeSays(DecisionCandidate.Buy), JevSays("Sell")));
        e.Claude.Status.Should().Be(AiStepStatus.Succeeded);
        e.Jev.Status.Should().Be(AiStepStatus.Succeeded);
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        e.NoTradeReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Changed_selection_is_NO_TRADE_even_when_both_agree()
    {
        var p = Pipeline(ClaudeSays(), JevSays());
        var e = await p.RunAsync(TestData.Snapshot(_clock), TestData.Selection(_clock, version: 2), 100m, 0m, default);
        e.Decision.Should().Be(DecisionCandidate.NoTrade);
        e.NoTradeReason.Should().Contain("Selection changed");
    }

    [Fact]
    public async Task Caller_cancellation_records_nothing()
    {
        using var cts = new CancellationTokenSource();
        var claude = new FakeClaude(async (_, ct) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); });
        var act = () => Run(Pipeline(claude, JevSays()), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Events.Should().BeEmpty();
    }

    [Fact]
    public void Status_names_cover_each_outcome_the_panel_shows()
    {
        Enum.GetNames<AiStepStatus>().Should().BeEquivalentTo("Disabled", "Unavailable", "TimedOut", "Rejected", "Succeeded", "NotRun");
    }
}
