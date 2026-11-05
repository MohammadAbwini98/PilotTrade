namespace PilotTrade.Infrastructure.Persistence;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain;
using PilotTrade.Domain.Decision;

// Task 02-04: the decision event and its audit event are written in one SaveChanges, so neither exists without the other.
public sealed class EfDecisionEventStore : IDecisionEventStore
{
    private readonly DbContextOptions<PilotTradeDbContext> _options;
    public EfDecisionEventStore(DbContextOptions<PilotTradeDbContext> options) { _options = options; }

    public void Record(DecisionEvent evt)
    {
        using var db = new PilotTradeDbContext(_options);
        db.DecisionEvents.Add(new DecisionEventEntity
        {
            DecisionId = evt.DecisionId, AccountId = evt.AccountId, Environment = evt.Environment.ToString(), Epic = evt.Epic,
            SelectionVersion = evt.SelectionVersion, SnapshotId = evt.SnapshotId, SnapshotAsOfUtc = evt.SnapshotAsOfUtc,
            StartedUtc = evt.StartedUtc, RecordedUtc = evt.RecordedUtc, RecordedTicks = evt.RecordedUtc.UtcTicks,
            ClaudeStatus = evt.Claude.Status.ToString(), JevStatus = evt.Jev.Status.ToString(),
            ClaudeJson = JsonSerializer.Serialize(evt.Claude), JevJson = JsonSerializer.Serialize(evt.Jev),
            PromptSha256 = evt.PromptSha256, PromptChars = evt.PromptChars, Decision = evt.Decision.ToString(),
            NoTradeReason = evt.NoTradeReason, RiskVeto = evt.RiskVeto, StrategyId = evt.StrategyId, StrategyVersion = evt.StrategyVersion,
            AuditEventId = evt.AuditEventId,
        });
        db.AuditEvents.Add(new AuditEventEntity
        {
            Id = evt.AuditEventId, OccurredUtc = evt.RecordedUtc, Category = "Decision", Action = "AiDecisionRecorded", Actor = "system",
            Environment = evt.Environment.ToString(), AccountId = evt.AccountId, InstrumentEpic = evt.Epic, CorrelationId = evt.DecisionId,
            DetailsJson = JsonSerializer.Serialize(new
            {
                decisionId = evt.DecisionId, snapshotId = evt.SnapshotId, selectionVersion = evt.SelectionVersion,
                decision = evt.Decision.ToString(), claude = evt.Claude.Status.ToString(), jev = evt.Jev.Status.ToString(),
                noTradeReason = evt.NoTradeReason, promptSha256 = evt.PromptSha256,
            }),
            // The prompt and the replies are not stored at all; only their digest and status are.
            SensitiveRedacted = true,
        });
        db.SaveChanges();
    }

    public DecisionPage List(DecisionQuery q)
    {
        using var db = new PilotTradeDbContext(_options);
        var env = q.Environment.ToString();
        var rows = db.DecisionEvents.AsNoTracking().Where(x => x.Environment == env && x.AccountId == q.AccountId);
        if (!string.IsNullOrWhiteSpace(q.Epic)) rows = rows.Where(x => x.Epic == q.Epic);
        if (q.Decision is { } d) { var ds = d.ToString(); rows = rows.Where(x => x.Decision == ds); }
        if (q.Status is { } s) { var ss = s.ToString(); rows = rows.Where(x => x.ClaudeStatus == ss || x.JevStatus == ss); }
        var total = rows.Count();
        var items = rows.OrderByDescending(x => x.RecordedTicks).Take(Math.Clamp(q.Limit, 1, 200)).ToList().Select(ToDomain).ToList();
        return new DecisionPage(total, items);
    }

    public DecisionEvent? Get(string accountId, TradingEnvironment env, string decisionId)
    {
        using var db = new PilotTradeDbContext(_options);
        var e = env.ToString();
        var row = db.DecisionEvents.AsNoTracking().FirstOrDefault(x => x.DecisionId == decisionId && x.AccountId == accountId && x.Environment == e);
        return row is null ? null : ToDomain(row);
    }

    // A row this build cannot read keeps its status but loses its step details and is shown as NO_TRADE, never as a trade.
    private static DecisionEvent ToDomain(DecisionEventEntity e)
    {
        var env = Enum.TryParse<TradingEnvironment>(e.Environment, out var en) ? en : TradingEnvironment.Demo;
        var claude = Step(e.ClaudeJson, e.ClaudeStatus, "Claude API");
        var jev = Step(e.JevJson, e.JevStatus, "JEV/TypeSafe AI");
        var decision = Enum.TryParse<DecisionCandidate>(e.Decision, out var d) && Enum.IsDefined(d) ? d : DecisionCandidate.NoTrade;
        var reason = e.NoTradeReason;
        if (decision is DecisionCandidate.Buy or DecisionCandidate.Sell && (claude.Status != AiStepStatus.Succeeded || jev.Status != AiStepStatus.Succeeded))
        { decision = DecisionCandidate.NoTrade; reason = "Inconsistent record: a step did not succeed"; }
        return new DecisionEvent(e.DecisionId, e.AccountId, env, e.Epic, e.SelectionVersion, e.SnapshotId, e.SnapshotAsOfUtc, e.StartedUtc,
            e.RecordedUtc, claude, jev, e.PromptSha256, e.PromptChars, decision, reason, e.RiskVeto, e.StrategyId, e.StrategyVersion, e.AuditEventId);
    }

    private static AiStepRecord Step(string json, string status, string provider)
    {
        var st = Enum.TryParse<AiStepStatus>(status, out var s) && Enum.IsDefined(s) ? s : AiStepStatus.Unavailable;
        try
        {
            var r = JsonSerializer.Deserialize<AiStepRecord>(json);
            if (r is not null && r.Status == st) return r;
        }
        catch (JsonException) { }
        return new AiStepRecord(provider, st == AiStepStatus.Succeeded ? AiStepStatus.Unavailable : st, null, null, null, null, null, null, null, null, "Stored details unreadable");
    }
}
