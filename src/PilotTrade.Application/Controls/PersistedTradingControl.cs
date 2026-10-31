namespace PilotTrade.Application.Controls;

using PilotTrade.Domain;
using PilotTrade.Domain.Execution;

public sealed class PersistedTradingControl : ITradingControl
{
    private readonly IPersistedExecutionState _store;
    private readonly TradingEnvironment _env;
    private readonly string _accountId;
    private readonly IClock _clock;

    public TradingControlState State { get; } = new();

    public PersistedTradingControl(IPersistedExecutionState store, TradingEnvironment env, string accountId, IClock clock)
    {
        _store = store; _env = env; _accountId = accountId; _clock = clock;
        var loaded = store.LoadAsync(env, accountId, CancellationToken.None).GetAwaiter().GetResult();
        if (loaded is not null)
        {
            State.KillSwitchEngaged = loaded.KillSwitchEngaged;
            State.Mode = loaded.Mode;
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
        var s = new PersistedExecutionState(_env, _accountId, State.KillSwitchEngaged, State.Mode, State.LiveArmed, State.LiveArmedUntilUtc, State.SuspensionReason, _clock.UtcNow);
        _store.SaveAsync(s, CancellationToken.None).GetAwaiter().GetResult();
    }

    public void EngageKillSwitch() { State.KillSwitchEngaged = true; State.SuspensionReason = "Kill switch engaged"; Persist(); }
    public void DisarmLive() { State.LiveArmed = false; State.LiveArmedUntilUtc = null; Persist(); }
}