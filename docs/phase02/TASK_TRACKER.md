# PilotTrade Phase 02 task tracker

This tracker is updated from code and test results on branch `feature/phase02-owner-control-center`, not from expectations. Details are in [IMPLEMENTATION_LOG.md](IMPLEMENTATION_LOG.md).

| Task | Description | State | Evidence or reason |
|---|---|---|---|
| 02-01 | Owner control-center shell | **PARTIAL** | Code, local checks and Windows CI run 42061252141 on `56c5791` all pass. Still open: visual acceptance, which is BLOCKED and needs the owner. |
| 02-02 | Overview dashboard | **PARTIAL** | Code and local checks pass (web 55/55, .NET 102 + 236/239). Adds read-only `GET /api/status`. Windows CI run 42075680649 on `76e7068` passes. Still open: the owner's visual check. |
| 02-03 | Multi-instrument market monitor | **PARTIAL** | Code and local checks pass (web 63/63, .NET 120 + 246/249). Adds the `Watchlists` table (migration `AddWatchlists`) and the watchlist, monitor and policy APIs. Windows CI run 42082050125 on `6750c88` passes. Still open: the owner's visual check and a real Demo check. |
| 02-04 | Claude/JEV observability | **PARTIAL** | Code and local checks pass (web 69/69, .NET 138 + 252/255). Adds the `DecisionEvents` table (migration `AddDecisionEvents`), the read-only decision-history and AI-status APIs, and the AI Decisions page. Runtime AI is not connected: the pipeline exists but is not registered, so no real decision exists. Windows CI run 42279387387 on `d961372` passes (the first run, on `22cc860`, failed on the secret scan and was fixed). Still open: the owner's visual check, and runtime wiring in the execution-readiness phase. |
| 02-05 | Risk Center / kill switch | **PARTIAL** | Code and local checks pass (web 74/74, .NET 168 + 258/261). Adds the read-only risk view (`GET /api/risk`), the kill-switch state (`GET /api/kill-switch`) and the guarded, audited engage and clear endpoints. The kill switch now keeps its reason, time and author (migration `AddKillSwitchReason`). A clear needs the typed phrase and passes only when every server-side check passes. Clearing enables nothing. Not yet checked: Windows CI on this commit and the owner's visual check. |
| 02-06 | Orders / positions / reconciliation | NOT STARTED | The current read-only lists and no-order evidence were moved into the new page. |
| 02-07 | Audit / alerts / diagnostics | NOT STARTED | There is no audit read API. |
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

   Any screenshot must not show the token field while it is filled in.

## Cross-phase holds

- **Phase 01 stays PARTIAL.** Still open: standard-user Windows 10/11 tests, Edge/Chrome and accessibility checks, the owner's decision on the development-only `minimatch` advisory, and a real rollback exercise.
- Phase 03 read-only Demo release: **NO-GO** until independent acceptance.
- Capital.com automated Demo execution: **DISABLED**.
- Live trading: **LOCKED**.
