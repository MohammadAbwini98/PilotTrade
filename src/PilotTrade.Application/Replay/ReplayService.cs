namespace PilotTrade.Application.Replay;

using PilotTrade.Domain;
using PilotTrade.Domain.Market;

public sealed record ReplayResult(int TotalBars, int Decisions, int NoTradeCount, decimal NetPnL, decimal MaxDrawdown);

public sealed class ReplayService
{
    public ReplayResult Run(IReadOnlyList<Candle> candles)
    {
        // Skeleton: count completed bars; no look-ahead; real metrics require cost model.
        var closed = candles.Where(c => c.IsClosed).ToList();
        return new ReplayResult(closed.Count, 0, 0, 0m, 0m);
    }
}