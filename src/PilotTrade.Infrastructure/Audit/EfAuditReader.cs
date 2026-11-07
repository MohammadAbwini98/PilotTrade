namespace PilotTrade.Infrastructure.Audit;

using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain.Audit;
using PilotTrade.Infrastructure.Persistence;

// Task 02-07. Reads with its own short-lived context and never selects DetailsJson.
public sealed class EfAuditReader : IAuditReader
{
    private readonly DbContextOptions<PilotTradeDbContext> _options;
    public EfAuditReader(DbContextOptions<PilotTradeDbContext> options) { _options = options; }

    public async Task<AuditRecordPage> ReadAsync(AuditQuery query, CancellationToken ct)
    {
        await using var db = new PilotTradeDbContext(_options);
        var account = query.AccountId;
        // This account's events and system events with no account. Another account's events never match.
        var scoped = db.AuditEvents.AsNoTracking().Where(a => a.AccountId == account || a.AccountId == null);
        var categories = (await scoped.Select(a => a.Category).Distinct().ToListAsync(ct)).OrderBy(c => c, StringComparer.Ordinal).ToList();

        var rows = scoped;
        if (!string.IsNullOrWhiteSpace(query.Category)) rows = rows.Where(a => a.Category == query.Category);
        if (!string.IsNullOrWhiteSpace(query.Epic)) rows = rows.Where(a => a.InstrumentEpic == query.Epic);
        if (!string.IsNullOrWhiteSpace(query.CorrelationId)) rows = rows.Where(a => a.CorrelationId == query.CorrelationId);
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var t = query.Text.Trim();
            rows = rows.Where(a => a.Category.Contains(t) || a.Action.Contains(t) || a.Actor.Contains(t) || (a.InstrumentEpic != null && a.InstrumentEpic.Contains(t)));
        }

        // SQLite cannot compare or order DateTimeOffset columns, so only the id and time of the matching rows are read to apply
        // the period and the order; full rows (still without details) are read for the requested page alone.
        var keys = (await rows.Select(a => new { a.Id, a.OccurredUtc }).ToListAsync(ct))
            .Where(k => (query.FromUtc is null || k.OccurredUtc >= query.FromUtc) && (query.ToUtc is null || k.OccurredUtc <= query.ToUtc))
            .OrderByDescending(k => k.OccurredUtc).ThenBy(k => k.Id, StringComparer.Ordinal)
            .ToList();
        var pageIds = keys.Skip(query.Offset).Take(query.Limit).Select(k => k.Id).ToList();
        var byId = pageIds.Count == 0 ? new Dictionary<string, AuditRecord>() : (await scoped.Where(a => pageIds.Contains(a.Id))
                .Select(a => new AuditRecord(a.Id, a.OccurredUtc, a.Category, a.Action, a.Actor, a.Environment, a.AccountId, a.InstrumentEpic, a.CorrelationId, a.SensitiveRedacted))
                .ToListAsync(ct))
            .ToDictionary(a => a.Id, StringComparer.Ordinal);
        return new AuditRecordPage(keys.Count, pageIds.Where(byId.ContainsKey).Select(id => byId[id]).ToList(), categories);
    }
}

public sealed class EfDatabaseHealth : IDatabaseHealth
{
    private readonly DbContextOptions<PilotTradeDbContext> _options;
    public EfDatabaseHealth(DbContextOptions<PilotTradeDbContext> options) { _options = options; }

    public async Task<DatabaseHealthReport> CheckAsync(CancellationToken ct)
    {
        await using var db = new PilotTradeDbContext(_options);
        if (!await db.Database.CanConnectAsync(ct)) return new DatabaseHealthReport(false, 0);
        // A read, so a reachable file that cannot be queried is not reported as healthy.
        await db.AuditEvents.AsNoTracking().CountAsync(ct);
        return new DatabaseHealthReport(true, (await db.Database.GetPendingMigrationsAsync(ct)).Count());
    }
}
