namespace PilotTrade.Domain.AI;

using PilotTrade.Domain;
using PilotTrade.Domain.Market;

public sealed record ClaudeAnalysisRequest(
    string SnapshotId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    int SelectionVersion,
    MarketSnapshotBundle Snapshot,
    decimal AvailableRiskBudget,
    decimal CurrentExposureSameInstrument);

public sealed record ClaudeAnalysisResult(
    string AnalysisId,
    string SnapshotId,
    string Epic,
    TradingEnvironment Environment,
    int SelectionVersion,
    string ModelId,
    string ModelVersion,
    DecisionCandidate Signal,
    string MarketRegime,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Contradictions,
    IReadOnlyList<string> EntryConditions,
    IReadOnlyList<string> InvalidationConditions,
    IReadOnlyList<string> UncertaintyNotes,
    DateTimeOffset ValidUntilUtc,
    DateTimeOffset ProducedUtc,
    int InputTokens,
    int OutputTokens,
    decimal CostUsd,
    TimeSpan Latency,
    string AccountId);

public sealed record JevQuestion(string Name, string Type, IReadOnlyList<string>? Choices);
public sealed record JevRequest(
    string SnapshotId,
    string ModelId,
    string StateJson,
    IReadOnlyList<JevQuestion> Questions,
    string AccountId,
    string Epic,
    TradingEnvironment Environment,
    int SelectionVersion);

public sealed record JevAnswer(string QuestionName, string? Choice, double? Score, string? Text);
public sealed record JevResult(
    string EvaluationId,
    string SnapshotId,
    string ModelId,
    string ModelVersion,
    IReadOnlyList<JevAnswer> Answers,
    DateTimeOffset ProducedUtc,
    TimeSpan Latency,
    string AccountId,
    string Epic,
    TradingEnvironment Environment,
    int SelectionVersion);

public interface IClaudeAnalyst
{
    Task<ClaudeAnalysisResult> AnalyzeAsync(ClaudeAnalysisRequest request, CancellationToken ct);
}

public interface IJevEvaluator
{
    Task<JevResult> EvaluateAsync(JevRequest request, CancellationToken ct);
}

/// The analysis request sent to Claude. Shared by the client and the decision record, which keeps only its SHA-256 and length.
public static class ClaudePrompt
{
    public const string System = "You are a market analyst. Respond ONLY with valid JSON matching the required schema. No prose.";

    public static string Build(ClaudeAnalysisRequest request) => global::System.Text.Json.JsonSerializer.Serialize(new
    {
        snapshotId = request.Snapshot.SnapshotId,
        instrumentEpic = request.Snapshot.Epic,
        environment = request.Snapshot.Environment.ToString().ToLowerInvariant(),
        selectionVersion = request.Snapshot.SelectionVersion,
        quote = request.Snapshot.Quote,
        indicators = request.Snapshot.Indicators,
        availableRiskBudget = request.AvailableRiskBudget,
        currentExposure = request.CurrentExposureSameInstrument
    });
}
