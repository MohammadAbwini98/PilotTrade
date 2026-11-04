namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Market;

public interface IInstrumentSelectionService
{
    InstrumentSelection Select(string accountId, TradingEnvironment env, string epic, string selectedBy);
    InstrumentSelection? Current(string accountId, TradingEnvironment env);
}

// Every change bumps the version, which invalidates snapshots and AI results built for the previous selection.
// With a store, the owner's choice of instrument survives a restart, but its version does not: a restored choice gets
// a new id and a version above anything stored, so no snapshot, decision or AI result from before the restart can
// match it. The instrument itself is checked again before any snapshot uses it.
public sealed class InstrumentSelectionService : IInstrumentSelectionService
{
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly IInstrumentSelectionStore? _store;
    private readonly object _lock = new();
    private readonly Dictionary<(string, TradingEnvironment), InstrumentSelection> _current = new();
    private int _version;
    private bool _loaded;

    public InstrumentSelectionService(IClock clock, IIdGenerator ids, IInstrumentSelectionStore? store = null)
    {
        _clock = clock; _ids = ids; _store = store;
    }

    public InstrumentSelection Select(string accountId, TradingEnvironment env, string epic, string selectedBy)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var selection = new InstrumentSelection(_ids.NewId("sel"), accountId, env, epic, ++_version, _clock.UtcNow, selectedBy);
            // Stored first: a choice that cannot be saved is not made.
            _store?.Save(selection);
            _current[(accountId, env)] = selection;
            return selection;
        }
    }

    public InstrumentSelection? Current(string accountId, TradingEnvironment env)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _current.TryGetValue((accountId, env), out var s) ? s : null;
        }
    }

    private void EnsureLoaded()
    {
        if (_loaded || _store is null) { _loaded = true; return; }
        _version = Math.Max(_version, _store.HighestStoredVersion());
        foreach (var stored in _store.LoadAll())
        {
            var restored = new InstrumentSelection(_ids.NewId("sel"), stored.AccountId, stored.Environment, stored.Epic, ++_version, _clock.UtcNow, "restored");
            _store.Save(restored);
            _current[(stored.AccountId, stored.Environment)] = restored;
        }
        _loaded = true;
    }
}
