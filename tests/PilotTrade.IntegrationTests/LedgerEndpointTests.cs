namespace PilotTrade.IntegrationTests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PilotTrade.Api;
using PilotTrade.Infrastructure.Persistence;

// Task 02-06: the read-only order ledger. Every test also checks that no order request reached the broker.
public sealed class LedgerEndpointTests : IDisposable
{
    private readonly string _db = ApiTestHost.NewDatabasePath();
    public void Dispose() => ApiTestHost.DeleteDatabase(_db);

    private ApiTestHost Host(string account = "ACC-A", string mode = "capital_demo") => new(mode, "demo", account, _db);

    private static OrderJournalEntry Entry(string id, string account, string epic, OrderState state, DateTimeOffset at, string? reference = null, string? deals = null) =>
        new($"cmd_{id}_secret_0001", $"intent_{id}_secret_0002", TradingEnvironment.Demo, account, epic, state, "digest", "snap", "{\"risk\":\"ok\"}",
            reference, deals, at, null, null, null, at, $"intent_{id}_secret_0002", OrderSide.Buy, 1.25m);

    private static async Task<JsonElement> Ledger(HttpClient client, string query) =>
        await client.GetFromJsonAsync<JsonElement>("/api/ledger?" + query);

    [Fact]
    public async Task Ledger_NeedsTheOwnerAndTheConfiguredAccount_ValidatesFilters_AndHasNoWriteRoute()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        (await host.CreateClient().GetAsync("/api/ledger?accountId=ACC-A")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var client = await host.OwnerClientAsync();
        (await client.GetAsync("/api/ledger?accountId=ACC-B")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreach (var bad in new[] { "limit=0", "limit=101", "offset=-1", "status=Done", "epic=EUR%20USD", "q=" + new string('x', 65) })
            (await client.GetAsync("/api/ledger?accountId=ACC-A&" + bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);

        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete, HttpMethod.Patch })
        {
            var request = new HttpRequestMessage(method, "/api/ledger?accountId=ACC-A") { Content = JsonContent.Create(new { accountId = "ACC-A" }) };
            request.Headers.Add("Origin", "http://localhost");
            ((int)(await client.SendAsync(request)).StatusCode).Should().BeGreaterThanOrEqualTo(400, method.Method);
        }
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyJournal_ReportsNoOrders_WithBrokerObservedHoldings()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        var v = await Ledger(await host.OwnerClientAsync(), "accountId=ACC-A");
        v.GetProperty("journalTotal").GetInt32().Should().Be(0);
        v.GetProperty("journal").GetProperty("items").GetArrayLength().Should().Be(0);
        v.TryGetProperty("executionEnabled", out _).Should().BeFalse();
        v.GetProperty("brokerExecutionEnabled").GetBoolean().Should().BeFalse();
        v.GetProperty("runtimeMode").GetString().Should().Be("Capital.com Demo");
        v.GetProperty("execution").GetString().Should().Be("Capital.com Demo order submission disabled (read-only)");
        var position = v.GetProperty("positions").EnumerateArray().Single();
        position.GetProperty("provenance").GetString().Should().Be("Broker-observed (Capital.com Demo, read-only)");
        position.GetProperty("inJournal").GetBoolean().Should().BeFalse();
        position.GetProperty("positionRef").GetString().Should().StartWith("••••");
        v.GetProperty("alerts").EnumerateArray().Should().Contain(a => a.GetProperty("kind").GetString() == "NotInJournal");
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task StateHistory_IsPersisted_SurvivesRestart_AndAuditDrillDownWithholdsDetails()
    {
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
        using (var first = Host())
        {
            var journal = first.Services.GetRequiredService<IOrderJournal>();
            var e = await journal.CreateAsync(Entry("u", "ACC-A", "EURUSD", OrderState.Proposed, t0), CancellationToken.None);
            await journal.UpdateAsync(e with { State = OrderState.Submitting, SubmittedUtc = t0.AddSeconds(1), UpdatedUtc = t0.AddSeconds(1) }, CancellationToken.None);
            // The same state again is not a transition.
            await journal.UpdateAsync(e with { State = OrderState.Submitting, UpdatedUtc = t0.AddSeconds(2) }, CancellationToken.None);
            await journal.UpdateAsync(e with { State = OrderState.Unknown, RejectionReason = "Broker outcome uncertain (TimeoutException)", UpdatedUtc = t0.AddSeconds(3) }, CancellationToken.None);
            await first.Services.GetRequiredService<IAuditLogger>().LogAsync(new AuditEvent("audit-ledger-1", t0.AddSeconds(3), "Execution", "Submit", "system", "demo", "ACC-A", "EURUSD",
                "intent_u_secret_0002", "{\"brokerDealReference\":\"ref-protected-9999\"}", true), CancellationToken.None);
            // An audit event for another account with the same correlation is not this account's.
            await first.Services.GetRequiredService<IAuditLogger>().LogAsync(new AuditEvent("audit-ledger-2", t0.AddSeconds(4), "Execution", "Submit", "system", "demo", "ACC-B", "EURUSD",
                "intent_u_secret_0002", "{}", true), CancellationToken.None);
        }

        using var second = Host();
        await second.StoreDemoCredentialsAsync();
        var response = await (await second.OwnerClientAsync()).GetAsync("/api/ledger?accountId=ACC-A");
        var raw = await response.Content.ReadAsStringAsync();
        foreach (var hidden in new[] { "cmd_u_secret_0001", "intent_u_secret_0002", "ref-protected-9999", "brokerDealReference", "TEST-KEY", "riskAuthorization", "audit-ledger-1" })
            raw.Should().NotContain(hidden);

        var item = JsonDocument.Parse(raw).RootElement.GetProperty("journal").GetProperty("items").EnumerateArray().Single();
        item.GetProperty("statusGroup").GetString().Should().Be("Unknown");
        item.GetProperty("reconciliation").GetString().Should().Be("Blocked");
        item.GetProperty("side").GetString().Should().Be("Buy");
        item.GetProperty("size").GetDecimal().Should().Be(1.25m);
        item.GetProperty("evidence").GetString().Should().Be("Locally inferred");
        item.GetProperty("transitions").EnumerateArray().Select(t => $"{(t.GetProperty("fromState").ValueKind == JsonValueKind.Null ? "-" : t.GetProperty("fromState").GetString())}>{t.GetProperty("toState").GetString()}")
            .Should().Equal("->Proposed", "Proposed>Submitting", "Submitting>Unknown");
        item.GetProperty("transitions").EnumerateArray().Last().GetProperty("source").GetString().Should().Be("CapitalDemo");
        var audit = item.GetProperty("audit").EnumerateArray().Single();
        audit.GetProperty("id").GetString().Should().Be("••••er-1");
        audit.GetProperty("refers").GetString().Should().Be("Order intent");
        audit.GetProperty("sensitiveRedacted").GetBoolean().Should().BeTrue();
        JsonDocument.Parse(raw).RootElement.GetProperty("alerts").EnumerateArray().Should().Contain(a => a.GetProperty("kind").GetString() == "UnknownOutcome");

        using var scope = second.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<PilotTradeDbContext>().OrderStateTransitions.CountAsync()).Should().Be(3);
        second.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task Filters_AndPagination_AreAppliedOnTheServer()
    {
        using var host = Host();
        await host.StoreDemoCredentialsAsync();
        var journal = host.Services.GetRequiredService<IOrderJournal>();
        var t0 = DateTimeOffset.UtcNow.AddHours(-1);
        await journal.CreateAsync(Entry("a", "ACC-A", "EURUSD", OrderState.Rejected, t0) with { RejectionReason = "Market closed" }, CancellationToken.None);
        await journal.CreateAsync(Entry("b", "ACC-A", "GOLD", OrderState.Proposed, t0.AddMinutes(1)), CancellationToken.None);
        await journal.CreateAsync(Entry("c", "ACC-A", "EURUSD", OrderState.BrokerPending, t0.AddMinutes(2), "ref-c-pending-0003"), CancellationToken.None);
        await journal.CreateAsync(Entry("z", "ACC-Z", "EURUSD", OrderState.Unknown, t0.AddMinutes(3)), CancellationToken.None);
        var client = await host.OwnerClientAsync();

        var page1 = await Ledger(client, "accountId=ACC-A&limit=2");
        page1.GetProperty("journal").GetProperty("total").GetInt32().Should().Be(3, "another account's rows are never counted");
        page1.GetProperty("journal").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("epic").GetString()).Should().Equal("EURUSD", "GOLD");
        var page2 = await Ledger(client, "accountId=ACC-A&limit=2&offset=2");
        page2.GetProperty("journal").GetProperty("items").EnumerateArray().Single().GetProperty("reason").GetString().Should().Be("Market closed");
        (await Ledger(client, "accountId=ACC-A&epic=GOLD")).GetProperty("journal").GetProperty("items").EnumerateArray().Single().GetProperty("statusGroup").GetString().Should().Be("Pending");
        var pending = (await Ledger(client, "accountId=ACC-A&status=Unknown")).GetProperty("journal").GetProperty("items").EnumerateArray().Single();
        pending.GetProperty("state").GetString().Should().Be("BrokerPending");
        pending.GetProperty("evidence").GetString().Should().Be("Broker-observed");
        pending.GetProperty("dealReference").GetString().Should().Be("••••0003");
        (await Ledger(client, "accountId=ACC-A&q=0003")).GetProperty("journal").GetProperty("total").GetInt32().Should().Be(1);
        var counts = page1.GetProperty("statusCounts");
        counts.GetProperty("Rejected").GetInt32().Should().Be(1);
        counts.GetProperty("Unknown").GetInt32().Should().Be(1);
        counts.GetProperty("Completed").GetInt32().Should().Be(0);
        page1.GetProperty("confirms").EnumerateArray().Single().GetProperty("state").GetString().Should().Be("BrokerPending");
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task CompletedOrders_AreMatchedOrPartial_AgainstBrokerHoldings()
    {
        using var host = Host("ACC-B");
        await host.StoreDemoCredentialsAsync();
        var journal = host.Services.GetRequiredService<IOrderJournal>();
        var t0 = DateTimeOffset.UtcNow.AddMinutes(-10);
        await journal.CreateAsync(Entry("m", "ACC-B", "GOLD", OrderState.Reconciled, t0, "ref-m-0011", "deal-GOLD,wo-GOLD"), CancellationToken.None);
        await journal.CreateAsync(Entry("p", "ACC-B", "GOLD", OrderState.Confirmed, t0.AddMinutes(1), "ref-p-0012", "deal-GOLD,deal-gone-0013"), CancellationToken.None);
        var v = await Ledger(await host.OwnerClientAsync(), "accountId=ACC-B");
        var items = v.GetProperty("journal").GetProperty("items").EnumerateArray().ToList();
        items.Single(i => i.GetProperty("dealReference").GetString() == "••••0011").GetProperty("reconciliation").GetString().Should().Be("Matched");
        items.Single(i => i.GetProperty("dealReference").GetString() == "••••0012").GetProperty("reconciliation").GetString().Should().Be("Partial");
        v.GetProperty("positions").EnumerateArray().Single().GetProperty("inJournal").GetBoolean().Should().BeTrue();
        v.GetProperty("workingOrders").EnumerateArray().Single().GetProperty("inJournal").GetBoolean().Should().BeTrue();
        v.GetProperty("alerts").EnumerateArray().Should().Contain(a => a.GetProperty("kind").GetString() == "MissingAtBroker");
        host.Capital.OrderRequests.Should().BeEmpty();
    }

    [Fact]
    public async Task SimulatorRows_AreNeverShownAsBrokerConfirmations()
    {
        using var host = Host("SIM-ACC", "simulation");
        var journal = host.Services.GetRequiredService<IOrderJournal>();
        var t0 = DateTimeOffset.UtcNow;
        await journal.CreateAsync(Entry("s", "SIM-ACC", "EURUSD", OrderState.Confirmed, t0, "sim-ref-0021", "sim-deal-0022"), CancellationToken.None);
        var v = await Ledger(await host.OwnerClientAsync(), "accountId=SIM-ACC");
        v.GetProperty("source").GetString().Should().Be("Simulator");
        var item = v.GetProperty("journal").GetProperty("items").EnumerateArray().Single();
        item.GetProperty("evidence").GetString().Should().Be("Simulator");
        item.GetProperty("reconciliationDetail").GetString().Should().Contain("not a Capital.com confirmation");
        v.GetProperty("confirms").EnumerateArray().Single().GetProperty("evidence").GetString().Should().Be("Simulator");
    }
}
