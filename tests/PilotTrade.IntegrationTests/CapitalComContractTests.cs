namespace PilotTrade.IntegrationTests;

using System.Text.Json;

public class CapitalComContractTests
{
    private const string BaseUri = "https://demo-api-capital.backend-capital.com";
    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
    private readonly List<TimeSpan> _delays = new();
    private readonly CapitalFake _fake;
    private readonly InMemorySecretStore _secrets = new();

    public CapitalComContractTests() { _fake = new CapitalFake(_clock); }

    private async Task<CapitalComBrokerAdapter> Adapter(bool credentials = true)
    {
        if (credentials)
        {
            await _secrets.SetAsync("capital:Demo:api_key", "TEST-KEY", CancellationToken.None);
            await _secrets.SetAsync("capital:Demo:identifier", "TEST-USER", CancellationToken.None);
            await _secrets.SetAsync("capital:Demo:password", "TEST-PASS", CancellationToken.None);
        }
        // The delay advances the fake clock, so rate gates and backoff run instantly and can be asserted.
        return new CapitalComBrokerAdapter(new BrokerEnvironmentConfig(TradingEnvironment.Demo, BaseUri, "ACC-A", false),
            _secrets, new HttpClient(_fake), _clock, new GuidIdGenerator(),
            (d, _) => { _delays.Add(d); _clock.UtcNow += d; return Task.CompletedTask; });
    }

    private static Task<BrokerException> Fails(Func<Task> call) => Assert.ThrowsAsync<BrokerException>(call);

    // Session and headers

    [Fact]
    public async Task Session_ParsesTokensAndActiveAccount()
    {
        var session = await (await Adapter()).CreateSessionAsync(CancellationToken.None);
        session.CstToken.Should().Be("cst-1");
        session.SecurityToken.Should().Be("sec-1");
        session.AccountId.Should().Be("ACC-A");
    }

    [Fact]
    public async Task Session_SendsCredentialsOnlyToSessionEndpoint_AndTokensPerRequest()
    {
        await (await Adapter()).GetAccountsAsync(CancellationToken.None);
        var login = _fake.Requests[0];
        login.Path.Should().Be("/api/v1/session");
        login.ApiKey.Should().Be("TEST-KEY");
        login.Body.Should().Contain("TEST-USER").And.Contain("TEST-PASS");
        var read = _fake.Requests[1];
        read.ApiKey.Should().BeNull();
        read.Body.Should().BeNull();
        read.Cst.Should().Be("cst-1");
        read.SecurityToken.Should().Be("sec-1");
    }

