namespace PilotTrade.Infrastructure.Audit;

using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain;
using PilotTrade.Domain.Audit;
using PilotTrade.Infrastructure.Persistence;

public sealed class EfAuditLogger : IAuditLogger
{
    private readonly PilotTradeDbContext _db;
    public EfAuditLogger(PilotTradeDbContext db) { _db = db; }

    public async Task LogAsync(AuditEvent evt, CancellationToken ct)
    {
        _db.AuditEvents.Add(new AuditEventEntity
        {
            Id = evt.Id,
            OccurredUtc = evt.OccurredUtc,
            Category = evt.Category,
            Action = evt.Action,
            Actor = evt.Actor,
            Environment = evt.Environment,
            AccountId = evt.AccountId,
            InstrumentEpic = evt.InstrumentEpic,
            CorrelationId = evt.CorrelationId,
            DetailsJson = evt.DetailsJson,
            SensitiveRedacted = evt.SensitiveRedacted
        });
        await _db.SaveChangesAsync(ct);
    }
}