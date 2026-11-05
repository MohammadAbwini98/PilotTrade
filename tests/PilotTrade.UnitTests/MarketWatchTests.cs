namespace PilotTrade.UnitTests;

// A read-only broker for the market monitor: metadata and quotes are scripted per epic; anything else fails the test.
internal sealed class WatchBroker : IBrokerAdapter
{
    public Dictionary<string, InstrumentMetadata> Meta { get; } = new();
    public Dictionary<string, BrokerException> MetaErrors { get; } = new();
    public List<MarketQuote> Quotes { get; } = new();
    public BrokerException? QuoteError { get; set; }
    public int QuoteCalls { get; private set; }
    public bool ExecutionEnabled => false;
    public TradingEnvironment Environment => TradingEnvironment.Demo;

    public Task<InstrumentMetadata?> GetInstrumentAsync(string epic, string accountId, CancellationToken ct)
    {
        if (MetaErrors.TryGetValue(epic, out var e)) throw e;
        return Task.FromResult(Meta.GetValueOrDefault(epic));
    }
    public Task<IReadOnlyList<MarketQuote>> GetQuotesAsync(IEnumerable<string> epics, CancellationToken ct)
    {
        QuoteCalls++;
        if (QuoteError is not null) throw QuoteError;
        var wanted = epics.ToHashSet();
        return Task.FromResult<IReadOnlyList<MarketQuote>>(Quotes.Where(q => wanted.Contains(q.Epic)).ToList());
    }

    public Task<BrokerSubmissionResult> SubmitOrderAsync(OrderIntent intent, CancellationToken ct) => throw new InvalidOperationException("The monitor must never submit.");
    public Task<BrokerSubmissionResult?> GetConfirmationAsync(string reference, CancellationToken ct) => throw new NotSupportedException();
    public Task<BrokerSession> CreateSessionAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<BrokerAccountSummary>> GetAccountsAsync(CancellationToken ct) => throw new NotSupportedException();
    public Task<BrokerAccount> GetAccountAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<InstrumentSummary>> SearchInstrumentsAsync(string query, int limit, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Candle>> GetHistoryAsync(string epic, string timeframe, int count, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<Position>> GetPositionsAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<IReadOnlyList<WorkingOrder>> GetWorkingOrdersAsync(string accountId, CancellationToken ct) => throw new NotSupportedException();
    public Task<bool> CancelOrderAsync(string orderId, string accountId, CancellationToken ct) => throw new InvalidOperationException("The monitor must never cancel.");
    public Task<bool> ClosePositionAsync(string positionId, decimal? size, CancellationToken ct) => throw new InvalidOperationException("The monitor must never close.");
}

public class MarketWatchTests
{
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero));
    private readonly WatchBroker _broker = new();

    private InstrumentMetadata Meta(string epic, InstrumentAssetClass cls, int precision = 2, MarketStatus status = MarketStatus.Open, decimal tick = 0.01m) =>
        new(epic, epic + " name", epic, cls, status, "USD", null, precision, tick, 0.01m, 100m, 0.01m, null, null, 1m, null, true, true, true,
            new[] { "Mon 00:00-23:59" }, _clock.UtcNow, "ACC", TradingEnvironment.Demo);

    private void Add(string epic, InstrumentAssetClass cls, decimal bid, decimal ask, int precision = 2, MarketStatus status = MarketStatus.Open,
        TimeSpan? age = null, FeedHealth health = FeedHealth.Healthy)
    {
        _broker.Meta[epic] = Meta(epic, cls, precision, status);
        var ts = _clock.UtcNow - (age ?? TimeSpan.FromSeconds(2));
        _broker.Quotes.Add(new MarketQuote(epic, bid, ask, null, ask - bid, ts, _clock.UtcNow, health));
    }

    private Task<MarketWatch> Build(params string[] epics) =>
        new MarketWatchService(_broker, _clock, MarketDataPolicy.Default).BuildAsync("ACC", epics, CancellationToken.None);

