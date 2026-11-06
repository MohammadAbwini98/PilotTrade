namespace PilotTrade.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Api;
using PilotTrade.Application.Controls;
using Microsoft.EntityFrameworkCore;
using PilotTrade.Infrastructure.Persistence;

// Task 02-05: the Risk Center's read-only risk view and the owner's kill switch.
public sealed class KillSwitchEndpointTests : IDisposable
{
    private readonly string _db = ApiTestHost.NewDatabasePath();
    public void Dispose() => ApiTestHost.DeleteDatabase(_db);

    private ApiTestHost Host(string account = "ACC-A") => new("capital_demo", "demo", account, _db);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body, bool withCsrf = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "http://localhost");
        if (withCsrf)
            request.Headers.Add(ApiSecurity.CsrfHeader, (await client.GetFromJsonAsync<JsonElement>("/api/security/csrf-token")).GetProperty("csrfToken").GetString()!);
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> Engage(HttpClient client, string reason = "maintenance", string account = "ACC-A") =>
        PostAsync(client, "/api/kill-switch/engage", new { accountId = account, reason });

    private static Task<HttpResponseMessage> Clear(HttpClient client, string confirmation = KillSwitchService.ClearPhrase, string account = "ACC-A") =>
        PostAsync(client, "/api/kill-switch/clear", new { accountId = account, confirmation });

    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Endpoints_NeedTheOwner_TheConfiguredAccount_AndCsrf()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        var anonymous = host.CreateClient();
        (await anonymous.GetAsync("/api/kill-switch?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/api/risk?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var client = await host.OwnerClientAsync();
        (await client.GetAsync("/api/kill-switch?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/risk?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Engage(client, account: "ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(client, "/api/kill-switch/engage", new { accountId = "ACC-A", reason = "x" }, withCsrf: false)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        host.Services.GetRequiredService<ITradingControl>().State.KillSwitchEngaged.Should().BeFalse();
    }

    [Fact]
    public async Task Engage_PersistsWithItsReasonAcrossRestart_AndIsAudited()
    {
        using (var first = Host())
        {
            await first.StoreDemoCredentialsAsync();
            var client = await first.OwnerClientAsync();
            var engaged = await Engage(client, "unexpected fills");
            engaged.StatusCode.Should().Be(HttpStatusCode.OK);
            (await Json(engaged)).GetProperty("reason").GetString().Should().Be("Owner: unexpected fills");
            using var scope = first.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PilotTradeDbContext>();
            (await db.AuditEvents.CountAsync(a => a.Category == "KillSwitch" && a.Action == "Engaged")).Should().Be(1);
        }

        using var second = Host();
        await second.StoreDemoCredentialsAsync();
        var after = await (await second.OwnerClientAsync()).GetFromJsonAsync<JsonElement>("/api/kill-switch?accountId=ACC-A");
        after.GetProperty("engaged").GetBoolean().Should().BeTrue();
        after.GetProperty("reason").GetString().Should().Be("Owner: unexpected fills");
        after.GetProperty("changedBy").GetString().Should().Be("owner");
        after.GetProperty("changedUtc").ValueKind.Should().Be(JsonValueKind.String);
        second.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Engage_WorksWhenTheBrokerFails_AndTheRiskViewReportsUnknown()
    {
        // No credentials are stored, so every broker read fails.
        using var host = Host();
        var client = await host.OwnerClientAsync();

        var risk = await client.GetAsync("/api/risk?accountId=ACC-A");
        risk.StatusCode.Should().Be(HttpStatusCode.OK);
        var view = await Json(risk);
        view.GetProperty("readErrors").GetArrayLength().Should().BeGreaterThan(0);
        view.GetProperty("accountFields").EnumerateArray().Should().OnlyContain(f => f.GetProperty("provenance").GetString() == "Unknown");
        view.GetProperty("policyEditable").GetBoolean().Should().BeFalse();

        (await Engage(client, "broker unreachable")).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Services.GetRequiredService<ITradingControl>().State.KillSwitchEngaged.Should().BeTrue();

        // A clear cannot pass while the broker account and its working orders cannot be read.
        var clear = await Clear(client);
        clear.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var failed = (await Json(clear)).GetProperty("checks").EnumerateArray().Where(c => !c.GetProperty("passed").GetBoolean()).Select(c => c.GetProperty("name").GetString()).ToList();
        failed.Should().Contain(new[] { "Broker account matches", "No open working orders" });
        host.Services.GetRequiredService<ITradingControl>().State.KillSwitchEngaged.Should().BeTrue();
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task InvalidReasonAndWrongPhrase_AreRejected()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();
        (await Engage(client, "  ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Engage(client, new string('x', KillSwitchService.MaxReasonLength + 1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Services.GetRequiredService<ITradingControl>().State.KillSwitchEngaged.Should().BeFalse();

        (await Engage(client)).EnsureSuccessStatusCode();
        (await Clear(client, "yes")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        host.Services.GetRequiredService<ITradingControl>().State.KillSwitchEngaged.Should().BeTrue();
    }

    [Fact]
    public async Task Clear_IsRefusedWhileWorkingOrdersAreOpen()
    {
        using var host = Host("ACC-B");
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();
        (await Engage(client, account: "ACC-B")).EnsureSuccessStatusCode();
        var clear = await Clear(client, account: "ACC-B");
        clear.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Json(clear)).GetProperty("checks").EnumerateArray()
            .Should().Contain(c => c.GetProperty("name").GetString() == "No open working orders" && !c.GetProperty("passed").GetBoolean());
        var state = await client.GetFromJsonAsync<JsonElement>("/api/kill-switch?accountId=ACC-B");
        state.GetProperty("engaged").GetBoolean().Should().BeTrue();
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Clear_SucceedsOnlyWhenEveryCheckPasses_AndEnablesNothing()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();
        (await Engage(client)).EnsureSuccessStatusCode();
        var state = await client.GetFromJsonAsync<JsonElement>("/api/kill-switch?accountId=ACC-A");
        state.GetProperty("clearChecks").EnumerateArray().Should().OnlyContain(c => c.GetProperty("passed").GetBoolean());

        (await Clear(client)).StatusCode.Should().Be(HttpStatusCode.OK);
        host.Services.GetRequiredService<ITradingControl>().State.KillSwitchEngaged.Should().BeFalse();
        (await Clear(client)).StatusCode.Should().Be(HttpStatusCode.Conflict, "there is nothing left to clear");
        var runtime = await client.GetFromJsonAsync<JsonElement>("/api/runtime");
        runtime.GetProperty("executionEnabled").GetBoolean().Should().BeFalse();
        runtime.GetProperty("liveLocked").GetBoolean().Should().BeTrue();
        host.Capital.OrderRequests.Should().BeEmpty();
    }
}
