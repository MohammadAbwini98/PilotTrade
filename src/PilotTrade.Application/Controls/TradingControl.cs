namespace PilotTrade.Application.Controls;

using PilotTrade.Domain;

public sealed class TradingControlState
{
    public bool KillSwitchEngaged { get; set; }
    public bool LiveArmed { get; set; }
    public DateTimeOffset? LiveArmedUntilUtc { get; set; }
    public string? SuspensionReason { get; set; }
    public bool ReconciliationComplete { get; set; }
    public TradingMode Mode { get; set; } = TradingMode.MonitorOnly;
}

public interface ITradingControl
{
    TradingControlState State { get; }
    void EngageKillSwitch();
    void DisarmLive();
}