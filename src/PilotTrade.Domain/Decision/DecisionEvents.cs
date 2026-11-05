namespace PilotTrade.Domain.Decision;

using PilotTrade.Domain;

// Task 02-04: one auditable record per AI decision attempt. It holds metadata only:
// prompt text, model output text and provider error messages are never stored.

/// What happened at one AI step. NotRun means an earlier step failed, so this one was not asked.
public enum AiStepStatus { Disabled, Unavailable, TimedOut, Rejected, Succeeded, NotRun }

public sealed record AiStepRecord(
    string Provider,
    AiStepStatus Status,
    string? Model,
    double? LatencyMs,
    int? InputTokens,
    int? OutputTokens,
    // Null when the provider or client does not report it; never filled with an estimate.
    decimal? CostUsd,
    double? Confidence,
    // The step's own answer (Claude signal, JEV direction), only when the step succeeded.
    DecisionCandidate? Output,
    // Null when no reply was parsed (disabled, unavailable, timed out, not run).
    bool? SchemaValid,
    // A fixed, safe description such as "Timed out after 20 s"; never a raw provider message.
    string? Problem);

public sealed record DecisionEvent(
    string DecisionId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    int SelectionVersion,
    string SnapshotId,
    DateTimeOffset SnapshotAsOfUtc,
    DateTimeOffset StartedUtc,
    DateTimeOffset RecordedUtc,
    AiStepRecord Claude,
    AiStepRecord Jev,
    // SHA-256 of the analysis request sent to Claude and its length; the text itself is not kept.
    string? PromptSha256,
    int? PromptChars,
    DecisionCandidate Decision,
    string? NoTradeReason,
    // Null: no risk check ran. Decisions are not turned into orders in this build.
    string? RiskVeto,
    string StrategyId,
    string StrategyVersion,
    string AuditEventId);

public sealed record DecisionQuery(
    string AccountId,
    TradingEnvironment Environment,
    string? Epic = null,
    DecisionCandidate? Decision = null,
    AiStepStatus? Status = null,
    int Limit = 50);

public sealed record DecisionPage(int Total, IReadOnlyList<DecisionEvent> Items);

public interface IDecisionEventStore
{
    /// Stores the event and its audit event together; neither exists without the other.
    void Record(DecisionEvent evt);
    DecisionPage List(DecisionQuery query);
    DecisionEvent? Get(string accountId, TradingEnvironment env, string decisionId);
}

/// The AI client could not read its API key. The key itself is never in the message.
public sealed class AiCredentialMissingException(string provider) : InvalidOperationException($"{provider} API key missing");

/// The reply did not match the required schema or named a different snapshot, instrument, environment or selection.
public sealed class AiSchemaException(string problem) : InvalidOperationException(problem);
