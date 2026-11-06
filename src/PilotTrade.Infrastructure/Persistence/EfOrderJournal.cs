namespace PilotTrade.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain;
using PilotTrade.Domain.Execution;

public sealed class EfOrderJournal : IOrderJournal
{
    private readonly PilotTradeDbContext _db;
    private readonly string _adapter;

    // The adapter (Simulation or CapitalDemo) is stamped on every row and transition, so a simulated fill can never be read as a broker confirmation.
    public EfOrderJournal(PilotTradeDbContext db, string adapter = "Unknown") { _db = db; _adapter = adapter; }

    private static OrderJournalEntry ToEntry(OrderJournalEntity e) => new(
        e.CommandId, e.IntentId, Enum.Parse<TradingEnvironment>(e.Environment), e.AccountId, e.Epic,
        Enum.Parse<OrderState>(e.State), e.RequestDigest, e.DecisionSnapshotId, e.RiskAuthorizationJson,
        e.BrokerDealReference, e.BrokerDealId, e.CreatedUtc, e.SubmittedUtc, e.ConfirmedUtc,
        e.RejectionReason, e.UpdatedUtc, e.CorrelationId,
        Enum.TryParse<OrderSide>(e.Side, out var side) ? side : null, e.Size);

    private static OrderJournalEntity ToEntity(OrderJournalEntry e) => new()
    {
        CommandId = e.CommandId, IntentId = e.IntentId, Environment = e.Environment.ToString(),
        AccountId = e.AccountId, Epic = e.Epic, State = e.State.ToString(), RequestDigest = e.RequestDigest,
        DecisionSnapshotId = e.DecisionSnapshotId, RiskAuthorizationJson = e.RiskAuthorizationJson,
        BrokerDealReference = e.BrokerDealReference, BrokerDealId = e.BrokerDealId, CreatedUtc = e.CreatedUtc,
        SubmittedUtc = e.SubmittedUtc, ConfirmedUtc = e.ConfirmedUtc, RejectionReason = e.RejectionReason,
        UpdatedUtc = e.UpdatedUtc, CorrelationId = e.CorrelationId, Side = e.Side?.ToString(), Size = e.Size
    };

    private OrderStateTransitionEntity Transition(OrderJournalEntry entry, string? from) => new()
    {
        CommandId = entry.CommandId, Environment = entry.Environment.ToString(), AccountId = entry.AccountId,
        FromState = from, ToState = entry.State.ToString(), OccurredUtc = entry.UpdatedUtc,
        Reason = entry.RejectionReason is { Length: > 200 } r ? r[..200] : entry.RejectionReason, Adapter = _adapter
    };

    public async Task<OrderJournalEntry> CreateAsync(OrderJournalEntry entry, CancellationToken ct)
    {
        var entity = ToEntity(entry);
        entity.Adapter = _adapter;
        _db.OrderJournals.Add(entity);
        // The first transition is saved with the row itself, so the history can never miss the start.
        _db.OrderStateTransitions.Add(Transition(entry, null));
        await _db.SaveChangesAsync(ct);
        return entry;
    }

    public async Task UpdateAsync(OrderJournalEntry entry, CancellationToken ct)
    {
        var e = await _db.OrderJournals.FirstAsync(x => x.CommandId == entry.CommandId, ct);
        var from = e.State;
        var updated = ToEntity(entry);
        // Side, size and adapter are set once at creation; an update never clears them.
        updated.Adapter = e.Adapter;
        updated.Side ??= e.Side;
        updated.Size ??= e.Size;
        _db.Entry(e).CurrentValues.SetValues(updated);
        if (from != updated.State) _db.OrderStateTransitions.Add(Transition(entry, from));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<OrderJournalEntry?> GetAsync(string commandId, CancellationToken ct)
    {
        var e = await _db.OrderJournals.FirstOrDefaultAsync(x => x.CommandId == commandId, ct);
        return e is null ? null : ToEntry(e);
    }

    public async Task<OrderJournalEntry?> FindByIntentAsync(string intentId, CancellationToken ct)
    {
        var e = await _db.OrderJournals.FirstOrDefaultAsync(x => x.IntentId == intentId, ct);
        return e is null ? null : ToEntry(e);
    }

    public async Task<IReadOnlyList<OrderJournalEntry>> ListUncertainAsync(TradingEnvironment env, string accountId, CancellationToken ct)
    {
        // A row left in Submitting means the process stopped mid-submit, and BrokerPending has never been confirmed, so both outcomes are unknown too.
        var list = await _db.OrderJournals.Where(x => x.Environment == env.ToString() && x.AccountId == accountId && (x.State == OrderState.Unknown.ToString() || x.State == OrderState.Submitting.ToString() || x.State == OrderState.BrokerPending.ToString())).ToListAsync(ct);
        return list.Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<OrderJournalEntry>> ListAsync(TradingEnvironment env, string accountId, CancellationToken ct)
    {
        var list = await _db.OrderJournals.Where(x => x.Environment == env.ToString() && x.AccountId == accountId).ToListAsync(ct);
        return list.Select(ToEntry).ToList();
    }
}