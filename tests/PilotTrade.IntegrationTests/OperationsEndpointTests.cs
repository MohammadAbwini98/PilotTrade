namespace PilotTrade.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Application.Controls;
using PilotTrade.Domain.Decision;

// Task 02-07: audit explorer, alerts and diagnostics. All three are read-only; every test also checks no order reached the broker.
public sealed class OperationsEndpointTests : IDisposable
{
    private readonly string _db = ApiTestHost.NewDatabasePath();
    public void Dispose() => ApiTestHost.DeleteDatabase(_db);

    private ApiTestHost Host(string mode = "capital_demo") => new(mode, "demo", "ACC-A", _db);

    private static async Task Audit(ApiTestHost host, string id, DateTimeOffset at, string category, string action, string? account, string? epic = null, string? correlation = null, string details = "{}") =>
        await host.Services.GetRequiredService<IAuditLogger>().LogAsync(new AuditEvent(id, at, category, action, "owner", "demo", account, epic, correlation, details, true), CancellationToken.None);

    private static AiStepRecord Step(string provider, AiStepStatus status, string? problem) =>
        new(provider, status, null, null, null, null, null, null, null, null, problem);

    private static DecisionEvent Decision(string id, string account, DateTimeOffset at, AiStepStatus claude, string? problem) =>
        new(id, account, TradingEnvironment.Demo, "EURUSD", 1, "snap-1", at, at, at, Step("Claude API", claude, problem), Step("JEV/TypeSafe AI", AiStepStatus.NotRun, null),
            null, null, DecisionCandidate.NoTrade, "AI step failed", null, "strategy", "1", "audit-" + id);

