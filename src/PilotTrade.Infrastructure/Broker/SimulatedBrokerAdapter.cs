namespace PilotTrade.Infrastructure.Broker;

using System.Collections.Concurrent;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

public sealed class SimulatedBrokerAdapter : IBrokerAdapter
{
    private readonly TradingEnvironment _env;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly ConcurrentDictionary<string, InstrumentMetadata> _instruments = new();
    private readonly ConcurrentDictionary<string, List<Candle>> _candles = new();
    private readonly ConcurrentDictionary<string, MarketQuote> _quotes = new();
    private readonly ConcurrentDictionary<string, Position> _positions = new();
    private readonly ConcurrentDictionary<string, WorkingOrder> _orders = new();
    private readonly ConcurrentDictionary<string, BrokerSubmissionResult> _confirms = new();
    private BrokerAccount _account;

    public SimulatedBrokerAdapter(TradingEnvironment env, IClock clock, IIdGenerator ids)
    {
        _env = env; _clock = clock; _ids = ids;
        _account = new BrokerAccount("SIM-ACC", env, "USD", 10000m, 10000m, 8000m, 0m, 0m, 0m, clock.UtcNow);
        Seed();
    }

    public TradingEnvironment Environment => _env;

    private void Seed()
    {
        var now = _clock.UtcNow;
        var defs = new (string epic, string name, string sym, InstrumentAssetClass cls, string quote, int prec, decimal tick, decimal min, decimal max, decimal step)[]
        {
            ("EURUSD", "EUR/USD", "EURUSD", InstrumentAssetClass.Fx, "USD", 5, 0.00001m, 0.01m, 1000m, 0.01m),
            ("BTCUSD", "BTC/USD", "BTCUSD", InstrumentAssetClass.CryptoCfd, "USD", 2, 0.01m, 0.01m, 10m, 0.01m),
            ("GOLD", "Gold", "XAUUSD", InstrumentAssetClass.Commodity, "USD", 2, 0.01m, 0.01m, 50m, 0.01m),
            ("US500", "US 500", "US500", InstrumentAssetClass.Index, "USD", 1, 0.1m, 1m, 100m, 1m),
            ("AAPL", "Apple Inc", "AAPL", InstrumentAssetClass.Share, "USD", 2, 0.01m, 1m, 1000m, 1m),
        };
        foreach (var d in defs)
        {
            var meta = new InstrumentMetadata(d.epic, d.name, d.sym, d.cls, MarketStatus.Open, d.quote, d.quote, d.prec, d.tick, d.min, d.max, d.step, 0.05m, 20m, 1m, 100m, true, true, true, new[]{"24/5"}, now, "SIM-ACC", _env);
            _instruments[d.epic] = meta;
            _quotes[d.epic] = new MarketQuote(d.epic, 1.0m + (d.epic.GetHashCode() % 100) / 1000m, 1.0m + (d.epic.GetHashCode() % 100) / 1000m + 0.0002m, null, 0.0002m, now, now, FeedHealth.Healthy);
            var list = new List<Candle>();
            var price = 100m;
            for (int i = 60; i >= 0; i--)
            {
                var ot = now.AddMinutes(-i * 5);
                price += (i % 7 - 3) * 0.1m;
                list.Add(new Candle(d.epic, "5m", ot, ot.AddMinutes(5), true, price, price + 0.5m, price - 0.5m, price + 0.1m, 1000 + i));
            }
            _candles[d.epic] = list;
        }
    }

    public Task<BrokerSession> CreateSessionAsync(CancellationToken ct)
        => Task.FromResult(new BrokerSession(_ids.NewId("sess"), "CST_SIM", "SEC_SIM", _clock.UtcNow.AddHours(10), _env, "SIM-ACC"));

    public Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct) => Task.FromResult(_account with { AsOfUtc = _clock.UtcNow });

    public Task<IReadOnlyList<InstrumentMetadata>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct)
    {
        var q = (query ?? "").ToLowerInvariant();
        var res = _instruments.Values.Where(i => i.Name.ToLowerInvariant().Contains(q) || i.Epic.ToLowerInvariant().Contains(q) || i.Symbol.ToLowerInvariant().Contains(q)).Take(limit <= 0 ? 50 : limit).ToList();
        return Task.FromResult<IReadOnlyList<InstrumentMetadata>>(res);
    }

    public Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct)
        => Task.FromResult(_instruments.TryGetValue(epic, out var m) ? m with { RetrievedUtc = _clock.UtcNow, AccountId = accountId, Environment = _env } : null);

    public Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct)
    {
        if (!_candles.TryGetValue(epic, out var list)) return Task.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());
        return Task.FromResult<IReadOnlyList<Candle>>(list.TakeLast(count).ToList());
    }

    public Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<Position>>(_positions.Values.Where(p => p.AccountId == accountId).ToList());

    public Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<WorkingOrder>>(_orders.Values.Where(o => o.AccountId == accountId).ToList());

    public Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var dealRef = _ids.NewId("deal");
        var meta = _instruments.TryGetValue(intent.Epic, out var m) ? m : null;
        // validate
        if (meta is null) return Task.FromResult(new BrokerSubmissionResult(intent.IntentId, null, null, OrderState.Rejected, "Unknown instrument", now, null));
        if (intent.Size < meta.MinDealSize || intent.Size > meta.MaxDealSize) return Task.FromResult(new BrokerSubmissionResult(intent.IntentId, null, null, OrderState.Rejected, "Size out of range", now, null));
        // market order -> confirmed position
        var posId = _ids.NewId("pos");
        var price = _quotes.TryGetValue(intent.Epic, out var q) ? (intent.Side == OrderSide.Buy ? q.Ask : q.Bid) : 1m;
        _positions[posId] = new Position(posId, intent.AccountId, _env, intent.Epic, intent.Side, intent.Size, price, price, 0m, intent.StopLevel, intent.TakeProfitLevel, now, now);
        var res = new BrokerSubmissionResult(intent.IntentId, dealRef, posId, OrderState.Confirmed, null, now, now);
        _confirms[dealRef] = res;
        return Task.FromResult(res);
    }

    public Task<BrokerSubmissionResult?> GetConfirmationAsync(string brokerDealReference, CancellationToken ct)
        => Task.FromResult(_confirms.TryGetValue(brokerDealReference, out var r) ? r : null);

    public Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct)
    {
        return Task.FromResult(_orders.TryRemove(orderId, out _));
    }

    public Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct)
    {
        if (_positions.TryRemove(positionId, out _)) return Task.FromResult(true);
        return Task.FromResult(false);
    }

    public Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct)
    {
        var res = epics.Where(e => _quotes.ContainsKey(e)).Select(e => _quotes[e] with { ReceivedUtc = _clock.UtcNow }).ToList();
        return Task.FromResult<IReadOnlyList<MarketQuote>>(res);
    }
}