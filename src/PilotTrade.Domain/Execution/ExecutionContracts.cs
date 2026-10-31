namespace PilotTrade.Domain.Execution;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Risk;

public sealed record ExecutionRequest(
    OrderIntent Intent,
    OrderPreview Preview,
    RiskEvaluationResult Risk,
    TradingMode Mode,
    bool LiveArmed);

public sealed record ExecutionResult(
    string IntentId,
    OrderState FinalState,
    string? BrokerDealReference,
    string? BrokerDealId,
    string? Error,
    DateTimeOffset CompletedUtc);

public interface IExecutionGateway
{
    Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken ct);
}

public interface IPositionGuardian
{
    Task MonitorAsync(string accountId, TradingEnvironment env, CancellationToken ct);
}