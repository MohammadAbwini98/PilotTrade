namespace PilotTrade.Application.Services;

using System.Collections.Concurrent;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Market;

public sealed record MarketDataPolicy(TimeSpan MaxQuoteAge, decimal MaxSpreadPercent, int MinClosedCandles, int MaxCandleAgeInBars)
{
    public static MarketDataPolicy Default { get; } = new(TimeSpan.FromSeconds(60), 5m, 21, 3);
}

// Any missing or invalid input raises MarketDataRejectedException, which callers report as NO_TRADE.
public sealed class MarketDataService : IMarketDataService
{
    private readonly IBrokerAdapter _broker;
    private readonly IClock _clock;
    private readonly MarketDataPolicy _policy;
    private readonly ConcurrentDictionary<string, (FeedHealth Health, DateTimeOffset? LastQuoteUtc)> _feeds = new();

    public MarketDataService(IBrokerAdapter broker, IClock clock, MarketDataPolicy? policy = null)
    { _broker = broker; _clock = clock; _policy = policy ?? MarketDataPolicy.Default; }

    public async Task<MarketSnapshotBundle> BuildSnapshotAsync(string accountId, TradingEnvironment env, string epic, int selectionVersion, IReadOnlyList<string> timeframes, CancellationToken ct)
    {
        if (env != _broker.Environment) throw new MarketDataRejectedException("Environment mismatch");
        if (timeframes.Count == 0) throw new MarketDataRejectedException("No timeframes requested");
        try
        {
            var meta = await _broker.GetInstrumentAsync(epic, accountId, ct) ?? throw new MarketDataRejectedException("Instrument metadata missing");
            if (meta.Epic != epic) throw new MarketDataRejectedException("Instrument metadata mismatch");
            if (meta.Status != MarketStatus.Open) throw new MarketDataRejectedException($"Instrument not open: {meta.Status}");
            if (meta.MinDealSize <= 0 || meta.DealSizeStep <= 0 || meta.TickSize <= 0) throw new MarketDataRejectedException("Instrument dealing rules invalid");

            var quotes = await _broker.GetQuotesAsync(new[] { epic }, ct);
            var quote = quotes.FirstOrDefault(q => q.Epic == epic) ?? throw new MarketDataRejectedException("No quote");
            ValidateQuote(epic, quote);

            var candlesByTf = new Dictionary<string, IReadOnlyList<Candle>>();
            var indicators = new Dictionary<string, decimal>();
            foreach (var tf in timeframes)
            {
                var candles = await _broker.GetHistoryAsync(epic, tf, 200, ct);
                var closed = ValidateCandles(epic, tf, candles);
                candlesByTf[tf] = closed;
                ComputeIndicators(tf, closed, indicators);
            }

            _feeds[epic] = (FeedHealth.Healthy, quote.SourceTimestampUtc);
            var snapshotId = $"snap_{epic}_{selectionVersion}_{_clock.UtcNow.Ticks}";
            return new MarketSnapshotBundle(snapshotId, accountId, env, epic, selectionVersion, _clock.UtcNow, quote, candlesByTf, indicators);
        }
        catch (BrokerException ex)
        {
            _feeds[epic] = (FeedHealth.Disconnected, LastQuoteUtc(epic));
            throw new MarketDataRejectedException($"Broker data unavailable: {ex.Kind}");
        }
    }

    private void ValidateQuote(string epic, MarketQuote quote)
    {
        void Reject(FeedHealth health, string reason)
        {
            _feeds[epic] = (health, quote.SourceTimestampUtc);
            throw new MarketDataRejectedException(reason);
        }

        if (quote.Health != FeedHealth.Healthy) Reject(quote.Health, "Feed unhealthy");
        if (quote.Bid <= 0 || quote.Ask <= 0) Reject(FeedHealth.Unknown, "Quote price invalid");
        if (quote.Ask < quote.Bid) Reject(FeedHealth.Unknown, "Quote crossed");
        var age = _clock.UtcNow - quote.SourceTimestampUtc;
        if (age > _policy.MaxQuoteAge) Reject(FeedHealth.Stale, "Stale quote");
        if (age < -_policy.MaxQuoteAge) Reject(FeedHealth.Unknown, "Quote timestamp in the future");
        var mid = (quote.Bid + quote.Ask) / 2m;
        if ((quote.Ask - quote.Bid) / mid * 100m > _policy.MaxSpreadPercent) Reject(FeedHealth.Healthy, "Spread too wide");
    }

