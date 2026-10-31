namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Risk;

public sealed class RiskEngine : IRiskEngine
{
    private static decimal ToPriceDistance(decimal value, DistanceUnit unit, decimal price) =>
        unit == DistanceUnit.Percentage ? value / 100m * price : value;

    public RiskEvaluationResult Evaluate(RiskEvaluationRequest request)
    {
        var reasons = new List<string>();
        var policy = request.Policy;
        var acct = request.Account;
        var inst = request.Instrument;
        var quote = request.Quote;
        var intent = request.Intent;
        var preview = request.Preview;
        var now = request.NowUtc;

        // Feed freshness
        var priceAge = now - quote.SourceTimestampUtc;
        if (priceAge > policy.MaxPriceAge) reasons.Add($"Stale price: {priceAge}");
        if (quote.Health != FeedHealth.Healthy) reasons.Add($"Feed unhealthy: {quote.Health}");

        // Signal age
        if (now - intent.CreatedUtc > policy.MaxSignalAge) reasons.Add("Signal expired");

        // Instrument allow/block
        if (policy.InstrumentBlocklist.Contains(intent.Epic, StringComparer.OrdinalIgnoreCase)) reasons.Add("Instrument blocked");
        if (policy.InstrumentAllowlist.Count > 0 && !policy.InstrumentAllowlist.Contains(intent.Epic, StringComparer.OrdinalIgnoreCase)) reasons.Add("Instrument not allowlisted");

        // Market status
        if (inst.Status != MarketStatus.Open) reasons.Add($"Market not open: {inst.Status}");

        // Currency conversion
        if (!string.Equals(inst.QuoteCurrency, acct.Currency, StringComparison.OrdinalIgnoreCase))
            reasons.Add("Currency conversion unavailable");

        // Size constraints
        if (intent.Size < inst.MinDealSize) reasons.Add("Size below minimum");
        if (intent.Size > inst.MaxDealSize) reasons.Add("Size above maximum");
        if (inst.DealSizeStep > 0)
        {
            var remainder = intent.Size % inst.DealSizeStep;
            if (remainder != 0) reasons.Add("Size not in step increments");
        }

        // Spread
        if (quote.Spread > policy.MaxSpread) reasons.Add($"Spread {quote.Spread} > max {policy.MaxSpread}");

        // Protective stop
        if (policy.RequireProtectiveStop && intent.StopLevel is null) reasons.Add("Protective stop required");
        if (intent.StopLevel is not null)
        {
            var entry = intent.Side == OrderSide.Buy ? quote.Ask : quote.Bid;
            var dist = Math.Abs(entry - intent.StopLevel.Value);
            if (inst.MinStopDistance is null || inst.MaxStopDistance is null) reasons.Add("Stop rules missing");
            else
            {
                if (dist < ToPriceDistance(inst.MinStopDistance.Value, inst.MinStopDistanceUnit, entry)) reasons.Add("Stop distance below minimum");
                if (dist > ToPriceDistance(inst.MaxStopDistance.Value, inst.MaxStopDistanceUnit, entry)) reasons.Add("Stop distance above maximum");
            }
        }

        // Per-trade risk
        if (preview.EstimatedRiskAccountCurrency > policy.MaxRiskPerTradeAccountCurrency)
            reasons.Add($"Per-trade risk {preview.EstimatedRiskAccountCurrency} > {policy.MaxRiskPerTradeAccountCurrency}");
        if (acct.Equity is not > 0) reasons.Add("Equity unavailable");
        else
        {
            var pctRisk = preview.EstimatedRiskAccountCurrency / acct.Equity.Value * 100m;
            if (pctRisk > policy.MaxRiskPerTradePercentEquity) reasons.Add($"Per-trade risk pct {pctRisk:F2}% > {policy.MaxRiskPerTradePercentEquity}%");
        }

        // Daily loss
        var unrealizedLoss = request.AllPositions.Sum(p => p.UnrealizedPnL.HasValue && p.UnrealizedPnL.Value < 0 ? -p.UnrealizedPnL.Value : 0m);
        if (request.AllPositions.Any(p => p.UnrealizedPnL is null)) reasons.Add("Position P/L missing");
        if (acct.RealizedPnLToday is null) reasons.Add("Realized P/L unavailable");
        else
        {
            var totalLoss = Math.Abs(Math.Min(acct.RealizedPnLToday.Value, 0m)) + unrealizedLoss;
            if (totalLoss >= policy.MaxDailyLossAccountCurrency) reasons.Add($"Daily loss {totalLoss} >= {policy.MaxDailyLossAccountCurrency}");
        }

        // Exposure
        if (request.AllPositions.Any(p => p.OpenLevel is null)) reasons.Add("Position level missing");
        var sameEpicExposure = request.AllPositions.Where(p => p.Epic == intent.Epic).Sum(p => p.Size * (p.OpenLevel ?? 0m));
        var grossExposure = request.AllPositions.Sum(p => p.Size * (p.OpenLevel ?? 0m)) + (intent.Size * (intent.Side == OrderSide.Buy ? quote.Ask : quote.Bid));
        if (grossExposure > policy.MaxGrossExposureAccountCurrency) reasons.Add("Gross exposure exceeded");
        var netExposure = request.AllPositions.Sum(p => p.Direction == OrderSide.Buy ? p.Size * (p.OpenLevel ?? 0m) : -p.Size * (p.OpenLevel ?? 0m));
        netExposure += intent.Side == OrderSide.Buy ? intent.Size * quote.Ask : -intent.Size * quote.Bid;
        if (Math.Abs(netExposure) > policy.MaxNetExposureAccountCurrency) reasons.Add("Net exposure exceeded");
        if (sameEpicExposure + intent.Size * quote.Ask > policy.MaxPerInstrumentExposureAccountCurrency) reasons.Add("Per-instrument exposure exceeded");

        // Concurrency
        if (request.AllPositions.Count >= policy.MaxConcurrentPositions && !request.AllPositions.Any(p => p.Epic == intent.Epic)) reasons.Add("Max concurrent positions");
        if (request.AllWorkingOrders.Count >= policy.MaxPendingOrders) reasons.Add("Max pending orders");

        // Margin
        if (acct.MarginUsed is null) reasons.Add("Margin used unavailable");
        else if (acct.Equity is > 0 && acct.MarginUsed.Value / acct.Equity.Value * 100m > policy.MaxMarginUtilizationPercent) reasons.Add("Margin utilization exceeded");
        if (acct.FreeMargin is null) reasons.Add("Free margin unavailable");
        else if (acct.FreeMargin.Value < policy.MinFreeMarginBufferAccountCurrency) reasons.Add("Insufficient free margin buffer");

        // Modeled downside
        if (policy.MaxModeledDownsideAccountCurrency is not null && preview.EstimatedRiskAccountCurrency > policy.MaxModeledDownsideAccountCurrency.Value)
            reasons.Add("Modeled downside exceeds policy");

        // Risk policy result
        var decision = reasons.Count == 0 ? RiskDecision.Approved : RiskDecision.Rejected;
        return new RiskEvaluationResult(decision, reasons, preview.EstimatedRiskAccountCurrency, preview.EstimatedRiskAccountCurrency, now);
    }
}