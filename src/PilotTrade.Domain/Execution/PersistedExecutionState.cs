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
    DateTimeOffset UpdatedUtc,
    // Task 02-05: why the kill switch is in its current state, when it changed and who changed it ("owner" or "system").
    string? KillSwitchReason = null,
    DateTimeOffset? KillSwitchChangedUtc = null,
    string? KillSwitchChangedBy = null);

public interface IPersistedExecutionState
{
    Task<PersistedExecutionState?> LoadAsync(TradingEnvironment env, string accountId, CancellationToken ct);
    Task SaveAsync(PersistedExecutionState state, CancellationToken ct);
}