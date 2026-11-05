namespace PilotTrade.Infrastructure.Persistence;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain;
using PilotTrade.Domain.Market;

// One row per account and environment. The list and its audit event are written in one SaveChanges, so neither exists without the other.
public sealed class EfWatchlistStore : IWatchlistStore
{
    private readonly DbContextOptions<PilotTradeDbContext> _options;
    public EfWatchlistStore(DbContextOptions<PilotTradeDbContext> options) { _options = options; }

    private static string Key(string accountId, TradingEnvironment env) => $"{env}:{accountId}";

    public Watchlist Load(string accountId, TradingEnvironment env)
    {
        using var db = new PilotTradeDbContext(_options);
        var e = db.Watchlists.AsNoTracking().FirstOrDefault(x => x.Id == Key(accountId, env));
        return e is null ? Watchlist.Empty(accountId, env) : ToDomain(e, env);
    }

    public Watchlist Save(string accountId, TradingEnvironment env, IReadOnlyList<string> epics, int expectedVersion, string updatedBy, DateTimeOffset nowUtc, string auditId)
    {
        using var db = new PilotTradeDbContext(_options);
        var id = Key(accountId, env);
        var e = db.Watchlists.FirstOrDefault(x => x.Id == id);
        var stored = e?.Version ?? 0;
        if (stored != expectedVersion) throw new WatchlistConflictException(e is null ? Watchlist.Empty(accountId, env) : ToDomain(e, env));
        var before = e is null ? Array.Empty<string>() : Read(e.EpicsJson);
        if (e is null) { e = new WatchlistEntity { Id = id, AccountId = accountId, Environment = env.ToString() }; db.Watchlists.Add(e); }
        e.EpicsJson = JsonSerializer.Serialize(epics);
        e.Version = stored + 1;
        e.UpdatedUtc = nowUtc;
        e.UpdatedBy = updatedBy;
        db.AuditEvents.Add(new AuditEventEntity
        {
            Id = auditId, OccurredUtc = nowUtc, Category = "Settings", Action = "WatchlistUpdated", Actor = updatedBy,
            Environment = env.ToString(), AccountId = accountId,
            DetailsJson = JsonSerializer.Serialize(new { version = e.Version, before, after = epics }), SensitiveRedacted = false
        });
        try { db.SaveChanges(); }
        catch (DbUpdateConcurrencyException) { throw new WatchlistConflictException(Load(accountId, env)); }
        return ToDomain(e, env);
    }

    private static string[] Read(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? Array.Empty<string>(); }
        catch (JsonException) { return Array.Empty<string>(); }
    }

    // A stored list this build cannot read is shown as empty at its stored version, never guessed at.
    private static Watchlist ToDomain(WatchlistEntity e, TradingEnvironment env)
    {
        var epics = Read(e.EpicsJson);
        if (WatchlistRules.Problem(epics) is not null) epics = Array.Empty<string>();
        return new Watchlist(e.AccountId, env, epics, e.Version, e.UpdatedUtc, e.UpdatedBy);
    }
}
