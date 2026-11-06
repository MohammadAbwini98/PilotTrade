namespace PilotTrade.Domain.Execution;

// Task 02-06: read-only access to the persisted order journal, its state history and the audit events that refer to it.
// Nothing here can create, change or send an order.

// States filters on the journal state; Text matches the epic or part of an order or broker reference.
public sealed record LedgerQuery(string? Epic, IReadOnlyCollection<OrderState>? States, string? Text, int Offset, int Limit);

// Adapter is the adapter that wrote the row (Simulation or CapitalDemo); null for rows written before task 02-06.
public sealed record LedgerRow(OrderJournalEntry Entry, string? Adapter);

public sealed record OrderTransition(string CommandId, string? FromState, string ToState, DateTimeOffset OccurredUtc, string? Reason, string Adapter);

// An audit event's identity only: its details are never read here, since they can hold broker references.
public sealed record AuditReference(string Id, DateTimeOffset OccurredUtc, string Category, string Action, string Actor, bool SensitiveRedacted, string CorrelationId);

public sealed record LedgerPage(
    int Total,
    IReadOnlyList<LedgerRow> Rows,
    IReadOnlyDictionary<string, IReadOnlyList<OrderTransition>> Transitions,
    IReadOnlyList<AuditReference> Audit,
    IReadOnlyList<LedgerRow> Confirms);

public interface IOrderLedgerReader
{
    // Newest first. Transitions are returned for the rows on the page; audit references for any of their identifiers.
    // Confirms are the newest rows (up to maxConfirms) that carry a broker reference, filtered by epic only.
    Task<LedgerPage> ReadAsync(TradingEnvironment environment, string accountId, LedgerQuery query, int maxConfirms, CancellationToken ct);
}
