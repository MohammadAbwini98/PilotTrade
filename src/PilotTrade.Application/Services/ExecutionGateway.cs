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
    private readonly IOrderJournal _journal;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly HashSet<string> _submittedIntents = new();
    private readonly object _lock = new();

    public ExecutionGateway(IBrokerAdapter broker, ITradingControl control, IAuditLogger audit, IOrderJournal journal, IClock clock, IIdGenerator ids)
    { _broker = broker; _control = control; _audit = audit; _journal = journal; _clock = clock; _ids = ids; }

    public async Task<ExecutionResult> ExecuteAsync(ExecutionRequest request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var intent = request.Intent;

        // Adapter-level lock: Capital.com execution stays off, and nothing is journaled because nothing is sent.
        if (!_broker.ExecutionEnabled)
            return Reject(intent.IntentId, "Broker execution disabled", now);

        // Kill switch
        if (_control.State.KillSwitchEngaged)
            return Reject(intent.IntentId, "Kill switch engaged", now);
        if (!_control.State.ReconciliationComplete)
            return Reject(intent.IntentId, "Startup reconciliation incomplete", now);

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

        // Idempotency: if already journaled/submitted, return existing
        var existing = await _journal.FindByIntentAsync(intent.IntentId, ct);
        if (existing is not null)
        {
            return new ExecutionResult(intent.IntentId, existing.State, existing.BrokerDealReference, existing.BrokerDealId, existing.RejectionReason ?? "Duplicate intent suppressed", _clock.UtcNow);
        }

        var commandId = _ids.NewId("cmd");
        var digest = intent.IntentId; // simplified digest
        var entry = new OrderJournalEntry(commandId, intent.IntentId, _broker.Environment, intent.AccountId, intent.Epic,
            OrderState.Proposed, digest, intent.SnapshotId, null, null, null, now, null, null, null, now, intent.IntentId);
        await _journal.CreateAsync(entry, ct);

        // Submit
        try
        {
            entry = entry with { State = OrderState.Submitting, UpdatedUtc = _clock.UtcNow, SubmittedUtc = _clock.UtcNow };
            await _journal.UpdateAsync(entry, ct);
            lock (_lock) _submittedIntents.Add(intent.IntentId);
            var result = await _broker.SubmitOrderAsync(intent, ct);
            var finalState = result.State;
            entry = entry with
            {
                State = finalState,
                BrokerDealReference = result.BrokerDealReference,
                BrokerDealId = result.BrokerDealId,
                RejectionReason = result.RejectionReason,
                ConfirmedUtc = result.ConfirmedUtc,
                UpdatedUtc = _clock.UtcNow
            };
            await _journal.UpdateAsync(entry, ct);
            await _audit.LogAsync(new AuditEvent(_ids.NewId("audit"), now, "Execution", "Submit", "system", _broker.Environment.ToString().ToLowerInvariant(), intent.AccountId, intent.Epic, intent.IntentId, JsonSerializer.Serialize(result), true), ct);
            return new ExecutionResult(intent.IntentId, finalState, result.BrokerDealReference, result.BrokerDealId, result.RejectionReason, _clock.UtcNow);
        }
        catch (Exception ex)
        {
            // Outcome unknown: journal it for reconciliation and never resubmit.
            var reason = $"Broker outcome uncertain ({ex.GetType().Name})";
            entry = entry with { State = OrderState.Unknown, RejectionReason = reason, UpdatedUtc = _clock.UtcNow };
            await _journal.UpdateAsync(entry, CancellationToken.None);
            return new ExecutionResult(intent.IntentId, OrderState.Unknown, null, null, reason, _clock.UtcNow);
        }
    }

    private static ExecutionResult Reject(string intentId, string reason, DateTimeOffset now)
        => new(intentId, OrderState.Rejected, null, null, reason, now);
}