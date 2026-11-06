namespace PilotTrade.Domain.Risk;

using PilotTrade.Domain;
using PilotTrade.Domain.Market;

// Task 02-05: the built-in limits the Risk Center measures against. They are not editable in this build (editing is task 02-09),
// and no order path uses them yet. Amounts are in the account currency.
public static class RiskPolicyDefaults
{
    public static RiskPolicy Create(TradingEnvironment env, string accountId, TimeSpan maxPriceAge, DateTimeOffset effectiveUtc) => new(
        PolicyId: "builtin-defaults",
        Environment: env,
        AccountId: accountId,
        MaxRiskPerTradeAccountCurrency: 100m,
        MaxRiskPerTradePercentEquity: 1m,
        MaxDailyLossAccountCurrency: 500m,
        MaxGrossExposureAccountCurrency: 10_000m,
        MaxNetExposureAccountCurrency: 10_000m,
        MaxPerInstrumentExposureAccountCurrency: 5_000m,
        MaxConcurrentPositions: 5,
        MaxPendingOrders: 10,
        MaxMarginUtilizationPercent: 50m,
        MinFreeMarginBufferAccountCurrency: 100m,
        MaxOrdersPerHour: 10,
        TradeCooldown: TimeSpan.FromMinutes(5),
        MaxSpreadPercent: SpreadLimits.DefaultPercent,
        MaxPriceAge: maxPriceAge,
        MaxSignalAge: TimeSpan.FromMinutes(5),
        MaxVolatility: null,
        InstrumentAllowlist: Array.Empty<string>(),
        InstrumentBlocklist: Array.Empty<string>(),
        RequireProtectiveStop: true,
        MaxModeledDownsideAccountCurrency: 200m,
        TradingTimezone: "UTC",
        EffectiveUtc: effectiveUtc);
}
