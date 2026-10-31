# Capital.com Demo Integration Hardening: Technical Report

Date: 2026-10-31. Branch: `feature/capital-demo-readonly-hardening`, based on `master` at `c1679ba`.

**Result: read-only Demo is NO-GO.** The owner's authenticated run on `c3adf2d` failed its quote step, and the fix has not run against the real API. Demo execution is disabled and Live is locked.

## Authenticated run, 2026-10-31 18:38 UTC

| Step | Recorded | What it does and does not show |
|---|---|---|
| accounts | Pass, 1 account | Login and parsing work; multiple accounts unproven |
| accountIdentityAndFinancials | Pass | Account matched; available and equity present; margin used and realized P&L absent. No values recorded or compared |
| instrumentMetadata | Pass, status Closed | Presence only, one epic. Closed is plausible on a Saturday evening |
| positions | Pass, 0 | Empty parsing only |
| workingOrders | Pass, 0 | Empty parsing only |
| quote | **Fail**, `MalformedResponse` | Adapter bug, see below |
| candles | Pass, 50 | Count only; freshness was not checked |

**Quote failure.** `GET /markets?epics=` is documented to answer with `marketDetails[]`, holding `instrument` and `snapshot`. Only `searchTerm` answers with `markets[]`. The adapter read `markets`, so every real quote request would fail and all Capital market data would be NO_TRADE. The mock had the same wrong assumption, which is why the tests passed. It failed closed, and the closed market was not the cause. The real response body was never recorded, so this diagnosis rests on the documentation.

**Changes after the run.** The adapter and fake now follow the documented shape. The probe records the adapter, host and build; measures order-endpoint use instead of hardcoding it; names malformed fields; checks metadata, quote and candle values; and cannot pass freshness on a closed market. `DemoEvidenceRunner` checks the output path before creating the adapter, with regression tests. Evidence files are ignored by git.

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
| Integration, contract and API tests | 185 passed, 3 skipped (macOS only) |
| Frontend lint and build | Pass |
| NuGet and npm vulnerability audits | No findings |
| Publish win-x64 and osx-arm64 | Both succeed |
| Published win-x64 smoke test, anonymous part | Pass: Live refused, `/health`, UI and script served, `/api` refuses anonymous callers |
| Secret scan of tracked files and both publish outputs | No findings; it catches known shapes only |
| Built UI against a local stub | Token refusal, account view, instrument switch clearing the snapshot, and NO_TRADE all work; nothing stored in the browser |

## Not verified

- **Real Capital.com API after the fix:** the quote parsing, its time zone conversion and the stricter probe checks have run against mocks of the documented shapes only.
- **macOS at runtime:** the macOS tests and smoke test have never run. They run in CI after a push.
- **Published smoke test owner flow:** written but never run. It was skipped locally so the real owner token stayed unread, and CI will be its first run. The same flow is tested in-process.
- **Workflow changes:** `build.yml` has not been run.
- **UI with the real API:** driven against a stub only; there is no automated frontend test suite.

## Remaining blockers

1. The owner must repeat the evidence run on the fixed build during market hours, for 2 asset classes, and compare exposure with the web platform.
2. A push is needed for macOS and CI verification.
3. Before the execution phase: Capital.com documents no margin-used or realized-P&L field, so the risk engine rejects all Capital orders. Solving this needs a proven data source or an owner-approved policy decision.
4. Build logs remain in 5 older commits of the public repository. History was not rewritten. A pattern search of all commits found no known credential shapes, but it cannot prove the logs hold nothing private.

## GO/NO-GO

The exact criteria are in `docs/DEMO_RELEASE_GO_NO_GO.md`. Current decision: **NO-GO** for read-only Demo and Demo execution; Live stays locked.
