# Capital.com Demo Integration Hardening: Technical Report

Date: 2026-10-31. Branch: `feature/capital-demo-readonly-hardening`, based on `master` at `c1679ba`.

**Result: the code is ready for the authenticated read-only evidence run, and read-only Demo remains NO-GO until it passes.** No authenticated Capital.com request was made, so nothing here is real broker validation. Demo execution is disabled and Live is locked.

## What changed

| Area | Change | Main files |
|---|---|---|
| Broker and accounts | Explicit modes with simulation as default; lookup by requested account; session proof of the active account; mismatch, missing account and malformed finance refused; missing values stay null | `Infrastructure/Broker/*`, `Domain/Broker/*`, `Domain/Enums.cs` |
| Exposure | Positions and working orders parsed from documented shapes; failures are errors, not empty lists | `CapitalComBrokerAdapter.cs`, `BrokerErrors.cs` |
| Instruments and market data | Real metadata and dealing rules; versioned selection; quote and candle validation; NO_TRADE on bad data; switching outdates snapshots and AI results | `InstrumentSelectionService.cs`, `InstrumentCatalogService.cs`, `MarketDataService.cs`, `DecisionCoordinator.cs`, `Domain/AI`, `Domain/Decision`, `Domain/Market`, `Infrastructure/AI/*` |
| Credentials | Persistent macOS keychain store without administrator rights; Windows preserved; Demo and Live separated; rollback on failed writes | `Infrastructure/Security/*` |
| API security | Owner authorization on all `/api` routes; parsed Origin; single-use CSRF; sanitized errors; no broker request logging | `Api/ApiSecurity.cs`, `Api/Program.cs`, `CsrfTokenService.cs` |
| Execution integrity | Order contract tested but submission disabled; no resubmission; reconciliation of multiple deals; kill switch on uncertainty; risk fails closed | `CapitalComOrderContract.cs`, `OrderReconciliationService.cs`, `ExecutionGateway.cs`, `RiskEngine.cs`, `EfOrderJournal.cs`, `StartupReconciliationService.cs` |
| Dependencies | Microsoft.OpenApi at 2.13.0, patched for GHSA-v5pm-xwqc-g5wc; no suppression | `PilotTrade.Api.csproj` |
| UI | Owner token in memory only; CSRF; real accounts; dynamic instruments; visible NO_TRADE and missing values | `Web/src/App.tsx`, `Web/vite.config.ts`, `Api/wwwroot` |
| Tests and CI | Contract, API and per-OS credential tests; lint, audits, smoke test and secret scan in CI | `tests/*`, `.github/workflows/build.yml`, `scripts/*` |
| Docs | Status and contract documents rewritten | `docs/*`, `README.md`, `CHANGELOG.md`, `.env.example` |

`git show --stat` on the commit gives the complete file list.

## Results (Windows x64, 2026-10-31)

| Check | Result |
|---|---|
| Solution build | 0 errors, 0 warnings from the test and source projects |
| Unit tests | 49 passed |
| Integration, contract and API tests | 164 passed, 3 skipped (macOS only) |
| Credentials and owner authorization on their own | 50 passed, 3 skipped (macOS only) |
| Frontend lint and build | Pass |
| NuGet and npm vulnerability audits | No findings |
| Publish win-x64 and osx-arm64 | Both succeed |
| Published win-x64 smoke test, anonymous part | Pass: Live refused, `/health`, UI and script served, `/api` refuses anonymous callers |
| Secret scan of tracked files and both publish outputs | No findings; it catches known shapes only |
| Built UI against a local stub | Token refusal, account view, instrument switch clearing the snapshot, and NO_TRADE all work; nothing stored in the browser |

## Not verified

- **Real Capital.com API:** never called. Field shapes come from documentation.
- **macOS at runtime:** the macOS tests and smoke test have never run. They run in CI after a push.
- **Published smoke test owner flow:** written but never run. It was skipped locally so the real owner token stayed unread, and CI will be its first run. The same flow is tested in-process.
- **Workflow changes:** `build.yml` has not been run.
- **UI with the real API:** driven against a stub only; there is no automated frontend test suite.

## Remaining blockers

1. The authenticated evidence run needs owner approval.
2. A push is needed for macOS and CI verification.
3. Before the execution phase: Capital.com documents no margin-used or realized-P&L field, so the risk engine rejects all Capital orders. Solving this needs a proven data source or an owner-approved policy decision.
4. Build logs remain in 5 older commits of the public repository. History was not rewritten. A pattern search of all commits found no known credential shapes, but it cannot prove the logs hold nothing private.

## GO/NO-GO

The exact criteria are in `docs/DEMO_RELEASE_GO_NO_GO.md`. Current decision: **NO-GO** for read-only Demo and Demo execution; Live stays locked.
