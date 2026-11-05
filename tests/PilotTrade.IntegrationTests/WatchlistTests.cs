namespace PilotTrade.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Api;
using PilotTrade.Infrastructure.Persistence;

// Task 02-03: the owner's watchlist is stored with a version and an audit event; the monitor reads quotes and never orders.
public sealed class WatchlistTests : IDisposable
{
    private readonly string _db = ApiTestHost.NewDatabasePath();
    public void Dispose() => ApiTestHost.DeleteDatabase(_db);

    private ApiTestHost Demo() => new("capital_demo", "demo", "ACC-A", _db);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/security/csrf-token")).GetProperty("csrfToken").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(ApiSecurity.CsrfHeader, csrf);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Save(HttpClient client, int expectedVersion, params string[] epics) =>
        PostAsync(client, "/api/watchlist", new { accountId = "ACC-A", epics, expectedVersion });

    [Fact]
    public async Task Watchlist_NeedsTheOwnerToken_TheConfiguredAccount_AndCsrf()
    {
        using var host = Demo();
        await host.StoreDemoCredentialsAsync();
        (await host.CreateClient().GetAsync("/api/watchlist?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await host.CreateClient().GetAsync("/api/market/watch?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var client = await host.OwnerClientAsync();
        (await client.GetAsync("/api/watchlist?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/market/watch?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(client, "/api/watchlist", new { accountId = "ACC-B", epics = new[] { "EURUSD" }, expectedVersion = 0 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        // Without the CSRF token and Origin the write is refused.
        (await client.PostAsJsonAsync("/api/watchlist", new { accountId = "ACC-A", epics = new[] { "EURUSD" }, expectedVersion = 0 })).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task Save_IsVersioned_Audited_AndRefusesAStaleVersion()
    {
        using var host = Demo();
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();

        var empty = await client.GetFromJsonAsync<JsonElement>("/api/watchlist?accountId=ACC-A");
        empty.GetProperty("version").GetInt32().Should().Be(0);
        empty.GetProperty("epics").GetArrayLength().Should().Be(0);

        var first = await Save(client, 0, "EURUSD", "GOLD");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await first.Content.ReadFromJsonAsync<JsonElement>();
        saved.GetProperty("version").GetInt32().Should().Be(1);
        saved.GetProperty("epics").EnumerateArray().Select(e => e.GetString()).Should().Equal("EURUSD", "GOLD");

        // A second tab still holding version 0 must not overwrite the newer list.
        var stale = await Save(client, 0, "EURUSD");
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflict = await stale.Content.ReadFromJsonAsync<JsonElement>();
        conflict.GetProperty("error").GetString().Should().Be("WatchlistChanged");
        conflict.GetProperty("current").GetProperty("version").GetInt32().Should().Be(1);

        (await Save(client, 1, "GOLD")).StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PilotTradeDbContext>();
        var audits = await db.AuditEvents.Where(a => a.Action == "WatchlistUpdated").ToListAsync();
        audits.Should().HaveCount(2, "the refused write leaves no audit event");
        audits.Should().OnlyContain(a => a.Category == "Settings");
        audits.Should().Contain(a => a.DetailsJson.Contains("\"after\":[\"GOLD\"]"));
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(new[] { "EURUSD", "EURUSD" }, "An epic is listed twice.")]
    [InlineData(new[] { "EUR USD" }, "An epic is not valid.")]
    [InlineData(new[] { "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K" }, "At most 10 instruments.")]
    [InlineData(new[] { "NOSUCH" }, "Unknown instrument: NOSUCH")]
    public async Task Save_RefusesInvalidLists_AndStoresNothing(string[] epics, string reason)
    {
        using var host = Demo();
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();
        var response = await Save(client, 0, epics);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reason").GetString().Should().Be(reason);
        (await client.GetFromJsonAsync<JsonElement>("/api/watchlist?accountId=ACC-A")).GetProperty("version").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Watchlist_SurvivesRestart()
    {
        using (var first = Demo())
        {
            await first.StoreDemoCredentialsAsync();
            (await Save(await first.OwnerClientAsync(), 0, "GOLD", "EURUSD")).EnsureSuccessStatusCode();
        }
        using var second = Demo();
        var restored = await (await second.OwnerClientAsync()).GetFromJsonAsync<JsonElement>("/api/watchlist?accountId=ACC-A");
        restored.GetProperty("version").GetInt32().Should().Be(1);
        restored.GetProperty("epics").EnumerateArray().Select(e => e.GetString()).Should().Equal("GOLD", "EURUSD");
    }

    [Fact]
    public async Task Watch_ReportsEveryRow_AndAClosedMarketIsBlocked()
    {
        using var host = Demo();
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();
        (await Save(client, 0, "EURUSD", "GOLD")).EnsureSuccessStatusCode();

        var watch = await client.GetFromJsonAsync<JsonElement>("/api/market/watch?accountId=ACC-A");
        watch.GetProperty("watchlistVersion").GetInt32().Should().Be(1);
        watch.GetProperty("maxQuoteAgeSeconds").GetDouble().Should().Be(60);
        var rows = watch.GetProperty("rows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("epic").GetString()).Should().Equal("EURUSD", "GOLD");
        foreach (var row in rows)
        {
            row.GetProperty("quote").GetProperty("bid").GetDecimal().Should().BeGreaterThan(0);
            row.GetProperty("quote").TryGetProperty("sourceTimestampUtc", out _).Should().BeTrue();
            row.GetProperty("dealing").GetProperty("pricePrecision").ValueKind.Should().Be(JsonValueKind.Number);
            row.GetProperty("maxSpreadPercent").ValueKind.Should().Be(JsonValueKind.Number);
        }

        host.Capital.MarketDetails["GOLD"] = CapitalFake.MarketDetail("GOLD", currency: "EUR", status: "CLOSED");
        var closed = (await client.GetFromJsonAsync<JsonElement>("/api/market/watch?accountId=ACC-A")).GetProperty("rows")[1];
        closed.GetProperty("quoteChecksPassed").GetBoolean().Should().BeFalse();
        closed.GetProperty("blockers").EnumerateArray().Select(b => b.GetString()).Should().Contain(b => b!.StartsWith("Instrument not open"));

        // Monitoring changes neither the selection nor any order.
        (await client.GetAsync("/api/instruments/selection?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Simulator_WatchCoversEveryAssetClass()
    {
        using var host = new ApiTestHost(databasePath: _db);
        var client = await host.OwnerClientAsync();
        var runtime = await client.GetFromJsonAsync<JsonElement>("/api/runtime");
        var account = runtime.GetProperty("accountId").GetString()!;
        var saved = await PostAsync(client, "/api/watchlist", new { accountId = account, epics = new[] { "EURUSD", "BTCUSD", "GOLD", "US500", "AAPL" }, expectedVersion = 0 });
        saved.EnsureSuccessStatusCode();
        var rows = (await client.GetFromJsonAsync<JsonElement>($"/api/market/watch?accountId={account}")).GetProperty("rows").EnumerateArray().ToList();
        rows.Select(r => r.GetProperty("assetClass").GetString()).Should().Equal("Fx", "CryptoCfd", "Commodity", "Index", "Share");
        rows.Select(r => r.GetProperty("dealing").GetProperty("pricePrecision").GetInt32()).Should().Equal(5, 2, 2, 1, 2);
    }

    [Fact]
    public async Task Policy_ShowsTheBuiltInLimits_AsNotEditable()
    {
        using var host = Demo();
        await host.StoreDemoCredentialsAsync();
        var policy = await (await host.OwnerClientAsync()).GetFromJsonAsync<JsonElement>("/api/market/policy");
        policy.GetProperty("source").GetString().Should().Be("Built-in defaults");
        policy.GetProperty("editable").GetBoolean().Should().BeFalse();
        policy.GetProperty("maxQuoteAgeSeconds").GetDouble().Should().Be(60);
        var spreads = policy.GetProperty("maxSpreadPercent");
        spreads.GetProperty("Fx").GetDecimal().Should().Be(0.05m);
        spreads.GetProperty("CryptoCfd").GetDecimal().Should().Be(1m);
        spreads.GetProperty("Share").GetDecimal().Should().Be(0.5m);
    }
}