    [Fact]
    public async Task AllThree_NeedTheOwnerAndTheConfiguredAccount_AndHaveNoWriteRoutes()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        var anonymous = host.CreateClient();
        var client = await host.OwnerClientAsync();
        foreach (var path in new[] { "/api/audit", "/api/alerts", "/api/diagnostics" })
        {
            (await anonymous.GetAsync(path + "?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            (await client.GetAsync(path + "?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden, path);
            (await client.GetAsync(path + "?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.OK, path);
            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
            {
                var request = new HttpRequestMessage(method, path + "?accountId=ACC-A") { Content = JsonContent.Create(new { accountId = "ACC-A" }) };
                request.Headers.Add("Origin", "http://localhost");
                ((int)(await client.SendAsync(request)).StatusCode).Should().BeGreaterThanOrEqualTo(400, $"{method} {path}");
            }
        }
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Audit_ValidatesFilters()
    {
        using var host = Host("simulation");
        var client = await host.OwnerClientAsync();
        foreach (var bad in new[] { "limit=0", "limit=101", "offset=-1", "from=yesterday", "from=2026-11-07T10:00:00Z&to=2026-11-06T10:00:00Z",
                     "category=Kill%20Switch", "category=" + new string('a', 65), "epic=EUR%20USD", "correlation=a%20b", "q=" + new string('x', 65) })
            (await client.GetAsync("/api/audit?accountId=ACC-A&" + bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
    }

    [Fact]
    public async Task Audit_ShowsThisAccountAndSystemEventsOnly_MasksIdentifiers_AndNeverReturnsDetails()
    {
        using var host = Host("simulation");
        var t0 = DateTimeOffset.UtcNow.AddHours(-3);
        await Audit(host, "audit-own-secret-0001", t0, "KillSwitch", "Engage", "ACC-A", correlation: "corr-own-secret-7777", details: "{\"token\":\"TOKEN-PROTECTED-1\"}");
        await Audit(host, "audit-sys-secret-0002", t0.AddMinutes(1), "Demo", "TestConnection", null);
        await Audit(host, "audit-other-secret-0003", t0.AddMinutes(2), "KillSwitch", "Clear", "ACC-B", details: "{\"note\":\"OTHER-ACCOUNT-ONLY\"}");

        var response = await (await host.OwnerClientAsync()).GetAsync("/api/audit?accountId=ACC-A");
        var raw = await response.Content.ReadAsStringAsync();
        foreach (var hidden in new[] { "audit-own-secret-0001", "audit-sys-secret-0002", "audit-other-secret-0003", "corr-own-secret-7777", "TOKEN-PROTECTED-1", "OTHER-ACCOUNT-ONLY", "ACC-B", "detailsJson" })
            raw.Should().NotContain(hidden);

        var v = JsonDocument.Parse(raw).RootElement;
        v.GetProperty("total").GetInt32().Should().Be(2);
        var items = v.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("action").GetString()).Should().Equal("TestConnection", "Engage");
        items.Select(i => i.GetProperty("scope").GetString()).Should().Equal("System", "This account");
        items[1].GetProperty("reference").GetString().Should().Be("••••0001");
        items[1].GetProperty("correlationRef").GetString().Should().Be("••••7777");
        items.Should().OnlyContain(i => i.GetProperty("detailsRedacted").GetBoolean());
        v.GetProperty("categories").EnumerateArray().Select(c => c.GetString()).Should().Equal("Demo", "KillSwitch");
        v.GetProperty("classification").GetString().Should().NotBeNullOrWhiteSpace();
        v.GetProperty("retention").GetString().Should().Contain("no export");
    }

    [Fact]
    public async Task Audit_FiltersByPeriodCategoryEpicCorrelationAndText_AndPages()
    {
        using var host = Host("simulation");
        var t0 = new DateTimeOffset(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 5; i++)
            await Audit(host, $"audit-page-{i}", t0.AddHours(i), i % 2 == 0 ? "Execution" : "KillSwitch", i % 2 == 0 ? "Submit" : "Engage", "ACC-A",
                epic: i < 3 ? "EURUSD" : "GOLD", correlation: $"corr-{i}");
        var client = await host.OwnerClientAsync();
        async Task<JsonElement> Read(string q) => await client.GetFromJsonAsync<JsonElement>("/api/audit?accountId=ACC-A&" + q);
        string[] Actions(JsonElement v) => v.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("occurredUtc").GetDateTimeOffset().Hour.ToString()).ToArray();

        var first = await Read("limit=2");
        first.GetProperty("total").GetInt32().Should().Be(5);
        Actions(first).Should().Equal("12", "11");
        Actions(await Read("limit=2&offset=4")).Should().Equal("8");
        Actions(await Read("category=KillSwitch")).Should().Equal("11", "9");
        Actions(await Read("epic=GOLD")).Should().Equal("12", "11");
        Actions(await Read("correlation=corr-2")).Should().Equal("10");
        Actions(await Read("q=Engage")).Should().Equal("11", "9");
        Actions(await Read("from=2026-11-01T09:00:00Z&to=2026-11-01T11:00:00Z")).Should().Equal("11", "10", "9");
        (await Read("category=Nothing")).GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Alerts_ReportOnlyRealConditions_AndNothingWhenAllIsWell()
    {
        using var host = Host("simulation");
        var client = await host.OwnerClientAsync();
        var quiet = await client.GetFromJsonAsync<JsonElement>("/api/alerts?accountId=ACC-A");
        // With no recorded decisions and an empty journal there are no AI or order alerts. Other alerts mirror real state only
        // (this host has no selected instrument and the simulator does not know ACC-A, so those reads are flagged).
        quiet.GetProperty("alerts").EnumerateArray().Select(a => a.GetProperty("key").GetString()).Should().NotContain(k => k!.StartsWith("Ai:") || k!.StartsWith("Ledger:") || k == "RiskVeto");
        var engagedAtStart = host.Services.GetRequiredService<ITradingControl>().State.KillSwitchEngaged;
        quiet.GetProperty("alerts").EnumerateArray().Any(a => a.GetProperty("key").GetString() == "KillSwitchEngaged").Should().Be(engagedAtStart);
        quiet.GetProperty("note").GetString().Should().Contain("nothing is emailed or messaged");

        var store = host.Services.GetRequiredService<IDecisionEventStore>();
        var now = DateTimeOffset.UtcNow;
        store.Record(Decision("dec-timeout-1", "ACC-A", now.AddMinutes(-2), AiStepStatus.TimedOut, "Timed out after 20 s"));
        store.Record(Decision("dec-reject-1", "ACC-A", now.AddMinutes(-1), AiStepStatus.Rejected, "Reply failed schema validation"));
        store.Record(Decision("dec-other-1", "ACC-B", now, AiStepStatus.TimedOut, "Timed out after 20 s"));
        var control = host.Services.GetRequiredService<ITradingControl>();
        control.State.KillSwitchEngaged = true;
        control.State.KillSwitchReason = "Owner stop";
        control.State.KillSwitchChangedUtc = now;

        var v = await client.GetFromJsonAsync<JsonElement>("/api/alerts?accountId=ACC-A");
        var alerts = v.GetProperty("alerts").EnumerateArray().ToList();
        alerts[0].GetProperty("key").GetString().Should().Be("KillSwitchEngaged");
        alerts[0].GetProperty("severity").GetString().Should().Be("danger");
        var timeout = alerts.Single(a => a.GetProperty("key").GetString() == "Ai:TimedOut");
        timeout.GetProperty("detail").GetString().Should().StartWith("1 recorded decisions").And.Contain("Timed out after 20 s");
        alerts.Single(a => a.GetProperty("key").GetString() == "Ai:Rejected").GetProperty("detail").GetString().Should().Contain("schema validation");
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Alerts_InDemoMode_ReportMissingCredentials_AndUnknownBrokerOutcomes()
    {
        using var host = Host();
        var journal = host.Services.GetRequiredService<IOrderJournal>();
        var at = DateTimeOffset.UtcNow.AddMinutes(-1);
        await journal.CreateAsync(new OrderJournalEntry("cmd_x_secret_0001", "intent_x_secret_0002", TradingEnvironment.Demo, "ACC-A", "EURUSD", OrderState.Unknown, "d", "snap", null,
            null, null, at, null, null, null, at, "intent_x_secret_0002", OrderSide.Buy, 1m), CancellationToken.None);
        var raw = await (await (await host.OwnerClientAsync()).GetAsync("/api/alerts?accountId=ACC-A")).Content.ReadAsStringAsync();
        raw.Should().NotContain("cmd_x_secret_0001").And.NotContain("TEST-KEY");
        var keys = JsonDocument.Parse(raw).RootElement.GetProperty("alerts").EnumerateArray().Select(a => a.GetProperty("key").GetString()).ToList();
        keys.Should().Contain("Credentials:CapitalDemo").And.Contain("Ledger:UnknownOutcome");
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Diagnostics_UseTheFiveStatuses_AndNeverClaimBrokerOrderPermission()
    {
        using var host = Host("simulation");
        var v = await (await host.OwnerClientAsync()).GetFromJsonAsync<JsonElement>("/api/diagnostics?accountId=ACC-A");
        var components = v.GetProperty("components").EnumerateArray().ToDictionary(c => c.GetProperty("key").GetString()!, c => c.GetProperty("status").GetString());
        components["api"].Should().Be("Healthy");
        components["database"].Should().Be("Healthy");
        components["broker"].Should().Be("Healthy");
        components["ai"].Should().Be("Not Configured");
        components["credentials"].Should().Be("Not Configured");
        components.Values.Should().OnlyContain(s => s == "Healthy" || s == "Degraded" || s == "Unavailable" || s == "Not Configured" || s == "Unknown");
        v.GetProperty("runtimeMode").GetString().Should().Be("Simulation");
        v.GetProperty("execution").GetString().Should().Be("Simulated fills only. No Capital.com orders are sent.");
        v.GetProperty("live").GetString().Should().Be("Live locked");
        v.GetProperty("accountRef").GetString().Should().Be("••••");
        v.GetProperty("version").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Diagnostics_ReportABrokerFailureAsUnavailable_WithAFixedMessage()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        Func<HttpResponseMessage?> down = () => CapitalFake.Json(HttpStatusCode.InternalServerError, "secret broker body");
        host.Capital.Script("GET /api/v1/accounts", down, down, down, down, down, down);
        var raw = await (await (await host.OwnerClientAsync()).GetAsync("/api/diagnostics?accountId=ACC-A")).Content.ReadAsStringAsync();
        raw.Should().NotContain("secret broker body").And.NotContain("TEST-KEY");
        var v = JsonDocument.Parse(raw).RootElement;
        var broker = v.GetProperty("components").EnumerateArray().Single(c => c.GetProperty("key").GetString() == "broker");
        broker.GetProperty("status").GetString().Should().Be("Unavailable");
        broker.GetProperty("detail").GetString().Should().StartWith("Account read failed");
        v.GetProperty("overall").GetString().Should().Be("Unavailable");
        v.GetProperty("failures").EnumerateArray().Should().Contain(f => f.GetProperty("source").GetString() == "Broker connection");
        v.GetProperty("execution").GetString().Should().Be("Capital.com Demo order submission disabled (read-only)");
        host.Capital.OrderRequests.Should().BeEmpty();
    }
}
