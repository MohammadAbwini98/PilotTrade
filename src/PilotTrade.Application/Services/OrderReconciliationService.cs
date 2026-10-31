namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Execution;

public sealed record ReconciliationSummary(int Reconciled, int StillUncertain);

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

            if (confirmation is null || confirmation.State == OrderState.Unknown) { uncertain++; continue; }

            // One broker reference can touch several deals, so all of them are journaled.
            var dealIds = confirmation.AffectedDeals is { Count: > 0 } deals ? string.Join(",", deals.Select(d => d.DealId)) : confirmation.BrokerDealId;
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
}
