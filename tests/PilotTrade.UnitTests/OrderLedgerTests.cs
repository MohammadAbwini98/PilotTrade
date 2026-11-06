namespace PilotTrade.UnitTests;

using System.Text.Json;

// Task 02-06: a reader over an in-memory list, so the ledger service can be tested without a database.
internal sealed class ListLedgerReader : IOrderLedgerReader
{
    public List<LedgerRow> Rows { get; } = new();
    public List<OrderTransition> Transitions { get; } = new();
    public List<AuditReference> Audit { get; } = new();

    public Task<LedgerPage> ReadAsync(TradingEnvironment environment, string accountId, LedgerQuery query, int maxConfirms, CancellationToken ct)
    {
        var all = Rows.Where(r => r.Entry.Environment == environment && r.Entry.AccountId == accountId).ToList();
        var filtered = all.Where(r => (query.Epic is null || r.Entry.Epic == query.Epic) && (query.States is null || query.States.Contains(r.Entry.State))).ToList();
        var page = filtered.OrderByDescending(r => r.Entry.CreatedUtc).Skip(query.Offset).Take(query.Limit).ToList();
        var ids = page.Select(r => r.Entry.CommandId).ToHashSet();
        return Task.FromResult(new LedgerPage(filtered.Count, page,
            Transitions.Where(t => ids.Contains(t.CommandId)).GroupBy(t => t.CommandId).ToDictionary(g => g.Key, g => (IReadOnlyList<OrderTransition>)g.ToList()),
            Audit.ToList(),
            all.Where(r => r.Entry.BrokerDealReference is not null).Take(maxConfirms).ToList()));
    }
}

public class OrderLedgerTests
{
    private static readonly DateTimeOffset Now = RiskData.Now;
    private readonly RiskBroker _broker = new();
    private readonly InMemoryOrderJournal _journal = new();
    private readonly ListLedgerReader _reader = new();
    private readonly TestTradingControl _control = new();

    public OrderLedgerTests() { _control.State.Reconciliation = ReconciliationOutcome.NotCompared; }

    private static OrderJournalEntry Entry(string id, OrderState state, string? reference = null, string? deals = null, string epic = "EURUSD", int minutesAgo = 0, string account = "ACC-A") =>
        new("cmd_" + id + "_0001", "intent_" + id + "_0002", TradingEnvironment.Demo, account, epic, state, "digest", "snap", "{}", reference, deals,
            Now.AddMinutes(-minutesAgo), null, null, state == OrderState.Rejected ? "Insufficient funds" : null, Now.AddMinutes(-minutesAgo), "intent_" + id + "_0002", OrderSide.Buy, 1.5m);

    private async Task Add(OrderJournalEntry e, string? adapter = "CapitalDemo")
    {
        await _journal.CreateAsync(e, CancellationToken.None);
        _reader.Rows.Add(new LedgerRow(e, adapter));
    }

    private Task<LedgerView> Build(string? epic = null, string? status = null, int offset = 0, int limit = 25) =>
        new OrderLedgerService(_reader, _journal, _broker, _control, new BrokerRuntimeOptions(BrokerMode.CapitalDemo, TradingEnvironment.Demo, "ACC-A"), new FakeClock(Now))
            .BuildAsync("ACC-A", new LedgerQuery(epic, OrderLedgerService.StatesFor(status), null, offset, limit), CancellationToken.None);

    [Theory]
    [InlineData(OrderState.Proposed, "Pending")]
    [InlineData(OrderState.Validated, "Pending")]
    [InlineData(OrderState.RiskApproved, "Pending")]
    [InlineData(OrderState.Previewed, "Pending")]
    [InlineData(OrderState.Submitting, "Unknown")]
    [InlineData(OrderState.BrokerPending, "Unknown")]
    [InlineData(OrderState.Unknown, "Unknown")]
    [InlineData(OrderState.Rejected, "Rejected")]
    [InlineData(OrderState.Cancelled, "Cancelled")]
    [InlineData(OrderState.Confirmed, "Completed")]
    [InlineData(OrderState.Reconciled, "Completed")]
    public void EveryState_HasOneGroup_AndUncertainStatesAreNeverCompleted(OrderState state, string group)
    {
        OrderLedgerService.Group(state).Should().Be(group);
        OrderLedgerService.StatesFor(group).Should().Contain(state);
    }

