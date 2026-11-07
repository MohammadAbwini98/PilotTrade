namespace PilotTrade.Application.Services;

using PilotTrade.Application.Controls;
using PilotTrade.Domain;
using PilotTrade.Domain.Audit;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Decision;
using PilotTrade.Domain.Market;
using PilotTrade.Domain.Security;

// Task 02-07. Alerts and diagnostics are derived on request from recorded and current state. Nothing here writes, retries,
// places, amends, cancels or closes anything, and nothing is sent anywhere: alerts are shown on the dashboard only.
public sealed record AlertView(string Key, string Severity, string Category, string Title, string Detail, DateTimeOffset? ObservedUtc, string Source);
public sealed record AlertsView(DateTimeOffset CheckedUtc, IReadOnlyList<AlertView> Alerts, string Note);

public static class DiagnosticStatus
{
    public const string Healthy = "Healthy";
    public const string Degraded = "Degraded";
    public const string Unavailable = "Unavailable";
    public const string NotConfigured = "Not Configured";
    public const string Unknown = "Unknown";
}

public sealed record DiagnosticComponent(string Key, string Label, string Status, string Detail, DateTimeOffset? AsOfUtc);
public sealed record OperationalFailure(DateTimeOffset? OccurredUtc, string Source, string Detail);
public sealed record DiagnosticsView(
    DateTimeOffset CheckedUtc, string Overall, string Version, string RuntimeMode, string Execution, string Live, string AccountRef,
    IReadOnlyList<DiagnosticComponent> Components, IReadOnlyList<OperationalFailure> Failures, string Note);

public sealed class OperationsMonitorService
{
    public static readonly TimeSpan BrokerCheckTimeout = TimeSpan.FromSeconds(10);
    private static readonly string[] DemoCredentialKeys = { "capital:Demo:api_key", "capital:Demo:identifier", "capital:Demo:password" };
    private static readonly AiStepStatus[] AiFailures = { AiStepStatus.TimedOut, AiStepStatus.Rejected, AiStepStatus.Unavailable };

    private readonly OrderLedgerService _ledger;
    private readonly RiskMonitorService _risk;
    private readonly ITradingControl _control;
    private readonly IDecisionEventStore _decisions;
    private readonly IBrokerAdapter _broker;
    private readonly IMarketDataService _market;
    private readonly IInstrumentSelectionService _selections;
    private readonly ISecretStore _secrets;
    private readonly IDatabaseHealth _database;
    private readonly BrokerRuntimeOptions _options;
    private readonly IClock _clock;

    public OperationsMonitorService(OrderLedgerService ledger, RiskMonitorService risk, ITradingControl control, IDecisionEventStore decisions,
        IBrokerAdapter broker, IMarketDataService market, IInstrumentSelectionService selections, ISecretStore secrets, IDatabaseHealth database,
        BrokerRuntimeOptions options, IClock clock)
    {
        _ledger = ledger; _risk = risk; _control = control; _decisions = decisions; _broker = broker; _market = market;
        _selections = selections; _secrets = secrets; _database = database; _options = options; _clock = clock;
    }

    private string BrokerSource => _options.Mode == BrokerMode.CapitalDemo ? "Capital.com Demo (read-only)" : "Simulator";

