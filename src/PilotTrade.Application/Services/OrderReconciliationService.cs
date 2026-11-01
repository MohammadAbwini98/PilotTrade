namespace PilotTrade.Application.Services;

using PilotTrade.Application.Controls;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Execution;

public sealed record ReconciliationSummary(int Reconciled, int StillUncertain, int UnknownAtBroker = 0, int MissingAtBroker = 0);

// Resolves uncertain orders by reading the broker only. It has no path to submit an order.
public sealed class OrderReconciliationService
{
    private readonly IBrokerAdapter _broker;
    private readonly IOrderJournal _journal;
    private readonly IClock _clock;

    public OrderReconciliationService(IBrokerAdapter broker, IOrderJournal journal, IClock clock)
    { _broker = broker; _journal = journal; _clock = clock; }

    public async Task<ReconciliationSummary> ReconcileAsync(TradingEnvironment env, string accountId, CancellationToken ct)
    {
        int reconciled = 0, uncertain = 0;
        foreach (var entry in await _journal.ListUncertainAsync(env, accountId, ct))
        {
            if (string.IsNullOrWhiteSpace(entry.BrokerDealReference)) { uncertain++; continue; }

            BrokerSubmissionResult? confirmation;
            try { confirmation = await _broker.GetConfirmationAsync(entry.BrokerDealReference, ct); }
            catch (BrokerException) { uncertain++; continue; }

            if (confirmation is null || confirmation.State is not (OrderState.Confirmed or OrderState.Rejected)) { uncertain++; continue; }

            // One broker reference can touch several deals, so all of them are journaled.
            var dealIds = confirmation.AffectedDeals is { Count: > 0 } deals ? string.Join(",", deals.Select(d => d.DealId)) : confirmation.BrokerDealId;
            if (confirmation.State == OrderState.Confirmed && string.IsNullOrWhiteSpace(dealIds)) { uncertain++; continue; }

            await _journal.UpdateAsync(entry with
            {
                State = confirmation.State == OrderState.Rejected ? OrderState.Rejected : OrderState.Reconciled,
                BrokerDealId = dealIds,
                ConfirmedUtc = confirmation.ConfirmedUtc,
                RejectionReason = confirmation.RejectionReason,
                UpdatedUtc = _clock.UtcNow
            }, ct);
            reconciled++;
        }
        return new ReconciliationSummary(reconciled, uncertain);
    }

    // Compares what the broker holds with what the journal expects. A failed read propagates, so it can never look like an empty account.
    public async Task<ReconciliationSummary> CompareWithBrokerAsync(TradingEnvironment env, string accountId, CancellationToken ct)
    {
        var positions = await _broker.GetPositionsAsync(accountId, ct);
        var orders = await _broker.GetWorkingOrdersAsync(accountId, ct);
        var atBroker = positions.Select(p => p.PositionId).Concat(orders.Select(o => o.OrderId)).ToHashSet(StringComparer.Ordinal);

        int undealt = 0;
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in await _journal.ListAsync(env, accountId, ct))
        {
            if (entry.State is not (OrderState.Confirmed or OrderState.Reconciled)) continue;
            if (string.IsNullOrWhiteSpace(entry.BrokerDealId)) { undealt++; continue; }
            expected.UnionWith(entry.BrokerDealId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return new ReconciliationSummary(0, undealt, atBroker.Count(id => !expected.Contains(id)), expected.Count(id => !atBroker.Contains(id)));
    }

    // Only a fully matched broker and journal lets execution proceed. Anything else leaves reconciliation incomplete, which the gateway refuses.
    public async Task RunStartupAsync(ITradingControl control, TradingEnvironment env, string accountId, CancellationToken ct)
    {
        control.State.ReconciliationComplete = false;
        try
        {
            var orders = await ReconcileAsync(env, accountId, ct);
            // While the adapter cannot execute, the gateway refuses every order, so a read-only run stays incomplete and adds no broker traffic.
            if (orders.StillUncertain == 0 && !_broker.ExecutionEnabled)
            {
                control.State.SuspensionReason = "Broker holdings are not compared while execution is disabled";
                return;
            }
            var held = orders.StillUncertain > 0 ? null : await CompareWithBrokerAsync(env, accountId, ct);
            var uncertain = orders.StillUncertain + (held?.StillUncertain ?? 0);
            if (uncertain > 0)
            {
                control.EngageKillSwitch();
                control.State.SuspensionReason = $"Startup: {uncertain} uncertain orders require reconciliation";
            }
            else if (held!.UnknownAtBroker > 0 || held.MissingAtBroker > 0)
            {
                control.State.SuspensionReason = $"Startup: {held.UnknownAtBroker} broker items are not in the journal, {held.MissingAtBroker} journaled deals are not at the broker";
            }
            else control.State.ReconciliationComplete = true;
        }
        catch (BrokerException e) when (e.Kind == BrokerErrorKind.CredentialsMissing)
        {
            control.State.SuspensionReason = "Startup reconciliation needs broker credentials";
        }
        catch
        {
            control.EngageKillSwitch();
            control.State.SuspensionReason = "Startup reconciliation failed";
        }
    }
}