    private IReadOnlyList<Candle> ValidateCandles(string epic, string timeframe, IReadOnlyList<Candle> candles)
    {
        var now = _clock.UtcNow;
        Candle? previous = null;
        foreach (var c in candles)
        {
            if (c.Epic != epic || c.Timeframe != timeframe) throw new MarketDataRejectedException($"{timeframe} candle identity mismatch");
            if (c.Open <= 0 || c.High <= 0 || c.Low <= 0 || c.Close <= 0) throw new MarketDataRejectedException($"{timeframe} candle price invalid");
            if (c.High < c.Low || c.High < Math.Max(c.Open, c.Close) || c.Low > Math.Min(c.Open, c.Close)) throw new MarketDataRejectedException($"{timeframe} candle range invalid");
            if (c.CloseTimeUtc <= c.OpenTimeUtc) throw new MarketDataRejectedException($"{timeframe} candle time invalid");
            if (previous is not null && c.OpenTimeUtc <= previous.OpenTimeUtc) throw new MarketDataRejectedException($"{timeframe} candles out of order");
            if (c.OpenTimeUtc > now) throw new MarketDataRejectedException($"{timeframe} candle timestamp in the future");
            previous = c;
        }

        var closed = candles.Where(c => c.IsClosed && c.CloseTimeUtc <= now).ToList();
        if (closed.Count < _policy.MinClosedCandles) throw new MarketDataRejectedException($"{timeframe} candle history insufficient");
        var last = closed[^1];
        var bar = last.CloseTimeUtc - last.OpenTimeUtc;
        if (now - last.CloseTimeUtc > bar * _policy.MaxCandleAgeInBars) throw new MarketDataRejectedException($"{timeframe} candles stale");
        return closed;
    }

    private static void ComputeIndicators(string tf, IReadOnlyList<Candle> candles, Dictionary<string, decimal> indicators)
    {
        var closes = candles.Select(c => c.Close).ToList();
        indicators[$"{tf}.lastClose"] = closes.Last();
        indicators[$"{tf}.ema20"] = Ema(closes, 20);
        indicators[$"{tf}.rsi14"] = Rsi(closes, 14);
        indicators[$"{tf}.atr14"] = Atr(candles, 14);
    }

    private static decimal Ema(List<decimal> closes, int period)
    {
        var k = 2m / (period + 1);
        var ema = closes.Take(period).Average();
        foreach (var c in closes.Skip(period)) ema = c * k + ema * (1 - k);
        return ema;
    }

    private static decimal Rsi(List<decimal> closes, int period)
    {
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
        var trs = new List<decimal>();
        for (int i = 1; i < candles.Count; i++)
        {
            var c = candles[i]; var p = candles[i - 1];
            trs.Add(Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - p.Close), Math.Abs(c.Low - p.Close))));
        }
        return trs.TakeLast(period).Average();
    }

    public FeedHealth GetFeedHealth(string epic)
    {
        if (!_feeds.TryGetValue(epic, out var feed)) return FeedHealth.Unknown;
        if (feed.Health == FeedHealth.Healthy && (feed.LastQuoteUtc is null || _clock.UtcNow - feed.LastQuoteUtc > _policy.MaxQuoteAge)) return FeedHealth.Stale;
        return feed.Health;
    }

    public DateTimeOffset? LastQuoteUtc(string epic) => _feeds.TryGetValue(epic, out var feed) ? feed.LastQuoteUtc : null;
}
