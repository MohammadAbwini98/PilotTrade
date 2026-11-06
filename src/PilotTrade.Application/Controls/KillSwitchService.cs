namespace PilotTrade.Application.Controls;

using System.Text.Json;
using PilotTrade.Domain;
using PilotTrade.Domain.Audit;
using PilotTrade.Domain.Broker;
using PilotTrade.Domain.Execution;

public sealed record ClearCheck(string Name, bool Passed, string Detail);

public sealed record EngageOutcome(bool Changed, bool AuditRecorded);

public sealed record ClearOutcome(bool Cleared, bool ConfirmationAccepted, IReadOnlyList<ClearCheck> Checks, bool AuditRecorded);

// Task 02-05: the owner's emergency stop and its guarded release.
// Engaging never depends on the broker, the journal, the risk view or the audit log: it only sets and saves the switch.
// Clearing needs a typed confirmation and passes only when every server-side check passes. Clearing enables nothing:
// Demo order submission stays disabled and Live stays locked.
public sealed class KillSwitchService
{
    public const string ClearPhrase = "CLEAR KILL SWITCH";
    public const int MaxReasonLength = 200;

    private readonly ITradingControl _control;
    private readonly IBrokerAdapter _broker;
    private readonly IOrderJournal _journal;
    private readonly IAuditLogger _audit;
    private readonly IClock _clock;
    private readonly IIdGenerator _ids;
    private readonly SemaphoreSlim _clearing = new(1, 1);

    public KillSwitchService(ITradingControl control, IBrokerAdapter broker, IOrderJournal journal, IAuditLogger audit, IClock clock, IIdGenerator ids)
    { _control = control; _broker = broker; _journal = journal; _audit = audit; _clock = clock; _ids = ids; }

    public static string? ReasonProblem(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? "A reason is required"
        : reason.Trim().Length > MaxReasonLength ? $"The reason is longer than {MaxReasonLength} characters"
        : reason.Any(char.IsControl) ? "The reason contains control characters"
        : null;

    public async Task<EngageOutcome> EngageAsync(TradingEnvironment env, string accountId, string reason, CancellationToken ct)
    {
        var text = $"Owner: {reason.Trim()}";
        var previous = _control.State.KillSwitchReason;
        // A save failure propagates after the switch is engaged in memory; the caller reports it.
        var changed = _control.EngageKillSwitch(text, "owner");
        var audited = await TryAuditAsync(env, accountId, changed ? "Engaged" : "EngageRepeated",
            new { reason = text, alreadyEngagedWith = changed ? null : previous });
        return new EngageOutcome(changed, audited);
    }

    // Every check runs and is reported, so the owner sees all that blocks a clear, not just the first.
    public async Task<IReadOnlyList<ClearCheck>> CheckAsync(TradingEnvironment env, string accountId, CancellationToken ct)
    {
        var s = _control.State;
        var checks = new List<ClearCheck>
        {
            new("Kill switch engaged", s.KillSwitchEngaged, s.KillSwitchEngaged ? "Engaged" : "Not engaged: there is nothing to clear"),
            s.Reconciliation switch
            {
                ReconciliationOutcome.Complete => new("Startup reconciliation", true, "Complete: the journal and the broker match"),
                ReconciliationOutcome.NotCompared => new("Startup reconciliation", true, "No uncertain order in the journal; holdings not compared because execution is disabled"),
                ReconciliationOutcome.NotRun => new("Startup reconciliation", false, "Has not run"),
                ReconciliationOutcome.Uncertain => new("Startup reconciliation", false, "Uncertain orders remain"),
                ReconciliationOutcome.Mismatch => new("Startup reconciliation", false, "The journal and the broker do not match"),
                ReconciliationOutcome.CredentialsMissing => new("Startup reconciliation", false, "Could not run: broker credentials are missing"),
                _ => new("Startup reconciliation", false, "Failed"),
            }
        };

        try
        {
            var uncertain = await _journal.ListUncertainAsync(env, accountId, ct);
            checks.Add(new("No uncertain broker confirmation", uncertain.Count == 0,
                uncertain.Count == 0 ? "No journaled order is waiting for a broker confirmation" : $"{uncertain.Count} journaled orders have no certain broker outcome"));
        }
        catch (Exception) { checks.Add(new("No uncertain broker confirmation", false, "The order journal could not be read")); }

        try
        {
            var account = await _broker.GetAccountAsync(accountId, ct);
            var matches = account.AccountId == accountId && account.Environment == env;
            checks.Add(new("Broker account matches", matches, matches ? "The broker reports the configured account and environment" : "The broker reported a different account or environment"));
        }
        catch (Exception) { checks.Add(new("Broker account matches", false, "The broker account could not be read")); }

        try
        {
            var orders = await _broker.GetWorkingOrdersAsync(accountId, ct);
            checks.Add(new("No open working orders", orders.Count == 0,
                orders.Count == 0 ? "The broker reports no working orders" : $"{orders.Count} working orders are open at the broker. Review them there first; this app does not cancel orders."));
        }
        catch (Exception) { checks.Add(new("No open working orders", false, "Working orders could not be read")); }

        return checks;
    }

    public async Task<ClearOutcome> ClearAsync(TradingEnvironment env, string accountId, string? confirmation, CancellationToken ct)
    {
        if (!string.Equals(confirmation, ClearPhrase, StringComparison.Ordinal))
        {
            var audited = await TryAuditAsync(env, accountId, "ClearRefused", new { failed = new[] { "Confirmation" } });
            return new ClearOutcome(false, false, Array.Empty<ClearCheck>(), audited);
        }

        await _clearing.WaitAsync(ct);
        try
        {
            var version = _control.State.KillSwitchVersion;
            var checks = await CheckAsync(env, accountId, ct);
            var failed = checks.Where(c => !c.Passed).Select(c => c.Name).ToList();
            // Cleared only when nothing failed and no engage happened while the checks ran.
            if (failed.Count == 0 && !_control.ClearKillSwitch("owner", version)) failed.Add("Kill switch changed during the checks");
            var cleared = failed.Count == 0;
            var audited = await TryAuditAsync(env, accountId, cleared ? "Cleared" : "ClearRefused", new { failed });
            if (!cleared && failed.Contains("Kill switch changed during the checks"))
                checks = checks.Append(new ClearCheck("Kill switch unchanged during the checks", false, "The kill switch changed while the checks ran; check again")).ToList();
            return new ClearOutcome(cleared, true, checks, audited);
        }
        finally { _clearing.Release(); }
    }

    // The audit log records the change; a failed audit write never undoes an engage.
    private async Task<bool> TryAuditAsync(TradingEnvironment env, string accountId, string action, object details)
    {
        try
        {
            await _audit.LogAsync(new AuditEvent(_ids.NewId("audit"), _clock.UtcNow, "KillSwitch", action, "owner", env.ToString(), accountId, null, null,
                JsonSerializer.Serialize(details), false), CancellationToken.None);
            return true;
        }
        catch (Exception) { return false; }
    }
}
