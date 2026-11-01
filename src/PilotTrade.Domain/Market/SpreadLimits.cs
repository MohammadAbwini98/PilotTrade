namespace PilotTrade.Domain.Market;

using PilotTrade.Domain;

// A flat spread limit cannot fit both EURUSD and BTCUSD, so limits are a percentage of mid per asset class.
// A class that is not listed has no limit and is refused. These defaults are the owner's to change.
public static class SpreadLimits
{
    public static IReadOnlyDictionary<InstrumentAssetClass, decimal> DefaultPercent { get; } = new Dictionary<InstrumentAssetClass, decimal>
    {
        [InstrumentAssetClass.Fx] = 0.05m,
        [InstrumentAssetClass.Commodity] = 0.2m,
        [InstrumentAssetClass.Index] = 0.2m,
        [InstrumentAssetClass.CryptoCfd] = 1m,
        [InstrumentAssetClass.Share] = 0.5m,
        [InstrumentAssetClass.Etf] = 0.5m
    };

    public static decimal PercentOfMid(decimal bid, decimal ask) => (ask - bid) / ((bid + ask) / 2m) * 100m;
}