    [Fact]
    public void Masking_ShowsOnlyTheLastFourCharacters()
    {
        OrderLedgerService.Mask("deal-reference-ABCD").Should().Be("••••ABCD");
        OrderLedgerService.Mask("short").Should().Be("••••");
        OrderLedgerService.Mask(null).Should().BeNull();
        OrderLedgerService.MaskList("deal-GOLD-1111, deal-GOLD-2222").Should().Be("••••1111, ••••2222");
    }

    [Fact]
    public void Evidence_KeepsSimulatorBrokerAndInferredApart()
    {
        var confirmed = Entry("a", OrderState.Confirmed, "ref-0001-XYZW", "deal-0001-XYZW");
        OrderLedgerService.Evidence(new LedgerRow(confirmed, "Simulation")).Should().Be("Simulator", "a simulator fill is never a Capital.com confirmation");
        OrderLedgerService.Evidence(new LedgerRow(confirmed, null)).Should().Be("Unknown source");
        OrderLedgerService.Evidence(new LedgerRow(confirmed, "CapitalDemo")).Should().Be("Broker-observed");
        OrderLedgerService.Evidence(new LedgerRow(Entry("b", OrderState.Unknown), "CapitalDemo")).Should().Be("Locally inferred");
    }

    [Fact]
    public void Reconciliation_IsBlockedOrUnknownUnlessTheBrokerHoldsEveryDeal()
    {
        var held = new HashSet<string> { "deal-A-0001", "deal-B-0002" };
        LedgerRow Row(OrderState s, string? deals = null, string adapter = "CapitalDemo") => new(Entry("x", s, "ref-x-0009", deals), adapter);

        OrderLedgerService.Reconcile(Row(OrderState.Proposed), held).Status.Should().Be("Not sent");
        OrderLedgerService.Reconcile(Row(OrderState.Rejected), held).Status.Should().Be("Not applicable");
        OrderLedgerService.Reconcile(Row(OrderState.Unknown), held).Status.Should().Be("Blocked");
        OrderLedgerService.Reconcile(Row(OrderState.BrokerPending), held).Status.Should().Be("Blocked");
        OrderLedgerService.Reconcile(Row(OrderState.Submitting), held).Detail.Should().Contain("never resent");
        OrderLedgerService.Reconcile(Row(OrderState.Confirmed), held).Status.Should().Be("Blocked", "a completed order without a deal id cannot be matched");
        OrderLedgerService.Reconcile(Row(OrderState.Confirmed, "deal-A-0001"), null).Status.Should().Be("Unknown");
        OrderLedgerService.Reconcile(Row(OrderState.Confirmed, "deal-A-0001,deal-B-0002"), held).Status.Should().Be("Matched");
        OrderLedgerService.Reconcile(Row(OrderState.Reconciled, "deal-A-0001,deal-C-0003"), held).Status.Should().Be("Partial");
        var gone = OrderLedgerService.Reconcile(Row(OrderState.Confirmed, "deal-C-0003"), held);
        gone.Status.Should().Be("Not held");
        gone.Detail.Should().Contain("no confirmation that they closed");
        OrderLedgerService.Reconcile(Row(OrderState.Confirmed, "deal-A-0001", "Simulation"), held).Detail.Should().Contain("not a Capital.com confirmation");
    }

    [Fact]
    public async Task EmptyJournal_ShowsNoOrders_AndInventsNothing()
    {
        var v = await Build();
        v.JournalTotal.Should().Be(0);
        v.Journal.Items.Should().BeEmpty();
        v.StatusCounts.Values.Should().OnlyContain(c => c == 0);
        v.Confirms.Should().BeEmpty();
        v.Positions.Should().BeEmpty();
        v.Alerts.Should().BeEmpty();
        v.ExecutionEnabled.Should().BeFalse();
        _broker.OrderCalls.Should().Be(0);
    }

    [Fact]
    public async Task UnknownOutcome_IsAlerted_Blocked_AndNotComplete()
    {
        await Add(Entry("u", OrderState.Unknown, "ref-u-AAAA"));
        var v = await Build();
        var item = v.Journal.Items.Single();
        item.StatusGroup.Should().Be("Unknown");
        item.Reconciliation.Should().Be("Blocked");
        item.RetryState.Should().Contain("Not retried");
        v.StatusCounts["Completed"].Should().Be(0);
        v.Alerts.Should().Contain(a => a.Kind == "UnknownOutcome" && a.Severity == "danger");
    }

