namespace PilotTrade.Application.Services;

using System.Security.Cryptography;
using System.Text;
using PilotTrade.Domain;
using PilotTrade.Domain.AI;
using PilotTrade.Domain.Decision;
using PilotTrade.Domain.Market;

public sealed record AiPipelineOptions(
    bool Enabled,
    TimeSpan ClaudeTimeout,
    TimeSpan JevTimeout,
    string JevModelId,
    string StrategyId,
    string StrategyVersion)
{
    public static AiPipelineOptions Default { get; } = new(false, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(10), "jev", "strategy", "0");
}

// Task 02-04: asks Claude, then JEV, passes both to the coordinator and records one decision event.
// Every failure (disabled, unavailable, timeout, invalid schema, provider error) ends in NO_TRADE, never in a
// substituted or mocked answer. Only Claude and JEV are used; there is no fallback model.
// This build does not register the pipeline at runtime: wiring it to live AI belongs to the execution-readiness phase.
public sealed class AiDecisionPipeline
{
    public const string ClaudeProvider = "Claude API";
    public const string JevProvider = "JEV/TypeSafe AI";

    private static readonly IReadOnlyList<JevQuestion> Questions =
        new[] { new JevQuestion("direction", "choice", new[] { "Buy", "Sell", "Hold", "NoTrade" }) };

    private readonly IClaudeAnalyst? _claude;
    private readonly IJevEvaluator? _jev;
    private readonly IDecisionCoordinator _coordinator;
    private readonly IDecisionEventStore _store;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly AiPipelineOptions _options;

    public AiDecisionPipeline(IClaudeAnalyst? claude, IJevEvaluator? jev, IDecisionCoordinator coordinator, IDecisionEventStore store,
        IClock clock, IIdGenerator ids, AiPipelineOptions options)
    { _claude = claude; _jev = jev; _coordinator = coordinator; _store = store; _clock = clock; _ids = ids; _options = options; }

    public async Task<DecisionEvent> RunAsync(MarketSnapshotBundle snapshot, InstrumentSelection? currentSelection,
        decimal availableRiskBudget, decimal currentExposure, CancellationToken ct)
    {
        var started = _clock.UtcNow;
        var request = new ClaudeAnalysisRequest(snapshot.SnapshotId, snapshot.AccountId, snapshot.Environment, snapshot.Epic,
            snapshot.SelectionVersion, snapshot, availableRiskBudget, currentExposure);
        var prompt = ClaudePrompt.Build(request);

        var (claudeStep, claude) = await AskClaudeAsync(request, ct);
        var (jevStep, jev) = claudeStep.Status == AiStepStatus.Succeeded
            ? await AskJevAsync(snapshot, ct)
            : (NotRun(JevProvider, claudeStep.Status), (JevResult?)null);

        var decision = _coordinator.Decide(new DecisionCoordinatorInput(snapshot, claude, jev, _options.StrategyId, _options.StrategyVersion, currentSelection));
        // The coordinator already refuses a missing answer; this keeps a failed step from ever producing a trade.
        var candidate = claudeStep.Status == AiStepStatus.Succeeded && jevStep.Status == AiStepStatus.Succeeded ? decision.Candidate : DecisionCandidate.NoTrade;
        var reason = candidate != DecisionCandidate.NoTrade ? null
            : StepReason(claudeStep) ?? StepReason(jevStep) ?? decision.RejectionReason
              ?? (claudeStep.Output != jevStep.Output ? "Claude and JEV disagree" : "AI answered NO_TRADE");

        var evt = new DecisionEvent(
            _ids.NewId("aidec"), snapshot.AccountId, snapshot.Environment, snapshot.Epic, snapshot.SelectionVersion, snapshot.SnapshotId,
            snapshot.AsOfUtc, started, _clock.UtcNow, claudeStep, jevStep,
            claudeStep.Status is AiStepStatus.Disabled ? null : Sha256(prompt), claudeStep.Status is AiStepStatus.Disabled ? null : prompt.Length,
            candidate, reason, null, _options.StrategyId, _options.StrategyVersion, _ids.NewId("audit"));
        _store.Record(evt);
        return evt;
    }

