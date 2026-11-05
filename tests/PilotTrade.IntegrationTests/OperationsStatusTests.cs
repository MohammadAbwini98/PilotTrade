namespace PilotTrade.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Api;
using PilotTrade.Application.Controls;

// Task 02-02: GET /api/status reports the server's own safety state for the overview, and changes nothing.
public sealed class OperationsStatusTests : IDisposable
{
    private readonly string _db = ApiTestHost.NewDatabasePath();
    public void Dispose() => ApiTestHost.DeleteDatabase(_db);

    private ApiTestHost Host() => new("capital_demo", "demo", "ACC-A", _db);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/security/csrf-token")).GetProperty("csrfToken").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(ApiSecurity.CsrfHeader, csrf);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Status_NeedsTheOwnerToken_AndTheConfiguredAccount()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        (await host.CreateClient().GetAsync("/api/status?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var client = await host.OwnerClientAsync();
        (await client.GetAsync("/api/status?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Status_ReportsServerState_AndFeedOnlyForTheSelectedInstrument()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();

        var before = await client.GetFromJsonAsync<JsonElement>("/api/status?accountId=ACC-A");
        before.GetProperty("killSwitchEngaged").GetBoolean().Should().BeFalse();
        before.GetProperty("feed").ValueKind.Should().Be(JsonValueKind.Null);
        before.TryGetProperty("checkedUtc", out _).Should().BeTrue();
        // With Demo execution disabled the broker holdings are not compared, so reconciliation is reported as not complete.
        before.GetProperty("reconciliationComplete").GetBoolean().Should().BeFalse();
        before.GetProperty("suspensionReason").GetString().Should().NotBeNullOrEmpty();

        var selected = await PostAsync(client, "/api/instruments/select", new { accountId = "ACC-A", epic = "EURUSD" });
        selected.EnsureSuccessStatusCode();
        var unknown = await client.GetFromJsonAsync<JsonElement>("/api/status?accountId=ACC-A");
        unknown.GetProperty("feed").GetProperty("epic").GetString().Should().Be("EURUSD");
        unknown.GetProperty("feed").GetProperty("health").GetString().Should().Be("Unknown");
        unknown.GetProperty("feed").GetProperty("lastQuoteUtc").ValueKind.Should().Be(JsonValueKind.Null);

        var version = (await selected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("selection").GetProperty("selectionVersion").GetInt32();
        (await PostAsync(client, "/api/market/snapshot", new { accountId = "ACC-A", epic = "EURUSD", selectionVersion = version })).EnsureSuccessStatusCode();
        var quoted = await client.GetFromJsonAsync<JsonElement>("/api/status?accountId=ACC-A");
        quoted.GetProperty("feed").GetProperty("lastQuoteUtc").ValueKind.Should().Be(JsonValueKind.String);

        host.Services.GetRequiredService<ITradingControl>().EngageKillSwitch();
        var stopped = await client.GetFromJsonAsync<JsonElement>("/api/status?accountId=ACC-A");
        stopped.GetProperty("killSwitchEngaged").GetBoolean().Should().BeTrue();

        host.Capital.OrderRequests.Should().BeEmpty();
    }
}
