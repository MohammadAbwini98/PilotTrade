namespace PilotTrade.Infrastructure.Broker;

using System.Collections.Concurrent;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;

public sealed class SimulatedBrokerAdapter : IBrokerAdapter
{
    private const string AccountId = BrokerRuntimeOptions.SimulationAccountId;
    private static readonly IReadOnlyDictionary<string, TimeSpan> Timeframes = new Dictionary<string, TimeSpan>
    {
        ["1m"] = TimeSpan.FromMinutes(1), ["5m"] = TimeSpan.FromMinutes(5), ["15m"] = TimeSpan.FromMinutes(15), ["30m"] = TimeSpan.FromMinutes(30),
        ["1h"] = TimeSpan.FromHours(1), ["4h"] = TimeSpan.FromHours(4), ["1d"] = TimeSpan.FromDays(1), ["1w"] = TimeSpan.FromDays(7),
    };

    private readonly TradingEnvironment _env;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly ConcurrentDictionary<string, InstrumentMetadata> _instruments = new();
    private readonly ConcurrentDictionary<string, decimal> _basePrices = new();
    private readonly ConcurrentDictionary<string, Position> _positions = new();
    private readonly ConcurrentDictionary<string, WorkingOrder> _orders = new();
    private readonly ConcurrentDictionary<string, BrokerSubmissionResult> _confirms = new();
    private readonly BrokerAccount _account;

    public SimulatedBrokerAdapter(TradingEnvironment env, IClock clock, IIdGenerator ids)
    {
        _env = env; _clock = clock; _ids = ids;
        _account = new BrokerAccount(AccountId, env, "USD", 10000m, 10000m, 8000m, 0m, 0m, 0m, clock.UtcNow);
        Seed();
    }

    public TradingEnvironment Environment => _env;
    public bool ExecutionEnabled => true;

    private void Seed()
    {
        var now = _clock.UtcNow;
        var defs = new (string epic, string name, string sym, InstrumentAssetClass cls, string quote, int prec, decimal tick, decimal min, decimal max, decimal step, decimal price)[]
        {
            ("EURUSD", "EUR/USD", "EURUSD", InstrumentAssetClass.Fx, "USD", 5, 0.00001m, 0.01m, 1000m, 0.01m, 1.1m),
            ("BTCUSD", "BTC/USD", "BTCUSD", InstrumentAssetClass.CryptoCfd, "USD", 2, 0.01m, 0.01m, 10m, 0.01m, 65000m),
            ("GOLD", "Gold", "XAUUSD", InstrumentAssetClass.Commodity, "USD", 2, 0.01m, 0.01m, 50m, 0.01m, 2400m),
            ("US500", "US 500", "US500", InstrumentAssetClass.Index, "USD", 1, 0.1m, 1m, 100m, 1m, 5500m),
            ("AAPL", "Apple Inc", "AAPL", InstrumentAssetClass.Share, "USD", 2, 0.01m, 1m, 1000m, 1m, 190m),
        };
        foreach (var d in defs)
        {
            _instruments[d.epic] = new InstrumentMetadata(d.epic, d.name, d.sym, d.cls, MarketStatus.Open, d.quote, d.quote, d.prec, d.tick, d.min, d.max, d.step, 0.05m, 20m, 1m, 100m, true, true, true, new[]{"24/5"}, now, AccountId, _env);
            _basePrices[d.epic] = d.price;
        }
    }

    public Task<BrokerSession> CreateSessionAsync(CancellationToken ct)
        => Task.FromResult(new BrokerSession(_ids.NewId("sess"), "CST_SIM", "SEC_SIM", _clock.UtcNow.AddHours(10), _env, AccountId));

    private static void RequireAccount(string accountId)
    {
        if (accountId != AccountId) throw new BrokerException(BrokerErrorKind.AccountNotFound, "Requested account not found");
    }

    public Task<IReadOnlyList<BrokerAccountSummary>> GetAccountsAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BrokerAccountSummary>>(new[] { new BrokerAccountSummary(AccountId, "Simulation", "ENABLED", _account.Currency, true) });

