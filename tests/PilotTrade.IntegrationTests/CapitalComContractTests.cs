namespace PilotTrade.IntegrationTests;

public class FakeClock : IClock
{
    public FakeClock(DateTimeOffset now) { UtcNow = now; }
    public DateTimeOffset UtcNow { get; set; }
}

public class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _fn;
    public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> fn) { _fn = fn; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(_fn(request));
}

public class CapitalComContractTests
{
    [Fact]
    public async Task Session_ParsesCstAndSecurityToken()
    {
        var handler = new FakeHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Headers.Add("CST", "CST123");
            resp.Headers.Add("X-SECURITY-TOKEN", "SEC456");
            resp.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            return resp;
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo-api-capital.backend-capital.com") };
        var secrets = new InMemorySecretStore();
        await secrets.SetAsync("capital:Demo:api_key", "KEY", CancellationToken.None);
        await secrets.SetAsync("capital:Demo:identifier", "USER", CancellationToken.None);
        await secrets.SetAsync("capital:Demo:password", "PASS", CancellationToken.None);
        var adapter = new CapitalComBrokerAdapter(
            new BrokerEnvironmentConfig(TradingEnvironment.Demo, "https://demo-api-capital.backend-capital.com", "ACC", false),
            secrets, http, new FakeClock(DateTimeOffset.UtcNow), new GuidIdGenerator());
        var session = await adapter.CreateSessionAsync(CancellationToken.None);
        session.CstToken.Should().Be("CST123");
        session.SecurityToken.Should().Be("SEC456");
    }

    [Fact]
    public async Task SearchInstruments_ParsesMarkets()
    {
        var handler = new FakeHandler(req =>
        {
            var json = "{\"markets\":[{\"epic\":\"EURUSD\",\"instrumentName\":\"EUR/USD\"}]}";
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            return resp;
        });
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://demo-api-capital.backend-capital.com") };
        var secrets = new InMemorySecretStore();
        await secrets.SetAsync("capital:Demo:api_key", "KEY", CancellationToken.None);
        await secrets.SetAsync("capital:Demo:identifier", "USER", CancellationToken.None);
        await secrets.SetAsync("capital:Demo:password", "PASS", CancellationToken.None);
        // pre-create session by faking session endpoint too
        var adapter = new CapitalComBrokerAdapter(
            new BrokerEnvironmentConfig(TradingEnvironment.Demo, "https://demo-api-capital.backend-capital.com", "ACC", false),
            secrets, http, new FakeClock(DateTimeOffset.UtcNow), new GuidIdGenerator());
        // Inject session via reflection? Instead call search triggers EnsureSession which calls /session; our handler returns markets for any path. Need session response with headers.
        // Make handler return headers for /session and markets for /markets
        var h2 = new FakeHandler(req =>
        {
            if (req.RequestUri!.PathAndQuery.Contains("/session"))
            {
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Headers.Add("CST", "C"); r.Headers.Add("X-SECURITY-TOKEN", "S");
                r.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                return r;
            }
            var r2 = new HttpResponseMessage(HttpStatusCode.OK);
            r2.Content = new StringContent("{\"markets\":[{\"epic\":\"EURUSD\",\"instrumentName\":\"EUR/USD\"}]}", System.Text.Encoding.UTF8, "application/json");
            return r2;
        });
        var http2 = new HttpClient(h2) { BaseAddress = new Uri("https://demo-api-capital.backend-capital.com") };
        var adapter2 = new CapitalComBrokerAdapter(
            new BrokerEnvironmentConfig(TradingEnvironment.Demo, "https://demo-api-capital.backend-capital.com", "ACC", false),
            secrets, http2, new FakeClock(DateTimeOffset.UtcNow), new GuidIdGenerator());
        var res = await adapter2.SearchInstrumentsAsync("EUR", 10, CancellationToken.None);
        res.Should().HaveCount(1);
        res[0].Epic.Should().Be("EURUSD");
    }
}