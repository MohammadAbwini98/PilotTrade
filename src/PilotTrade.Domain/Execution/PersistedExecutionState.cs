namespace PilotTrade.Domain.Execution;

using PilotTrade.Domain;

public sealed record PersistedExecutionState(
    TradingEnvironment Environment,
    string AccountId,
    bool KillSwitchEngaged,
    TradingMode Mode,
    bool LiveArmed,
    DateTimeOffset? LiveArmedUntilUtc,
    string? SuspensionReason,
    DateTimeOffset UpdatedUtc);

public interface IPersistedExecutionState
{
    Task<PersistedExecutionState?> LoadAsync(TradingEnvironment env, string accountId, CancellationToken ct);
    Task SaveAsync(PersistedExecutionState state, CancellationToken ct);
}