    public async Task<AlertsView> AlertsAsync(string accountId, CancellationToken ct)
    {
        var alerts = new List<AlertView>();
        var env = _options.Environment;

        // A source that cannot be read is itself an alert; it never hides the others.
        async Task Check(string source, Func<Task> run)
        {
            try { await run(); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                alerts.Add(new("CheckFailed:" + source, "warn", "Monitoring", $"{source} could not be checked",
                    ex is BrokerException b ? $"The check failed: {b.Kind}. The state it covers is unknown." : "The check failed. The state it covers is unknown.", null, source));
            }
        }

        var state = _control.State;
        if (state.KillSwitchEngaged)
            alerts.Add(new("KillSwitchEngaged", "danger", "Kill switch", "Kill switch engaged",
                "New orders are refused until the owner clears it in the Risk Center." + (state.KillSwitchReason is { Length: > 0 } r ? $" Reason: {r}" : ""),
                state.KillSwitchChangedUtc, "Kill switch"));
        if (state.Reconciliation is not (ReconciliationOutcome.Complete or ReconciliationOutcome.NotCompared))
            alerts.Add(new("StartupReconciliation", state.Reconciliation is ReconciliationOutcome.Mismatch or ReconciliationOutcome.Uncertain ? "danger" : "warn",
                "Reconciliation", $"Startup reconciliation: {state.Reconciliation}", state.SuspensionReason ?? "The journal was not confirmed against the broker at startup.",
                null, "Startup reconciliation"));

        await Check("Order journal", async () =>
        {
            var ledger = await _ledger.BuildAsync(accountId, new LedgerQuery(null, null, null, 0, 1), ct);
            foreach (var a in ledger.Alerts.Where(a => a.Kind is "UnknownOutcome" or "ReconciliationBlocked" or "MissingAtBroker" or "NotInJournal" or "AccountMismatch"))
                alerts.Add(new("Ledger:" + a.Kind, a.Severity, a.Kind == "UnknownOutcome" ? "Orders" : "Reconciliation",
                    a.Kind switch
                    {
                        "UnknownOutcome" => "Order with unknown broker outcome",
                        "ReconciliationBlocked" => "Journal cannot be matched to the broker",
                        "MissingAtBroker" => "Journal and broker disagree",
                        "NotInJournal" => "Broker holdings not in the journal",
                        _ => "Broker items from another account",
                    }, a.Message, ledger.CheckedUtc, "Order ledger"));
        });

        await Check("Risk monitor", async () =>
        {
            var risk = await _risk.BuildAsync(env, accountId, _selections.Current(accountId, env)?.Epic, BrokerSource, ct);
            foreach (var f in risk.Flags.Where(f => f.Key is "staleData" or "missingMargin" or "missingRealizedPnL" or "accountMismatch"))
            {
                if (f.Raised == false) continue;
                var category = f.Key == "staleData" ? "Market data" : f.Key == "accountMismatch" ? "Connectivity" : "Risk";
                alerts.Add(f.Raised == true
                    ? new("Risk:" + f.Key, f.Key == "accountMismatch" ? "danger" : "warn", category, f.Label, f.Detail, risk.CheckedUtc, "Risk monitor")
                    : new("Risk:" + f.Key, "warn", category, f.Label + ": not determined", f.Detail, risk.CheckedUtc, "Risk monitor"));
            }
            foreach (var err in risk.ReadErrors)
                alerts.Add(new("BrokerRead:" + err, "warn", "Connectivity", "Broker read failed", err, risk.CheckedUtc, "Risk monitor"));
        });

        await Check("AI decision log", () =>
        {
            foreach (var status in AiFailures)
            {
                var page = _decisions.List(new DecisionQuery(accountId, env, Status: status, Limit: 1));
                if (page.Total == 0) continue;
                var latest = page.Items[0];
                var step = latest.Claude.Status == status ? latest.Claude : latest.Jev;
                var what = status switch
                {
                    AiStepStatus.TimedOut => "timed out",
                    AiStepStatus.Rejected => "was rejected (invalid or unusable reply)",
                    _ => "was unavailable",
                };
                alerts.Add(new("Ai:" + status, "warn", "AI", $"Recorded AI step {what}",
                    $"{page.Total} recorded decisions include an AI step that {what}. Latest: {step.Provider}" + (step.Problem is { Length: > 0 } p ? $", {p}" : "") + ". Each ended as NO_TRADE.",
                    latest.RecordedUtc, "AI decision log"));
            }
            var vetoes = _decisions.List(new DecisionQuery(accountId, env, Limit: 200)).Items.Where(d => !string.IsNullOrWhiteSpace(d.RiskVeto)).ToList();
            if (vetoes.Count > 0)
                alerts.Add(new("RiskVeto", "info", "Risk", "Risk veto recorded",
                    $"{vetoes.Count} of the latest 200 recorded decisions were vetoed by risk. Latest: {vetoes[0].RiskVeto}", vetoes[0].RecordedUtc, "AI decision log"));
            return Task.CompletedTask;
        });

        if (_options.Mode == BrokerMode.CapitalDemo)
            await Check("Credential store", async () =>
            {
                var missing = 0;
                foreach (var key in DemoCredentialKeys) if (string.IsNullOrWhiteSpace(await _secrets.GetAsync(key, ct))) missing++;
                if (missing > 0)
                    alerts.Add(new("Credentials:CapitalDemo", "danger", "Credentials", "Capital.com Demo credentials incomplete",
                        $"{missing} of {DemoCredentialKeys.Length} Capital.com Demo credential entries are missing from Windows Credential Manager. Broker reads fail until they are set.", null, "Credential store"));
            });

        var order = new Dictionary<string, int> { ["danger"] = 0, ["warn"] = 1, ["info"] = 2 };
        var sorted = alerts.OrderBy(a => order.GetValueOrDefault(a.Severity, 3)).ThenByDescending(a => a.ObservedUtc ?? DateTimeOffset.MinValue).ToList();
        return new AlertsView(_clock.UtcNow, sorted,
            "Alerts reflect the current state, checked when this page loads. They are shown here only: nothing is emailed or messaged, and no action is taken automatically. Cleared conditions are not kept as history.");
    }