    [Fact]
    public async Task EveryAssetClass_GetsItsOwnSpreadLimit_AndPassesWhenInsideIt()
    {
        Add("EURUSD", InstrumentAssetClass.Fx, 1.08000m, 1.08002m, 5);
        Add("BTCUSD", InstrumentAssetClass.CryptoCfd, 65000m, 65300m);
        Add("GOLD", InstrumentAssetClass.Commodity, 2400m, 2400.5m);
        Add("US500", InstrumentAssetClass.Index, 5800m, 5800.5m, 1);
        Add("AAPL", InstrumentAssetClass.Share, 230m, 230.2m);

        var watch = await Build("EURUSD", "BTCUSD", "GOLD", "US500", "AAPL");

        watch.Rows.Select(r => (r.Epic, r.AssetClass, r.MaxSpreadPercent)).Should().Equal(
            ("EURUSD", "Fx", 0.05m), ("BTCUSD", "CryptoCfd", 1m), ("GOLD", "Commodity", 0.2m), ("US500", "Index", 0.2m), ("AAPL", "Share", 0.5m));
        watch.Rows.Should().OnlyContain(r => r.QuoteChecksPassed && r.Blockers.Count == 0);
        watch.MaxQuoteAgeSeconds.Should().Be(60);
        _broker.QuoteCalls.Should().Be(1, "quotes are fetched in one batch");
    }

    [Fact]
    public async Task SpreadWideForFx_IsBlocked_ButTheSameSpreadIsFineForCrypto()
    {
        // 0.46% of mid: too wide for FX (0.05%), within the crypto CFD limit (1%).
        Add("EURUSD", InstrumentAssetClass.Fx, 1.0800m, 1.0850m, 4);
        Add("BTCUSD", InstrumentAssetClass.CryptoCfd, 65000m, 65300m);
        var watch = await Build("EURUSD", "BTCUSD");
        watch.Rows[0].Blockers.Should().Equal("Spread too wide");
        watch.Rows[0].Quote!.SpreadPercent.Should().BeGreaterThan(0.05m);
        watch.Rows[1].QuoteChecksPassed.Should().BeTrue();
    }

    [Fact]
    public async Task ClosedMarket_InhibitsReadiness_ButStillShowsItsLastQuote()
    {
        Add("US500", InstrumentAssetClass.Index, 5800m, 5800.5m, 1, MarketStatus.Closed);
        var row = (await Build("US500")).Rows.Single();
        row.QuoteChecksPassed.Should().BeFalse();
        row.Blockers.Should().Contain("Instrument not open: Closed");
        row.MarketStatus.Should().Be("Closed");
        row.Quote.Should().NotBeNull();
    }

    [Fact]
    public async Task StaleQuote_IsMarkedStaleWithItsAge()
    {
        Add("GOLD", InstrumentAssetClass.Commodity, 2400m, 2400.5m, age: TimeSpan.FromSeconds(90));
        var row = (await Build("GOLD")).Rows.Single();
        row.Blockers.Should().Equal("Stale quote");
        row.Quote!.Health.Should().Be("Stale");
        row.Quote.AgeSeconds.Should().Be(90);
    }

    [Fact]
    public async Task FutureTimestamp_IsNotTreatedAsFresh()
    {
        Add("GOLD", InstrumentAssetClass.Commodity, 2400m, 2400.5m, age: TimeSpan.FromSeconds(-90));
        var row = (await Build("GOLD")).Rows.Single();
        row.QuoteChecksPassed.Should().BeFalse();
        row.Blockers.Should().Equal("Quote timestamp in the future");
    }

    [Fact]
    public async Task UnhealthyFeed_CrossedQuote_AndZeroPrices_AreBlocked()
    {
        Add("EURUSD", InstrumentAssetClass.Fx, 1.08m, 1.08001m, 5, health: FeedHealth.Disconnected);
        Add("GOLD", InstrumentAssetClass.Commodity, 2401m, 2400m);
        Add("AAPL", InstrumentAssetClass.Share, 0m, 230m);
        var rows = (await Build("EURUSD", "GOLD", "AAPL")).Rows;
        rows[0].Blockers.Should().Equal("Feed unhealthy");
        rows[1].Blockers.Should().Equal("Quote crossed");
        rows[1].Quote.Should().BeNull();
        rows[2].Blockers.Should().Equal("Quote price invalid");
    }

