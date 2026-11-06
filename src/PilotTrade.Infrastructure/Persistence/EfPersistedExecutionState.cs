namespace PilotTrade.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain;
using PilotTrade.Domain.Execution;

public sealed class EfPersistedExecutionState : IPersistedExecutionState
{
    private readonly PilotTradeDbContext _db;
    public EfPersistedExecutionState(PilotTradeDbContext db) { _db = db; }

    public async Task<PersistedExecutionState?> LoadAsync(TradingEnvironment env, string accountId, CancellationToken ct)
    {
        var e = await _db.ExecutionStates.FirstOrDefaultAsync(x => x.Environment == env.ToString() && x.AccountId == accountId, ct);
        if (e is null) return null;
        return new PersistedExecutionState(env, accountId, e.KillSwitchEngaged, Enum.Parse<TradingMode>(e.Mode), e.LiveArmed, e.LiveArmedUntilUtc, e.SuspensionReason, e.UpdatedUtc,
            e.KillSwitchReason, e.KillSwitchChangedUtc, e.KillSwitchChangedBy);
    }

    public async Task SaveAsync(PersistedExecutionState state, CancellationToken ct)
    {
        var id = $"{state.Environment}:{state.AccountId}";
        var e = await _db.ExecutionStates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) { e = new ExecutionStateEntity { Id = id }; _db.ExecutionStates.Add(e); }
        e.Environment = state.Environment.ToString();
        e.AccountId = state.AccountId;
        e.KillSwitchEngaged = state.KillSwitchEngaged;
        e.Mode = state.Mode.ToString();
        e.LiveArmed = state.LiveArmed;
        e.LiveArmedUntilUtc = state.LiveArmedUntilUtc;
        e.SuspensionReason = state.SuspensionReason;
        e.UpdatedUtc = state.UpdatedUtc;
        e.KillSwitchReason = state.KillSwitchReason;
        e.KillSwitchChangedUtc = state.KillSwitchChangedUtc;
        e.KillSwitchChangedBy = state.KillSwitchChangedBy;
        await _db.SaveChangesAsync(ct);
    }
}