    [Fact]
    public async Task ProtectedIdentifiers_NeverAppearInFull()
    {
        await Add(Entry("p", OrderState.Confirmed, "ref-secret-REF1", "deal-secret-DEA1,deal-secret-DEA2"));
        _broker.OnPositions = () => new[] { RiskData.Pos("EURUSD", OrderSide.Buy, 1, 1m, 1m) with { PositionId = "deal-secret-DEA1" } };
        _reader.Audit.Add(new AuditReference("audit-1", Now, "Execution", "Submit", "system", true, "intent_p_0002"));
        var v = await Build();
        var json = JsonSerializer.Serialize(v);
        foreach (var full in new[] { "ref-secret-REF1", "deal-secret-DEA1", "deal-secret-DEA2", "cmd_p_0001", "intent_p_0002", "ACC-A" })
            json.Should().NotContain(full);
        v.Journal.Items.Single().DealIds.Should().Be("••••DEA1, ••••DEA2");
        v.Journal.Items.Single().Reconciliation.Should().Be("Partial");
        v.Journal.Items.Single().Audit.Single().Refers.Should().Be("Order intent");
        v.Positions!.Single().InJournal.Should().BeTrue();
        v.Confirms.Single().DealReference.Should().Be("••••REF1");
    }

    [Fact]
    public async Task FailedBrokerReads_LeaveHoldingsUnknown_NotEmpty()
    {
        _broker.OnPositions = null;
        await Add(Entry("c", OrderState.Confirmed, "ref-c-CCCC", "deal-c-CCCC"));
        var v = await Build();
        v.Positions.Should().BeNull();
        v.ReadErrors.Should().Contain(e => e.StartsWith("Positions could not be read"));
        v.Journal.Items.Single().Reconciliation.Should().Be("Unknown");
        v.Alerts.Should().Contain(a => a.Kind == "ReadError");
        v.Alerts.Should().NotContain(a => a.Kind == "MissingAtBroker");
    }

    [Fact]
    public async Task OtherAccountsHoldings_AreDropped_AndUntrackedBrokerItemsAreFlagged()
    {
        _broker.OnPositions = () => new[] { RiskData.Pos("EURUSD", OrderSide.Buy, 1, 1m, 1m), RiskData.Pos("GOLD", OrderSide.Sell, 1, 1m, 1m) with { AccountId = "ACC-Z" } };
        await Add(Entry("other", OrderState.Unknown, account: "ACC-Z"));
        var v = await Build();
        v.Positions.Should().ContainSingle().Which.InJournal.Should().BeFalse();
        v.JournalTotal.Should().Be(0);
        v.Alerts.Should().Contain(a => a.Kind == "AccountMismatch");
        v.Alerts.Should().Contain(a => a.Kind == "NotInJournal");
    }

    [Fact]
    public async Task Filters_AndPagination_ApplyToTheJournal()
    {
        await Add(Entry("1", OrderState.Rejected, epic: "GOLD", minutesAgo: 3));
        await Add(Entry("2", OrderState.Proposed, minutesAgo: 2));
        await Add(Entry("3", OrderState.Unknown, minutesAgo: 1));
        var first = await Build(limit: 2);
        first.Journal.Total.Should().Be(3);
        first.Journal.Items.Select(i => i.State).Should().Equal("Unknown", "Proposed");
        (await Build(offset: 2, limit: 2)).Journal.Items.Single().State.Should().Be("Rejected");
        (await Build(epic: "GOLD")).Journal.Items.Single().Reason.Should().Be("Insufficient funds");
        (await Build(status: "Pending")).Journal.Items.Single().Reconciliation.Should().Be("Not sent");
        first.StatusCounts.Should().Contain(new KeyValuePair<string, int>("Rejected", 1));
    }

    [Fact]
    public async Task IncompleteStartupReconciliation_IsAlerted()
    {
        _control.State.Reconciliation = ReconciliationOutcome.Uncertain;
        _control.State.SuspensionReason = "Startup: 1 uncertain orders require reconciliation";
        var v = await Build();
        v.Startup.Outcome.Should().Be("Uncertain");
        v.Alerts.Should().Contain(a => a.Kind == "StartupReconciliation" && a.Message.Contains("1 uncertain"));
    }
}
