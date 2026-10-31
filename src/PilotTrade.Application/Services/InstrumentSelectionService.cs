namespace PilotTrade.Application.Services;

using PilotTrade.Domain;
using PilotTrade.Domain.Market;

public interface IInstrumentSelectionService
{
    InstrumentSelection Select(string accountId, TradingEnvironment env, string epic, string selectedBy);
    InstrumentSelection? Current(string accountId, TradingEnvironment env);
}

// Every change bumps the version, which invalidates snapshots and AI results built for the previous selection.
// Versions only ever increase while the process runs; a restart clears the selection, so nothing old can match it.
public sealed class InstrumentSelectionService : IInstrumentSelectionService
{
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly object _lock = new();
    private readonly Dictionary<(string, TradingEnvironment), InstrumentSelection> _current = new();
    private int _version;

    public InstrumentSelectionService(IClock clock, IIdGenerator ids) { _clock = clock; _ids = ids; }

    public InstrumentSelection Select(string accountId, TradingEnvironment env, string epic, string selectedBy)
    {
        lock (_lock)
        {
            var selection = new InstrumentSelection(_ids.NewId("sel"), accountId, env, epic, ++_version, _clock.UtcNow, selectedBy);
            _current[(accountId, env)] = selection;
            return selection;
        }
    }

    public InstrumentSelection? Current(string accountId, TradingEnvironment env)
    {
        lock (_lock) return _current.TryGetValue((accountId, env), out var s) ? s : null;
    }
}
