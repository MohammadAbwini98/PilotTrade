namespace PilotTrade.Application.Controls;

using PilotTrade.Domain;

// What the last startup reconciliation found. Only Complete and NotCompared leave nothing unresolved; NotCompared means the
// journal had no uncertain order and broker holdings were not compared because execution is disabled.
public enum ReconciliationOutcome { NotRun, Complete, NotCompared, Uncertain, Mismatch, CredentialsMissing, Failed }

public sealed class TradingControlState
{
    public bool KillSwitchEngaged { get; set; }
    public bool LiveArmed { get; set; }
    public DateTimeOffset? LiveArmedUntilUtc { get; set; }
    public string? SuspensionReason { get; set; }
    public bool ReconciliationComplete { get; set; }
    public ReconciliationOutcome Reconciliation { get; set; } = ReconciliationOutcome.NotRun;
    public TradingMode Mode { get; set; } = TradingMode.MonitorOnly;
    // Task 02-05: the persisted reason for the kill switch's current state, and when and by whom it last changed.
    public string? KillSwitchReason { get; set; }
    public DateTimeOffset? KillSwitchChangedUtc { get; set; }
    public string? KillSwitchChangedBy { get; set; }
    // Bumped on every kill-switch change, so a clear that checked an older state cannot undo a newer engage.
    public long KillSwitchVersion { get; set; }
}

public interface ITradingControl
{
    TradingControlState State { get; }
    void EngageKillSwitch();
    // Returns false when the switch was already engaged; the first reason is kept.
    bool EngageKillSwitch(string reason, string actor);
    // Clears only if no kill-switch change happened since expectedVersion. Safety preconditions are checked by KillSwitchService.
    bool ClearKillSwitch(string actor, long expectedVersion);
    void DisarmLive();
}
