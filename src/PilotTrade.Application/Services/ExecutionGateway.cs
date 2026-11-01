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
        BrokerSubmissionResult result;
        try
        {
            entry = entry with { State = OrderState.Submitting, UpdatedUtc = _clock.UtcNow, SubmittedUtc = _clock.UtcNow };
            await _journal.UpdateAsync(entry, ct);
            lock (_lock) _submittedIntents.Add(intent.IntentId);
            result = await _broker.SubmitOrderAsync(intent, ct);
        }
        catch (Exception ex)
        {
            // Outcome unknown: journal it for reconciliation and never resubmit.
            var reason = $"Broker outcome uncertain ({ex.GetType().Name})";
            entry = entry with { State = OrderState.Unknown, RejectionReason = reason, UpdatedUtc = _clock.UtcNow };
            await TryRecordAsync(entry);
            return new ExecutionResult(intent.IntentId, OrderState.Unknown, null, null, reason, _clock.UtcNow);
        }

        // The broker has answered, so nothing past this point may lose its references or turn into a resubmission.
        var (finalState, finalReason) = Classify(result);
        var dealIds = result.AffectedDeals is { Count: > 0 } deals ? string.Join(",", deals.Select(d => d.DealId)) : result.BrokerDealId;
        entry = entry with
        {
            State = finalState,
            BrokerDealReference = result.BrokerDealReference,
            BrokerDealId = dealIds,
            RejectionReason = finalReason,
            ConfirmedUtc = finalState == OrderState.Confirmed ? result.ConfirmedUtc : null,
            UpdatedUtc = _clock.UtcNow
        };
        if (!await TryRecordAsync(entry))
        {
            // The journal still reads Submitting, which reconciliation treats as uncertain.
            Suspend("Order journal failed after a broker answer");
            return new ExecutionResult(intent.IntentId, OrderState.Unknown, result.BrokerDealReference, dealIds, "Order journal failed after a broker answer", _clock.UtcNow);
        }
        try
        {
            await _audit.LogAsync(new AuditEvent(_ids.NewId("audit"), now, "Execution", "Submit", "system", _broker.Environment.ToString().ToLowerInvariant(), intent.AccountId, intent.Epic, intent.IntentId, JsonSerializer.Serialize(result), true), CancellationToken.None);
        }
        catch (Exception)
        {
            // The order's state is already journaled and stays as it is; no further order runs without an audit trail.
            Suspend("Audit log failed after a broker answer");
        }
        return new ExecutionResult(intent.IntentId, finalState, result.BrokerDealReference, dealIds, finalReason, _clock.UtcNow);
    }

    // Only a referenced acceptance or an explicit rejection is a known outcome.
    private static (OrderState State, string? Reason) Classify(BrokerSubmissionResult result)
    {
        var referenced = !string.IsNullOrWhiteSpace(result.BrokerDealReference);
        return result.State switch
        {
            OrderState.Rejected => (OrderState.Rejected, result.RejectionReason),
            OrderState.Confirmed or OrderState.BrokerPending when referenced => (result.State, result.RejectionReason),
            OrderState.Confirmed or OrderState.BrokerPending => (OrderState.Unknown, "Broker outcome uncertain (no deal reference)"),
            _ => (OrderState.Unknown, result.RejectionReason ?? "Broker outcome uncertain")
        };
    }

    private async Task<bool> TryRecordAsync(OrderJournalEntry entry)
    {
        try { await _journal.UpdateAsync(entry, CancellationToken.None); return true; }
        catch (Exception) { return false; }
    }

    private void Suspend(string reason)
    {
        _control.EngageKillSwitch();
        _control.State.SuspensionReason = reason;
    }

    private static ExecutionResult Reject(string intentId, string reason, DateTimeOffset now)
        => new(intentId, OrderState.Rejected, null, null, reason, now);
}