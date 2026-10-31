namespace PilotTrade.Domain.Market;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

public sealed record InstrumentSelection(
    string SelectionId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    int SelectionVersion,
    DateTimeOffset SelectedUtc,
    string SelectedBy);

public sealed record FeatureSnapshot(
    string SnapshotId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    int SelectionVersion,
    string Timeframe,
    DateTimeOffset AsOfUtc,
    DateTimeOffset CreatedUtc,
    IReadOnlyDictionary<string, decimal> Indicators,
    IReadOnlyList<Candle> ClosedCandles,
    MarketQuote Quote);

public sealed record MarketSnapshotBundle(
    string SnapshotId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    int SelectionVersion,
    DateTimeOffset AsOfUtc,
    MarketQuote Quote,
    IReadOnlyDictionary<string, IReadOnlyList<Candle>> CandlesByTimeframe,
    IReadOnlyDictionary<string, decimal> Indicators);

public interface IInstrumentCatalog
{
    Task<IReadOnlyList<InstrumentMetadata>> SearchAsync(string query, int limit, CancellationToken ct);
    Task<InstrumentMetadata?> ResolveAsync(string epic, string accountId, TradingEnvironment env, CancellationToken ct);
}

public interface IMarketDataService
{
    Task<MarketSnapshotBundle> BuildSnapshotAsync(string accountId, TradingEnvironment env, string epic, int selectionVersion, IReadOnlyList<string> timeframes, CancellationToken ct);
    FeedHealth GetFeedHealth(string epic);
    DateTimeOffset? LastQuoteUtc(string epic);
}