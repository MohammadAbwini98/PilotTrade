namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Risk;

public sealed class RiskEngine : IRiskEngine
{
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
        if (inst.Status != MarketStatus.Open && inst.Status != MarketStatus.Unknown) reasons.Add($"Market not open: {inst.Status}");

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
        if (intent.StopLevel is not null && inst.MinStopDistance is not null)
        {
            var entry = intent.Side == OrderSide.Buy ? quote.Ask : quote.Bid;
            var dist = Math.Abs(entry - intent.StopLevel.Value);
            if (dist < inst.MinStopDistance.Value) reasons.Add("Stop distance below minimum");
            if (inst.MaxStopDistance is not null && dist > inst.MaxStopDistance.Value) reasons.Add("Stop distance above maximum");
        }

        // Per-trade risk
        if (preview.EstimatedRiskAccountCurrency > policy.MaxRiskPerTradeAccountCurrency)
            reasons.Add($"Per-trade risk {preview.EstimatedRiskAccountCurrency} > {policy.MaxRiskPerTradeAccountCurrency}");
        var equity = acct.Equity == 0 ? acct.Balance : acct.Equity;
        var pctRisk = equity > 0 ? preview.EstimatedRiskAccountCurrency / equity * 100m : 100m;
        if (pctRisk > policy.MaxRiskPerTradePercentEquity) reasons.Add($"Per-trade risk pct {pctRisk:F2}% > {policy.MaxRiskPerTradePercentEquity}%");

        // Daily loss
        var unrealizedLoss = request.AllPositions.Sum(p => p.UnrealizedPnL.HasValue && p.UnrealizedPnL.Value < 0 ? -p.UnrealizedPnL.Value : 0m);
        var totalLoss = Math.Abs(Math.Min(acct.RealizedPnLToday, 0m)) + unrealizedLoss;
        if (totalLoss >= policy.MaxDailyLossAccountCurrency) reasons.Add($"Daily loss {totalLoss} >= {policy.MaxDailyLossAccountCurrency}");

        // Exposure
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
        var marginUtil = acct.Equity > 0 ? (acct.MarginUsed / acct.Equity * 100m) : 0m;
        if (marginUtil > policy.MaxMarginUtilizationPercent) reasons.Add("Margin utilization exceeded");
        if (acct.FreeMargin < policy.MinFreeMarginBufferAccountCurrency) reasons.Add("Insufficient free margin buffer");

        // Modeled downside
        if (policy.MaxModeledDownsideAccountCurrency is not null && preview.EstimatedRiskAccountCurrency > policy.MaxModeledDownsideAccountCurrency.Value)
            reasons.Add("Modeled downside exceeds policy");

        // Risk policy result
        var decision = reasons.Count == 0 ? RiskDecision.Approved : RiskDecision.Rejected;
        return new RiskEvaluationResult(decision, reasons, preview.EstimatedRiskAccountCurrency, preview.EstimatedRiskAccountCurrency, now);
    }
}