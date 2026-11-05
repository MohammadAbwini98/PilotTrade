namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Market;

public sealed record WatchQuote(decimal Bid, decimal Ask, decimal SpreadPercent, DateTimeOffset SourceTimestampUtc, double AgeSeconds, string Health);

public sealed record WatchDealing(int PricePrecision, decimal TickSize, decimal MinDealSize, decimal MaxDealSize, decimal DealSizeStep,
    decimal? MinStopDistance, decimal? MaxStopDistance, DistanceUnit MinStopDistanceUnit, DistanceUnit MaxStopDistanceUnit,
    bool SupportsMarketOrders, bool SupportsLimitOrders, bool SupportsStopOrders, IReadOnlyList<string> TradingHours);

// One monitored instrument. QuoteChecksPassed says the quote would pass the checks a snapshot applies; candle checks
// happen only when a snapshot is built. Nothing here is a trade signal.
public sealed record WatchRow(string Epic, string? Name, string AssetClass, string MarketStatus, WatchQuote? Quote, decimal? MaxSpreadPercent,
    WatchDealing? Dealing, bool QuoteChecksPassed, IReadOnlyList<string> Blockers);

public sealed record MarketWatch(DateTimeOffset CheckedUtc, double MaxQuoteAgeSeconds, IReadOnlyList<WatchRow> Rows);

// Read-only: metadata per instrument and one batched quote request. It never touches selections, snapshots or orders.
public sealed class MarketWatchService
{
    private readonly IBrokerAdapter _broker;
    private readonly IClock _clock;
    private readonly MarketDataPolicy _policy;

    public MarketWatchService(IBrokerAdapter broker, IClock clock, MarketDataPolicy policy) { _broker = broker; _clock = clock; _policy = policy; }

    public async Task<MarketWatch> BuildAsync(string accountId, IReadOnlyList<string> epics, CancellationToken ct)
    {
        var metadata = new Dictionary<string, (InstrumentMetadata? Meta, string? Error)>();
        foreach (var epic in epics)
        {
            try { metadata[epic] = (await _broker.GetInstrumentAsync(epic, accountId, ct), null); }
            catch (BrokerException ex) { metadata[epic] = (null, $"Instrument metadata unavailable: {ex.Kind}"); }
        }

        IReadOnlyList<MarketQuote> quotes = Array.Empty<MarketQuote>();
        string? quoteError = null;
        if (epics.Count > 0)
        {
            try { quotes = await _broker.GetQuotesAsync(epics, ct); }
            catch (BrokerException ex) { quoteError = $"Broker data unavailable: {ex.Kind}"; }
        }

        var now = _clock.UtcNow;
        var rows = epics.Select(epic => Row(epic, metadata[epic], quotes.FirstOrDefault(q => q.Epic == epic), quoteError, now)).ToList();
        return new MarketWatch(now, _policy.MaxQuoteAge.TotalSeconds, rows);
    }

    private WatchRow Row(string epic, (InstrumentMetadata? Meta, string? Error) md, MarketQuote? quote, string? quoteError, DateTimeOffset now)
    {
        var blockers = new List<string>();
        var meta = md.Meta;
        if (md.Error is not null) blockers.Add(md.Error);
        else if (meta is null) blockers.Add("Instrument metadata missing");
        else if (meta.Epic != epic) { blockers.Add("Instrument metadata mismatch"); meta = null; }

        decimal? maxSpread = null;
        if (meta is not null)
        {
            if (meta.Status != Domain.MarketStatus.Open) blockers.Add($"Instrument not open: {meta.Status}");
            if (meta.MinDealSize <= 0 || meta.DealSizeStep <= 0 || meta.TickSize <= 0) blockers.Add("Instrument dealing rules invalid");
            if (_policy.MaxSpreadPercent.TryGetValue(meta.AssetClass, out var limit)) maxSpread = limit;
            else blockers.Add("Spread limit unknown");
        }

        WatchQuote? watchQuote = null;
        if (quoteError is not null) blockers.Add(quoteError);
        else if (quote is null) blockers.Add("No quote");
        else if (quote.Bid <= 0 || quote.Ask <= 0) blockers.Add("Quote price invalid");
        else if (quote.Ask < quote.Bid) blockers.Add("Quote crossed");
        else
        {
            var timestampHealth = _policy.FeedHealthAt(quote.SourceTimestampUtc, now);
            var health = timestampHealth != FeedHealth.Healthy ? timestampHealth : quote.Health;
            var spread = SpreadLimits.PercentOfMid(quote.Bid, quote.Ask);
            watchQuote = new WatchQuote(quote.Bid, quote.Ask, spread, quote.SourceTimestampUtc, (now - quote.SourceTimestampUtc).TotalSeconds, health.ToString());
            if (timestampHealth == FeedHealth.Stale) blockers.Add("Stale quote");
            else if (timestampHealth != FeedHealth.Healthy) blockers.Add("Quote timestamp in the future");
            else if (quote.Health != FeedHealth.Healthy) blockers.Add("Feed unhealthy");
            if (maxSpread is { } max && spread > max) blockers.Add("Spread too wide");
        }

        var dealing = meta is null ? null : new WatchDealing(meta.PricePrecision, meta.TickSize, meta.MinDealSize, meta.MaxDealSize, meta.DealSizeStep,
            meta.MinStopDistance, meta.MaxStopDistance, meta.MinStopDistanceUnit, meta.MaxStopDistanceUnit,
            meta.SupportsMarketOrders, meta.SupportsLimitOrders, meta.SupportsStopOrders, meta.TradingHours);
        return new WatchRow(epic, meta?.Name, (meta?.AssetClass ?? InstrumentAssetClass.Unknown).ToString(), (meta?.Status ?? Domain.MarketStatus.Unknown).ToString(),
            watchQuote, maxSpread, dealing, blockers.Count == 0, blockers);
    }
}