    private async Task<(AiStepRecord, ClaudeAnalysisResult?)> AskClaudeAsync(ClaudeAnalysisRequest request, CancellationToken ct)
    {
        if (!_options.Enabled) return (Step(ClaudeProvider, AiStepStatus.Disabled, "AI is disabled"), null);
        if (_claude is null) return (Step(ClaudeProvider, AiStepStatus.Unavailable, "Claude client not configured"), null);
        var start = _clock.UtcNow;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.ClaudeTimeout);
            var r = await _claude.AnalyzeAsync(request, cts.Token);
            return (new AiStepRecord(ClaudeProvider, AiStepStatus.Succeeded, r.ModelId, r.Latency.TotalMilliseconds,
                // The client reports 0 when the reply carried no usage, and does not compute cost.
                r.InputTokens > 0 ? r.InputTokens : null, r.OutputTokens > 0 ? r.OutputTokens : null, r.CostUsd > 0 ? r.CostUsd : null,
                null, r.Signal, true, null), r);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return (Failure(ClaudeProvider, ex, _options.ClaudeTimeout, start), null);
        }
    }

    private async Task<(AiStepRecord, JevResult?)> AskJevAsync(MarketSnapshotBundle snapshot, CancellationToken ct)
    {
        if (_jev is null) return (Step(JevProvider, AiStepStatus.Unavailable, "JEV client not configured"), null);
        var start = _clock.UtcNow;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_options.JevTimeout);
            var state = System.Text.Json.JsonSerializer.Serialize(new { snapshot.SnapshotId, snapshot.Epic, snapshot.Quote, snapshot.Indicators });
            var r = await _jev.EvaluateAsync(new JevRequest(snapshot.SnapshotId, _options.JevModelId, state, Questions, snapshot.AccountId,
                snapshot.Epic, snapshot.Environment, snapshot.SelectionVersion), cts.Token);
            var answer = r.Answers.FirstOrDefault(a => a.QuestionName.Equals("direction", StringComparison.OrdinalIgnoreCase));
            var latency = r.Latency.TotalMilliseconds;
            // Names only: Enum.TryParse would also accept "1" as Sell.
            var name = answer?.Choice is { } c ? Enum.GetNames<DecisionCandidate>().FirstOrDefault(n => n.Equals(c.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
            if (name is null || !Enum.TryParse<DecisionCandidate>(name, out var direction))
                return (new AiStepRecord(JevProvider, AiStepStatus.Rejected, r.ModelVersion, latency, null, null, null, null, null, false,
                    "Reply has no valid direction answer"), null);
            if (r.SnapshotId != snapshot.SnapshotId || r.Epic != snapshot.Epic || r.SelectionVersion != snapshot.SelectionVersion
                || r.AccountId != snapshot.AccountId || r.Environment != snapshot.Environment)
                return (new AiStepRecord(JevProvider, AiStepStatus.Rejected, r.ModelVersion, latency, null, null, null, null, null, false,
                    "Reply names a different snapshot or selection"), null);
            return (new AiStepRecord(JevProvider, AiStepStatus.Succeeded, r.ModelVersion, latency, null, null, null, answer.Score, direction, true, null), r);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return (Failure(JevProvider, ex, _options.JevTimeout, start), null);
        }
    }

    // Fixed descriptions only: a provider message could echo the prompt, a header or a key.
    private AiStepRecord Failure(string provider, Exception ex, TimeSpan timeout, DateTimeOffset start)
    {
        var latency = (_clock.UtcNow - start).TotalMilliseconds;
        return ex switch
        {
            OperationCanceledException => new(provider, AiStepStatus.TimedOut, null, latency, null, null, null, null, null, null, $"Timed out after {timeout.TotalSeconds:0.#} s"),
            AiCredentialMissingException => new(provider, AiStepStatus.Unavailable, null, null, null, null, null, null, null, null, "API key missing"),
            AiSchemaException s => new(provider, AiStepStatus.Rejected, null, latency, null, null, null, null, null, false, s.Message),
            System.Text.Json.JsonException or KeyNotFoundException or FormatException or InvalidCastException or IndexOutOfRangeException
                => new(provider, AiStepStatus.Rejected, null, latency, null, null, null, null, null, false, "Reply does not match the schema"),
            HttpRequestException h => new(provider, AiStepStatus.Unavailable, null, latency, null, null, null, null, null, null,
                h.StatusCode is { } code ? $"Provider error (HTTP {(int)code})" : "Provider unreachable"),
            _ => new(provider, AiStepStatus.Unavailable, null, latency, null, null, null, null, null, null, "Provider error"),
        };
    }

    private static AiStepRecord Step(string provider, AiStepStatus status, string problem) =>
        new(provider, status, null, null, null, null, null, null, null, null, problem);

    private static AiStepRecord NotRun(string provider, AiStepStatus earlier) =>
        Step(provider, earlier == AiStepStatus.Disabled ? AiStepStatus.Disabled : AiStepStatus.NotRun,
            earlier == AiStepStatus.Disabled ? "AI is disabled" : "Not asked: Claude did not succeed");

    private static string? StepReason(AiStepRecord s) => s.Status switch
    {
        AiStepStatus.Succeeded or AiStepStatus.NotRun => null,
        AiStepStatus.Disabled => "AI is disabled",
        _ => $"{s.Provider} {Label(s.Status)}: {s.Problem}",
    };

    private static string Label(AiStepStatus s) => s switch
    {
        AiStepStatus.TimedOut => "timed out",
        AiStepStatus.Rejected => "reply rejected",
        _ => "unavailable",
    };

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
