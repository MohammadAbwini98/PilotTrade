namespace PilotTrade.Domain.Risk;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Market;

public sealed record RiskPolicy(
    string PolicyId,
    TradingEnvironment Environment,
    string AccountId,
    decimal MaxRiskPerTradeAccountCurrency,
    decimal MaxRiskPerTradePercentEquity,
    decimal MaxDailyLossAccountCurrency,
    decimal MaxGrossExposureAccountCurrency,
    decimal MaxNetExposureAccountCurrency,
    decimal MaxPerInstrumentExposureAccountCurrency,
    int MaxConcurrentPositions,
    int MaxPendingOrders,
    decimal MaxMarginUtilizationPercent,
    decimal MinFreeMarginBufferAccountCurrency,
    int MaxOrdersPerHour,
    TimeSpan TradeCooldown,
    IReadOnlyDictionary<InstrumentAssetClass, decimal> MaxSpreadPercent,
    TimeSpan MaxPriceAge,
    TimeSpan MaxSignalAge,
    decimal? MaxVolatility,
    IReadOnlyList<string> InstrumentAllowlist,
    IReadOnlyList<string> InstrumentBlocklist,
    bool RequireProtectiveStop,
    decimal? MaxModeledDownsideAccountCurrency,
    string TradingTimezone,
    DateTimeOffset EffectiveUtc);

public sealed record RiskEvaluationRequest(
    OrderIntent Intent,
    OrderPreview Preview,
    RiskPolicy Policy,
    BrokerAccount Account,
    InstrumentMetadata Instrument,
    MarketQuote Quote,
    IReadOnlyList<Position> AllPositions,
    IReadOnlyList<WorkingOrder> AllWorkingOrders,
    DateTimeOffset NowUtc);

public sealed record RiskEvaluationResult(
    RiskDecision Decision,
    IReadOnlyList<string> Reasons,
    decimal CalculatedRiskAccountCurrency,
    decimal? ModeledMaxLossAccountCurrency,
    DateTimeOffset EvaluatedUtc);

public interface IRiskEngine
{
    RiskEvaluationResult Evaluate(RiskEvaluationRequest request);
}