namespace PilotTrade.Application.Services;

using PilotTrade.Application.Controls;
using PilotTrade.Domain;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Execution;

public sealed record LedgerAlert(string Severity, string Kind, string Message);
public sealed record LedgerTransitionView(string? FromState, string ToState, DateTimeOffset OccurredUtc, string? Reason, string Source);
public sealed record LedgerAuditView(string Id, DateTimeOffset OccurredUtc, string Category, string Action, string Actor, bool SensitiveRedacted, string Refers);
public sealed record LedgerEntryView(
    string CommandRef, string IntentRef, string AccountRef, string Epic, string? Side, decimal? Size,
    string State, string StatusGroup, string Evidence, string Reconciliation, string ReconciliationDetail, string? RetryState,
    string? DealReference, string? DealIds,
    DateTimeOffset CreatedUtc, DateTimeOffset? SubmittedUtc, DateTimeOffset? ConfirmedUtc, DateTimeOffset UpdatedUtc, string? Reason,
    IReadOnlyList<LedgerTransitionView> Transitions, IReadOnlyList<LedgerAuditView> Audit);
public sealed record LedgerPositionView(string PositionRef, string Epic, string Direction, decimal Size, decimal? OpenLevel, decimal? CurrentLevel,
    decimal? UnrealizedPnL, string? Currency, DateTimeOffset OpenedUtc, DateTimeOffset AsOfUtc, string Provenance, bool InJournal);
public sealed record LedgerWorkingOrderView(string OrderRef, string Epic, string Direction, string Type, decimal Size, decimal? Level,
    DateTimeOffset CreatedUtc, DateTimeOffset AsOfUtc, string Provenance, bool InJournal);
public sealed record LedgerConfirmView(string CommandRef, string Epic, string DealReference, string? DealIds, string State, string Evidence, DateTimeOffset? ConfirmedUtc, DateTimeOffset UpdatedUtc);
public sealed record LedgerPageView(int Total, int Offset, int Limit, IReadOnlyList<LedgerEntryView> Items);
public sealed record LedgerStartup(string Outcome, bool Complete, string? SuspensionReason);
public sealed record LedgerView(
    DateTimeOffset CheckedUtc, string Source, bool ExecutionEnabled, string AccountRef,
    LedgerStartup Startup, IReadOnlyDictionary<string, int> StatusCounts, int JournalTotal,
    IReadOnlyList<LedgerAlert> Alerts, LedgerPageView Journal,
    IReadOnlyList<LedgerPositionView>? Positions, IReadOnlyList<LedgerWorkingOrderView>? WorkingOrders,
    IReadOnlyList<LedgerConfirmView> Confirms, IReadOnlyList<string> ReadErrors);

// Task 02-06: a read-only ledger that explains orders and positions end to end. It reads the journal, its state history, the
// audit log and the broker's holdings, and changes nothing. It has no path to place, amend, cancel or close an order.
// Broker-observed state, state this app inferred, and Unknown are kept apart: a simulator fill is never shown as a Capital.com
// confirmation, an unknown outcome never counts as complete, and a deal the broker no longer holds is not reported as closed.
public sealed class OrderLedgerService
{
    public const int MaxLimit = 100;
    public const int MaxConfirms = 100;
    public static readonly IReadOnlyList<string> StatusGroups = new[] { "Pending", "Unknown", "Rejected", "Cancelled", "Completed" };

    private readonly IOrderLedgerReader _reader;
    private readonly IOrderJournal _journal;
    private readonly IBrokerAdapter _broker;
    private readonly ITradingControl _control;
    private readonly BrokerRuntimeOptions _options;
    private readonly IClock _clock;

    public OrderLedgerService(IOrderLedgerReader reader, IOrderJournal journal, IBrokerAdapter broker, ITradingControl control, BrokerRuntimeOptions options, IClock clock)
    { _reader = reader; _journal = journal; _broker = broker; _control = control; _options = options; _clock = clock; }

    public static string Group(OrderState s) => s switch
    {
        OrderState.Proposed or OrderState.Validated or OrderState.RiskApproved or OrderState.Previewed => "Pending",
        OrderState.Submitting or OrderState.BrokerPending or OrderState.Unknown => "Unknown",
        OrderState.Rejected => "Rejected",
        OrderState.Cancelled => "Cancelled",
        OrderState.Confirmed or OrderState.Reconciled => "Completed",
        _ => "Unknown"
    };

    public static IReadOnlyCollection<OrderState>? StatesFor(string? group) => string.IsNullOrWhiteSpace(group) ? null
        : Enum.GetValues<OrderState>().Where(s => Group(s) == group).ToList();

