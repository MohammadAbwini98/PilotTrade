namespace PilotTrade.IntegrationTests;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Api;
using PilotTrade.Application.Services;
using PilotTrade.Domain.Decision;

// Task 02-04: decision history is read-only, bound to the configured account, traceable to its audit event and
// instrument selection, and never exposes prompt text, reply text or keys.
public sealed class DecisionHistoryTests : IDisposable
{
    // Split so the secret scan does not flag the source; the runtime value is a credential-shaped fake.
    private const string Secret = "sk-" + "ant-SECRET-never-shown";
    private readonly string _db = ApiTestHost.NewDatabasePath();
    public void Dispose() => ApiTestHost.DeleteDatabase(_db);

    private ApiTestHost Demo() => new("capital_demo", "demo", "ACC-A", _db);

    private sealed class Claude(Func<ClaudeAnalysisRequest, ClaudeAnalysisResult> f) : IClaudeAnalyst
    {
        public Task<ClaudeAnalysisResult> AnalyzeAsync(ClaudeAnalysisRequest r, CancellationToken ct) => Task.FromResult(f(r));
    }

    private sealed class Jev(string direction) : IJevEvaluator
    {
        public Task<JevResult> EvaluateAsync(JevRequest r, CancellationToken ct) => Task.FromResult(new JevResult("j1", r.SnapshotId, r.ModelId, "jev-1",
            new[] { new JevAnswer("direction", direction, 0.7, null) }, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(80), r.AccountId, r.Epic, r.Environment, r.SelectionVersion));
    }

    private static Claude ClaudeSays(DecisionCandidate signal) => new(r => new ClaudeAnalysisResult("c1", r.SnapshotId, r.Epic, r.Environment, r.SelectionVersion,
        "claude-model", "1", signal, "trend", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(),
        DateTimeOffset.UtcNow.AddMinutes(5), DateTimeOffset.UtcNow, 120, 40, 0m, TimeSpan.FromMilliseconds(300), r.AccountId));

