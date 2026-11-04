namespace PilotTrade.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using PilotTrade.Domain;
using PilotTrade.Domain.Market;

// One row per account and environment. Each call uses its own context, so it never shares one with other requests.
public sealed class EfInstrumentSelectionStore : IInstrumentSelectionStore
{
    private readonly DbContextOptions<PilotTradeDbContext> _options;
    public EfInstrumentSelectionStore(DbContextOptions<PilotTradeDbContext> options) { _options = options; }

    private static string Key(string accountId, TradingEnvironment env) => $"{env}:{accountId}";

    public IReadOnlyList<InstrumentSelection> LoadAll()
    {
        using var db = new PilotTradeDbContext(_options);
        var list = new List<InstrumentSelection>();
        foreach (var e in db.InstrumentSelections.AsNoTracking().ToList())
        {
            // A row this build cannot read is skipped, never guessed at.
            if (!Enum.TryParse<TradingEnvironment>(e.Environment, out var env) || string.IsNullOrWhiteSpace(e.Epic) || e.Id != Key(e.AccountId, env)) continue;
            list.Add(new InstrumentSelection(e.Id, e.AccountId, env, e.Epic, e.SelectionVersion, e.SelectedUtc, e.SelectedBy));
        }
        return list;
    }

    public int HighestStoredVersion()
    {
        using var db = new PilotTradeDbContext(_options);
        var selections = db.InstrumentSelections.Select(x => (int?)x.SelectionVersion).Max() ?? 0;
        var decisions = db.Decisions.Select(x => (int?)x.SelectionVersion).Max() ?? 0;
        var analyses = db.ClaudeAnalyses.Select(x => (int?)x.SelectionVersion).Max() ?? 0;
        var intents = db.OrderIntents.Select(x => x.SelectionVersion).ToList()
            .Select(v => int.TryParse(v, out var n) ? n : 0).DefaultIfEmpty(0).Max();
        return Math.Max(Math.Max(selections, decisions), Math.Max(analyses, intents));
    }

    public void Save(InstrumentSelection selection)
    {
        using var db = new PilotTradeDbContext(_options);
        var id = Key(selection.AccountId, selection.Environment);
        var e = db.InstrumentSelections.FirstOrDefault(x => x.Id == id);
        if (e is null) { e = new InstrumentSelectionEntity { Id = id }; db.InstrumentSelections.Add(e); }
        e.AccountId = selection.AccountId;
        e.Environment = selection.Environment.ToString();
        e.Epic = selection.Epic;
        e.SelectionVersion = selection.SelectionVersion;
        e.SelectedUtc = selection.SelectedUtc;
        e.SelectedBy = selection.SelectedBy;
        db.SaveChanges();
    }
}
