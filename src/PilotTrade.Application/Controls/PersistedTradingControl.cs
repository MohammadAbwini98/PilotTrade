namespace PilotTrade.Application.Controls;

using PilotTrade.Domain;
using PilotTrade.Domain.Execution;

public sealed class PersistedTradingControl : ITradingControl
{
    private readonly IPersistedExecutionState _store;
    private readonly TradingEnvironment _env;
    private readonly string _accountId;
    private readonly IClock _clock;
    private readonly object _gate = new();

    public TradingControlState State { get; } = new();

    public PersistedTradingControl(IPersistedExecutionState store, TradingEnvironment env, string accountId, IClock clock)
    {
        _store = store; _env = env; _accountId = accountId; _clock = clock;
        var loaded = store.LoadAsync(env, accountId, CancellationToken.None).GetAwaiter().GetResult();
        if (loaded is not null)
        {
            State.KillSwitchEngaged = loaded.KillSwitchEngaged;
            State.Mode = loaded.Mode;
            State.KillSwitchReason = loaded.KillSwitchReason;
            State.KillSwitchChangedUtc = loaded.KillSwitchChangedUtc;
            State.KillSwitchChangedBy = loaded.KillSwitchChangedBy;
            // A database written before 02-05 has no kill-switch reason; its suspension reason is the closest record of it.
            if (loaded.KillSwitchEngaged && loaded.KillSwitchReason is null) State.KillSwitchReason = loaded.SuspensionReason ?? "Engaged before restart; no reason was recorded";
            if (loaded.KillSwitchEngaged) State.SuspensionReason = State.KillSwitchReason;
            // Never restore live armed across restart
            State.LiveArmed = false;
            State.LiveArmedUntilUtc = null;
            if (loaded.LiveArmed) State.SuspensionReason = "Live authorization cleared on restart";
        }
        else
        {
            State.Mode = TradingMode.MonitorOnly;
            State.LiveArmed = false;
        }
    }

    private void Persist()
    {
        var s = new PersistedExecutionState(_env, _accountId, State.KillSwitchEngaged, State.Mode, State.LiveArmed, State.LiveArmedUntilUtc, State.SuspensionReason, _clock.UtcNow,
            State.KillSwitchReason, State.KillSwitchChangedUtc, State.KillSwitchChangedBy);
        _store.SaveAsync(s, CancellationToken.None).GetAwaiter().GetResult();
    }

    public void EngageKillSwitch() => EngageKillSwitch("Kill switch engaged", "system");

    // The in-memory state is engaged before saving, so a failed save still blocks new entries in this process; the error reaches the caller.
    public bool EngageKillSwitch(string reason, string actor)
    {
        lock (_gate)
        {
            if (State.KillSwitchEngaged) return false;
            State.KillSwitchEngaged = true;
            State.KillSwitchReason = reason;
            State.KillSwitchChangedUtc = _clock.UtcNow;
            State.KillSwitchChangedBy = actor;
            State.KillSwitchVersion++;
            State.SuspensionReason = reason;
            Persist();
            return true;
        }
    }

    // Saves first: the switch is released in memory only once the release is stored.
    public bool ClearKillSwitch(string actor, long expectedVersion)
    {
        lock (_gate)
        {
            if (!State.KillSwitchEngaged || State.KillSwitchVersion != expectedVersion) return false;
            var previous = (State.KillSwitchReason, State.KillSwitchChangedUtc, State.KillSwitchChangedBy, State.SuspensionReason);
            State.KillSwitchEngaged = false;
            State.KillSwitchReason = "Cleared by the owner after the safety checks passed";
            State.KillSwitchChangedUtc = _clock.UtcNow;
            State.KillSwitchChangedBy = actor;
            if (State.SuspensionReason == previous.KillSwitchReason) State.SuspensionReason = null;
            try { Persist(); }
            catch
            {
                State.KillSwitchEngaged = true;
                (State.KillSwitchReason, State.KillSwitchChangedUtc, State.KillSwitchChangedBy, State.SuspensionReason) = previous;
                throw;
            }
            State.KillSwitchVersion++;
            return true;
        }
    }

    public void DisarmLive() { lock (_gate) { State.LiveArmed = false; State.LiveArmedUntilUtc = null; Persist(); } }
}
