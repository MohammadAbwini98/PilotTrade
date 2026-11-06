namespace PilotTrade.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain;
using PilotTrade.Domain.Execution;

// Task 02-06. Reads with its own short-lived context, so it never shares change tracking with the journal's writer.
public sealed class EfOrderLedgerReader : IOrderLedgerReader
{
    private readonly DbContextOptions<PilotTradeDbContext> _options;
    public EfOrderLedgerReader(DbContextOptions<PilotTradeDbContext> options) { _options = options; }

    public async Task<LedgerPage> ReadAsync(TradingEnvironment environment, string accountId, LedgerQuery query, int maxConfirms, CancellationToken ct)
    {
        await using var db = new PilotTradeDbContext(_options);
        var env = environment.ToString();
        var rows = db.OrderJournals.AsNoTracking().Where(x => x.Environment == env && x.AccountId == accountId);
        if (!string.IsNullOrWhiteSpace(query.Epic)) rows = rows.Where(x => x.Epic == query.Epic);
        if (query.States is { Count: > 0 })
        {
            var states = query.States.Select(s => s.ToString()).ToList();
            rows = rows.Where(x => states.Contains(x.State));
        }
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var t = query.Text.Trim();
            rows = rows.Where(x => x.Epic.Contains(t) || x.CommandId.Contains(t) || x.IntentId.Contains(t)
                || (x.BrokerDealReference != null && x.BrokerDealReference.Contains(t)) || (x.BrokerDealId != null && x.BrokerDealId.Contains(t)));
        }

        // SQLite cannot order by a DateTimeOffset column, so the filtered rows are ordered and paged here.
        var all = await rows.ToListAsync(ct);
        var page = all.OrderByDescending(x => x.CreatedUtc).ThenBy(x => x.CommandId, StringComparer.Ordinal)
            .Skip(query.Offset).Take(query.Limit).ToList();

        var ids = page.Select(x => x.CommandId).ToList();
        var transitions = (await db.OrderStateTransitions.AsNoTracking()
                .Where(x => x.Environment == env && x.AccountId == accountId && ids.Contains(x.CommandId)).ToListAsync(ct))
            .OrderBy(x => x.Id)
            .GroupBy(x => x.CommandId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderTransition>)g
                .Select(x => new OrderTransition(x.CommandId, x.FromState, x.ToState, x.OccurredUtc, x.Reason, x.Adapter)).ToList());

        var keys = page.SelectMany(x => new[] { x.CommandId, x.IntentId, x.CorrelationId }).Where(k => !string.IsNullOrEmpty(k)).Distinct().ToList();
        var audit = keys.Count == 0 ? new List<AuditReference>() : (await db.AuditEvents.AsNoTracking()
                .Where(a => a.AccountId == accountId && a.CorrelationId != null && keys.Contains(a.CorrelationId))
                .Select(a => new { a.Id, a.OccurredUtc, a.Category, a.Action, a.Actor, a.SensitiveRedacted, a.CorrelationId })
                .ToListAsync(ct))
            .OrderBy(a => a.OccurredUtc)
            .Select(a => new AuditReference(a.Id, a.OccurredUtc, a.Category, a.Action, a.Actor, a.SensitiveRedacted, a.CorrelationId!)).ToList();

        var confirmRows = db.OrderJournals.AsNoTracking().Where(x => x.Environment == env && x.AccountId == accountId && x.BrokerDealReference != null);
        if (!string.IsNullOrWhiteSpace(query.Epic)) confirmRows = confirmRows.Where(x => x.Epic == query.Epic);
        var confirms = (await confirmRows.ToListAsync(ct)).OrderByDescending(x => x.UpdatedUtc).Take(maxConfirms)
            .Select(e => new LedgerRow(ToEntry(e), e.Adapter)).ToList();

        return new LedgerPage(all.Count, page.Select(e => new LedgerRow(ToEntry(e), e.Adapter)).ToList(), transitions, audit, confirms);
    }

    private static OrderJournalEntry ToEntry(OrderJournalEntity e) => new(
        e.CommandId, e.IntentId, Enum.Parse<TradingEnvironment>(e.Environment), e.AccountId, e.Epic,
        Enum.TryParse<OrderState>(e.State, out var state) ? state : OrderState.Unknown, e.RequestDigest, e.DecisionSnapshotId, null,
        e.BrokerDealReference, e.BrokerDealId, e.CreatedUtc, e.SubmittedUtc, e.ConfirmedUtc,
        e.RejectionReason, e.UpdatedUtc, e.CorrelationId,
        Enum.TryParse<OrderSide>(e.Side, out var side) ? side : null, e.Size);
}
