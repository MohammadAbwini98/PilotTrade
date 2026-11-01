namespace PilotTrade.IntegrationTests;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Api;

[Trait("Category", "OwnerAuthorization")]
public sealed class SecurityEndpointTests : IDisposable
{
    private readonly ApiTestHost _host = new();
    public void Dispose() => _host.Dispose();

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/api/security/csrf-token");
        return body.GetProperty("csrfToken").GetString()!;
    }

    private static HttpRequestMessage Post(string path, object body, string? origin, string? csrf)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        if (origin is not null) request.Headers.Add("Origin", origin);
        if (csrf is not null) request.Headers.Add(ApiSecurity.CsrfHeader, csrf);
        return request;
    }

    private static readonly object Credentials = new { apiKey = "NEW-KEY", apiPassword = "NEW-PASS", identifier = "NEW-USER", replace = false };

    [Theory]
    [InlineData("/api/runtime")]
    [InlineData("/api/accounts")]
    [InlineData("/api/account?accountId=SIM-ACC")]
    [InlineData("/api/positions?accountId=SIM-ACC")]
    [InlineData("/api/orders?accountId=SIM-ACC")]
    [InlineData("/api/demo/credential-status")]
    [InlineData("/api/security/csrf-token")]
    [InlineData("/api/instruments/search?q=EUR")]
    public async Task SensitiveReads_RequireTheOwnerToken(string path)
    {
        var client = _host.CreateClient();
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        client.DefaultRequestHeaders.Add(ApiSecurity.OwnerHeader, "wrong-token");
        (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Health_StaysPublic_AndCarriesNoAccountData()
    {
        var response = await _host.CreateClient().GetAsync("/health");
        response.EnsureSuccessStatusCode();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("SIM-ACC");
    }

    [Fact]
    public async Task OwnerToken_IsRefusedFromAnotherMachine()
    {
        var client = await _host.OwnerClientAsync();
        _host.RemoteAddress = IPAddress.Parse("192.168.1.20");
        (await client.GetAsync("/api/runtime")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OwnerToken_IsRefusedForAForeignHost_BlockingDnsRebinding()
    {
        var client = await _host.OwnerClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/runtime");
        request.Headers.Host = "evil.test";
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigureCredentials_WithoutOwner_IsRefused_EvenWithOriginAndCsrf()
    {
        var csrf = await CsrfAsync(await _host.OwnerClientAsync());
        var response = await _host.CreateClient().SendAsync(Post("/api/demo/configure-credentials", Credentials, "http://localhost", csrf));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _host.Secrets.GetAsync("capital:Demo:api_key", CancellationToken.None)).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("http://localhost.evil.test")]
    [InlineData("http://evil.test/localhost")]
    [InlineData("http://localhost@evil.test")]
    public async Task ConfigureCredentials_BadOrigin_IsRefused(string? origin)
    {
        var client = await _host.OwnerClientAsync();
        var response = await client.SendAsync(Post("/api/demo/configure-credentials", Credentials, origin, await CsrfAsync(client)));
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigureCredentials_MissingOrReusedCsrf_IsRefused()
    {
        var client = await _host.OwnerClientAsync();
        (await client.SendAsync(Post("/api/demo/configure-credentials", Credentials, "http://localhost", null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var csrf = await CsrfAsync(client);
        (await client.SendAsync(Post("/api/demo/configure-credentials", Credentials, "http://localhost", csrf))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.SendAsync(Post("/api/demo/configure-credentials", Credentials, "http://localhost", csrf))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ConfigureCredentials_NeverEchoesSecrets_AndRefusesSilentReplacement()
    {
        var client = await _host.OwnerClientAsync();
        var first = await client.SendAsync(Post("/api/demo/configure-credentials", Credentials, "http://localhost", await CsrfAsync(client)));
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        (await first.Content.ReadAsStringAsync()).Should().NotContain("NEW-");

        // Configuration and retrieval reach the same store.
        (await _host.Secrets.GetAsync("capital:Demo:password", CancellationToken.None)).Should().Be("NEW-PASS");
        var status = await client.GetStringAsync("/api/demo/credential-status");
        status.Should().Contain("\"apiKeyPresent\":true").And.NotContain("NEW-");

        var second = await client.SendAsync(Post("/api/demo/configure-credentials", Credentials, "http://localhost", await CsrfAsync(client)));
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadAsStringAsync()).Should().NotContain("NEW-");

        var replacement = new { apiKey = "K2", apiPassword = "P2", identifier = "I2", replace = true };
        (await client.SendAsync(Post("/api/demo/configure-credentials", replacement, "http://localhost", await CsrfAsync(client)))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _host.Secrets.GetAsync("capital:Demo:api_key", CancellationToken.None)).Should().Be("K2");
    }

    [Fact]
    public async Task CredentialStatus_ReturnsOnlyPresenceFlags()
    {
        await _host.StoreDemoCredentialsAsync();
        var json = await (await _host.OwnerClientAsync()).GetStringAsync("/api/demo/credential-status");
        json.Should().Be("{\"apiKeyPresent\":true,\"identifierPresent\":true,\"passwordPresent\":true}");
    }

    [Theory]
    [InlineData("http://localhost", "localhost", true)]
    [InlineData("http://localhost:5099", "localhost:5099", true)]
    [InlineData("http://127.0.0.1:5173", "127.0.0.1:5099", true)]
    [InlineData("http://[::1]:5099", "[::1]:5099", true)]
    [InlineData("http://localhost:9999", "localhost:5099", false)]
    [InlineData("http://localhost.evil.test", "localhost", false)]
    [InlineData("http://127.0.0.1.evil.test", "localhost", false)]
    [InlineData("http://evil.test/localhost", "localhost", false)]
    [InlineData("http://localhost@evil.test", "localhost", false)]
    [InlineData("http://user@localhost", "localhost", false)]
    [InlineData("http://localhost/", "localhost", false)]
    [InlineData("http://localhost/path", "localhost", false)]
    [InlineData("file://localhost", "localhost", false)]
    [InlineData("null", "localhost", false)]
    [InlineData("", "localhost", false)]
    public void OriginIsParsedStrictly(string origin, string host, bool allowed) =>
        ApiSecurity.IsAllowedOrigin(origin, host).Should().Be(allowed);

    [Fact]
    public async Task OpenApiDocument_IsServedWithThePatchedDependency()
    {
        var response = await _host.CreateClient().GetAsync("/openapi/v1.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        doc.GetProperty("paths").TryGetProperty("/api/runtime", out _).Should().BeTrue();
    }
}

public sealed class DemoApiFlowTests : IDisposable
{
    private readonly ApiTestHost _host = new("capital_demo", "demo", "ACC-A");
    public void Dispose() => _host.Dispose();

    private async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/security/csrf-token")).GetProperty("csrfToken").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(ApiSecurity.CsrfHeader, csrf);
        return await client.SendAsync(request);
    }

    private async Task<int> SelectAsync(HttpClient client, string epic)
    {
        var response = await PostAsync(client, "/api/instruments/select", new { accountId = "ACC-A", epic });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("selection").GetProperty("selectionVersion").GetInt32();
    }

    private async Task<JsonElement> SnapshotAsync(HttpClient client, string epic, int version) =>
        await (await PostAsync(client, "/api/market/snapshot", new { accountId = "ACC-A", epic, selectionVersion = version })).Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Runtime_ReportsDemo_WithExecutionDisabledAndLiveLocked()
    {
        var runtime = await (await _host.OwnerClientAsync()).GetFromJsonAsync<JsonElement>("/api/runtime");
        runtime.GetProperty("mode").GetString().Should().Be("CapitalDemo");
        runtime.GetProperty("executionEnabled").GetBoolean().Should().BeFalse();
        runtime.GetProperty("liveLocked").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData("capital_demo", "live", "A")]
    [InlineData("simulation", "live", null)]
    [InlineData("capital_live", "demo", "A")]
    public void LiveRequests_StopStartup(string mode, string env, string? account)
    {
        using var host = new ApiTestHost(mode, env, account);
        Assert.ThrowsAny<Exception>(() => host.CreateClient());
    }

    [Fact]
    public async Task MissingCredentials_Return409_WithoutCallingTheBroker()
    {
        var response = await (await _host.OwnerClientAsync()).GetAsync("/api/accounts");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _host.Capital.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task AccountAndExposure_AreServedForTheConfiguredAccount_WithoutTokens()
    {
        using var host = new ApiTestHost("capital_demo", "demo", "ACC-B");
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();

        var account = await client.GetStringAsync("/api/account?accountId=ACC-B");
        account.Should().Contain("ACC-B").And.Contain("\"marginUsed\":null");
        var positions = await client.GetStringAsync("/api/positions?accountId=ACC-B");
        positions.Should().Contain("deal-GOLD").And.Contain("\"currency\":\"USD\"");
        (await client.GetStringAsync("/api/orders?accountId=ACC-B")).Should().Contain("wo-GOLD");
        foreach (var text in new[] { account, positions }) text.Should().NotContain("cst-").And.NotContain("sec-").And.NotContain("TEST-");
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/account?accountId=ACC-B")]
    [InlineData("/api/positions?accountId=ACC-B")]
    [InlineData("/api/orders?accountId=ACC-B")]
    [InlineData("/api/instruments/selection?accountId=ACC-B")]
    [InlineData("/api/instruments/EURUSD?accountId=ACC-B")]
    [InlineData("/api/account?accountId=ACC-X")]
    public async Task AnotherAccount_IsRefused_WithoutCallingTheBroker(string path)
    {
        await _host.StoreDemoCredentialsAsync();
        var response = await (await _host.OwnerClientAsync()).GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"AccountNotConfigured\"}");
        _host.Capital.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/api/instruments/select")]
    [InlineData("/api/market/snapshot")]
    public async Task AnotherAccount_CannotSelectOrSnapshot(string path)
    {
        await _host.StoreDemoCredentialsAsync();
        var response = await PostAsync(await _host.OwnerClientAsync(), path, new { accountId = "ACC-B", epic = "EURUSD", selectionVersion = 1 });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _host.Capital.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task CapitalDemoMode_ResolvesTheRealAdapter_ForRoutesAndServices()
    {
        await _host.StoreDemoCredentialsAsync();
        var client = await _host.OwnerClientAsync();
        _host.Services.GetRequiredService<IBrokerAdapter>().Should().BeOfType<CapitalComBrokerAdapter>();
        var version = await SelectAsync(client, "EURUSD");
        (await SnapshotAsync(client, "EURUSD", version)).GetProperty("outcome").GetString().Should().Be("SNAPSHOT_READY");
        _host.Capital.Requests.Select(r => r.Path).Should().Contain("/api/v1/prices/EURUSD").And.Contain("/api/v1/markets/EURUSD");
        _host.Capital.Requests.Should().Contain(r => r.PathAndQuery.Contains("resolution=HOUR"));
        _host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task BrokerErrors_ReturnFixedCodes_NeverTheBrokerBody()
    {
        await _host.StoreDemoCredentialsAsync();
        Func<HttpResponseMessage?> down = () => CapitalFake.Json(HttpStatusCode.InternalServerError, "secret broker body");
        _host.Capital.Script("GET /api/v1/positions", down, down, down);
        var response = await (await _host.OwnerClientAsync()).GetAsync("/api/positions?accountId=ACC-A");
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"Unavailable\"}");
    }

    [Fact]
    public async Task SwitchingInstrument_OutdatesEarlierRequests_AndServesTheNewOne()
    {
        await _host.StoreDemoCredentialsAsync();
        var client = await _host.OwnerClientAsync();

        var first = await SelectAsync(client, "EURUSD");
        (await SnapshotAsync(client, "EURUSD", first)).GetProperty("outcome").GetString().Should().Be("SNAPSHOT_READY");

        var second = await SelectAsync(client, "GOLD");
        second.Should().BeGreaterThan(first);
        var outdated = await SnapshotAsync(client, "EURUSD", first);
        outdated.GetProperty("outcome").GetString().Should().Be("NO_TRADE");
        outdated.GetProperty("snapshot").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task UnknownInstrument_CannotBeSelected()
    {
        await _host.StoreDemoCredentialsAsync();
        var client = await _host.OwnerClientAsync();
        (await PostAsync(client, "/api/instruments/select", new { accountId = "ACC-A", epic = "NOPE" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task StaleFeed_IsNoTrade()
    {
        await _host.StoreDemoCredentialsAsync();
        _host.Capital.MarketsJson = "{\"marketDetails\":[" + CapitalFake.QuoteDetail("EURUSD", "{\"bid\":1.1,\"offer\":1.1002,\"updateTime\":\"2020-01-01T00:00:00\",\"delayTime\":0}") + "]}";
        var client = await _host.OwnerClientAsync();
        var version = await SelectAsync(client, "EURUSD");
        var result = await SnapshotAsync(client, "EURUSD", version);
        result.GetProperty("outcome").GetString().Should().Be("NO_TRADE");
        result.GetProperty("reason").GetString().Should().Contain("Stale quote");
    }

    [Fact]
    public async Task TestConnection_ReturnsSanitizedEvidence()
    {
        await _host.StoreDemoCredentialsAsync();
        var response = await PostAsync(await _host.OwnerClientAsync(), "/api/demo/test-connection", new { });
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync();
        text.Should().NotContain("TEST-").And.NotContain("cst-").And.NotContain("ACC-A");
        _host.Capital.OrderRequests.Should().BeEmpty();
    }
}