    public Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct)
    {
        RequireAccount(accountId);
        return Task.FromResult(_account with { AsOfUtc = _clock.UtcNow });
    }

    public Task<IReadOnlyList<InstrumentSummary>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct)
    {
        var q = (query ?? "").ToLowerInvariant();
        var res = _instruments.Values.Where(i => i.Name.ToLowerInvariant().Contains(q) || i.Epic.ToLowerInvariant().Contains(q) || i.Symbol.ToLowerInvariant().Contains(q))
            .OrderBy(i => i.Epic, StringComparer.Ordinal)
            .Take(limit <= 0 ? 50 : limit)
            .Select(i => new InstrumentSummary(i.Epic, i.Name, i.Symbol, i.AssetClass, i.Status, _basePrices[i.Epic], _basePrices[i.Epic] * 1.0002m))
            .ToList();
        return Task.FromResult<IReadOnlyList<InstrumentSummary>>(res);
    }

    public Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct)
    {
        RequireAccount(accountId);
        return Task.FromResult(_instruments.TryGetValue(epic, out var m) ? m with { RetrievedUtc = _clock.UtcNow, AccountId = accountId, Environment = _env } : null);
    }

    // Deterministic for a given clock: each candle's price depends only on its time bucket.
    public Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct)
    {
        if (!Timeframes.TryGetValue(timeframe, out var span))
            throw new BrokerException(BrokerErrorKind.InvalidRequest, "Unsupported timeframe");
        if (!_basePrices.TryGetValue(epic, out var basePrice)) return Task.FromResult<IReadOnlyList<Candle>>(Array.Empty<Candle>());

        var lastBucket = _clock.UtcNow.UtcTicks / span.Ticks - 1; // newest fully closed candle
        var list = new List<Candle>();
        for (var bucket = lastBucket - Math.Clamp(count, 1, 1000) + 1; bucket <= lastBucket; bucket++)
        {
            var open = PriceAt(basePrice, bucket);
            var close = PriceAt(basePrice, bucket + 1);
            var range = basePrice * 0.0002m;
            var openTime = new DateTimeOffset(bucket * span.Ticks, TimeSpan.Zero);
            list.Add(new Candle(epic, timeframe, openTime, openTime + span, true, open, Math.Max(open, close) + range, Math.Min(open, close) - range, close, 1000m));
        }
        return Task.FromResult<IReadOnlyList<Candle>>(list);
    }

    private static decimal PriceAt(decimal basePrice, long bucket) => basePrice * (1m + (bucket % 17 - 8) * 0.0005m);

    public Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct)
    {
        RequireAccount(accountId);
        return Task.FromResult<IReadOnlyList<Position>>(_positions.Values.Where(p => p.AccountId == accountId).ToList());
    }

    public Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct)
    {
        RequireAccount(accountId);
        return Task.FromResult<IReadOnlyList<WorkingOrder>>(_orders.Values.Where(o => o.AccountId == accountId).ToList());
    }

    private MarketQuote QuoteFor(string epic)
    {
        var now = _clock.UtcNow;
        var bid = _basePrices[epic];
        var spread = bid * 0.0002m;
        return new MarketQuote(epic, bid, bid + spread, bid + spread / 2m, spread, now, now, FeedHealth.Healthy);
    }

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
        var q = QuoteFor(intent.Epic);
        var price = intent.Side == OrderSide.Buy ? q.Ask : q.Bid;
        _positions[posId] = new Position(posId, intent.AccountId, _env, intent.Epic, intent.Side, intent.Size, price, price, 0m, intent.StopLevel, intent.TakeProfitLevel, now, now);
        var res = new BrokerSubmissionResult(intent.IntentId, dealRef, posId, OrderState.Confirmed, null, now, now, new[] { new AffectedDeal(posId, "OPENED") });
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
        var res = epics.Where(_basePrices.ContainsKey).Select(QuoteFor).ToList();
        return Task.FromResult<IReadOnlyList<MarketQuote>>(res);
    }
}