    public async Task<DiagnosticsView> DiagnosticsAsync(string accountId, string version, CancellationToken ct)
    {
        var env = _options.Environment;
        var failures = new List<OperationalFailure>();
        var components = new List<DiagnosticComponent> { new("api", "API", DiagnosticStatus.Healthy, "Responding to this request.", _clock.UtcNow) };

        try
        {
            var db = await _database.CheckAsync(ct);
            components.Add(!db.Reachable
                ? new("database", "Database", DiagnosticStatus.Unavailable, "The local database could not be opened.", _clock.UtcNow)
                : db.PendingMigrations > 0
                    ? new("database", "Database", DiagnosticStatus.Degraded, $"{db.PendingMigrations} schema migrations are not applied.", _clock.UtcNow)
                    : new("database", "Database", DiagnosticStatus.Healthy, "Reachable; schema up to date.", _clock.UtcNow));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            components.Add(new("database", "Database", DiagnosticStatus.Unavailable, "The local database could not be read.", _clock.UtcNow));
            failures.Add(new(_clock.UtcNow, "Database", "The local database could not be read."));
        }

        var credentialsMissing = 0;
        try { foreach (var key in DemoCredentialKeys) if (string.IsNullOrWhiteSpace(await _secrets.GetAsync(key, ct))) credentialsMissing++; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { credentialsMissing = -1; failures.Add(new(_clock.UtcNow, "Credential store", "Windows Credential Manager could not be read.")); }

        if (_options.Mode != BrokerMode.CapitalDemo)
            components.Add(new("broker", "Broker connection", DiagnosticStatus.Healthy, "Simulator. No Capital.com connection is used.", _clock.UtcNow));
        else if (credentialsMissing != 0)
            components.Add(new("broker", "Broker connection", credentialsMissing < 0 ? DiagnosticStatus.Unknown : DiagnosticStatus.NotConfigured,
                credentialsMissing < 0 ? "Credential presence could not be checked." : "Capital.com Demo credentials are not all set.", null));
        else
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(BrokerCheckTimeout);
            try
            {
                await _broker.GetAccountAsync(accountId, cts.Token);
                components.Add(new("broker", "Broker connection", DiagnosticStatus.Healthy, "Capital.com Demo account read succeeded (read-only).", _clock.UtcNow));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                components.Add(new("broker", "Broker connection", DiagnosticStatus.Unavailable, $"No reply within {BrokerCheckTimeout.TotalSeconds:0} s.", _clock.UtcNow));
                failures.Add(new(_clock.UtcNow, "Broker connection", $"Account read timed out after {BrokerCheckTimeout.TotalSeconds:0} s."));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var detail = ex is BrokerException b ? $"Account read failed: {b.Kind}." : "Account read failed.";
                components.Add(new("broker", "Broker connection", DiagnosticStatus.Unavailable, detail, _clock.UtcNow));
                failures.Add(new(_clock.UtcNow, "Broker connection", detail));
            }
        }

        var epic = _selections.Current(accountId, env)?.Epic;
        if (epic is null)
            components.Add(new("marketFeed", "Market feed", DiagnosticStatus.NotConfigured, "No instrument is selected.", null));
        else
        {
            var health = _market.GetFeedHealth(epic);
            var last = _market.LastQuoteUtc(epic);
            var (status, detail) = health switch
            {
                FeedHealth.Healthy => (DiagnosticStatus.Healthy, $"{epic}: quotes are fresh."),
                FeedHealth.Stale => (DiagnosticStatus.Degraded, $"{epic}: the latest quote is stale."),
                FeedHealth.Disconnected => (DiagnosticStatus.Unavailable, $"{epic}: the feed is disconnected."),
                _ => (DiagnosticStatus.Unknown, $"{epic}: no quote has been received yet."),
            };
            components.Add(new("marketFeed", "Market feed", status, detail, last));
        }

        var state = _control.State;
        components.Add(new("reconciliation", "Startup reconciliation",
            state.Reconciliation switch
            {
                ReconciliationOutcome.Complete => DiagnosticStatus.Healthy,
                ReconciliationOutcome.NotCompared or ReconciliationOutcome.NotRun => DiagnosticStatus.Unknown,
                _ => DiagnosticStatus.Degraded,
            },
            state.Reconciliation == ReconciliationOutcome.NotCompared ? "Not compared with a broker (simulator or no Demo account)." : $"Outcome: {state.Reconciliation}.", null));
        components.Add(new("killSwitch", "Kill switch", state.KillSwitchEngaged ? DiagnosticStatus.Degraded : DiagnosticStatus.Healthy,
            state.KillSwitchEngaged ? "Engaged: new orders are refused." : "Not engaged.", state.KillSwitchChangedUtc));
        components.Add(new("ai", "AI runtime (Claude and JEV)", DiagnosticStatus.NotConfigured, "Runtime AI is not connected in this build.", null));
        components.Add(new("credentials", "Capital.com Demo credentials",
            credentialsMissing < 0 ? DiagnosticStatus.Unknown
                : credentialsMissing == 0 ? DiagnosticStatus.Healthy
                : credentialsMissing == DemoCredentialKeys.Length ? DiagnosticStatus.NotConfigured : DiagnosticStatus.Degraded,
            credentialsMissing < 0 ? "Presence could not be checked." : $"{DemoCredentialKeys.Length - credentialsMissing} of {DemoCredentialKeys.Length} entries present. Values are never shown.", null));

        try
        {
            failures.AddRange(AiFailures
                .SelectMany(s => _decisions.List(new DecisionQuery(accountId, env, Status: s, Limit: 5)).Items
                    .Select(d => (d, step: d.Claude.Status == s ? d.Claude : d.Jev)))
                .GroupBy(x => x.d.DecisionId).Select(g => g.First())
                .OrderByDescending(x => x.d.RecordedUtc).Take(5)
                .Select(x => new OperationalFailure(x.d.RecordedUtc, "AI decision log", $"{x.step.Provider}: {x.step.Status}" + (x.step.Problem is { Length: > 0 } p ? $", {p}" : ""))));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { failures.Add(new(_clock.UtcNow, "AI decision log", "The decision log could not be read.")); }

        var rank = new Dictionary<string, int> { [DiagnosticStatus.Unavailable] = 3, [DiagnosticStatus.Degraded] = 2, [DiagnosticStatus.Unknown] = 1, [DiagnosticStatus.Healthy] = 0 };
        var worst = components.Where(c => c.Status != DiagnosticStatus.NotConfigured).Select(c => rank[c.Status]).DefaultIfEmpty(0).Max();
        var overall = rank.First(kv => kv.Value == worst).Key;

        var exec = _broker.ExecutionEnabled;
        return new DiagnosticsView(_clock.UtcNow, overall, version, RuntimeLabels.Mode(_options.Mode), RuntimeLabels.Execution(_options.Mode, exec), RuntimeLabels.Live,
            OrderLedgerService.Mask(accountId)!, components, failures.OrderByDescending(f => f.OccurredUtc ?? DateTimeOffset.MinValue).ToList(),
            "Checked when requested. Read-only: the broker check reads the account only. Messages are fixed summaries, never raw broker or provider replies.");
    }
}
