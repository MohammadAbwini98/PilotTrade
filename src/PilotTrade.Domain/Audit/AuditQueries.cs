namespace PilotTrade.Domain.Audit;

// Task 02-07. Read-only access to the audit log. A record never carries DetailsJson: the details may name protected values,
// so they are not read for display at all.
public sealed record AuditQuery(
    string AccountId,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string? Category = null,
    string? Epic = null,
    string? CorrelationId = null,
    string? Text = null,
    int Offset = 0,
    int Limit = 50);

// AccountId is null for a system event that belongs to no account (for example a Demo connection check).
public sealed record AuditRecord(
    string Id, DateTimeOffset OccurredUtc, string Category, string Action, string Actor, string Environment,
    string? AccountId, string? Epic, string? CorrelationId, bool SensitiveRedacted);

public sealed record AuditRecordPage(int Total, IReadOnlyList<AuditRecord> Items, IReadOnlyList<string> Categories);

public interface IAuditReader
{
    // Returns the configured account's events and system events with no account; never another account's.
    Task<AuditRecordPage> ReadAsync(AuditQuery query, CancellationToken ct);
}

public sealed record DatabaseHealthReport(bool Reachable, int PendingMigrations);

public interface IDatabaseHealth
{
    Task<DatabaseHealthReport> CheckAsync(CancellationToken ct);
}
