namespace PilotTrade.Domain.Audit;

using PilotTrade.Domain;

public interface IAuditLogger
{
    Task LogAsync(AuditEvent evt, CancellationToken ct);
}