    [Fact]
    public async Task Session_MissingTokenHeader_IsMalformed()
    {
        _fake.Script("POST /api/v1/session", () => CapitalFake.Json(HttpStatusCode.OK, "{\"currentAccountId\":\"ACC-A\"}"));
        var adapter = await Adapter();
        (await Fails(() => adapter.CreateSessionAsync(CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task Session_MissingActiveAccount_IsMalformed()
    {
        _fake.SessionBody = "{}";
        var adapter = await Adapter();
        (await Fails(() => adapter.CreateSessionAsync(CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task MissingCredentials_FailWithoutAnyRequest()
    {
        var adapter = await Adapter(credentials: false);
        (await Fails(() => adapter.GetAccountsAsync(CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.CredentialsMissing);
        _fake.Requests.Should().BeEmpty();
    }

    [Fact]
    public void LiveEnvironment_AndForeignHost_AreRefused()
    {
        Assert.Throws<InvalidOperationException>(() => new CapitalComBrokerAdapter(new BrokerEnvironmentConfig(TradingEnvironment.Live, BaseUri, "A", false), _secrets, new HttpClient(_fake), _clock, new GuidIdGenerator()));
        Assert.Throws<InvalidOperationException>(() => new CapitalComBrokerAdapter(new BrokerEnvironmentConfig(TradingEnvironment.Demo, BaseUri, "A", true), _secrets, new HttpClient(_fake), _clock, new GuidIdGenerator()));
        Assert.Throws<InvalidOperationException>(() => new CapitalComBrokerAdapter(new BrokerEnvironmentConfig(TradingEnvironment.Demo, "https://api-capital.backend-capital.com", "A", false), _secrets, new HttpClient(_fake), _clock, new GuidIdGenerator()));
    }

    // Multi-account selection and switching

    [Fact]
    public async Task Accounts_ListsEveryAccount()
    {
        var accounts = await (await Adapter()).GetAccountsAsync(CancellationToken.None);
        accounts.Select(a => a.AccountId).Should().Equal("ACC-A", "ACC-B");
        accounts[0].Preferred.Should().BeTrue();
        accounts[1].Preferred.Should().BeFalse();
    }

    [Fact]
    public async Task AlreadyActiveAccount_IsNotSwitched()
    {
        var account = await (await Adapter()).GetAccountAsync("ACC-A", CancellationToken.None);
        account.AccountId.Should().Be("ACC-A");
        _fake.Requests.Should().NotContain(r => r.Method == "PUT");
    }

    [Fact]
    public async Task OtherAccount_IsSwitchedAndVerified_AndReturnsItsOwnValues()
    {
        var account = await (await Adapter()).GetAccountAsync("ACC-B", CancellationToken.None);
        account.AccountId.Should().Be("ACC-B");
        account.Currency.Should().Be("EUR");
        account.Balance.Should().Be(500m);
        var put = _fake.Requests.Single(r => r.Method == "PUT");
        put.Body.Should().Contain("ACC-B");
        var putIndex = _fake.Requests.IndexOf(put);
        _fake.Requests[putIndex + 1].PathAndQuery.Should().Be("/api/v1/session"); // re-verified before the read
    }

    [Fact]
    public async Task SwitchingBackAndForth_ReturnsEachAccountsPositions()
    {
        var adapter = await Adapter();
        (await adapter.GetPositionsAsync("ACC-B", CancellationToken.None)).Single().Epic.Should().Be("GOLD");
        (await adapter.GetPositionsAsync("ACC-A", CancellationToken.None)).Single().Epic.Should().Be("EURUSD");
        (await adapter.GetPositionsAsync("ACC-B", CancellationToken.None)).Single().AccountId.Should().Be("ACC-B");
    }

    [Fact]
    public async Task UnknownAccount_IsNotFound_NotAnotherAccountsData()
    {
        var adapter = await Adapter();
        (await Fails(() => adapter.GetAccountAsync("ACC-X", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.AccountNotFound);
    }

    [Fact]
    public async Task EmptyAccountId_IsRefused()
    {
        var adapter = await Adapter();
        (await Fails(() => adapter.GetPositionsAsync(" ", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.AccountNotFound);
        _fake.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerIgnoresSwitch_IsAccountMismatch_AndNoDataIsRead()
    {
        _fake.IgnoreAccountSwitch = true;
        var adapter = await Adapter();
        (await Fails(() => adapter.GetPositionsAsync("ACC-B", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.AccountMismatch);
        _fake.Requests.Should().NotContain(r => r.Path == "/api/v1/positions");
    }

    [Fact]
    public async Task ActiveAccountMissingFromList_IsNotFound()
    {
        _fake.AccountsJson = "{\"accounts\":[{\"accountId\":\"ACC-B\",\"preferred\":false,\"currency\":\"EUR\",\"balance\":{\"balance\":1,\"deposit\":1,\"profitLoss\":0,\"available\":1}}]}";
        var adapter = await Adapter();
        (await Fails(() => adapter.GetAccountAsync("ACC-A", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.AccountNotFound);
    }

    [Fact]
    public async Task ReloginDuringRead_DiscardsDefaultAccountData_AndRepeatsOnRequestedAccount()
    {
        var adapter = await Adapter();
        await adapter.GetPositionsAsync("ACC-B", CancellationToken.None);
        // The session dies at the next positions read; the re-login lands back on ACC-A, whose data must not be returned.
        _fake.Script("GET /api/v1/positions", () => CapitalFake.Json(HttpStatusCode.Unauthorized, "{}"));
        var positions = await adapter.GetPositionsAsync("ACC-B", CancellationToken.None);
        positions.Single().Epic.Should().Be("GOLD");
        _fake.SessionCount.Should().Be(2);
    }

    // Financial fields

    [Fact]
    public async Task Financials_ParseNestedBalance_AndNeverInventMissingValues()
    {
        var a = await (await Adapter()).GetAccountAsync("ACC-A", CancellationToken.None);
        a.Balance.Should().Be(1100m);
        a.FreeMargin.Should().Be(900m);
        a.UnrealizedPnL.Should().Be(100m);
        a.Equity.Should().Be(1100m);
        a.EquityIsDerived.Should().BeTrue();
        a.MarginUsed.Should().BeNull();
        a.RealizedPnLToday.Should().BeNull();
    }

    [Fact]
    public async Task InconsistentBalance_LeavesEquityUnproven()
    {
        var a = await (await Adapter()).GetAccountAsync("ACC-B", CancellationToken.None);
        a.Equity.Should().BeNull();
        a.EquityIsDerived.Should().BeFalse();
    }

    [Theory]
    [InlineData("balance")]
    [InlineData("deposit")]
    [InlineData("profitLoss")]
    [InlineData("available")]
    public async Task MissingFinancialField_IsMalformed_NotZero(string field)
    {
        var fields = new Dictionary<string, object> { ["balance"] = 1, ["deposit"] = 1, ["profitLoss"] = 0, ["available"] = 1 };
        fields.Remove(field);
        _fake.AccountsJson = JsonSerializer.Serialize(new { accounts = new[] { new { accountId = "ACC-A", preferred = true, currency = "USD", balance = fields } } });
        var adapter = await Adapter();
        var ex = await Fails(() => adapter.GetAccountAsync("ACC-A", CancellationToken.None));
        ex.Kind.Should().Be(BrokerErrorKind.MalformedResponse);
        ex.Message.Should().Contain(field);
    }

    [Theory]
    [InlineData("{\"accounts\":[{\"accountId\":\"ACC-A\",\"preferred\":true,\"currency\":\"USD\",\"balance\":{\"balance\":\"1\",\"deposit\":1,\"profitLoss\":0,\"available\":1}}]}")]
    [InlineData("{\"accounts\":[{\"accountId\":\"ACC-A\",\"preferred\":true,\"currency\":\"USD\",\"balance\":{\"balance\":null,\"deposit\":1,\"profitLoss\":0,\"available\":1}}]}")]
    [InlineData("{\"accounts\":[{\"accountId\":\"ACC-A\",\"preferred\":true,\"balance\":{\"balance\":1,\"deposit\":1,\"profitLoss\":0,\"available\":1}}]}")]
    [InlineData("{\"accounts\":[{\"accountId\":\"ACC-A\",\"preferred\":true,\"currency\":\"USD\"}]}")]
    [InlineData("{\"accounts\":{}}")]
    [InlineData("not json")]
    public async Task MalformedAccounts_AreRejected(string json)
    {
        _fake.AccountsJson = json;
        var adapter = await Adapter();
        (await Fails(() => adapter.GetAccountAsync("ACC-A", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task DuplicateAccount_IsMalformed()
    {
        var one = "{\"accountId\":\"ACC-A\",\"preferred\":true,\"currency\":\"USD\",\"balance\":{\"balance\":1,\"deposit\":1,\"profitLoss\":0,\"available\":1}}";
        _fake.AccountsJson = $"{{\"accounts\":[{one},{one}]}}";
        var adapter = await Adapter();
        (await Fails(() => adapter.GetAccountAsync("ACC-A", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    // Instrument metadata

    [Fact]
    public async Task Metadata_MapsDocumentedDealingRules()
    {
        var m = (await (await Adapter()).GetInstrumentAsync("EURUSD", "ACC-A", CancellationToken.None))!;
        m.Status.Should().Be(MarketStatus.Open);
        m.AssetClass.Should().Be(InstrumentAssetClass.Fx);
        m.QuoteCurrency.Should().Be("USD");
        m.PricePrecision.Should().Be(4);
        m.TickSize.Should().Be(0.0001m);
        m.MinDealSize.Should().Be(0.5m);
        m.MaxDealSize.Should().Be(200m);
        m.DealSizeStep.Should().Be(0.1m);
        m.MinStopDistance.Should().Be(0.2m);
        m.MaxStopDistance.Should().Be(30m);
        m.MinStopDistanceUnit.Should().Be(DistanceUnit.Percentage);
        m.Leverage.Should().Be(20m);
        m.GuaranteedStopAllowed.Should().BeTrue();
        m.TradingHours.Should().ContainSingle().Which.Should().Contain("mon");
        m.AccountId.Should().Be("ACC-A");
    }

    [Fact]
    public async Task Metadata_IsInstrumentSpecific_NotBtcOnly()
    {
        var adapter = await Adapter();
        (await adapter.GetInstrumentAsync("GOLD", "ACC-A", CancellationToken.None))!.QuoteCurrency.Should().Be("EUR");
        (await adapter.GetInstrumentAsync("EURUSD", "ACC-A", CancellationToken.None))!.QuoteCurrency.Should().Be("USD");
    }

    [Fact]
    public async Task UnknownEpic_ReturnsNull()
    {
        (await (await Adapter()).GetInstrumentAsync("NOPE", "ACC-A", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData("TRADEABLE", MarketStatus.Open)]
    [InlineData("CLOSED", MarketStatus.Closed)]
    [InlineData("SOMETHING_NEW", MarketStatus.Unknown)]
    public async Task MarketStatus_OnlyDocumentedValueIsOpen(string raw, MarketStatus expected)
    {
        _fake.MarketDetails["EURUSD"] = CapitalFake.MarketDetail("EURUSD", status: raw);
        (await (await Adapter()).GetInstrumentAsync("EURUSD", "ACC-A", CancellationToken.None))!.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("\"minDealSize\":{\"unit\":\"POINTS\",\"value\":0.5},", "")]
    [InlineData("\"minSizeIncrement\":{\"unit\":\"POINTS\",\"value\":0.1},", "")]
    [InlineData("\"minStopOrProfitDistance\":{\"unit\":\"PERCENTAGE\",\"value\":0.2},", "")]
    [InlineData("\"value\":0.5", "\"value\":0")]
    [InlineData("\"unit\":\"POINTS\",\"value\":0.5", "\"unit\":\"WHAT\",\"value\":0.5")]
    [InlineData("\"decimalPlacesFactor\":4", "\"decimalPlacesFactor\":-1")]
    [InlineData("\"epic\":\"EURUSD\"", "\"epic\":\"OTHER\"")]
    [InlineData("\"currency\":\"USD\",", "")]
    public async Task IncompleteMetadata_IsMalformed(string from, string to)
    {
        var compact = CapitalFake.MarketDetail("EURUSD");
        compact.Should().Contain(from);
        _fake.MarketDetails["EURUSD"] = compact.Replace(from, to);
        var adapter = await Adapter();
        (await Fails(() => adapter.GetInstrumentAsync("EURUSD", "ACC-A", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task Search_ParsesFlatMarkets_AndEscapesTheQuery()
    {
        var results = await (await Adapter()).SearchInstrumentsAsync("EUR/USD & x", 10, CancellationToken.None);
        results.Single().Epic.Should().Be("EURUSD");
        results.Single().Status.Should().Be(MarketStatus.Open);
        _fake.Requests.Last().PathAndQuery.Should().Contain("searchTerm=EUR%2FUSD%20%26%20x");
    }

    [Fact]
    public async Task Search_EmptyQuery_MakesNoRequest()
    {
        (await (await Adapter()).SearchInstrumentsAsync("  ", 10, CancellationToken.None)).Should().BeEmpty();
        _fake.Requests.Should().BeEmpty();
    }

    // Positions and working orders

    [Fact]
    public async Task Positions_MapDocumentedFields()
    {
        var p = (await (await Adapter()).GetPositionsAsync("ACC-A", CancellationToken.None)).Single();
        p.PositionId.Should().Be("deal-EURUSD");
        p.Epic.Should().Be("EURUSD");
        p.Direction.Should().Be(OrderSide.Sell);
        p.Size.Should().Be(2m);
        p.OpenLevel.Should().Be(1.2m);
        p.CurrentLevel.Should().Be(1.11m); // a short closes at the offer
        p.UnrealizedPnL.Should().Be(-3.5m);
        p.StopLevel.Should().Be(1.3m);
        p.ProfitLevel.Should().BeNull();
        p.OpenedUtc.Should().Be(new DateTimeOffset(2026, 10, 30, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task WorkingOrders_MapDocumentedFields()
    {
        var o = (await (await Adapter()).GetWorkingOrdersAsync("ACC-B", CancellationToken.None)).Single();
        o.OrderId.Should().Be("wo-GOLD");
        o.AccountId.Should().Be("ACC-B");
        o.Direction.Should().Be(OrderSide.Buy);
        o.Type.Should().Be(OrderType.Limit);
        o.Size.Should().Be(3m);
        o.Level.Should().Be(1.05m);
        o.StopDistance.Should().Be(20m);
        o.ProfitDistance.Should().Be(40m);
    }

    [Fact]
    public async Task EmptyAccount_IsEmpty()
    {
        (await (await Adapter()).GetWorkingOrdersAsync("ACC-A", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerFailure_IsNotReportedAsEmpty()
    {
        Func<HttpResponseMessage?> down = () => CapitalFake.Json(HttpStatusCode.InternalServerError, "secret body");
        _fake.Script("GET /api/v1/positions", down, down, down);
        var adapter = await Adapter();
        var ex = await Fails(() => adapter.GetPositionsAsync("ACC-A", CancellationToken.None));
        ex.Kind.Should().Be(BrokerErrorKind.Unavailable);
        ex.Message.Should().NotContain("secret body");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"positions\":[{\"market\":{\"epic\":\"E\"}}]}")]
    [InlineData("{\"positions\":[{\"position\":{\"dealId\":\"d\",\"size\":1,\"upl\":0,\"direction\":\"FLAT\",\"level\":1,\"createdDateUTC\":\"2026-01-01T00:00:00\"},\"market\":{\"epic\":\"E\"}}]}")]
    [InlineData("{\"positions\":[{\"position\":{\"dealId\":\"d\",\"size\":0,\"upl\":0,\"direction\":\"BUY\",\"level\":1,\"createdDateUTC\":\"2026-01-01T00:00:00\"},\"market\":{\"epic\":\"E\"}}]}")]
    [InlineData("{\"positions\":[{\"position\":{\"dealId\":\"d\",\"size\":1,\"direction\":\"BUY\",\"level\":1,\"createdDateUTC\":\"2026-01-01T00:00:00\"},\"market\":{\"epic\":\"E\"}}]}")]
    public async Task MalformedPositions_AreRejected(string json)
    {
        _fake.Positions["ACC-A"] = json;
        var adapter = await Adapter();
        (await Fails(() => adapter.GetPositionsAsync("ACC-A", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"workingOrders\":[{\"workingOrderData\":{\"dealId\":\"d\",\"direction\":\"BUY\",\"epic\":\"E\",\"orderSize\":1,\"orderLevel\":1,\"orderType\":\"TRAILING\",\"createdDateUTC\":\"2026-01-01T00:00:00\"}}]}")]
    public async Task MalformedWorkingOrders_AreRejected(string json)
    {
        _fake.WorkingOrders["ACC-A"] = json;
        var adapter = await Adapter();
        (await Fails(() => adapter.GetWorkingOrdersAsync("ACC-A", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    // Quotes and candles

    [Fact]
    public async Task Quotes_ParseTimestampAndHealth()
    {
        var q = (await (await Adapter()).GetQuotesAsync(new[] { "EURUSD" }, CancellationToken.None)).Single();
        q.Bid.Should().Be(1.1m);
        q.Ask.Should().Be(1.1002m);
        q.SourceTimestampUtc.Should().Be(_clock.UtcNow);
        q.Health.Should().Be(FeedHealth.Healthy);
    }

    [Fact]
    public async Task DelayedQuote_IsNotHealthy()
    {
        _fake.MarketsJson = "{\"markets\":[{\"epic\":\"E\",\"bid\":1,\"offer\":2,\"updateTimeUTC\":\"2026-10-31T12:00:00\",\"delayTime\":15}]}";
        (await (await Adapter()).GetQuotesAsync(new[] { "E" }, CancellationToken.None)).Single().Health.Should().Be(FeedHealth.Stale);
    }

    [Theory]
    [InlineData("{\"markets\":[{\"epic\":\"E\",\"bid\":2,\"offer\":1,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}]}")]
    [InlineData("{\"markets\":[{\"epic\":\"E\",\"bid\":0,\"offer\":1,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}]}")]
    [InlineData("{\"markets\":[{\"epic\":\"E\",\"offer\":1,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}]}")]
    [InlineData("{\"markets\":[{\"epic\":\"E\",\"bid\":1,\"offer\":2}]}")]
    [InlineData("{\"markets\":[{\"epic\":\"E\",\"bid\":1,\"offer\":2,\"updateTimeUTC\":\"soon\"}]}")]
    public async Task MalformedQuotes_AreRejected(string json)
    {
        _fake.MarketsJson = json;
        var adapter = await Adapter();
        (await Fails(() => adapter.GetQuotesAsync(new[] { "E" }, CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task QuoteRequests_AreBatchedAtTheDocumentedLimit()
    {
        _fake.MarketsJson = "{\"markets\":[]}";
        await (await Adapter()).GetQuotesAsync(Enumerable.Range(0, 120).Select(i => $"E{i}"), CancellationToken.None);
        _fake.Requests.Count(r => r.Path == "/api/v1/markets").Should().Be(3);
    }

    [Fact]
    public async Task Candles_UseMidPrices_AndMarkTheFormingBarOpen()
    {
        _fake.PricesJson = """
            {"prices":[
              {"snapshotTimeUTC":"2026-10-31T11:50:00","openPrice":{"bid":1,"ask":3},"closePrice":{"bid":2,"ask":4},"highPrice":{"bid":5,"ask":7},"lowPrice":{"bid":0.5,"ask":1.5},"lastTradedVolume":9},
              {"snapshotTimeUTC":"2026-10-31T12:00:00","openPrice":{"bid":1,"ask":3},"closePrice":{"bid":2,"ask":4},"highPrice":{"bid":5,"ask":7},"lowPrice":{"bid":0.5,"ask":1.5}}
            ]}
            """;
        var candles = await (await Adapter()).GetHistoryAsync("EURUSD", "5m", 2, CancellationToken.None);
        candles[0].Open.Should().Be(2m);
        candles[0].Close.Should().Be(3m);
        candles[0].High.Should().Be(6m);
        candles[0].Low.Should().Be(1m);
        candles[0].Volume.Should().Be(9m);
        candles[0].IsClosed.Should().BeTrue();
        candles[1].IsClosed.Should().BeFalse();
        candles[1].Volume.Should().BeNull();
        _fake.Requests.Last().PathAndQuery.Should().Contain("resolution=MINUTE_5").And.Contain("max=2");
    }

    [Fact]
    public async Task Candles_UnsupportedTimeframe_MakesNoRequest()
    {
        var adapter = await Adapter();
        (await Fails(() => adapter.GetHistoryAsync("EURUSD", "7m", 2, CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.InvalidRequest);
        _fake.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Candles_MissingPriceSide_IsMalformed()
    {
        _fake.PricesJson = "{\"prices\":[{\"snapshotTimeUTC\":\"2026-10-31T11:50:00\",\"openPrice\":{\"bid\":1},\"closePrice\":{\"bid\":2,\"ask\":4},\"highPrice\":{\"bid\":5,\"ask\":7},\"lowPrice\":{\"bid\":0.5,\"ask\":1.5}}]}";
        var adapter = await Adapter();
        (await Fails(() => adapter.GetHistoryAsync("EURUSD", "5m", 2, CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    // Authentication and retries

    [Fact]
    public async Task BadCredentials_FailAsAuthentication_WithoutRetryOrBody()
    {
        _fake.Script("POST /api/v1/session", () => CapitalFake.Json(HttpStatusCode.Unauthorized, "{\"errorCode\":\"secret-detail\"}"));
        var adapter = await Adapter();
        var ex = await Fails(() => adapter.CreateSessionAsync(CancellationToken.None));
        ex.Kind.Should().Be(BrokerErrorKind.Authentication);
        ex.Message.Should().NotContain("secret-detail");
        _fake.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task ExpiredSession_ReauthenticatesOnce()
    {
        var adapter = await Adapter();
        _fake.Script("GET /api/v1/accounts", () => CapitalFake.Json(HttpStatusCode.Unauthorized, "{}"));
        (await adapter.GetAccountsAsync(CancellationToken.None)).Should().HaveCount(2);
        _fake.SessionCount.Should().Be(2);
        _fake.Requests.Last().Cst.Should().Be("cst-2");
    }

    [Fact]
    public async Task RepeatedUnauthorized_StopsAfterOneReauthentication()
    {
        Func<HttpResponseMessage?> denied = () => CapitalFake.Json(HttpStatusCode.Unauthorized, "{}");
        _fake.Script("GET /api/v1/accounts", denied, denied, denied, denied);
        var adapter = await Adapter();
        (await Fails(() => adapter.GetAccountsAsync(CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.Authentication);
        _fake.SessionCount.Should().Be(2);
    }

    [Fact]
    public async Task IdleSession_IsReplacedBeforeExpiry()
    {
        var adapter = await Adapter();
        await adapter.GetAccountsAsync(CancellationToken.None);
        _clock.UtcNow += TimeSpan.FromMinutes(9.5);
        await adapter.GetAccountsAsync(CancellationToken.None);
        _fake.SessionCount.Should().Be(2);
    }

    [Fact]
    public async Task RateLimit_HonorsRetryAfter()
    {
        var limited = CapitalFake.Json(HttpStatusCode.TooManyRequests, "{}");
        limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        _fake.Script("GET /api/v1/accounts", () => limited);
        (await (await Adapter()).GetAccountsAsync(CancellationToken.None)).Should().HaveCount(2);
        _delays.Should().Contain(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task RateLimit_GivesUpAfterThreeAttempts()
    {
        Func<HttpResponseMessage?> limited = () => CapitalFake.Json(HttpStatusCode.TooManyRequests, "{}");
        _fake.Script("GET /api/v1/accounts", limited, limited, limited, limited);
        var adapter = await Adapter();
        (await Fails(() => adapter.GetAccountsAsync(CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.RateLimited);
        _fake.Requests.Count(r => r.Path == "/api/v1/accounts").Should().Be(3);
    }

    [Fact]
    public async Task SessionRateLimit_RetriesLogin()
    {
        _fake.Script("POST /api/v1/session", () => CapitalFake.Json(HttpStatusCode.TooManyRequests, "{}"));
        (await (await Adapter()).CreateSessionAsync(CancellationToken.None)).CstToken.Should().Be("cst-1");
    }

    [Fact]
    public async Task BadRequest_IsNotRetried()
    {
        _fake.Script("GET /api/v1/accounts", () => CapitalFake.Json(HttpStatusCode.BadRequest, "{}"));
        var adapter = await Adapter();
        (await Fails(() => adapter.GetAccountsAsync(CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.InvalidRequest);
        _fake.Requests.Count(r => r.Path == "/api/v1/accounts").Should().Be(1);
    }

    [Fact]
    public async Task TransientNetworkFailure_IsRetried_ThenReportedSafely()
    {
        _fake.Script("GET /api/v1/accounts", () => throw new HttpRequestException("https://secret-url"));
        (await (await Adapter()).GetAccountsAsync(CancellationToken.None)).Should().HaveCount(2);

        Func<HttpResponseMessage?> broken = () => throw new HttpRequestException("https://secret-url");
        _fake.Script("GET /api/v1/accounts", broken, broken, broken);
        var adapter = await Adapter();
        var ex = await Fails(() => adapter.GetAccountsAsync(CancellationToken.None));
        ex.Kind.Should().Be(BrokerErrorKind.Unavailable);
        ex.Message.Should().NotContain("secret-url");
    }

    [Fact]
    public async Task Requests_AreSpacedToTheDocumentedRate()
    {
        var adapter = await Adapter();
        await adapter.GetAccountsAsync(CancellationToken.None);
        await adapter.GetAccountsAsync(CancellationToken.None);
        _delays.Should().Contain(d => d > TimeSpan.Zero && d <= TimeSpan.FromMilliseconds(100));
    }

    // Execution stays disabled

    private OrderIntent Intent(OrderType type = OrderType.Market, decimal? level = null) =>
        new("i1", "ACC-A", TradingEnvironment.Demo, "EURUSD", OrderSide.Buy, type, 1.5m, level, 1.0m, 1.3m, null, _clock.UtcNow, "1", "snap", "s");

    [Fact]
    public async Task Execution_IsDisabled_AndMakesNoRequest()
    {
        var adapter = await Adapter();
        adapter.ExecutionEnabled.Should().BeFalse();
        (await adapter.SubmitOrderAsync(Intent(), CancellationToken.None)).State.Should().Be(OrderState.Rejected);
        (await adapter.CancelOrderAsync("o", "ACC-A", CancellationToken.None)).Should().BeFalse();
        (await adapter.ClosePositionAsync("p", null, CancellationToken.None)).Should().BeFalse();
        _fake.Requests.Should().BeEmpty();
    }

    [Fact]
    public void OrderContract_BuildsDocumentedMarketRequest()
    {
        var (path, json) = CapitalComOrderContract.BuildRequest(Intent());
        path.Should().Be("/api/v1/positions");
        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("epic").GetString().Should().Be("EURUSD");
        root.GetProperty("direction").GetString().Should().Be("BUY");
        root.GetProperty("size").GetDecimal().Should().Be(1.5m);
        root.GetProperty("stopLevel").GetDecimal().Should().Be(1.0m);
        root.GetProperty("profitLevel").GetDecimal().Should().Be(1.3m);
    }

    [Theory]
    [InlineData(OrderType.Limit, "LIMIT")]
    [InlineData(OrderType.Stop, "STOP")]
    public void OrderContract_BuildsDocumentedWorkingOrder(OrderType type, string expected)
    {
        var (path, json) = CapitalComOrderContract.BuildRequest(Intent(type, 1.2m));
        path.Should().Be("/api/v1/workingorders");
        var root = JsonDocument.Parse(json).RootElement;
        root.GetProperty("type").GetString().Should().Be(expected);
        root.GetProperty("level").GetDecimal().Should().Be(1.2m);
    }

    [Fact]
    public void OrderContract_WorkingOrderWithoutLevel_IsRefused() =>
        Assert.Throws<BrokerException>(() => CapitalComOrderContract.BuildRequest(Intent(OrderType.Limit)));

    [Theory]
    [InlineData(HttpStatusCode.OK, "{\"dealReference\":\"o_1\"}", OrderState.BrokerPending)]
    [InlineData(HttpStatusCode.BadRequest, "{}", OrderState.Rejected)]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", OrderState.Unknown)]
    [InlineData(HttpStatusCode.RequestTimeout, "{}", OrderState.Unknown)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", OrderState.Unknown)]
    [InlineData(HttpStatusCode.OK, "{}", OrderState.Unknown)]
    [InlineData(HttpStatusCode.OK, "not json", OrderState.Unknown)]
    public void OrderContract_OnlyAnExplicitRefusalMeansNotPlaced(HttpStatusCode status, string body, OrderState expected) =>
        CapitalComOrderContract.ParseSubmission(status, body, Intent(), _clock.UtcNow).State.Should().Be(expected);

    [Fact]
    public async Task Confirmation_KeepsEveryAffectedDeal()
    {
        _fake.Script("GET /api/v1/confirms/o_1", () => CapitalFake.Json(HttpStatusCode.OK,
            "{\"date\":\"2026-10-31T12:00:00\",\"status\":\"OPEN\",\"dealStatus\":\"ACCEPTED\",\"epic\":\"EURUSD\",\"dealReference\":\"o_1\",\"dealId\":\"d0\",\"affectedDeals\":[{\"dealId\":\"d1\",\"status\":\"OPENED\"},{\"dealId\":\"d2\",\"status\":\"FULLY_CLOSED\"}],\"level\":1.1,\"size\":1,\"direction\":\"BUY\"}"));
        var c = (await (await Adapter()).GetConfirmationAsync("o_1", CancellationToken.None))!;
        c.State.Should().Be(OrderState.Confirmed);
        c.BrokerDealId.Should().Be("d0");
        c.AffectedDeals!.Select(d => d.DealId).Should().Equal("d1", "d2");
    }

    [Fact]
    public async Task Confirmation_NotFound_IsNull_AndFailureIsNot()
    {
        var adapter = await Adapter();
        (await adapter.GetConfirmationAsync("o_missing", CancellationToken.None)).Should().BeNull();
        Func<HttpResponseMessage?> down = () => CapitalFake.Json(HttpStatusCode.BadGateway, "{}");
        _fake.Script("GET /api/v1/confirms/o_2", down, down, down);
        (await Fails(() => adapter.GetConfirmationAsync("o_2", CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.Unavailable);
    }

    [Theory]
    [InlineData("REJECTED", OrderState.Rejected)]
    [InlineData("PENDING_REVIEW", OrderState.Unknown)]
    public void Confirmation_OtherStatuses(string dealStatus, OrderState expected) =>
        CapitalComOrderContract.ParseConfirmation($"{{\"dealReference\":\"o_1\",\"dealStatus\":\"{dealStatus}\"}}", "o_1", _clock.UtcNow).State.Should().Be(expected);

    [Fact]
    public void Confirmation_ForAnotherReference_IsMalformed() =>
        Assert.Throws<BrokerException>(() => CapitalComOrderContract.ParseConfirmation("{\"dealReference\":\"o_other\",\"dealStatus\":\"ACCEPTED\"}", "o_1", _clock.UtcNow));

    // Evidence probe

    [Fact]
    public async Task Probe_RecordsOnlySanitizedEvidence_AndNeverCallsOrderEndpoints()
    {
        _fake.MarketDetails["EURUSD"] = CapitalFake.MarketDetail("EURUSD");
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-B", "EURUSD", CancellationToken.None);

        evidence.OrderEndpointsCalled.Should().BeFalse();
        _fake.OrderRequests.Should().BeEmpty();
        _fake.Requests.Select(r => r.Method).Distinct().Should().BeSubsetOf(new[] { "GET", "POST", "PUT" });
        _fake.Requests.Where(r => r.Method != "GET").Select(r => r.Path).Distinct().Should().Equal("/api/v1/session");
        evidence.Steps.Should().Contain(s => s.Name == "positions" && s.Passed);
        var json = JsonSerializer.Serialize(evidence);
        foreach (var secret in new[] { "TEST-KEY", "TEST-USER", "TEST-PASS", "cst-1", "sec-1", "ACC-B", "500", "1100" })
            json.Should().NotContain(secret);
        evidence.AccountIdMasked.Should().Be(DemoReadOnlyProbe.Mask("ACC-B"));
    }

    [Fact]
    public async Task Probe_ReportsFailureByKindOnly()
    {
        Func<HttpResponseMessage?> down = () => CapitalFake.Json(HttpStatusCode.InternalServerError, "secret body");
        _fake.Script("GET /api/v1/positions", down, down, down);
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        evidence.AllPassed.Should().BeFalse();
        evidence.Steps.Single(s => s.Name == "positions").Detail.Should().Be("Unavailable");
        JsonSerializer.Serialize(evidence).Should().NotContain("secret body");
    }

    // Runtime configuration

    [Fact]
    public void RuntimeOptions_DefaultToSimulation()
    {
        var options = BrokerRuntimeOptions.Parse(null, null, null);
        options.Mode.Should().Be(BrokerMode.Simulation);
        options.Environment.Should().Be(TradingEnvironment.Demo);
        options.AccountId.Should().Be(BrokerRuntimeOptions.SimulationAccountId);
    }

    [Theory]
    [InlineData("capital_demo", "demo", null)]
    [InlineData("capital_live", "demo", "A")]
    [InlineData("live", "demo", "A")]
    [InlineData("capital_demo", "live", "A")]
    [InlineData("simulation", "live", "A")]
    public void RuntimeOptions_RefuseInvalidAndLive(string mode, string env, string? account) =>
        Assert.Throws<InvalidOperationException>(() => BrokerRuntimeOptions.Parse(mode, env, account));

    [Fact]
    public void Factory_SelectsTheRealAdapterOnlyWhenExplicit()
    {
        var http = new SingleClientFactory(new HttpClient(_fake));
        new BrokerAdapterFactory(_secrets, http, _clock, new GuidIdGenerator(), BrokerRuntimeOptions.Parse(null, null, null))
            .Create(TradingEnvironment.Demo).Should().BeOfType<SimulatedBrokerAdapter>();
        new BrokerAdapterFactory(_secrets, http, _clock, new GuidIdGenerator(), BrokerRuntimeOptions.Parse("capital_demo", "demo", "ACC-A"))
            .Create(TradingEnvironment.Demo).Should().BeOfType<CapitalComBrokerAdapter>();
        Assert.Throws<InvalidOperationException>(() =>
            new BrokerAdapterFactory(_secrets, http, _clock, new GuidIdGenerator(), BrokerRuntimeOptions.Parse("capital_demo", "demo", "ACC-A")).Create(TradingEnvironment.Live));
    }

    private sealed class SingleClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public SingleClientFactory(HttpClient client) { _client = client; }
        public HttpClient CreateClient(string name) => _client;
    }
}
