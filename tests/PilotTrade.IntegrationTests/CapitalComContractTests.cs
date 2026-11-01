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
        q.SourceTimestampUtc.Should().BeCloseTo(_clock.UtcNow, TimeSpan.FromSeconds(1)); // reading the offset waits on the rate limit
        q.Health.Should().Be(FeedHealth.Healthy);
    }

    [Theory]
    [InlineData("2026-10-31T11:58:00", 0, FeedHealth.Stale)]
    [InlineData("2026-10-31T12:05:00", 0, FeedHealth.Unknown)]
    [InlineData("2026-10-31T12:00:00", 5, FeedHealth.Stale)]
    public async Task Quotes_HealthFollowsTheSourceTimestampAndDelay(string updateTimeUtc, int delayTime, FeedHealth expected)
    {
        _fake.MarketsJson = Quote($"{{\"bid\":1,\"offer\":1.0001,\"updateTimeUTC\":\"{updateTimeUtc}\",\"delayTime\":{delayTime}}}", "EURUSD");
        var q = (await (await Adapter()).GetQuotesAsync(new[] { "EURUSD" }, CancellationToken.None)).Single();
        q.Health.Should().Be(expected);
    }

    // The response sample documented for GET /markets?epics=, time zone comes from the documented GET /session sample.
    private const string DocumentedEpicsSample = """
        {"marketDetails":[{
          "instrument":{"epic":"SILVER","symbol":"SILVER","expiry":"-","name":"Silver","lotSize":1,"type":"COMMODITIES","guaranteedStopAllowed":true,"streamingPricesAvailable":true,"currency":"USD","marginFactor":10,"marginFactorUnit":"PERCENTAGE"},
          "dealingRules":{"minDealSize":{"unit":"POINTS","value":1},"maxDealSize":{"unit":"POINTS","value":10000},"marketOrderPreference":"AVAILABLE_DEFAULT_OFF","trailingStopsPreference":"NOT_AVAILABLE"},
          "snapshot":{"marketStatus":"TRADEABLE","netChange":-0.627,"percentageChange":-0.24,"updateTime":"2022-04-06T11:21:55.542","delayTime":0,"bid":22.046,"offer":22.066,"high":22.098,"low":21.926,"decimalPlacesFactor":3,"scalingFactor":1,"marketModes":["REGULAR"]}
        }]}
        """;

    private static string Quote(string snapshot, string epic = "E") => "{\"marketDetails\":[" + CapitalFake.QuoteDetail(epic, snapshot) + "]}";

    [Fact]
    public async Task Quotes_ParseTheDocumentedEpicsSample_ConvertingLocalTimeWithTheSessionOffset()
    {
        _fake.MarketsJson = DocumentedEpicsSample;
        var q = (await (await Adapter()).GetQuotesAsync(new[] { "SILVER" }, CancellationToken.None)).Single();
        q.Epic.Should().Be("SILVER");
        q.Bid.Should().Be(22.046m);
        q.Ask.Should().Be(22.066m);
        q.SourceTimestampUtc.Should().Be(new DateTimeOffset(2022, 4, 6, 8, 21, 55, 542, TimeSpan.Zero));
    }

    [Fact]
    public async Task Quotes_PreferAnExplicitUtcField_WithoutReadingTheSession()
    {
        _fake.MarketsJson = Quote("{\"bid\":1,\"offer\":2,\"updateTime\":\"2026-10-31T15:00:00\",\"updateTimeUTC\":\"2026-10-31T12:00:00\"}");
        var q = (await (await Adapter()).GetQuotesAsync(new[] { "E" }, CancellationToken.None)).Single();
        q.SourceTimestampUtc.Should().Be(_clock.UtcNow);
        _fake.Requests.Should().NotContain(r => r.Method == "GET" && r.Path == "/api/v1/session");
    }

    [Theory]
    [InlineData("{\"clientId\":\"c\",\"accountId\":\"ACC-A\"}")]
    [InlineData("{\"accountId\":\"ACC-A\",\"timezoneOffset\":\"3\"}")]
    [InlineData("{\"accountId\":\"ACC-A\",\"timezoneOffset\":99}")]
    public async Task Quotes_WithoutAProvenSessionOffset_AreRejected(string session)
    {
        _fake.SessionDetailsJson = session;
        _fake.MarketsJson = Quote("{\"bid\":1,\"offer\":2,\"updateTime\":\"2026-10-31T15:00:00\"}");
        var adapter = await Adapter();
        (await Fails(() => adapter.GetQuotesAsync(new[] { "E" }, CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task Quotes_RefuseTheSearchShapeForAnEpicsQuery()
    {
        _fake.MarketsJson = "{\"markets\":[{\"epic\":\"E\",\"bid\":1,\"offer\":2,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}]}";
        var adapter = await Adapter();
        (await Fails(() => adapter.GetQuotesAsync(new[] { "E" }, CancellationToken.None))).Message.Should().EndWith("marketDetails");
    }

    [Fact]
    public async Task DelayedQuote_IsNotHealthy()
    {
        _fake.MarketsJson = Quote("{\"bid\":1,\"offer\":2,\"updateTimeUTC\":\"2026-10-31T12:00:00\",\"delayTime\":15}");
        (await (await Adapter()).GetQuotesAsync(new[] { "E" }, CancellationToken.None)).Single().Health.Should().Be(FeedHealth.Stale);
    }

    [Theory]
    [InlineData("{\"bid\":2,\"offer\":1,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}")]
    [InlineData("{\"bid\":0,\"offer\":1,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}")]
    [InlineData("{\"offer\":1,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}")]
    [InlineData("{\"bid\":1,\"offer\":2}")]
    [InlineData("{\"bid\":1,\"offer\":2,\"updateTimeUTC\":\"soon\"}")]
    [InlineData("{\"bid\":1,\"offer\":2,\"updateTime\":\"soon\"}")]
    public async Task MalformedQuotes_AreRejected(string snapshot)
    {
        _fake.MarketsJson = Quote(snapshot);
        var adapter = await Adapter();
        (await Fails(() => adapter.GetQuotesAsync(new[] { "E" }, CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task Quotes_ForAnEpicNotRequested_AreRejected()
    {
        _fake.MarketsJson = Quote("{\"bid\":1,\"offer\":2,\"updateTimeUTC\":\"2026-10-31T12:00:00\"}", "OTHER");
        var adapter = await Adapter();
        (await Fails(() => adapter.GetQuotesAsync(new[] { "E" }, CancellationToken.None))).Kind.Should().Be(BrokerErrorKind.MalformedResponse);
    }

    [Fact]
    public async Task QuoteRequests_AreBatchedAtTheDocumentedLimit()
    {
        _fake.MarketsJson = "{\"marketDetails\":[]}";
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

    [Fact]
    public async Task Probe_PassesAgainstDocumentedShapes_AndRecordsTheRealAdapter()
    {
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        evidence.Steps.Where(s => !s.Passed).Should().BeEmpty();
        evidence.AllPassed.Should().BeTrue();
        evidence.Adapter.Should().Be(nameof(CapitalComBrokerAdapter));
        evidence.ApiHost.Should().Be(new Uri(BaseUri).Host);
    }

    [Fact]
    public async Task Probe_RecordsTheEpicAndTheBrokersInstrumentName()
    {
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "GOLD", CancellationToken.None);
        evidence.RequestedEpic.Should().Be("GOLD");
        evidence.Steps.Single(s => s.Name == "instrumentMetadata").Detail.Should().Contain("instrumentName=GOLD name");
    }

    [Fact]
    public async Task Probe_BrokerAnswersForAnotherEpic_FailsTheMetadataStep()
    {
        _fake.MarketDetails["EURUSD"] = CapitalFake.MarketDetail("GOLD");
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var step = evidence.Steps.Single(s => s.Name == "instrumentMetadata");
        step.Passed.Should().BeFalse();
        step.Detail.Should().Be("MalformedResponse: instrument.epic");
    }

    [Fact]
    public async Task Probe_ActiveAccountComesFromTheBroker_AndAMismatchFails()
    {
        // The account read proves the session first; the probe's own read then gets a different answer.
        _fake.Script("GET /api/v1/session", () => null, () => CapitalFake.Json(HttpStatusCode.OK, "{\"accountId\":\"ACC-OTHER\"}"));
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var step = evidence.Steps.Single(s => s.Name == "activeSessionAccount");
        step.Passed.Should().BeFalse();
        step.Detail.Should().Be("activeAccountMatched=False");
        evidence.AllPassed.Should().BeFalse();
        JsonSerializer.Serialize(evidence).Should().NotContain("ACC-OTHER");
    }

    [Theory]
    [InlineData("ACC-A", "configured")]
    [InlineData(null, "autoSelected")]
    [InlineData(" ", "autoSelected")]
    public async Task Probe_RecordsWhereTheAccountCameFrom(string? configured, string expected)
    {
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync(configured, "EURUSD", CancellationToken.None);
        evidence.AccountSource.Should().Be(expected);
        evidence.Steps.Single(s => s.Name == "activeSessionAccount").Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Probe_NoAccountAvailable_RecordsNoSource()
    {
        _fake.AccountsJson = "{\"accounts\":[]}";
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync(null, "EURUSD", CancellationToken.None);
        evidence.AccountSource.Should().Be("none");
        evidence.AllPassed.Should().BeFalse();
    }

    [Fact]
    public async Task Probe_NamesTheMalformedField_WithoutItsValue()
    {
        _fake.MarketsJson = "{\"markets\":[{\"epic\":\"EURUSD\",\"bid\":1234.5}]}";
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        evidence.Steps.Single(s => s.Name == "quote").Detail.Should().Be("MalformedResponse: marketDetails");
        JsonSerializer.Serialize(evidence).Should().NotContain("1234.5");
    }

    [Fact]
    public async Task Probe_ClosedMarket_CannotPassFreshness()
    {
        _fake.MarketDetails["EURUSD"] = CapitalFake.MarketDetail("EURUSD", status: "CLOSED");
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        evidence.AllPassed.Should().BeFalse();
        evidence.Steps.Single(s => s.Name == "instrumentMetadata").Passed.Should().BeTrue();
        foreach (var step in new[] { "quote", "candles" })
        {
            evidence.Steps.Single(s => s.Name == step).Passed.Should().BeFalse();
            evidence.Steps.Single(s => s.Name == step).Detail.Should().Contain("marketStatus=Closed");
        }
    }

    [Theory]
    [InlineData("2026-10-31T11:58:00")] // older than the policy allows
    [InlineData("2026-10-31T12:05:00")] // in the future, which is how a wrong time zone conversion would show
    public async Task Probe_QuoteOutsideTheFreshnessWindow_Fails(string updateTimeUtc)
    {
        _fake.MarketsJson = Quote($"{{\"bid\":1,\"offer\":1.001,\"updateTimeUTC\":\"{updateTimeUtc}\"}}", "EURUSD");
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var quote = evidence.Steps.Single(s => s.Name == "quote");
        quote.Passed.Should().BeFalse();
        quote.Detail.Should().Contain("fresh=False");
    }

    [Fact]
    public async Task Probe_StaleCandles_Fail()
    {
        _fake.PricesJson = """{"prices":[{"snapshotTimeUTC":"2026-10-30T10:00:00","openPrice":{"bid":1,"ask":1.1},"closePrice":{"bid":1,"ask":1.1},"highPrice":{"bid":1.2,"ask":1.3},"lowPrice":{"bid":0.9,"ask":1}}]}""";
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        evidence.Steps.Single(s => s.Name == "candles").Passed.Should().BeFalse();
    }

    [Fact]
    public async Task Positions_CarryTheBrokerCurrency_AndLeaveItUnknownWhenAbsent()
    {
        var adapter = await Adapter();
        (await adapter.GetPositionsAsync("ACC-A", CancellationToken.None)).Single().Currency.Should().Be("USD");
        _fake.Positions["ACC-A"] = CapitalFake.PositionsFor("EURUSD").Replace("\"currency\":\"USD\",", "");
        (await adapter.GetPositionsAsync("ACC-A", CancellationToken.None)).Single().Currency.Should().BeNull();
    }

    [Fact]
    public async Task Probe_RunsTheApplicationSnapshotPath_ForBothTimeframes()
    {
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var step = evidence.Steps.Single(s => s.Name == "marketSnapshot");
        step.Passed.Should().BeTrue();
        step.Detail.Should().Be("5m=40 closed candle(s); 1h=40 closed candle(s)");
        _fake.Requests.Should().Contain(r => r.PathAndQuery.Contains("resolution=HOUR"));
    }

    [Fact]
    public async Task Probe_ClosedMarket_FailsTheSnapshot_WithTheSafeReason()
    {
        _fake.MarketDetails["EURUSD"] = CapitalFake.MarketDetail("EURUSD", status: "CLOSED");
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var step = evidence.Steps.Single(s => s.Name == "marketSnapshot");
        step.Passed.Should().BeFalse();
        step.Detail.Should().Be("NO_TRADE: Instrument not open: Closed");
    }

    [Fact]
    public async Task Probe_RecordsTheSpreadAndItsLimit_ForTheAssetClass()
    {
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var quote = evidence.Steps.Single(s => s.Name == "quote");
        quote.Detail.Should().Contain("spreadPercent=0.0182").And.Contain("spreadLimitPercent=0.05").And.Contain("spreadWithinPolicy=True");
    }

    [Fact]
    public async Task Probe_SpreadWideForTheClass_FailsTheQuoteStep()
    {
        _fake.MarketsJson = Quote($"{{\"bid\":1,\"offer\":1.001,\"updateTimeUTC\":\"{_clock.UtcNow.UtcDateTime:yyyy-MM-ddTHH:mm:ss}\"}}", "EURUSD");
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var quote = evidence.Steps.Single(s => s.Name == "quote");
        quote.Passed.Should().BeFalse();
        quote.Detail.Should().Contain("spreadWithinPolicy=False");
    }

    [Fact]
    public async Task Probe_ReportsMissingBars_WithoutFailingOnThem()
    {
        var now = _clock.UtcNow.UtcDateTime;
        var last = new DateTime(now.Ticks - now.Ticks % TimeSpan.FromMinutes(5).Ticks, DateTimeKind.Utc);
        var offsets = Enumerable.Range(1, 30).Append(33);
        var prices = offsets.Reverse().Select(i => $$$"""{"snapshotTimeUTC":"{{{last.AddMinutes(-5 * i):yyyy-MM-ddTHH:mm:ss}}}","openPrice":{"bid":1,"ask":1.1},"closePrice":{"bid":1,"ask":1.1},"highPrice":{"bid":1.2,"ask":1.3},"lowPrice":{"bid":0.9,"ask":1}}""");
        _fake.PricesJson = $$"""{"prices":[{{string.Join(",", prices)}}]}""";
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        var candles = evidence.Steps.Single(s => s.Name == "candles");
        candles.Detail.Should().Contain("missingBars=2");
        candles.Passed.Should().BeTrue();
    }

    [Fact]
    public async Task Probe_SearchFailure_FailsItsStep()
    {
        // The quote step reads the same path first, so it is let through.
        Func<HttpResponseMessage?> refused = () => CapitalFake.Json(HttpStatusCode.BadRequest, "{\"errorCode\":\"error.invalid.searchTerm\"}");
        _fake.Script("GET /api/v1/markets", () => null, refused);
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        evidence.Steps.Single(s => s.Name == "instrumentSearch").Passed.Should().BeFalse();
        evidence.AllPassed.Should().BeFalse();
    }

    [Fact]
    public async Task Probe_MeasuresOrderEndpointCalls_FromTheAdapter()
    {
        var adapter = await Adapter();
        adapter.NonSessionWriteRequests.Should().Be(0);
        await adapter.GetAccountAsync("ACC-B", CancellationToken.None); // includes PUT /session
        adapter.NonSessionWriteRequests.Should().Be(0);
    }

    // Evidence run output path

    private sealed class Scratch : IDisposable
    {
        public string Folder { get; } = Directory.CreateTempSubdirectory("pilottrade-evidence-").FullName;
        public void Dispose() => Directory.Delete(Folder, true);
    }

    private async Task<(int Code, int AdapterCreated, string Error)> RunEvidence(params string[] args)
    {
        var created = 0;
        var adapter = await Adapter();
        var error = new StringWriter();
        var code = await DemoEvidenceRunner.RunAsync(args, "ACC-A", () => { created++; return adapter; }, _clock, new StringWriter(), error, CancellationToken.None);
        return (code, created, error.ToString());
    }

    [Fact]
    public async Task Evidence_MissingOutputFolder_ExitsBeforeAnyBrokerRequest()
    {
        using var scratch = new Scratch();
        var path = Path.Combine(scratch.Folder, "missing", "evidence.json");
        var (code, created, error) = await RunEvidence(DemoEvidenceRunner.Flag, path);
        code.Should().Be(DemoEvidenceRunner.BadOutputPath);
        created.Should().Be(0);
        _fake.Requests.Should().BeEmpty();
        error.Should().Contain("No broker request was made");
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Evidence_OutputPathThatIsAFolder_ExitsBeforeAnyBrokerRequest()
    {
        using var scratch = new Scratch();
        var (code, created, _) = await RunEvidence(DemoEvidenceRunner.Flag, scratch.Folder);
        code.Should().Be(DemoEvidenceRunner.BadOutputPath);
        created.Should().Be(0);
        _fake.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(DemoEvidenceRunner.Flag)]
    [InlineData(DemoEvidenceRunner.Flag, "--epic", "EURUSD")]
    [InlineData(DemoEvidenceRunner.Flag, " ")]
    public async Task Evidence_WithoutAnOutputPath_ExitsBeforeAnyBrokerRequest(params string[] args)
    {
        var (code, created, error) = await RunEvidence(args);
        code.Should().Be(DemoEvidenceRunner.BadOutputPath);
        created.Should().Be(0);
        _fake.Requests.Should().BeEmpty();
        error.Should().Contain("Usage");
    }

    [Fact]
    public async Task Evidence_ExistingFolder_RunsTheProbeAndWritesTheFile()
    {
        using var scratch = new Scratch();
        var path = Path.Combine(scratch.Folder, "evidence.json");
        var (code, created, _) = await RunEvidence(DemoEvidenceRunner.Flag, path, "--epic", "GOLD");
        code.Should().Be(DemoEvidenceRunner.Success);
        created.Should().Be(1);
        _fake.Requests.Should().NotBeEmpty();
        _fake.Requests.Should().Contain(r => r.PathAndQuery.Contains("epics=GOLD"));
        Directory.GetFiles(scratch.Folder).Should().Equal(path);
        var text = await File.ReadAllTextAsync(path);
        text.Should().Contain(nameof(CapitalComBrokerAdapter));
        foreach (var secret in new[] { "TEST-KEY", "TEST-USER", "TEST-PASS", "cst-", "sec-", "ACC-A" }) text.Should().NotContain(secret);
    }

    [Fact]
    public async Task Evidence_ExistingFile_IsKept_AndExitsBeforeAnyBrokerRequest()
    {
        using var scratch = new Scratch();
        var path = Path.Combine(scratch.Folder, "evidence.json");
        await File.WriteAllTextAsync(path, "earlier run");
        var (code, created, error) = await RunEvidence(DemoEvidenceRunner.Flag, path);
        code.Should().Be(DemoEvidenceRunner.BadOutputPath);
        created.Should().Be(0);
        _fake.Requests.Should().BeEmpty();
        error.Should().Contain("already exists");
        (await File.ReadAllTextAsync(path)).Should().Be("earlier run");
    }

    [Fact]
    public async Task Evidence_FileThatAppearsDuringTheRun_IsKept_AndTheRunIsPrinted()
    {
        using var scratch = new Scratch();
        var path = Path.Combine(scratch.Folder, "evidence.json");
        var adapter = await Adapter();
        var error = new StringWriter();
        var code = await DemoEvidenceRunner.RunAsync(new[] { DemoEvidenceRunner.Flag, path }, "ACC-A", () => { File.WriteAllText(path, "other run"); return adapter; }, _clock, new StringWriter(), error, CancellationToken.None);
        code.Should().Be(DemoEvidenceRunner.WriteFailed);
        (await File.ReadAllTextAsync(path)).Should().Be("other run");
        error.ToString().Should().Contain(nameof(CapitalComBrokerAdapter));
    }

    [Fact]
    public async Task Evidence_RecordsCommit_TreeState_UtcTime_ActualEpic_AndAccountSource()
    {
        using var scratch = new Scratch();
        var path = Path.Combine(scratch.Folder, "evidence.json");
        (await RunEvidence(DemoEvidenceRunner.Flag, path, "--epic", "GOLD")).Code.Should().Be(DemoEvidenceRunner.Success);
        var evidence = JsonDocument.Parse(await File.ReadAllTextAsync(path)).RootElement;
        evidence.GetProperty("GitSha").GetString().Should().MatchRegex("^([0-9a-f]{40}|unknown)$");
        evidence.GetProperty("WorkingTree").GetString().Should().BeOneOf("clean", "dirty", "unknown");
        evidence.GetProperty("RunUtc").GetDateTimeOffset().Offset.Should().Be(TimeSpan.Zero);
        evidence.GetProperty("RequestedEpic").GetString().Should().Be("GOLD");
        evidence.GetProperty("InstrumentEpic").GetString().Should().Be("GOLD");
        evidence.GetProperty("AccountSource").GetString().Should().Be("configured");
    }

    [Fact]
    public async Task Probe_MetadataFailure_LeavesTheActualEpicUnrecorded()
    {
        _fake.MarketDetails["EURUSD"] = CapitalFake.MarketDetail("GOLD");
        var evidence = await new DemoReadOnlyProbe(await Adapter(), _clock).RunAsync("ACC-A", "EURUSD", CancellationToken.None);
        evidence.InstrumentEpic.Should().BeNull();
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