    [Fact]
    public async Task MissingMetadata_AndMissingQuote_AreUnknownNotZero()
    {
        _broker.Meta["AAPL"] = Meta("AAPL", InstrumentAssetClass.Share);
        var rows = (await Build("GONE", "AAPL")).Rows;
        rows[0].AssetClass.Should().Be("Unknown");
        rows[0].MarketStatus.Should().Be("Unknown");
        rows[0].Dealing.Should().BeNull();
        rows[0].MaxSpreadPercent.Should().BeNull();
        rows[0].Blockers.Should().Equal("Instrument metadata missing", "No quote");
        rows[1].Quote.Should().BeNull();
        rows[1].Blockers.Should().Equal("No quote");
    }

    [Fact]
    public async Task UnknownAssetClass_HasNoSpreadLimit_AndIsBlocked()
    {
        Add("ODD", InstrumentAssetClass.Unknown, 10m, 10.01m);
        var row = (await Build("ODD")).Rows.Single();
        row.MaxSpreadPercent.Should().BeNull();
        row.Blockers.Should().Equal("Spread limit unknown");
    }

    [Fact]
    public async Task BrokerErrors_ArePerInstrumentForMetadata_AndForAllRowsForQuotes()
    {
        Add("EURUSD", InstrumentAssetClass.Fx, 1.08m, 1.08001m, 5);
        Add("GOLD", InstrumentAssetClass.Commodity, 2400m, 2400.5m);
        _broker.MetaErrors["GOLD"] = new BrokerException(BrokerErrorKind.RateLimited, "slow down");
        var rows = (await Build("EURUSD", "GOLD")).Rows;
        rows[0].QuoteChecksPassed.Should().BeTrue();
        rows[1].Blockers.Should().Contain("Instrument metadata unavailable: RateLimited");

        _broker.QuoteError = new BrokerException(BrokerErrorKind.Unavailable, "down");
        rows = (await Build("EURUSD", "GOLD")).Rows;
        rows.Should().OnlyContain(r => r.Quote == null && r.Blockers.Contains("Broker data unavailable: Unavailable"));
    }

    [Fact]
    public async Task UnusualPrecision_IsReportedAsGiven()
    {
        _broker.Meta["JPYX"] = Meta("JPYX", InstrumentAssetClass.Fx, precision: 3, tick: 0.001m);
        _broker.Meta["PENNY"] = Meta("PENNY", InstrumentAssetClass.Share, precision: 6, tick: 0.000001m);
        _broker.Quotes.Add(new MarketQuote("JPYX", 151.234m, 151.240m, null, 0.006m, _clock.UtcNow, _clock.UtcNow, FeedHealth.Healthy));
        _broker.Quotes.Add(new MarketQuote("PENNY", 0.001234m, 0.001236m, null, 0.000002m, _clock.UtcNow, _clock.UtcNow, FeedHealth.Healthy));
        var rows = (await Build("JPYX", "PENNY")).Rows;
        rows[0].Dealing!.PricePrecision.Should().Be(3);
        rows[1].Dealing!.PricePrecision.Should().Be(6);
        rows[1].Dealing!.TickSize.Should().Be(0.000001m);
        rows[1].Quote!.Bid.Should().Be(0.001234m);
        rows[1].QuoteChecksPassed.Should().BeTrue();
    }

    [Fact]
    public async Task EmptyList_MakesNoBrokerCalls()
    {
        (await Build()).Rows.Should().BeEmpty();
        _broker.QuoteCalls.Should().Be(0);
    }
}

public class WatchlistRulesTests
{
    [Theory]
    [InlineData(new[] { "EURUSD", "GOLD" }, null)]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "EURUSD", "EURUSD" }, "An epic is listed twice.")]
    [InlineData(new[] { "BAD EPIC" }, "An epic is not valid.")]
    [InlineData(new[] { "" }, "An epic is not valid.")]
    [InlineData(new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" }, "At most 10 instruments.")]
    public void Validates(string[] epics, string? problem) => WatchlistRules.Problem(epics).Should().Be(problem);

    [Fact]
    public void MissingList_IsAProblem() => WatchlistRules.Problem(null).Should().Be("The list is missing.");
}
