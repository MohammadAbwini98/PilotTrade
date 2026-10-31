namespace PilotTrade.Domain.Decision;

using PilotTrade.Domain;
using PilotTrade.Domain.AI;
using PilotTrade.Domain.Market;

public sealed record StrategyDecision(
    string DecisionId,
    string SnapshotId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    int SelectionVersion,
    string StrategyId,
    string StrategyVersion,
    DecisionCandidate Candidate,
    string Explanation,
    IReadOnlyList<string> Checks,
    DateTimeOffset DecidedUtc,
    string? ClaudeAnalysisId,
    string? JevEvaluationId,
    string? RejectionReason);

public sealed record DecisionCoordinatorInput(
    MarketSnapshotBundle Snapshot,
    ClaudeAnalysisResult? Claude,
    JevResult? Jev,
    string StrategyId,
    string StrategyVersion);

public interface IDecisionCoordinator
{
    StrategyDecision Decide(DecisionCoordinatorInput input);
}