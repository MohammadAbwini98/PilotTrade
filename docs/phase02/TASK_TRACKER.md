# PilotTrade Phase 02 task tracker

This tracker is updated from code and test results on branch `feature/phase02-owner-control-center`, not from expectations. Details are in [IMPLEMENTATION_LOG.md](IMPLEMENTATION_LOG.md).

| Task | Description | State | Evidence or reason |
|---|---|---|---|
| 02-01 | Owner control-center shell | **PARTIAL** | Code and local checks are done: 46/46 frontend tests, lint, build, .NET 102 + 234/237, release checks and secret scan. Still open: CI on the commit, which needs a push, and visual acceptance, which is BLOCKED and needs the owner. |
| 02-02 | Overview dashboard | NOT STARTED | Waits for 02-01 to close. Polling, stale-data marks and backoff were moved here. |
| 02-03 | Multi-instrument market monitor | NOT STARTED | There is no watchlist API yet. |
| 02-04 | Claude/JEV observability | NOT STARTED | There is no decision-history API. The page shows "Not available". |
| 02-05 | Risk Center / kill switch | NOT STARTED | The backend has kill-switch code but no HTTP API. The page shows "Not available" and has no button. |
| 02-06 | Orders / positions / reconciliation | NOT STARTED | The current read-only lists and no-order evidence were moved into the new page. |
| 02-07 | Audit / alerts / diagnostics | NOT STARTED | There is no audit read API. |
| 02-08 | Broker / credentials settings | NOT STARTED | Credential presence and the read-only check are shown. Replacing credentials is not offered yet. |
| 02-09 | AI / strategy / risk / system settings | NOT STARTED | There is no settings API. |
| 02-10 | E2E and accessibility acceptance | NOT STARTED | |

## Owner actions for 02-01

1. **Approve a push**, so that CI runs on the 02-01 commit.
2. **Visual check.** Open the running app in Edge or Chrome, connect, and go through the nine sidebar sections with the mouse and with the keyboard (Tab and Enter). Check these points:
   - the badges read Simulation or Capital.com Demo / Read-only, Demo execution disabled, and Live locked;
   - the account appears masked as `••••` plus its last four characters;
   - narrowing the window below 760 px moves the menu above the page.

   Any screenshot must not show the token field while it is filled in.

## Cross-phase holds

- **Phase 01 stays PARTIAL.** Still open: standard-user Windows 10/11 tests, Edge/Chrome and accessibility checks, the owner's decision on the development-only `minimatch` advisory, and a real rollback exercise.
- Phase 03 read-only Demo release: **NO-GO** until independent acceptance.
- Capital.com automated Demo execution: **DISABLED**.
- Live trading: **LOCKED**.
