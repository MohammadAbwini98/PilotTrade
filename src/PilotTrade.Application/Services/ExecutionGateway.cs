namespace PilotTrade.Application.Services;

using PilotTrade.Application.Controls;
using PilotTrade.Domain;
using PilotTrade.Domain.Audit;
using PilotTrade.Domain.Broker;
using System.Text.Json;
using PilotTrade.Domain.Execution;

public sealed class ExecutionGateway : IExecutionGateway
{
    private readonly IBrokerAdapter _broker;
    private readonly ITradingControl _control;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly HashSet<string> _submittedIntents = new();
    private readonly object _lock = new();

    public ExecutionGateway(IBrokerAdapter broker, ITradingControl control, IAuditLogger audit, IClock clock, IIdGenerator ids)
    { _broker = broker; _control = control; _audit = audit; _clock = clock; _ids = ids; }

    public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var intent = request.Intent;

        // Kill switch
        if (_control.State.KillSwitchEngaged)
            return Reject(intent.IntentId, "Kill switch engaged", now);

        // Environment/mode checks
        if (request.Intent.Environment != _broker.Environment)
            return Reject(intent.IntentId, "Environment mismatch", now);

        if (_broker.Environment == TradingEnvironment.Live)
        {
            if (request.Mode != TradingMode.LiveArmed || !request.LiveArmed || !_control.State.LiveArmed)
                return Reject(intent.IntentId, "Live not armed", now);
            if (_control.State.LiveArmedUntilUtc is not null && now > _control.State.LiveArmedUntilUtc)
                return Reject(intent.IntentId, "Live authorization expired", now);
        }
        else
        {
            if (request.Mode != TradingMode.DemoAutomated)
                return Reject(intent.IntentId, "Demo automation not enabled", now);
        }

        if (request.Risk.Decision != RiskDecision.Approved)
            return Reject(intent.IntentId, $"Risk rejected: {string.Join(";", request.Risk.Reasons)}", now);

        // Idempotency
        lock (_lock) { if (_submittedIntents.Contains(intent.IntentId)) return new ExecutionResult(intent.IntentId, OrderState.Confirmed, null, null, "Duplicate intent suppressed", now); }

        // Submit
        try
        {
            lock (_lock) _submittedIntents.Add(intent.IntentId);
            var result = await _broker.SubmitOrderAsync(intent, ct);
            // If unknown/ambiguous, do not retry
            await _audit.LogAsync(new AuditEvent(_ids.NewId("audit"), now, "Execution", "Submit", "system", _broker.Environment.ToString().ToLowerInvariant(), intent.AccountId, intent.Epic, intent.IntentId, JsonSerializer.Serialize(result), true), ct);
            return new ExecutionResult(intent.IntentId, result.State, result.BrokerDealReference, result.BrokerDealId, result.RejectionReason, _clock.UtcNow);
        }
        catch (Exception ex)
        {
            // Unknown state: do not retry; mark unknown
            return new ExecutionResult(intent.IntentId, OrderState.Unknown, null, null, ex.Message, _clock.UtcNow);
        }
    }

    private static ExecutionResult Reject(string intentId, string reason, DateTimeOffset now)
        => new(intentId, OrderState.Rejected, null, null, reason, now);
}