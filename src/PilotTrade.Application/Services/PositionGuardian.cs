namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

public sealed class PositionGuardian : IPositionGuardian
{
    private readonly IBrokerAdapter _broker;
    private readonly IClock _clock;
    public PositionGuardian(IBrokerAdapter broker, IClock clock) { _broker = broker; _clock = clock; }

    public async Task MonitorAsync(string accountId, TradingEnvironment env, CancellationToken ct)
    {
        var positions = await _broker.GetPositionsAsync(accountId, ct);
        var orders = await _broker.GetWorkingOrdersAsync(accountId, ct);
        // Detect positions missing protective stops
        foreach (var p in positions)
        {
            if (p.StopLevel is null)
            {
                // Would raise alert; logged via audit in host
            }
        }
        _ = orders;
    }
}