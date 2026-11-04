namespace PilotTrade.IntegrationTests;

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Api;
using PilotTrade.Infrastructure.Persistence;

// Task 01-03: the owner's instrument choice survives a restart, but nothing built for the old choice is accepted.
public sealed class RestartPersistenceTests : IDisposable
{
    private readonly string _db = ApiTestHost.NewDatabasePath();
    public void Dispose() => ApiTestHost.DeleteDatabase(_db);

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, object body)
    {
        var csrf = (await client.GetFromJsonAsync<JsonElement>("/api/security/csrf-token")).GetProperty("csrfToken").GetString()!;
        var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Origin", "http://localhost");
        request.Headers.Add(ApiSecurity.CsrfHeader, csrf);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> SelectAsync(HttpClient client, string epic)
    {
        var response = await PostAsync(client, "/api/instruments/select", new { accountId = "ACC-A", epic });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("selection");
    }

    private static async Task<string> SnapshotOutcomeAsync(HttpClient client, string epic, int version) =>
        (await (await PostAsync(client, "/api/market/snapshot", new { accountId = "ACC-A", epic, selectionVersion = version }))
            .Content.ReadFromJsonAsync<JsonElement>()).GetProperty("outcome").GetString()!;

    private ApiTestHost Host(string account = "ACC-A") => new("capital_demo", "demo", account, _db);

    [Fact]
    public async Task Selection_SurvivesRestart_WithANewIdAndAHigherVersion()
    {
        string oldId;
        int oldVersion;
        using (var first = Host())
        {
            await first.StoreDemoCredentialsAsync();
            var client = await first.OwnerClientAsync();
            await SelectAsync(client, "EURUSD");
            var chosen = await SelectAsync(client, "GOLD");
            oldId = chosen.GetProperty("selectionId").GetString()!;
            oldVersion = chosen.GetProperty("selectionVersion").GetInt32();
            (await SnapshotOutcomeAsync(client, "GOLD", oldVersion)).Should().Be("SNAPSHOT_READY");
        }

        using var second = Host();
        await second.StoreDemoCredentialsAsync();
        var restarted = await second.OwnerClientAsync();
        var restored = await restarted.GetFromJsonAsync<JsonElement>("/api/instruments/selection?accountId=ACC-A");
        restored.GetProperty("epic").GetString().Should().Be("GOLD");
        restored.GetProperty("selectedBy").GetString().Should().Be("restored");
        restored.GetProperty("selectionId").GetString().Should().NotBe(oldId);
        var newVersion = restored.GetProperty("selectionVersion").GetInt32();
        newVersion.Should().BeGreaterThan(oldVersion);

        // A request built for the pre-restart selection is refused; one for the restored selection is checked afresh.
        (await SnapshotOutcomeAsync(restarted, "GOLD", oldVersion)).Should().Be("NO_TRADE");
        (await SnapshotOutcomeAsync(restarted, "GOLD", newVersion)).Should().Be("SNAPSHOT_READY");
        second.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task RestoredVersion_IsAboveEveryStoredDecisionAndAnalysis()
    {
        using (var first = Host())
        {
            await first.StoreDemoCredentialsAsync();
            await SelectAsync(await first.OwnerClientAsync(), "EURUSD");
            // Stands in for AI work recorded under a much later version before the restart.
            var db = first.Services.GetRequiredService<PilotTradeDbContext>();
            db.Decisions.Add(new DecisionEntity { DecisionId = "d1", SnapshotId = "s1", AccountId = "ACC-A", Environment = "Demo", Epic = "EURUSD", SelectionVersion = 40 });
            db.ClaudeAnalyses.Add(new ClaudeAnalysisEntity { AnalysisId = "a1", SnapshotId = "s1", Epic = "EURUSD", Environment = "Demo", SelectionVersion = 55 });
            await db.SaveChangesAsync();
        }

        using var second = Host();
        var restored = await (await second.OwnerClientAsync()).GetFromJsonAsync<JsonElement>("/api/instruments/selection?accountId=ACC-A");
        restored.GetProperty("selectionVersion").GetInt32().Should().BeGreaterThan(55);
    }

    [Fact]
    public async Task Selection_IsNotRestored_ForAnotherAccount()
    {
        using (var first = Host())
        {
            await first.StoreDemoCredentialsAsync();
            await SelectAsync(await first.OwnerClientAsync(), "EURUSD");
        }

        using var other = Host("ACC-B");
        (await (await other.OwnerClientAsync()).GetAsync("/api/instruments/selection?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task KillSwitch_AndJournal_SurviveRestart()
    {
        using (var first = Host())
        {
            var now = DateTimeOffset.UtcNow;
            await first.Services.GetRequiredService<IOrderJournal>().CreateAsync(new OrderJournalEntry("cmd_k", "intent_k", TradingEnvironment.Demo, "ACC-A", "EURUSD", OrderState.Rejected, "d", "snap", null, null, null, now, null, null, "test", now, "c"), CancellationToken.None);
            await first.Services.GetRequiredService<IPersistedExecutionState>().SaveAsync(new PersistedExecutionState(TradingEnvironment.Demo, "ACC-A", true, TradingMode.MonitorOnly, false, null, "test", now), CancellationToken.None);
        }

        using var second = Host();
        (await second.Services.GetRequiredService<IOrderJournal>().GetAsync("cmd_k", CancellationToken.None)).Should().NotBeNull();
        (await second.Services.GetRequiredService<IPersistedExecutionState>().LoadAsync(TradingEnvironment.Demo, "ACC-A", CancellationToken.None))!
            .KillSwitchEngaged.Should().BeTrue();
    }

    [Fact]
    public async Task NoOrderEvidence_ReportsJournalAndBrokerWrites_ForTheOwnerOnly()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        (await host.CreateClient().GetAsync("/api/evidence/no-orders?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var client = await host.OwnerClientAsync();
        (await client.GetAsync("/api/evidence/no-orders?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await SelectAsync(client, "EURUSD");
        var evidence = await client.GetFromJsonAsync<JsonElement>("/api/evidence/no-orders?accountId=ACC-A");
        evidence.GetProperty("adapter").GetString().Should().Be("capital.com");
        evidence.GetProperty("executionEnabled").GetBoolean().Should().BeFalse();
        evidence.GetProperty("liveLocked").GetBoolean().Should().BeTrue();
        evidence.GetProperty("journalEntries").GetInt32().Should().Be(0);
        evidence.GetProperty("brokerWriteRequestsSinceStart").GetInt32().Should().Be(0);
        evidence.GetProperty("noOrderSent").GetBoolean().Should().BeTrue();
        host.Capital.OrderRequests.Should().BeEmpty();

        var now = DateTimeOffset.UtcNow;
        await host.Services.GetRequiredService<IOrderJournal>().CreateAsync(new OrderJournalEntry("cmd_s", "intent_s", TradingEnvironment.Demo, "ACC-A", "EURUSD", OrderState.Unknown, "d", "snap", null, "ref-1", null, now, now, null, null, now, "c"), CancellationToken.None);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/evidence/no-orders?accountId=ACC-A");
        after.GetProperty("journalEntriesSentToBroker").GetInt32().Should().Be(1);
        after.GetProperty("noOrderSent").GetBoolean().Should().BeFalse();
    }
}
