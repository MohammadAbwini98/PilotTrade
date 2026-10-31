namespace PilotTrade.Domain.Broker;

using PilotTrade.Domain;

public sealed record BrokerEnvironmentConfig(
    TradingEnvironment Environment,
    string ApiBaseUri,
    string? AccountId,
    bool LiveExecutionEnabled);

public sealed record BrokerSession(
    string SessionId,
    string CstToken,
    string SecurityToken,
    DateTimeOffset ExpiresUtc,
    TradingEnvironment Environment,
    string AccountId);

public sealed record BrokerAccount(
    string AccountId,
    TradingEnvironment Environment,
    string Currency,
    decimal Balance,
    decimal Equity,
    decimal FreeMargin,
    decimal MarginUsed,
    decimal UnrealizedPnL,
    decimal RealizedPnLToday,
    DateTimeOffset AsOfUtc);

public sealed record InstrumentMetadata(
    string Epic,
    string Name,
    string Symbol,
    InstrumentAssetClass AssetClass,
    MarketStatus Status,
    string QuoteCurrency,
    string? SettlementCurrency,
    int PricePrecision,
    decimal TickSize,
    decimal MinDealSize,
    decimal MaxDealSize,
    decimal DealSizeStep,
    decimal? MarginFactor,
    decimal? Leverage,
    decimal? MinStopDistance,
    decimal? MaxStopDistance,
    bool SupportsMarketOrders,
    bool SupportsLimitOrders,
    bool SupportsStopOrders,
    IReadOnlyList<string> TradingHours,
    DateTimeOffset RetrievedUtc,
    string AccountId,
    TradingEnvironment Environment);

public sealed record MarketQuote(
    string Epic,
    decimal Bid,
    decimal Ask,
    decimal? Mid,
    decimal Spread,
    DateTimeOffset SourceTimestampUtc,
    DateTimeOffset ReceivedUtc,
    FeedHealth Health);

public sealed record Candle(
    string Epic,
    string Timeframe,
    DateTimeOffset OpenTimeUtc,
    DateTimeOffset CloseTimeUtc,
    bool IsClosed,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal? Volume);

public sealed record Position(
    string PositionId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    OrderSide Direction,
    decimal Size,
    decimal? OpenLevel,
    decimal? CurrentLevel,
    decimal? UnrealizedPnL,
    decimal? StopLevel,
    decimal? ProfitLevel,
    DateTimeOffset OpenedUtc,
    DateTimeOffset AsOfUtc);

public sealed record WorkingOrder(
    string OrderId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    OrderSide Direction,
    OrderType Type,
    decimal Size,
    decimal? Level,
    decimal? StopLevel,
    decimal? ProfitLevel,
    DateTimeOffset CreatedUtc,
    DateTimeOffset AsOfUtc);

public sealed record OrderIntent(
    string IntentId,
    string AccountId,
    TradingEnvironment Environment,
    string Epic,
    OrderSide Side,
    OrderType Type,
    decimal Size,
    decimal? LimitLevel,
    decimal? StopLevel,
    decimal? TakeProfitLevel,
    string? ClientReference,
    DateTimeOffset CreatedUtc,
    string SelectionVersion,
    string SnapshotId,
    string StrategyVersion);

public sealed record BrokerSubmissionResult(
    string IntentId,
    string? BrokerDealReference,
    string? BrokerDealId,
    OrderState State,
    string? RejectionReason,
    DateTimeOffset SubmittedUtc,
    DateTimeOffset? ConfirmedUtc);

public sealed record OrderPreview(
    OrderIntent Intent,
    decimal EstimatedEntryPrice,
    decimal EstimatedRiskAccountCurrency,
    decimal EstimatedMargin,
    decimal EstimatedFees,
    decimal? EstimatedRewardRiskRatio,
    RiskDecision RiskDecision,
    IReadOnlyList<string> RejectionReasons,
    DateTimeOffset PreviewedUtc);

public interface IBrokerAdapter
{
    TradingEnvironment Environment { get; }
    Task<BrokerSession> CreateSessionAsync(CancellationToken ct);
    Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct);
    Task<IReadOnlyList<InstrumentMetadata>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct);
    Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct);
    Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct);
    Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct);
    Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct);
    Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct);
    Task<BrokerSubmissionResult?> GetConfirmationAsync(string brokerDealReference, CancellationToken ct);
    Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct);
    Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct);
    Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct);
}

public interface IBrokerSessionFactory
{
    IBrokerAdapter Create(TradingEnvironment environment);
}