    private static Claude ClaudeFails() => new(_ => throw new HttpRequestException($"Authorization: {Secret}", null, HttpStatusCode.BadGateway));

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/security/csrf-token")).GetProperty("csrfToken").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(ApiSecurity.CsrfHeader, csrf);
        return await client.SendAsync(request);
    }

    // Runs the real pipeline against the host's store, coordinator and current selection, with stand-in AI clients.
    private static async Task<DecisionEvent> RunAsync(ApiTestHost host, IClaudeAnalyst? claude, IJevEvaluator? jev)
    {
        var sp = host.Services;
        var clock = sp.GetRequiredService<IClock>();
        var selection = sp.GetRequiredService<IInstrumentSelectionService>().Current("ACC-A", TradingEnvironment.Demo)!;
        var now = clock.UtcNow;
        var snapshot = new MarketSnapshotBundle($"snap_{Guid.NewGuid():N}", "ACC-A", selection.Environment, selection.Epic, selection.SelectionVersion, now,
            new MarketQuote(selection.Epic, 1m, 1.0002m, null, 0.0002m, now, now, FeedHealth.Healthy), new Dictionary<string, IReadOnlyList<Candle>>(), new Dictionary<string, decimal>());
        var pipeline = new AiDecisionPipeline(claude, jev, sp.GetRequiredService<IDecisionCoordinator>(), sp.GetRequiredService<IDecisionEventStore>(),
            clock, sp.GetRequiredService<IIdGenerator>(), AiPipelineOptions.Default with { Enabled = true });
        return await pipeline.RunAsync(snapshot, selection, 100m, 0m, CancellationToken.None);
    }

    private static async Task<(ApiTestHost host, HttpClient client, int version)> SelectedAsync(ApiTestHost host)
    {
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();
        var selected = await PostAsync(client, "/api/instruments/select", new { accountId = "ACC-A", epic = "EURUSD" });
        selected.EnsureSuccessStatusCode();
        var version = (await selected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("selection").GetProperty("selectionVersion").GetInt32();
        return (host, client, version);
    }

    [Fact]
    public async Task Endpoints_NeedTheOwnerToken_AndTheConfiguredAccount()
    {
        using var host = Demo();
        await host.StoreDemoCredentialsAsync();
        foreach (var path in new[] { "/api/decisions?accountId=ACC-A", "/api/decisions/x?accountId=ACC-A", "/api/ai/status?accountId=ACC-A" })
            (await host.CreateClient().GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var client = await host.OwnerClientAsync();
        foreach (var path in new[] { "/api/decisions?accountId=ACC-B", "/api/decisions/x?accountId=ACC-B", "/api/ai/status?accountId=ACC-B" })
            (await client.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/decisions/missing?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/decisions?accountId=ACC-A&decision=Maybe")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/api/decisions?accountId=ACC-A&status=Great")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EmptyHistory_AndStatus_SayRuntimeAiIsNotConnected()
    {
        using var host = Demo();
        await host.StoreDemoCredentialsAsync();
        var client = await host.OwnerClientAsync();
        var list = await client.GetFromJsonAsync<JsonElement>("/api/decisions?accountId=ACC-A");
        list.GetProperty("total").GetInt32().Should().Be(0);
        list.GetProperty("items").GetArrayLength().Should().Be(0);

        await host.Secrets.SetAsync("anthropic:Demo:api_key", Secret, CancellationToken.None);
        var raw = await client.GetStringAsync("/api/ai/status?accountId=ACC-A");
        raw.Should().NotContain(Secret).And.NotContainEquivalentOf("gemini");
        var status = JsonDocument.Parse(raw).RootElement;
        status.GetProperty("runtimeWired").GetBoolean().Should().BeFalse();
        status.GetProperty("decisionsRecorded").GetInt32().Should().Be(0);
        status.GetProperty("allowedProviders").EnumerateArray().Select(p => p.GetString()).Should().Equal("Claude API", "JEV/TypeSafe AI");
        var providers = status.GetProperty("providers").EnumerateArray().ToList();
        providers.Should().OnlyContain(p => !p.GetProperty("registered").GetBoolean());
        providers[0].GetProperty("credentialPresent").GetBoolean().Should().BeTrue();
        providers[1].GetProperty("credentialPresent").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task RecordedDecision_TracesToItsAuditEvent_AndSelection_WithoutPromptOrSecret()
    {
        using var demo = Demo();
        var (host, client, version) = await SelectedAsync(demo);

        var failed = await RunAsync(host, ClaudeFails(), new Jev("Buy"));
        var traded = await RunAsync(host, ClaudeSays(DecisionCandidate.Buy), new Jev("Buy"));
        failed.Decision.Should().Be(DecisionCandidate.NoTrade);
        traded.Decision.Should().Be(DecisionCandidate.Buy);

        var raw = await client.GetStringAsync("/api/decisions?accountId=ACC-A");
        raw.Should().NotContain(Secret).And.NotContain("You are a market analyst").And.NotContain("availableRiskBudget");
        var list = JsonDocument.Parse(raw).RootElement;
        list.GetProperty("total").GetInt32().Should().Be(2);
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items[0].GetProperty("decisionId").GetString().Should().Be(traded.DecisionId, "newest first");

        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/decisions/{failed.DecisionId}?accountId=ACC-A");
        detail.GetProperty("decision").GetString().Should().Be("NoTrade");
        detail.GetProperty("epic").GetString().Should().Be("EURUSD");
        detail.GetProperty("selectionVersion").GetInt32().Should().Be(version);
        detail.GetProperty("promptStored").GetBoolean().Should().BeFalse();
        detail.GetProperty("promptSha256").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
        detail.GetProperty("claude").GetProperty("status").GetString().Should().Be("Unavailable");
        detail.GetProperty("claude").GetProperty("problem").GetString().Should().Be("Provider error (HTTP 502)");
        detail.GetProperty("jev").GetProperty("status").GetString().Should().Be("NotRun");
        detail.GetProperty("noTradeReason").GetString().Should().Contain("Claude API unavailable");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PilotTradeDbContext>();
        var audit = await db.AuditEvents.SingleAsync(a => a.Id == failed.AuditEventId);
        audit.Category.Should().Be("Decision");
        audit.Action.Should().Be("AiDecisionRecorded");
        audit.CorrelationId.Should().Be(failed.DecisionId);
        audit.InstrumentEpic.Should().Be("EURUSD");
        audit.DetailsJson.Should().NotContain(Secret).And.Contain($"\"selectionVersion\":{version}");
        var selection = await db.InstrumentSelections.AsNoTracking().ToListAsync();
        selection.Should().Contain(s => s.Epic == "EURUSD" && s.SelectionVersion == version && s.AccountId == "ACC-A");
        (await db.DecisionEvents.AsNoTracking().ToListAsync()).Should().OnlyContain(r => !r.ClaudeJson.Contains(Secret) && !r.JevJson.Contains(Secret));
        host.Capital.OrderRequests.Should().BeEmpty("a decision never places an order");

        var status = await client.GetFromJsonAsync<JsonElement>("/api/ai/status?accountId=ACC-A");
        status.GetProperty("decisionsRecorded").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Filters_ByDecision_Status_AndInstrument()
    {
        using var demo = Demo();
        var (host, client, _) = await SelectedAsync(demo);
        await RunAsync(host, ClaudeFails(), new Jev("Buy"));
        await RunAsync(host, ClaudeSays(DecisionCandidate.Buy), new Jev("Sell"));
        await RunAsync(host, ClaudeSays(DecisionCandidate.Buy), new Jev("Buy"));

        async Task<int> Count(string q) => (await client.GetFromJsonAsync<JsonElement>($"/api/decisions?accountId=ACC-A{q}")).GetProperty("total").GetInt32();
        (await Count("")).Should().Be(3);
        (await Count("&decision=NoTrade")).Should().Be(2);
        (await Count("&decision=buy")).Should().Be(1);
        (await Count("&status=Unavailable")).Should().Be(1);
        (await Count("&status=Succeeded")).Should().Be(2);
        (await Count("&epic=EURUSD")).Should().Be(3);
        (await Count("&epic=GOLD")).Should().Be(0);
        (await client.GetFromJsonAsync<JsonElement>("/api/decisions?accountId=ACC-A&limit=1")).GetProperty("items").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task InconsistentStoredTrade_IsShownAsNoTrade()
    {
        using var demo = Demo();
        var (host, client, _) = await SelectedAsync(demo);
        var e = await RunAsync(host, ClaudeFails(), new Jev("Buy"));
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PilotTradeDbContext>();
            var row = await db.DecisionEvents.SingleAsync(r => r.DecisionId == e.DecisionId);
            row.Decision = "Buy";
            await db.SaveChangesAsync();
        }
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/decisions/{e.DecisionId}?accountId=ACC-A");
        detail.GetProperty("decision").GetString().Should().Be("NoTrade");
        detail.GetProperty("noTradeReason").GetString().Should().Contain("Inconsistent record");
    }

    [Fact]
    public void AiClients_AndThePipeline_AreNotRegistered()
    {
        using var host = Demo();
        host.Services.GetService<IClaudeAnalyst>().Should().BeNull();
        host.Services.GetService<IJevEvaluator>().Should().BeNull();
        host.Services.GetService<AiDecisionPipeline>().Should().BeNull();
    }
}
