# PilotTrade Phase 02 task tracker

This tracker is updated from code and test results on branch `feature/phase02-owner-control-center`, not from expectations. Details are in [IMPLEMENTATION_LOG.md](IMPLEMENTATION_LOG.md).

| Task | Description | State | Evidence or reason |
|---|---|---|---|
| 02-01 | Owner control-center shell | **PARTIAL** | Code, local checks and Windows CI run 42061252141 on `56c5791` all pass. Still open: visual acceptance, which is BLOCKED and needs the owner. |
| 02-02 | Overview dashboard | **PARTIAL** | Code and local checks pass (web 55/55, .NET 102 + 236/239). Adds read-only `GET /api/status`. Windows CI run 42075680649 on `76e7068` passes. Still open: the owner's visual check. |
| 02-03 | Multi-instrument market monitor | **PARTIAL** | Code and local checks pass (web 63/63, .NET 120 + 246/249). Adds the `Watchlists` table (migration `AddWatchlists`) and the watchlist, monitor and policy APIs. Windows CI run 42082050125 on `6750c88` passes. Still open: the owner's visual check and a real Demo check. |
| 02-04 | Claude/JEV observability | **PARTIAL** | Code and local checks pass (web 69/69, .NET 138 + 252/255). Adds the `DecisionEvents` table (migration `AddDecisionEvents`), the read-only decision-history and AI-status APIs, and the AI Decisions page. Runtime AI is not connected: the pipeline exists but is not registered, so no real decision exists. Windows CI run 42279387387 on `d961372` passes (the first run, on `22cc860`, failed on the secret scan and was fixed). Still open: the owner's visual check, and runtime wiring in the execution-readiness phase. |
| 02-05 | Risk Center / kill switch | **PASS** (owner decision, task level) | Windows CI run 42290558417 on `ae08a79` passes. Code and local checks pass (web 74/74, .NET 168 + 258/261). Adds the read-only risk view (`GET /api/risk`), the kill-switch state (`GET /api/kill-switch`) and the guarded, audited engage and clear endpoints. The kill switch now keeps its reason, time and author (migration `AddKillSwitchReason`). A clear needs the typed phrase and passes only when every server-side check passes. Clearing enables nothing. |
| 02-06 | Orders / positions / reconciliation | **PARTIAL** | Code and local checks pass (web 79/79, .NET 189 + 264/267). Adds the read-only ledger API (`GET /api/ledger`), persisted order-state history (migration `AddOrderStateHistory`) and the read-only Orders & Positions page: journal, state history, audit drill-down, positions, working orders, broker confirmations, reconciliation and alerts. Broker-observed, locally inferred, simulator and Unknown are kept apart. There is no place, amend, cancel or close control and no write route. Windows CI run 42326390010 on `cf3d32b` passes. **Corrections (local commit, not yet pushed or run in CI):** audit-event ids in the ledger are masked; the ledger names the runtime mode and says "Simulated fills only. No Capital.com orders are sent." in Simulation, so simulated execution never reads as broker permission; the journal is paged in the database (only keys are read to order, full rows for the page alone). Local checks pass (web 89/89, .NET 193 + 272/275). Still open: the owner's visual check, and CI on the correction commit. |
| 02-07 | Audit / alerts / diagnostics | **PARTIAL** | Code and local checks pass (web 89/89, .NET 193 + 272/275). Adds read-only `GET /api/audit`, `GET /api/alerts` and `GET /api/diagnostics`, the Audit & Alerts page (alerts plus a searchable, paged audit explorer) and a Diagnostics panel on the System page. Audit references are masked and stored details are never read. Alerts are current conditions only. Not done: export (none in this build) and audit severity (records have none). Still open: the owner's visual check, CI on the commit, and the two items above. |
| 02-08 | Broker / credentials settings | NOT STARTED | Credential presence and the read-only check are shown. Replacing credentials is not offered yet. |
| 02-09 | AI / strategy / risk / system settings | NOT STARTED | There is no settings API. |
| 02-10 | E2E and accessibility acceptance | NOT STARTED | |

## Owner actions for 02-01

1. Done: pushed with owner approval; CI run 42061252141 passed on `56c5791`.
2. **Visual check.** Open the running app in Edge or Chrome, connect, and go through the nine sidebar sections with the mouse and with the keyboard (Tab and Enter). Check these points:
   - the badges read Simulation or Capital.com Demo / Read-only, Demo execution disabled, and Live locked;
   - the account appears masked as `••••` plus its last four characters;
   - narrowing the window below 760 px moves the menu above the page.

   - the 02-02 overview shows provenance tags, the safety card and the warning list. After you stop the backend, the figures are marked stale within one poll.

   - on the 02-03 Markets page, add instruments to the watchlist and refresh the monitor. A closed or stale market must show as Blocked.

   - on the 02-04 AI Decisions page, check that it says runtime AI is not connected, shows API key presence only, and shows an empty history.

   - on the 02-05 Risk Center, check that missing broker fields show as Unknown with no figure. Engage the kill switch with a reason, restart the API, and check that it is still engaged with the same reason. Check that a clear needs the typed phrase. The page must have no control that enables orders, Demo execution or Live trading.

   - on the 02-06 Orders & Positions page, check that an empty journal reads "No orders in the journal yet", that the status counts are zero, that Runtime mode reads Simulation or Capital.com Demo, that Order submission reads "Simulated fills only. No Capital.com orders are sent." (Simulation) or "Capital.com Demo order submission disabled (read-only)" (Demo) in grey, never red, that every ID, including audit events, shows as `••••` plus four characters, and that the filters and page buttons work. The page must have no place, amend, cancel or close control.

   - on the 02-07 Audit & Alerts page, check that alerts show severity, title, detail, a UTC time with a labelled local time and the source, or "No alert conditions were observed in this check"; that the audit explorer filters by From/To date (UTC), category, instrument, correlation ID and search, pages by 25, and shows references as `••••` plus four characters with no stored details; that a From date after the To date is refused in the page; and that there is no acknowledge, resolve, export or delete control. Engage the kill switch on the Risk Center and refresh alerts: a Kill switch engaged alert appears.

   - on the System page, check the Diagnostics panel: overall status, version, runtime mode, order submission, Live locked, the masked account and a status per component (Healthy, Degraded, Unavailable, Not Configured or Unknown). Stop the API and press Run diagnostics: the panel must say the status is Unknown, not keep the old statuses.

   - with the keyboard alone, reach every filter, the Apply, Clear, page and Refresh buttons on Audit & Alerts, and Run diagnostics. Below 760 px the tables scroll inside their panels. Check in both Edge and Chrome.

   Any screenshot must not show the token field while it is filled in.

## Cross-phase holds

- **Phase 01 stays PARTIAL.** Still open: standard-user Windows 10/11 tests, Edge/Chrome and accessibility checks, the owner's decision on the development-only `minimatch` advisory, and a real rollback exercise.
- Phase 03 read-only Demo release: **NO-GO** until independent acceptance.
- Capital.com automated Demo execution: **DISABLED**.
- Live trading: **LOCKED**.
