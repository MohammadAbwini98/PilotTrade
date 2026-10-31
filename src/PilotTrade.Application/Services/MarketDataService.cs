namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Market;

public sealed class MarketDataService : IMarketDataService
{
    private readonly IBrokerAdapter _broker;
    private readonly IClock _clock;
    private readonly ConcurrentFeedHealth _health = new();

    public MarketDataService(IBrokerAdapter broker, IClock clock) { _broker = broker; _clock = clock; }

    public async Task<MarketSnapshotBundle> BuildSnapshotAsync(string accountId, TradingEnvironment env, string epic, int selectionVersion, IReadOnlyList<string> timeframes, CancellationToken ct)
    {
        var meta = await _broker.GetInstrumentAsync(epic, accountId, ct);
        if (meta is null) throw new InvalidOperationException("Instrument metadata missing");
        if (meta.Status != MarketStatus.Open && meta.Status != MarketStatus.Unknown) throw new InvalidOperationException($"Instrument not open: {meta.Status}");

        var quotes = await _broker.GetQuotesAsync(new[] { epic }, ct);
        var quote = quotes.FirstOrDefault() ?? throw new InvalidOperationException("No quote");
        if (quote.Health != FeedHealth.Healthy) throw new InvalidOperationException("Feed unhealthy");
        var age = _clock.UtcNow - quote.SourceTimestampUtc;
        if (age > TimeSpan.FromSeconds(60)) throw new InvalidOperationException("Stale quote");

        var candlesByTf = new Dictionary<string, IReadOnlyList<Candle>>();
        var indicators = new Dictionary<string, decimal>();
        foreach (var tf in timeframes)
        {
            var candles = await _broker.GetHistoryAsync(epic, tf, 200, ct);
            candlesByTf[tf] = candles;
            ComputeIndicators(epic, tf, candles, indicators);
        }

        var snapshotId = $"snap_{epic}_{selectionVersion}_{_clock.UtcNow.Ticks}";
        return new MarketSnapshotBundle(snapshotId, accountId, env, epic, selectionVersion, _clock.UtcNow, quote, candlesByTf, indicators);
    }

    private static void ComputeIndicators(string epic, string tf, IReadOnlyList<Candle> candles, Dictionary<string, decimal> indicators)
    {
        if (candles.Count == 0) return;
        var closes = candles.Select(c => c.Close).ToList();
        indicators[$"{tf}.lastClose"] = closes.Last();
        indicators[$"{tf}.ema20"] = Ema(closes, 20);
        indicators[$"{tf}.rsi14"] = Rsi(closes, 14);
        indicators[$"{tf}.atr14"] = Atr(candles, 14);
    }

    private static decimal Ema(List<decimal> closes, int period)
    {
        if (closes.Count < period) return closes.Last();
        var k = 2m / (period + 1);
        var ema = closes.Take(period).Average();
        foreach (var c in closes.Skip(period)) ema = c * k + ema * (1 - k);
        return ema;
    }

    private static decimal Rsi(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return 50m;
        decimal gain = 0, loss = 0;
        for (int i = closes.Count - period; i < closes.Count; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff >= 0) gain += diff; else loss -= diff;
        }
        if (loss == 0) return 100m;
        var rs = gain / loss;
        return 100m - (100m / (1 + rs));
    }

    private static decimal Atr(IReadOnlyList<Candle> candles, int period)
    {
        if (candles.Count < 2) return 0m;
        var trs = new List<decimal>();
        for (int i = 1; i < candles.Count; i++)
        {
            var c = candles[i]; var p = candles[i - 1];
            trs.Add(Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - p.Close), Math.Abs(c.Low - p.Close))));
        }
        return trs.TakeLast(period).DefaultIfEmpty(0m).Average();
    }

    public FeedHealth GetFeedHealth(string epic) => FeedHealth.Healthy;
    public DateTimeOffset? LastQuoteUtc(string epic) => _clock.UtcNow;
}

internal sealed class ConcurrentFeedHealth { }