    // Only the last four characters of a protected identifier are shown; a short one is hidden entirely.
    public static string? Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var v = value.Trim();
        return v.Length <= 6 ? "••••" : "••••" + v[^4..];
    }

    public static string? MaskList(string? value) => string.IsNullOrWhiteSpace(value) ? null
        : string.Join(", ", Split(value).Select(Mask));

    private static string[] Split(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string Evidence(LedgerRow row) => row.Adapter switch
    {
        "Simulation" => "Simulator",
        null or "" or "Unknown" => "Unknown source",
        _ when !string.IsNullOrWhiteSpace(row.Entry.BrokerDealReference) => "Broker-observed",
        _ => "Locally inferred"
    };

    // held is null when the broker's holdings could not be read.
    public static (string Status, string Detail) Reconcile(LedgerRow row, IReadOnlySet<string>? held)
    {
        var e = row.Entry;
        var sim = row.Adapter == "Simulation" ? " Simulator holdings, not a Capital.com confirmation." : "";
        switch (Group(e.State))
        {
            case "Pending": return ("Not sent", "Never sent to the broker.");
            case "Rejected": return ("Not applicable", "Rejected, so nothing is expected at the broker.");
            case "Cancelled": return ("Not applicable", "Cancelled, so nothing is expected at the broker.");
            case "Unknown":
                return ("Blocked", e.State == OrderState.Submitting
                    ? "The app stopped while sending. The outcome is unknown until reconciliation reads the broker. It is never resent."
                    : "The broker outcome is unknown until reconciliation reads the broker. It is never resent.");
        }
        if (string.IsNullOrWhiteSpace(e.BrokerDealId)) return ("Blocked", "Marked complete without a deal id, so it cannot be matched to the broker.");
        if (held is null) return ("Unknown", "The broker's holdings could not be read." + sim);
        var ids = Split(e.BrokerDealId);
        var count = ids.Count(held.Contains);
        if (count == ids.Length) return ("Matched", $"All {ids.Length} deals are held at the broker." + sim);
        if (count > 0) return ("Partial", $"{count} of {ids.Length} deals are held at the broker." + sim);
        return ("Not held", "The broker does not hold these deals now. This app has no confirmation that they closed." + sim);
    }

    private static async Task<T?> Read<T>(Func<Task<T>> read, string what, List<string> errors) where T : class
    {
        try { return await read(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add(ex is BrokerException b ? $"{what} could not be read: {b.Kind}" : $"{what} could not be read");
            return null;
        }
    }

    public async Task<LedgerView> BuildAsync(string accountId, LedgerQuery query, CancellationToken ct)
    {
        var env = _options.Environment;
        var errors = new List<string>();
        var source = _options.Mode == BrokerMode.CapitalDemo ? "Capital.com Demo (read-only)" : "Simulator";
        var holdingSource = _options.Mode == BrokerMode.CapitalDemo ? "Broker-observed (Capital.com Demo, read-only)" : "Simulator";

        var positions = await Read(() => _broker.GetPositionsAsync(accountId, ct), "Positions", errors);
        var orders = await Read(() => _broker.GetWorkingOrdersAsync(accountId, ct), "Working orders", errors);
        // Holdings reported for another account are not this account's holdings.
        var foreign = (positions?.Count(p => p.AccountId != accountId) ?? 0) + (orders?.Count(o => o.AccountId != accountId) ?? 0);
        positions = positions?.Where(p => p.AccountId == accountId).ToList();
        orders = orders?.Where(o => o.AccountId == accountId).ToList();
        IReadOnlySet<string>? held = positions is null || orders is null ? null
            : positions.Select(p => p.PositionId).Concat(orders.Select(o => o.OrderId)).ToHashSet(StringComparer.Ordinal);

        var all = await _journal.ListAsync(env, accountId, ct);
        var journaled = all.Where(e => !string.IsNullOrWhiteSpace(e.BrokerDealId)).SelectMany(e => Split(e.BrokerDealId!)).ToHashSet(StringComparer.Ordinal);
        var page = await _reader.ReadAsync(env, accountId, query, MaxConfirms, ct);

        var items = page.Rows.Select(row =>
        {
            var e = row.Entry;
            var (status, detail) = Reconcile(row, held);
            var keys = new[] { e.CommandId, e.IntentId, e.CorrelationId }.Where(k => !string.IsNullOrEmpty(k)).ToHashSet(StringComparer.Ordinal);
            var transitions = page.Transitions.TryGetValue(e.CommandId, out var t) ? t : Array.Empty<OrderTransition>();
            return new LedgerEntryView(
                Mask(e.CommandId)!, Mask(e.IntentId)!, Mask(e.AccountId)!, e.Epic, e.Side?.ToString(), e.Size,
                e.State.ToString(), Group(e.State), Evidence(row), status, detail,
                Group(e.State) == "Unknown" ? "Not retried. Duplicate submissions of this intent are refused." : null,
                Mask(e.BrokerDealReference), MaskList(e.BrokerDealId),
                e.CreatedUtc, e.SubmittedUtc, e.ConfirmedUtc, e.UpdatedUtc, e.RejectionReason,
                transitions.Select(x => new LedgerTransitionView(x.FromState, x.ToState, x.OccurredUtc, x.Reason, x.Adapter == "Simulation" ? "Simulator" : x.Adapter)).ToList(),
                page.Audit.Where(a => keys.Contains(a.CorrelationId))
                    .Select(a => new LedgerAuditView(a.Id, a.OccurredUtc, a.Category, a.Action, a.Actor, a.SensitiveRedacted,
                        a.CorrelationId == e.CommandId ? "Order command" : a.CorrelationId == e.IntentId ? "Order intent" : "Correlation")).ToList());
        }).ToList();

        var epic = query.Epic;
        var positionViews = positions?.Where(p => epic is null || p.Epic == epic)
            .Select(p => new LedgerPositionView(Mask(p.PositionId)!, p.Epic, p.Direction.ToString(), p.Size, p.OpenLevel, p.CurrentLevel,
                p.UnrealizedPnL, p.Currency, p.OpenedUtc, p.AsOfUtc, holdingSource, journaled.Contains(p.PositionId))).ToList();
        var orderViews = orders?.Where(o => epic is null || o.Epic == epic)
            .Select(o => new LedgerWorkingOrderView(Mask(o.OrderId)!, o.Epic, o.Direction.ToString(), o.Type.ToString(), o.Size, o.Level,
                o.CreatedUtc, o.AsOfUtc, holdingSource, journaled.Contains(o.OrderId))).ToList();

        var confirms = page.Confirms
            .Select(r => new LedgerConfirmView(Mask(r.Entry.CommandId)!, r.Entry.Epic, Mask(r.Entry.BrokerDealReference)!, MaskList(r.Entry.BrokerDealId),
                r.Entry.State.ToString(), Evidence(r), r.Entry.ConfirmedUtc, r.Entry.UpdatedUtc))
            .ToList();

        var state = _control.State;
        var alerts = new List<LedgerAlert>();
        var unknown = all.Count(e => Group(e.State) == "Unknown");
        if (unknown > 0)
            alerts.Add(new("danger", "UnknownOutcome", $"{unknown} orders have an unknown broker outcome. They are not complete, are never resent, and block a kill-switch clear until reconciliation resolves them."));
        var completedNoDeal = all.Count(e => Group(e.State) == "Completed" && string.IsNullOrWhiteSpace(e.BrokerDealId));
        if (completedNoDeal > 0)
            alerts.Add(new("danger", "ReconciliationBlocked", $"{completedNoDeal} completed orders have no deal id, so they cannot be matched to the broker."));
        if (held is not null)
        {
            var missing = journaled.Count(id => !held.Contains(id));
            if (missing > 0) alerts.Add(new("warn", "MissingAtBroker", $"{missing} journaled deals are not held at the broker now. No closure is confirmed by this app."));
            var untracked = held.Count(id => !journaled.Contains(id));
            if (untracked > 0) alerts.Add(new("warn", "NotInJournal", $"{untracked} broker positions or working orders are not in this app's journal."));
        }
        if (foreign > 0) alerts.Add(new("danger", "AccountMismatch", $"{foreign} broker items belong to another account and are not shown."));
        foreach (var err in errors) alerts.Add(new("warn", "ReadError", err));
        if (state.Reconciliation is not (ReconciliationOutcome.Complete or ReconciliationOutcome.NotCompared))
            alerts.Add(new("warn", "StartupReconciliation", $"Startup reconciliation: {state.Reconciliation}. {state.SuspensionReason}".Trim()));

        var counts = StatusGroups.ToDictionary(g => g, g => all.Count(e => Group(e.State) == g));
        return new LedgerView(_clock.UtcNow, source, _broker.ExecutionEnabled, Mask(accountId)!,
            new LedgerStartup(state.Reconciliation.ToString(), state.ReconciliationComplete, state.SuspensionReason),
            counts, all.Count, alerts, new LedgerPageView(page.Total, query.Offset, query.Limit, items),
            positionViews, orderViews, confirms, errors);
    }
}
