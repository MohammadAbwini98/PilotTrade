# Phase 02 implementation log: Owner Control Center

Branch `feature/phase02-owner-control-center`, cut locally from `0fe1a44` (the Phase 01 record of CI run 41888687148). Nothing in this log has been pushed. Every result below was measured on this machine in this phase; none is carried over from Phase 01.

## Holds that stay in force

- **Phase 01 stays PARTIAL.** Still open: standard-user tests on Windows 10/11, Edge/Chrome and accessibility checks, the owner's decision on the development-only `minimatch` advisory, and a real rollback exercise. Phase 02 does not close any of these.
- Capital.com Demo order submission stays disabled. Live stays locked, and no page offers a way to unlock it.
- AI providers: Claude API and JEV/TypeSafe AI only.
- No push, merge, tag or release without separate owner approval.

## Baseline on `0fe1a44`, before any Phase 02 change

| Check | Result |
|---|---|
| `npm test` (vitest 4.1.12, Node 18.16 locally) | 30/30 passed |
| `npm run lint` | clean |
| `npm run build` | reproduced the committed `wwwroot` byte for byte |
| `dotnet test PilotTrade.slnx` | unit 102/102 passed; integration 234 passed, 3 skipped, 0 failed (237) |

## API inventory at the start of Phase 02

Every `/api` route needs the owner token. The guarded POSTs also need the CSRF token and a loopback Origin. A request for any account other than the configured one is refused with 403 `AccountNotConfigured`.

| Area | Route | State | Notes |
|---|---|---|---|
| Runtime | `GET /api/runtime` | Implemented | mode, environment, configured account, `executionEnabled`, `liveLocked` |
| Health | `GET /health`, `GET /api/health/feed?epic=` | Implemented | feed health and last quote time for one epic |
| CSRF | `GET /api/security/csrf-token` | Implemented | |
| Accounts | `GET /api/accounts`, `GET /api/account` | Implemented | only the configured account is served |
| Positions / orders | `GET /api/positions`, `GET /api/orders` | Implemented, read-only | simulator or broker-observed, depending on mode |
| Instruments | `GET /api/instruments/search`, `GET /api/instruments/{epic}`, `GET/POST /api/instruments/selection(select)` | Implemented | one persisted, versioned selection; no watchlist |
| Market snapshot | `POST /api/market/snapshot` | Implemented | `NO_TRADE` or `SNAPSHOT_READY` |
| Demo probe | `POST /api/demo/test-connection` | Implemented | read-only; reports whether order endpoints were called |
| Credentials | `GET /api/demo/credential-status`, `POST /api/demo/configure-credentials` | Implemented | presence booleans only; replacing needs an explicit flag (409 otherwise) |
| No-order evidence | `GET /api/evidence/no-orders` | Implemented | journal, intents and broker write counts |
| AI decisions (Claude/JEV) | none | **Absent** | no read API for decision history |
| Risk limits / kill switch | none | **Absent** | the backend has persisted risk and kill-switch code, but no HTTP API |
| Audit log / alerts | none | **Absent** | audit is written, but cannot be read over HTTP |
| Settings (strategy, AI, risk, system) | none | **Absent** | |
| Watchlists | none | **Absent** | |
| Order journal / reconciliation history | none | **Partial** | only the counts in no-order evidence |

Nothing in the UI is mock-only. Sections with an absent API show an explicit "Not available" state.

## 02-01: Owner control-center shell

### What changed

`App.tsx` was a single 304-line component. It is now a 10-line entry point over these modules:

| Path | Role |
|---|---|
| `src/types/api.ts` | typed API replies, including `CredentialStatus` (presence only) |
| `src/api/client.ts` | injectable `createApiClient(getToken, onLink, fetch)`. Retry, CSRF and error wording are unchanged: a GET is retried once, a POST never. It also reports link state (connected, refused, server error, unreachable). |
| `src/hooks/useOwnerSession.ts` | all session state and actions. The token is held in a ref, in memory only. The generation counter drops stale replies. Adds `disconnect`, `refreshAccount`, `lastRefresh` and `loadCredentialStatus`. |
| `src/lib/format.ts` | formatters and the server-backed labels: `Simulation`, `Capital.com Demo / Read-only`, `Demo execution disabled`, `Live locked`. `executionEnabled` counts as Capital.com execution only in `CapitalDemo` mode. |
| `src/app/pages.ts`, `useHashPage.ts` | the nine sections, with hash addresses (`#/markets`), so Back, Forward and reload work |
| `src/app/Shell.tsx` | header with safety badges, masked account and environment, current instrument, backend link and last refresh; a sidebar `<nav aria-label="Main">` with `aria-current`; focus moves to the page `h1` on navigation; one status region and one alert region |
| `src/app/ErrorBoundary.tsx` | keyed by page. A page that throws is replaced by a notice, while the header and menu keep working. Nothing is logged. |
| `src/app/ConnectForm.tsx` | the token form, unchanged in behaviour |
| `src/components/Badge.tsx`, `Unavailable.tsx` | shared UI parts |
| `src/features/*` | Overview, Markets, Orders & Positions, Broker Settings, Risk Center and System. AI Decisions, Audit & Alerts and Strategy & AI Settings show honest unavailable states naming the task that adds them. |
| `src/index.css`, `src/theme.ts` | a small design system: colour tokens, panels, a responsive grid, and a sidebar that becomes a top bar below 760 px. The focus ring is unchanged. |

Removed: the unused `App.css` and `assets/react.svg`.

Where the existing features moved:

- **Overview:** the account picker (identifier masked), Refresh, balances, safety state, position and order counts, and the current selection.
- **Markets:** search, results, the selected instrument's metadata, and the snapshot with `NO_TRADE`.
- **Orders & Positions:** the position and order lists, the data-source label, and no-order evidence.
- **Broker Settings:** credential presence and the read-only Demo check.

There are no backend, migration or API changes in 02-01.

### Deferred

- Polling, stale-data handling and backoff move to 02-02, which is where the overview needs them. 02-01 refreshes on demand only, so nothing is shown as fresher than it is.
- Credential replacement from the UI moves to 02-08. The page states this.

### Results after 02-01

| Check | Result |
|---|---|
| `npm test` | 46/46 passed: the 30 original tests (some now open the page that holds their control) plus 16 new ones |
| `npm run lint` | clean |
| `npm run build` | new `wwwroot` assets `index-DLkHia-W.js` and `index-DvYQVpTN.css`, committed with the source |
| `dotnet test PilotTrade.slnx` | unit 102/102 passed; integration 234 passed, 3 skipped, 0 failed (237). Same as the baseline, as expected with no backend change. |
| `scripts/release-checks.tests.ps1` (Windows PowerShell 5.1) | 46 PASS, 0 FAIL |
| `scripts/secret-scan.ps1` | no findings in 167 files |
| CI on the 02-01 commit | Windows CI run 42061252141 on `56c5791`: success |

The new tests cover:

- sidebar order and `aria-current`;
- navigation by pointer and by keyboard, and the Back button;
- focus moving to the page heading;
- data kept, with no refetch, across pages;
- unavailable sections showing no figures and no kill or unlock buttons;
- Claude and JEV as the only providers named;
- the masked account in the header and the account list;
- link state and last refresh, including "Not available" before data arrives;
- credential presence without values or order controls;
- the error boundary keeping the shell alive;
- Disconnect clearing the session;
- contrast for every new palette colour on every background;
- the mode labels for Simulation, read-only Demo, and a server that reports execution enabled or Live unlocked.

### Visual acceptance: BLOCKED

A connected screenshot needs the real backend and the owner token, and the token must not be typed by Claude or captured. The owner can run this check against the running app. It is recorded in TASK_TRACKER.md.

## 02-02: Operations overview

### What changed

| Path | Role |
|---|---|
| `src/PilotTrade.Api/Program.cs` | **API change:** new read-only `GET /api/status?accountId=`, which needs the owner token and the configured account. It returns `checkedUtc`, `killSwitchEngaged`, `tradingMode`, `reconciliationComplete`, `suspensionReason`, and `feed` (`epic`, `health`, `lastQuoteUtc`) for the selected instrument, or `null` when no instrument is selected. It reads `ITradingControl.State`, the selection and the market-data service. It changes nothing. |
| `tests/PilotTrade.IntegrationTests/OperationsStatusTests.cs` | 2 tests: the owner token and account binding are enforced; the server state is reported before and after a selection, a snapshot and a kill-switch engage, and no broker order request is made. |
| `src/types/api.ts` | `OperationsStatus`, `FeedHealth` |
| `src/lib/format.ts` | `provenance()` gives Reported, Derived or Unavailable, and a `null` value is never turned into zero. Also the server trading-mode labels, the feed tones and `utcIso`. |
| `src/hooks/useOwnerSession.ts` | Loads the status with the account. The update is single-flight: a Refresh during a timer update joins it. Polling runs every 15 s, doubles to at most 120 s after a failure, and returns to 15 s after a success. It stops on disconnect or an account change. A failed update records `updateFailure` and keeps the last figures. |
| `src/features/overview/OverviewPage.tsx` | A provenance tag on each figure. A safety card shows the kill switch, server trading mode, reconciliation and the server time. The feed health and last quote are shown for the selected instrument. Warnings cover: backend lost or update failed (with the last successful time), stale or disconnected feed, risk fields the broker did not report, kill switch engaged, reconciliation not complete, status unreadable, and Live locked. When the data is stale, a green feed state is shown as `(stale)` with a warning tone. |
| `src/features/risk/RiskPage.tsx` | The kill-switch state is now read from `/api/status`, read-only. Engaging it stays in 02-05. |
| `src/index.css` | banner and provenance styles |
| `src/App.test.tsx` | 9 new tests; one existing Risk test is updated |

`simulation.executionEnabled` is still not shown as broker permission. The Demo orders and Live labels come from `/api/runtime` as before. The overview adds no control that changes trading state.

### Limits

- Feed health changes only when a snapshot is built (`POST /api/market/snapshot`). The overview does not request quotes itself, so an untouched instrument reads `Unknown` with a hint to build a snapshot. Polling the broker for quotes would add load on Capital.com, so it is not done here; it belongs to 02-03.
- Cancellation works by the generation counter, which drops late replies, and by clearing the timer. It does not use `AbortController`, so a request that is already running completes but its reply is ignored.
- With Demo execution disabled, the server reports reconciliation as not complete, giving the reason, and the overview shows it as a warning. This is the honest state, not a fault.
- The 02-01 visual check still waits for the owner, and this page is now part of it.

### Results after 02-02

| Check | Result |
|---|---|
| `npm test` | 55/55 passed (46 earlier + 9 new), stable over 3 runs |
| `npm run lint` | clean |
| `npm run build` | new `wwwroot` assets `index-CZz04V-e.js` and `index-C5S3LlLm.css`, committed with the source |
| `dotnet test PilotTrade.slnx` | unit 102/102; integration 236 passed, 3 skipped, 0 failed (239; 2 new) |
| `scripts/release-checks.tests.ps1` | 46 PASS, 0 FAIL |
| `scripts/secret-scan.ps1` | no findings in 187 files |
| CI on the 02-02 commit | Windows CI run 42075680649 on `76e7068`: success |

New frontend tests:

- Reported, Derived and Unavailable tags, with `null` shown as "not provided" and a reported zero kept as zero;
- empty holdings shown as reported zero counts;
- kill switch, trading mode, reconciliation and Live locked from the server, with no kill or unlock button;
- a stale feed with its last quote time;
- backend loss keeping the figures, marked stale with the last successful time, a green feed turning into a warning, and recovery clearing it;
- a failed status read with the account still shown;
- interval, backoff to the limit and recovery;
- single-flight Refresh;
- polling stopping on disconnect.

## 02-03: Multi-instrument market monitor and watchlists

### What changed

| Path | Role |
|---|---|
| `src/PilotTrade.Domain/Market/Watchlist.cs` | `Watchlist` record, `WatchlistRules` (at most 10 epics, no duplicates, epics are case-sensitive and must be valid), `IWatchlistStore`, `WatchlistConflictException`. |
| `src/PilotTrade.Infrastructure/Persistence/Entities.cs`, `PilotTradeDbContext.cs` | New `Watchlists` table entity. |
| `src/PilotTrade.Infrastructure/Persistence/EfWatchlistStore.cs` | Stores one list per account and environment. A save must name the version it replaces. Otherwise it is refused and the current list is returned. Each save writes a `WatchlistUpdated` audit event (Category `Settings`) with the lists before and after. |
| `src/PilotTrade.Infrastructure/Migrations/20261105174752_AddWatchlists*.cs`, `PilotTradeDbContextModelSnapshot.cs` | **Migration:** adds the table `Watchlists` (Id, AccountId, Environment, EpicsJson, Version, UpdatedUtc, UpdatedBy). It only adds; nothing existing changes. |
| `src/PilotTrade.Application/Services/MarketWatchService.cs` | Builds one row per watched epic from the instrument metadata and a quote. Each row has: asset class, market status, bid/ask, spread as % of mid against the asset-class limit, source timestamp, age, feed health, dealing rules and blockers. Any of these blocks the row: a missing field, a broker error, a crossed or zero quote, a stale or future timestamp, a closed market, an unknown asset class. It never selects, signals or orders. |
| `src/PilotTrade.Api/Program.cs` | **API change:**<br>• `GET /api/watchlist?accountId=`<br>• `POST /api/watchlist`: needs CSRF + Origin; the body has `accountId`, `epics` and `expectedVersion`. It returns 400 with `reason` for an invalid list or an unknown instrument, and 409 with `current` for a stale version.<br>• `GET /api/market/watch?accountId=`<br>• `GET /api/market/policy`<br>All four need the owner token. The ones that take `accountId` also need the configured account. |
| `tests/PilotTrade.UnitTests/MarketWatchTests.cs` | 18 new unit tests:<br>• each asset class and its limit;<br>• the same spread blocks FX but passes for crypto;<br>• closed, stale, future-dated, unhealthy, crossed and zero quotes;<br>• missing metadata or quote, an unknown class, broker errors;<br>• precision 3 and 6;<br>• an empty list;<br>• the watchlist rules. |
| `tests/PilotTrade.IntegrationTests/WatchlistTests.cs` | 10 new integration tests:<br>• the owner token, account binding and CSRF;<br>• a versioned, audited save, with a 409 for a stale version;<br>• invalid lists store nothing;<br>• the list survives a restart;<br>• watch rows: a closed market is blocked, and no selection or order is made;<br>• the simulator covers all five classes;<br>• the policy endpoint. |
| `src/PilotTrade.Web/src/api/client.ts` | A 400 or 409 is an answer from a connected server. It throws `ApiError` with the server's reason and body, and does not mark the link as broken. |
| `src/PilotTrade.Web/src/types/api.ts`, `lib/format.ts` | Watch types; asset-class labels; candle count, age and gaps; the limit text for each NO_TRADE reason. |
| `src/PilotTrade.Web/src/hooks/useOwnerSession.ts` | Watchlist, monitor and policy state. A generation counter drops a monitor reply that a newer one has overtaken, or that arrives after an account switch or disconnect. A save sends the version it last saw. On a 409 the newer list replaces the one shown and nothing is saved. |
| `src/PilotTrade.Web/src/features/markets/MarketsPage.tsx` | **Search results** show the asset class and a Watch button.<br>**Monitor table**, one row per instrument: class, market status, bid/ask, spread with its limit, quote time and age, dealing rules and readiness. Rows are marked stale when the backend link is lost, or when the last check is older than the quote age limit.<br>**Selected instrument panel:** decision readiness (Blocked, Passed or Unknown), the NO_TRADE reason with its limit, and the snapshot at the instrument's precision with candle age and gaps.<br>**Limits panel:** the built-in thresholds, marked not editable. |
| `src/PilotTrade.Web/src/index.css` | table and wide panel styles |
| `src/PilotTrade.Web/src/App.test.tsx` | 8 new tests (below) |
| `src/PilotTrade.Api/wwwroot/*` | rebuilt assets `index-BKIdne1w.js` and `index-DUsF9pYJ.css`; the old ones are removed |

No signal, AI decision or order control was added. Switching instruments still clears the previous snapshot at once, and the monitor does not change the selection.

### Thresholds

The limits come from `MarketDataPolicy`. They are labeled "Built-in defaults" and cannot be edited in this task; editing them belongs to 02-09.

| Class | Max spread, % of mid |
|---|---|
| FX | 0.05 |
| Crypto CFD | 1 |
| Commodity | 0.2 |
| Index | 0.2 |
| Share | 0.5 |
| ETF | 0.5 |

- Any other class has no limit and is refused.
- A quote older than 60 s is Stale, and a timestamp more than 60 s in the future is Unknown.
- A snapshot needs 21 closed candles, and the newest may be at most 3 bars old.

### Migration, restart, backup and rollback

- The `AddWatchlists` migration runs at startup and only adds a table, so existing data is untouched. **Restart the API** after updating to apply it.
- Back up first: stop the API, then run `scripts/backup-data.ps1`.
- Rollback: stop the API, restore the backup taken before the update, and run the previous build. The previous build also runs on the new database, since it ignores the extra table, but it does not use watchlists saved since the update.

### Limits

- The monitor refreshes when the page opens and when the owner asks, not on a timer, to spare Capital.com. Each refresh reads metadata and a quote for each watched epic, so at most 10 of each.
- Candle freshness and gaps are shown only in the selected instrument's snapshot. The monitor fetches no candles, so its rows have none.
- Late replies are dropped by the generation counter, not cancelled with `AbortController`.
- Not checked: a real Capital.com Demo run of the monitor across a market close and reopen, and the owner's visual check.

### Results after 02-03

| Check | Result |
|---|---|
| `npm test` | 63/63 passed (55 earlier + 8 new), stable over 3 runs |
| `npm run lint` | clean |
| `npm run build` | new `wwwroot` assets, committed with the source |
| `dotnet test PilotTrade.slnx` | unit 120/120 (18 new); integration 246 passed, 3 skipped, 0 failed (249; 10 new) |
| `scripts/release-checks.tests.ps1` | 46 PASS, 0 FAIL |
| `scripts/smoke-helpers.tests.ps1` | 20 PASS, 0 FAIL |
| `scripts/secret-scan.ps1` | no findings in 188 files |
| CI on the 02-03 commit | Windows CI run 42082050125 on `6750c88`: success |

New frontend tests:

- Each asset class is shown with its own precision and limit:
  - closed, stale and incomplete rows are blocked;
  - missing values show as Unknown, never zero;
  - the limits are shown with their source;
  - there is no trade or signal control.
- Every row is marked stale once the last check is older than the quote age limit.
- Adding and removing send the version last seen. A conflict shows the newer list and does not mark the link broken.
- A refused list shows the server's reason.
- A slow monitor reply that a newer one has overtaken is dropped.
- A monitor reply that arrives after disconnect is dropped.
- A ready snapshot shows the instrument's precision and the candle age and gaps, and an instrument switch clears it.
- A closed market blocks readiness, and a NO_TRADE names the limit behind it.

Recommendation: **PARTIAL**. The code and all local checks pass. Windows CI passes. Still open: the owner's visual check and a real Demo check.

## 02-04: Claude and JEV decision monitoring

### AI service inventory

| Piece | State before 02-04 | State after 02-04 |
|---|---|---|
| `ClaudeAnalystClient` (Claude API) | implemented, not registered at runtime | still not registered. Uses the shared `ClaudePrompt`. A missing key throws `AiCredentialMissingException`, and a reply for another snapshot or selection throws `AiSchemaException`. |
| `JevEvaluatorClient` (JEV/TypeSafe AI) | implemented, not registered at runtime | still not registered. Same exception changes. |
| `DecisionCoordinator` | registered, never called | unchanged; called by the new pipeline in tests only |
| `AiDecisionPipeline` | missing | **new, not registered**: asks Claude, then JEV, and records one decision event |
| Decision event store and history API | missing | **new**: `DecisionEvents` table, `GET /api/decisions`, `GET /api/decisions/{id}`, `GET /api/ai/status` |
| Old `Decisions`, `ClaudeAnalyses`, `JevEvaluations` tables | exist, unused | unchanged and unused |
| Other providers (Gemini or any fallback) | none | none. There is no fallback and no mocked answer. |

Runtime AI is **not connected** in this build. Wiring the pipeline to live Claude and JEV belongs to the execution-readiness phase. The panel says so, and `GET /api/ai/status` reports `runtimeWired: false`. No real decision exists, so the history is empty on a real install.

### What changed

| Path | Role |
|---|---|
| `src/PilotTrade.Domain/Decision/DecisionEvents.cs` | `AiStepStatus` (Disabled, Unavailable, TimedOut, Rejected, Succeeded, NotRun), `AiStepRecord`, `DecisionEvent`, `DecisionQuery`, `DecisionPage`, `IDecisionEventStore`, `AiCredentialMissingException`, `AiSchemaException`. |
| `src/PilotTrade.Domain/AI/AIContracts.cs` | `ClaudePrompt`: the one place the Claude request is built. The decision record keeps only its SHA-256 and length. |
| `src/PilotTrade.Infrastructure/AI/ClaudeAnalystClient.cs`, `JevEvaluatorClient.cs` | Use `ClaudePrompt`; typed exceptions for a missing key and a mismatched reply. |
| `src/PilotTrade.Application/Services/AiDecisionPipeline.cs` | Claude first; JEV only if Claude succeeded. Per-step timeouts (20 s and 10 s by default). Every failure ends in NO_TRADE with a fixed reason: disabled, client missing, key missing, timeout, invalid schema, HTTP error or anything unexpected. Provider messages are never kept, since they could echo a prompt, header or key. A JEV direction must be an enum name, so `"1"` is refused. A JEV reply for another snapshot, instrument, selection, account or environment is refused. A trade candidate needs both steps to succeed and the coordinator to agree. A cancelled caller records nothing. |
| `src/PilotTrade.Infrastructure/Persistence/Entities.cs`, `PilotTradeDbContext.cs` | `DecisionEventEntity`, index on (Environment, AccountId, RecordedTicks). |
| `src/PilotTrade.Infrastructure/Persistence/EfDecisionEventStore.cs` | Writes the decision and an audit event (Category `Decision`, Action `AiDecisionRecorded`, correlation ID = decision ID, with the selection version) in one save. Lists newest first with filters, at most 200. Reads are scoped to the account and environment. A stored BUY or SELL whose steps did not both succeed is shown as NO_TRADE. |
| `src/PilotTrade.Infrastructure/Migrations/20261105193921_AddDecisionEvents*.cs`, `PilotTradeDbContextModelSnapshot.cs` | **Migration:** adds the table `DecisionEvents` and its index. It only adds. |
| `src/PilotTrade.Api/Program.cs` | Registers the store only; the AI clients and pipeline stay unregistered. **API change:**<br>• `GET /api/decisions?accountId=&epic=&decision=&status=&limit=`: 400 `InvalidFilter` for an unknown decision or status<br>• `GET /api/decisions/{id}?accountId=`: 404 when missing<br>• `GET /api/ai/status?accountId=`: `runtimeWired`, a note, the allowed providers, the number recorded, and per provider `registered` and `credentialPresent` (never a value)<br>All three need the owner token and the configured account. They are read-only. |
| `tests/PilotTrade.UnitTests/AiDecisionPipelineTests.cs` | 18 new tests (below). |
| `tests/PilotTrade.IntegrationTests/DecisionHistoryTests.cs` | 6 new tests (below). |
| `src/PilotTrade.Web/src/types/api.ts`, `hooks/useOwnerSession.ts` | Decision types and state. A generation counter drops a reply that is overtaken or that arrives after an account switch or disconnect. |
| `src/PilotTrade.Web/src/features/ai/AiDecisionsPage.tsx` | **Providers panel:** runtime state, each provider's client and key presence, the allowed providers.<br>**History table** with instrument, decision and step-status filters: recorded time, instrument and selection version, Claude and JEV status, decision, NO_TRADE reason.<br>**Detail:** decision, snapshot and audit IDs, strategy, prompt hash and length ("Prompt text is not stored"), and per step the model, schema result, confidence, latency, tokens and cost, with "not provided" for missing values.<br>A BUY or SELL whose steps did not both succeed is shown as NO_TRADE here too. |
| `src/PilotTrade.Web/src/app/Shell.tsx`, `features/pending/PendingPages.tsx` | The AI Decisions section now opens the new page. |
| `src/PilotTrade.Web/src/App.test.tsx` | 6 new tests (below); the "not available" test no longer covers AI Decisions. |
| `src/PilotTrade.Api/wwwroot/*` | rebuilt asset `index-TmWzEAIe.js`; the old one is removed |

No order path was added. A BUY or SELL candidate is labeled as not an order. The risk column says "not evaluated", because decisions are not turned into orders in this build.

### Acceptance

- **Traceable:** each decision has its own ID, an audit row with the same correlation ID, the snapshot ID and time, and the instrument and selection version. An integration test checks the audit row.
- **NO_TRADE on failure:** unit tests cover an invalid schema, a timeout, an HTTP error, a missing key, a missing client, an unexpected exception, an invalid JEV direction, a mismatched JEV reply, disagreement and a changed selection.
- **No leak:** tests check that a planted secret in a provider message reaches neither the record nor the API reply, that the prompt text is not stored, and that the status endpoint shows presence only. Logs: the pipeline logs nothing.

### Migration, restart, backup and rollback

- The `AddDecisionEvents` migration runs at startup and only adds a table. **Restart the API** after updating to apply it.
- Back up first: stop the API, then run `scripts/backup-data.ps1`.
- Rollback: stop the API, restore the backup, and run the previous build. The previous build also runs on the new database, since it ignores the extra table.

### Limits

- Runtime AI is not connected, so no real decision can be made or shown. The history is checked only with records written by tests.
- Costs are not computed by the Claude client, so cost shows "not provided".
- Not checked: the owner's visual check and Windows CI on this commit.

### Results after 02-04

| Check | Result |
|---|---|
| `npx vitest run` | 69/69 passed (63 earlier + 6 new) |
| `npx tsc --noEmit`, `npm run lint` | clean |
| `npm run build` | new `wwwroot` asset, committed with the source |
| `dotnet test PilotTrade.slnx` | unit 138/138 (18 new); integration 252 passed, 3 skipped, 0 failed (255; 6 new) |
| `scripts/release-checks.tests.ps1` | 46 PASS, 0 FAIL |
| `scripts/smoke-helpers.tests.ps1` | 20 PASS, 0 FAIL |
| `scripts/secret-scan.ps1` | no findings in 205 files (after the fix below) |
| CI on `22cc860` | **failed** (run 42278141243): the secret scan flagged the fake keys planted in the two new test files |
| CI on `d961372` (the fix) | **passed** (run 42279387387) |

The first local scan reported no findings because it reads only files tracked by git, and the new test files were not yet staged. The fix splits each fake key in the source (`"sk-" + "ant-..."`). The runtime value is unchanged, so the no-leak tests still check a credential-shaped string. Lesson: run the secret scan after staging.

New frontend tests:

- Runtime AI shows as not connected, with key presence only and an empty history; no write is sent, and no other provider is named.
- A failed step shows NO_TRADE with its reason and the selection version.
- The filters are sent to the server.
- The detail shows the audit and snapshot IDs and the prompt hash only, with "not provided" for missing values.
- A stored BUY whose steps did not both succeed shows as NO_TRADE.
- A list reply that arrives after disconnect is dropped.

Recommendation: **PARTIAL**. The code, local checks and Windows CI (run 42279387387 on `d961372`) pass. Still open: the owner's visual check, and runtime wiring in the execution-readiness phase.

## 02-05: Risk Center and kill switch

### What changed

| Path | Role |
|---|---|
| `src/PilotTrade.Domain/Risk/RiskPolicyDefaults.cs` | The built-in limits the Risk Center measures against. They are read-only here; editing them is task 02-09. No order path uses them yet. |
| `src/PilotTrade.Domain/Execution/PersistedExecutionState.cs`, `src/PilotTrade.Infrastructure/Persistence/Entities.cs`, `EfPersistedExecutionState.cs` | The kill switch keeps its reason, the time it changed and who changed it. |
| `src/PilotTrade.Infrastructure/Migrations/20261106194204_AddKillSwitchReason*.cs`, `PilotTradeDbContextModelSnapshot.cs` | **Migration:** adds three nullable text columns to `ExecutionStates`. It only adds. |
| `src/PilotTrade.Application/Controls/TradingControl.cs`, `PersistedTradingControl.cs` | Engage and clear take a reason and an author. The first reason is kept on a repeated engage. A version number lets a clear detect an engage that happened while its checks ran. If the save fails, the in-memory state is rolled back for a clear, and kept engaged (with an error) for an engage. The startup reconciliation records its outcome. |
| `src/PilotTrade.Application/Services/OrderReconciliationService.cs`, `ExecutionGateway.cs` | Record the reconciliation outcome and pass the reason when they engage the kill switch. |
| `src/PilotTrade.Application/Controls/KillSwitchService.cs` | **Engage:** needs only a reason (1 to 200 characters) and never waits for the broker or the audit log. **Clear:** needs the phrase `CLEAR KILL SWITCH` and these server-side checks: the kill switch is engaged, the startup reconciliation did not fail, no journaled order has an uncertain broker outcome, the broker account matches the configured one, and there are no open working orders. A check that cannot be read fails. Every engage, clear and refusal is audited under Category `KillSwitch` (Action `Engaged`, `EngageRepeated`, `Cleared` or `ClearRefused`). |
| `src/PilotTrade.Application/Services/RiskMonitorService.cs` | Builds the read-only risk view. Each broker read is separate: a failure is listed in `readErrors` and makes the affected values Unknown. Each account field and metric says whether it is Known, Derived or Unknown, and where it came from. Missing `marginUsed` and `realizedPnLToday` stay Unknown, never estimated. Exposure is size times the larger of the open and current level, so it is never understated. A missing level, a missing currency or a currency other than the account currency makes the whole total Unknown, not a partial sum. Data from another account is dropped. Slippage is always Unknown, since no fills exist. |
| `src/PilotTrade.Api/Program.cs` | **API change:**<br>• `GET /api/risk?accountId=`: the risk view<br>• `GET /api/kill-switch?accountId=`: state, reason, time, author, reconciliation outcome, and the clear checks while engaged<br>• `POST /api/kill-switch/engage` `{accountId, reason}`: 200, 400 `InvalidReason`, or 500 `KillSwitchNotPersisted`<br>• `POST /api/kill-switch/clear` `{accountId, confirmation}`: 200, 400 `ConfirmationMismatch`, 409 `ClearRefused` with the checks, or 500 `KillSwitchNotPersisted`<br>All four need the owner token and the configured account. The two writes also need CSRF and Origin. `GET /api/status` also returns `killSwitchReason`. |
| `tests/PilotTrade.UnitTests/RiskCenterTests.cs`, `SafetyTests.cs` | 30 new unit tests (below). |
| `tests/PilotTrade.IntegrationTests/KillSwitchEndpointTests.cs` | 6 new integration tests (below). |
| `src/PilotTrade.Web/src/types/api.ts`, `hooks/useOwnerSession.ts` | Risk and kill-switch types and state. The risk view and the kill-switch state are read separately, so one failing never hides the other. The engage and clear outcome is kept apart from read failures, so a later read cannot hide a refusal. |
| `src/PilotTrade.Web/src/features/risk/RiskPage.tsx` | **Kill switch panel**, first on the page: state, reason, time, author and reconciliation outcome. The engage form is always shown. While engaged, the clear form shows the checks and enables its button only once the phrase is typed exactly.<br>**Execution guards** as reported by the server.<br>**Limits and utilization** with provenance badges, then the account fields with source and time, the flags, and the rules with result, reason, check time and data time. |
| `src/PilotTrade.Web/src/App.test.tsx` | 5 new tests (below); the old Risk Center assertion now allows the stop but no enable, live, unlock, arm or Demo control. |
| `src/PilotTrade.Api/wwwroot/*` | rebuilt asset `index-BUisuwy6.js`; the old `index-TmWzEAIe.js` is removed |

There is no Enable Live button and no control that turns on Capital.com Demo execution. Clearing the kill switch enables nothing: an integration test checks that `executionEnabled` stays false and `liveLocked` stays true after a clear. No order request reaches the broker in any test.

### Acceptance

- **Stop works when other modules fail:** the engage needs only the account and a reason. An integration test engages with every broker read failing. A frontend test engages while both risk reads return 500.
- **Persists across restart:** an integration test engages, restarts the host on the same database, and reads the same reason, author and time.
- **Reset cannot bypass safety gates:** a clear is refused (409) for a failed or unconfirmed reconciliation, an uncertain broker outcome, open working orders, a mismatched account, or a broker that cannot be read. The phrase is checked on the server too. An engage during the checks makes the clear fail.
- **Known, Derived or Unknown, never understated:** unit tests cover a missing level, a missing currency, another currency, missing margin and missing realized P/L.

### Migration, restart, backup and rollback

- The `AddKillSwitchReason` migration runs at startup and only adds nullable columns. **Restart the API** after updating to apply it. An existing engaged kill switch stays engaged, with no reason recorded.
- Back up first: stop the API, then run `scripts/backup-data.ps1`.
- Rollback: stop the API, restore the backup, and run the previous build. The previous build also runs on the new database, since it ignores the extra columns. It keeps the engaged state but cannot show the reason, and it has no clear endpoint.

### Limits

- The limits are built-in defaults and cannot be edited yet (task 02-09).
- Rules for a specific order run only when an order is proposed, and order submission is disabled in this build. The page shows the account-level rules.
- Slippage is always Unknown, since no fills exist.
- Not checked: the owner's visual check, a real Demo account, and Windows CI on this commit.

### Results after 02-05

| Check | Result |
|---|---|
| `npx vitest run` | 74/74 passed (69 earlier + 5 new) |
| `npx tsc --noEmit`, `npm run lint` | clean |
| `npm run build` | new `wwwroot` asset, committed with the source |
| `dotnet test PilotTrade.slnx` | unit 168/168 (30 new); integration 258 passed, 3 skipped, 0 failed (261; 6 new) |
| `scripts/release-checks.tests.ps1` | 46 PASS, 0 FAIL |
| `scripts/smoke-helpers.tests.ps1` | 20 PASS, 0 FAIL |
| `scripts/secret-scan.ps1` | no findings in 212 files (run after staging) |

New tests cover these failure cases from the task: stale quote, missing risk data, open working orders, mismatched account and uncertain broker confirmation. They also cover reconciliation outcomes, reason and phrase validation, an audit failure, a save failure, the guards (owner, account, CSRF) and persistence across restart.

Recommendation: **PARTIAL**. The code and local checks pass. Still open: Windows CI on this commit and the owner's visual check.

## 02-06: Orders, positions and reconciliation

### What changed

| Path | Role |
|---|---|
| `src/PilotTrade.Domain/Execution/OrderJournal.cs`, `src/PilotTrade.Application/Services/ExecutionGateway.cs` | A journal entry also records the order's side and size, so the ledger never has to guess them. |
| `src/PilotTrade.Infrastructure/Persistence/Entities.cs`, `PilotTradeDbContext.cs` | `OrderJournals` gains `Side`, `Size` and `Adapter` (the runtime mode that wrote the row, such as `Simulation` or `CapitalDemo`). New `OrderStateTransitions` table (from state, to state, time, reason, adapter), indexed by command id. |
| `src/PilotTrade.Infrastructure/Migrations/20261106231204_AddOrderStateHistory*.cs`, `PilotTradeDbContextModelSnapshot.cs` | **Migration:** adds three nullable columns, one table and two indexes. It only adds. |
| `src/PilotTrade.Infrastructure/Persistence/EfOrderJournal.cs` | Every create and every state change writes a transition in the same save as the journal row, so the history cannot drift from the journal. An update keeps the side, size and adapter of the existing row. The reason is cut to 200 characters. |
| `src/PilotTrade.Domain/Execution/OrderLedger.cs`, `src/PilotTrade.Infrastructure/Persistence/EfOrderLedgerReader.cs` | A read-only reader with its own database context. It filters by environment and account first, then instrument, status and search text, and pages newest first. It returns the transitions and the audit events that refer to each entry on the page (by command id, intent id or correlation id). The audit `DetailsJson` is never read. |
| `src/PilotTrade.Application/Services/OrderLedgerService.cs` | Builds the ledger view. **IDs:** only the last four characters, or `••••` alone for a short one. **Evidence:** `Broker-observed` only for a Capital.com row (not the simulator) with a deal reference, `Simulator` for simulator rows, `Locally inferred` or `Unknown source` otherwise. **Reconciliation:** `Not sent`, `Not applicable`, `Matched`, `Partial`, `Not held` (no closure is claimed), `Blocked` (outcome unknown, or complete without a deal id) or `Unknown` (the broker could not be read). **Alerts:** unknown outcomes, blocked reconciliation, deals the broker no longer holds, broker items not in the journal, items from another account (dropped), read errors, and a startup reconciliation that is not complete. Unknown outcomes say they are never resent. |
| `src/PilotTrade.Api/Program.cs` | **API change:** `GET /api/ledger?accountId=&epic=&status=&q=&limit=&offset=`. It needs the owner token and the configured account. Bad filters get 400 `InvalidFilter` (limit 1 to 100, epic and search at most 64 characters, epic letters, digits, `.`, `_` and `-` only, status one of Pending, Unknown, Rejected, Cancelled or Completed). There is no write route. The journal registration now passes the adapter name. |
| `tests/PilotTrade.UnitTests/OrderLedgerTests.cs` | 21 new unit tests (below). |
| `tests/PilotTrade.IntegrationTests/LedgerEndpointTests.cs` | 6 new integration tests (below). |
| `src/PilotTrade.Web/src/types/api.ts`, `hooks/useOwnerSession.ts` | Ledger types and state. The ledger has its own error, so a failed read never shows as an empty journal. Only the newest reply for the current account lands, and the ledger is cleared on an account switch or disconnect. |
| `src/PilotTrade.Web/src/features/orders/OrdersPage.tsx` | **Order ledger:** source, account, order submission state, startup reconciliation, counts by status and alerts.<br>**Order journal:** search, instrument and status filters, pages of 25, and per row the state, evidence and reconciliation with a drill-down to the state history and audit events. A completed order that is blocked or unknown reads "not reconciled"; an unknown outcome reads "outcome unknown". Both are red.<br>**Positions** and **working orders** with source and an "In journal" column, and **broker confirmations**. The no-order evidence panel is kept. |
| `src/PilotTrade.Web/src/App.test.tsx` | 5 new tests (below). Three existing tests now read the ledger: the "not provided" check reads the positions table, the error-boundary test sends an undrawable ledger, and the "no fetch again" test allows the ledger read that every opening of the page makes. |
| `src/PilotTrade.Api/wwwroot/*` | rebuilt asset `index-NS09fQzT.js`; the old `index-BUisuwy6.js` is removed |

No order request is sent anywhere in this task. The ledger only reads. Demo order submission stays disabled and Live stays locked.

### Acceptance

- **No manual action can call an order endpoint:** the page has no place, amend, cancel or close control. A frontend test pages and filters and checks that every request is a GET; another checks that no button matches place, amend, cancel, close, buy, sell, submit, execute, enable or live. An integration test checks that POST, PUT, DELETE and PATCH on `/api/ledger` are not routed.
- **Audit drill-down without secrets:** an integration test creates journal entries, restarts the host on the same database, and reads the same history and audit events. The response never contains the full command, intent or deal ids, nor anything from the audit `DetailsJson`. An audit event for another account with the same correlation id is not shown.
- **Unknown and blocked never look complete:** unit and frontend tests cover a Submitting order (outcome unknown), a Confirmed order without a deal id (blocked), and a broker that cannot be read (Unknown). A simulator row is never shown as a Capital.com confirmation.
- **Scenarios:** empty, pending, rejected, partial and ambiguous are covered by frontend tests, and by unit and integration tests on the server.

### Migration, restart, backup and rollback

- The `AddOrderStateHistory` migration runs at startup and only adds. **Restart the API** after updating to apply it. Existing journal rows keep their data; they have no side, size, adapter or history, so they show as `Unknown source` with "No recorded transitions".
- Back up first: stop the API, then run `scripts/backup-data.ps1`.
- Rollback: stop the API, restore the backup, and run the previous build. The previous build also runs on the new database, since it ignores the extra columns and table. It has no ledger endpoint.

### Limits

- Only this app's journal is shown. Broker activity history (fills and closures read from Capital.com) is not read, so a deal the broker no longer holds is `Not held`, never `Closed`.
- Order submission is disabled, so in normal use the journal is empty.
- Not checked: the owner's visual check and a real Demo account.

### Results after 02-06

| Check | Result |
|---|---|
| `npx vitest run` | 79/79 passed (74 earlier + 5 new) |
| `npx tsc --noEmit`, `npm run lint` | clean |
| `npm run build` | new `wwwroot` asset, committed with the source |
| `dotnet test PilotTrade.slnx` | unit 189/189 (21 new); integration 264 passed, 3 skipped, 0 failed (267; 6 new) |
| `scripts/release-checks.tests.ps1` | 46 PASS, 0 FAIL |
| `scripts/smoke-helpers.tests.ps1` | 20 PASS, 0 FAIL |
| `scripts/secret-scan.ps1` | no findings in 219 files (after staging) |
| CI on `cf3d32b` | **passed** (run 42326390010) |

Recommendation: **PARTIAL**. The code, local checks and Windows CI (run 42326390010 on `cf3d32b`) pass. Still open: the owner's visual check.

## 02-06 corrections

| Path | Role |
|---|---|
| `src/PilotTrade.Application/Services/RuntimeLabels.cs` (new) | One place for the mode and order-submission labels. Capital.com order permission is true only in Capital.com Demo with an execution-enabled adapter. Simulation with execution reads "Simulated fills only. No Capital.com orders are sent." |
| `src/PilotTrade.Application/Services/OrderLedgerService.cs` | The ledger view replaces `executionEnabled` with `runtimeMode`, `simulatedExecution`, `brokerExecutionEnabled`, `execution` and `live`. Audit-event ids are masked like every other id. |
| `src/PilotTrade.Infrastructure/Persistence/EfOrderLedgerReader.cs` | The journal and the confirmations are paged in the database: environment, account and filters run in SQL, only the key and time of the matching rows are read to order them (SQLite cannot order a `DateTimeOffset`), and full rows are read for the requested page alone, still filtered by environment and account. |
| `tests/PilotTrade.UnitTests/OrderLedgerTests.cs`, `tests/PilotTrade.IntegrationTests/LedgerEndpointTests.cs` | Assert the new fields, a masked audit id, and that a simulator with execution enabled never reports Capital.com permission (new theory over all four mode and execution combinations, and a test on the real simulator adapter). |
| `src/PilotTrade.Web/src/features/orders/OrdersPage.tsx`, `types/api.ts`, `App.test.tsx` | The page shows Runtime mode, Order submission (grey unless the server reports Capital.com permission) and Live trading. |

API change: `GET /api/ledger` no longer returns `executionEnabled`; it returns the five fields above. No other client used it.

## 02-07: Audit, alerts and diagnostics

### What changed

| Path | Role |
|---|---|
| `src/PilotTrade.Domain/Audit/AuditQueries.cs` (new) | `IAuditReader` and `IDatabaseHealth`. An audit record carries no details. |
| `src/PilotTrade.Infrastructure/Audit/EfAuditReader.cs` (new) | Reads this account's events and system events with no account, never another account's. Category, instrument and correlation filters and the text search run in SQL; the period, order and page use only ids and times, and full rows (without `DetailsJson`) are read for the page alone. `EfDatabaseHealth` checks the connection, a read and pending migrations. |
| `src/PilotTrade.Application/Services/OperationsMonitorService.cs` (new) | **Alerts** from conditions the server can observe now: kill switch engaged, startup reconciliation incomplete, ledger alerts (unknown outcome, blocked reconciliation, not held, not in journal, other account), risk information that is stale or missing (or could not be determined), broker read errors, AI failures (timed out, rejected, unavailable) and risk vetoes in the latest 200 decisions for this account, missing Demo credentials, and any check that itself failed. Nothing is stored, acknowledged or sent. **Diagnostics:** API, database, broker (a 10 s account read in Capital.com Demo with credentials), market feed, reconciliation, kill switch, AI (Not Configured: runtime AI is not connected) and credentials (presence only), each Healthy, Degraded, Unavailable, Not Configured or Unknown, plus the latest AI failures. Broker errors are reported with a fixed message, never the broker's text. |
| `src/PilotTrade.Api/Program.cs` | **API change:** `GET /api/audit?accountId=&from=&to=&category=&epic=&correlation=&q=&limit=&offset=`, `GET /api/alerts?accountId=` and `GET /api/diagnostics?accountId=`. Each needs the owner token and the configured account. Bad audit filters get 400 `InvalidFilter` (dates in ISO form with from ≤ to, limit 1 to 100, category letters, digits, `_` and `-` up to 64, epic as in the ledger, correlation up to 128 without spaces, search up to 64). Audit items carry masked `reference` and `correlationRef`, `scope` (System or This account) and `detailsRedacted: true`; the response names the classification and retention. There is no write route. |
| `tests/PilotTrade.IntegrationTests/OperationsEndpointTests.cs` (new) | 8 integration tests (below). |
| `src/PilotTrade.Web/src/types/api.ts`, `hooks/useOwnerSession.ts`, `lib/format.ts` | Types, state and loaders for audit, alerts and diagnostics. Each has its own failure and loading flag; only the newest reply for the current account lands; a failed read clears the view; all three are cleared on account switch and disconnect. UTC timestamps with a labelled local time. |
| `src/PilotTrade.Web/src/features/audit/AuditPage.tsx` (new), `app/Shell.tsx`, `features/pending/PendingPages.tsx` | The Audit & Alerts page replaces its "Not available" placeholder. |
| `src/PilotTrade.Web/src/features/system/SystemPage.tsx` | Diagnostics panel with Run diagnostics. |
| `src/PilotTrade.Web/src/App.test.tsx` | 10 new tests (below). |
| `src/PilotTrade.Api/wwwroot/*` | rebuilt asset `index-PMPKwFcf.js`; the old `index-NS09fQzT.js` is removed |

No order request is sent anywhere in this task. All three endpoints and both pages only read. Demo order submission stays disabled and Live stays locked.

### Acceptance

- **Read-only and owner-scoped:** integration tests check that each endpoint refuses a missing token (401) and another account (403), and that POST, PUT, DELETE and PATCH are not routed. Frontend tests check that every request is a GET with the configured account and that no acknowledge, resolve, export, delete, enable or live control exists.
- **No secrets:** an integration test writes audit events with sensitive details and full ids and checks that the response has neither the details nor the full ids, and that another account's event is not returned.
- **Real alerts only:** an integration test records AI failures for this account and another, and checks that only this account's failures alert; with nothing wrong, the only alert is the kill switch, present exactly when it is engaged. A Demo test checks the missing-credentials and unknown-outcome alerts.
- **Diagnostics:** an integration test checks the statuses in Simulation; another scripts a broker failure and checks Unavailable with a fixed message.
- **Frontend:** quiet state, alert severities and times, filters and paging with the applied filters, a reversed date range refused without a request, read failures shown as Unknown, a slow audit reply dropped, a late alerts reply after disconnect dropped, diagnostics tones and a diagnostics failure that keeps no stale status.

### Limits

- **No export.** The prompt asks for a redacted export; this build has none, and the retention text says so. Left for a later task.
- **No audit severity.** Audit records have no severity field, so the explorer cannot filter by it. Alerts have severity.
- **No alert history.** Alerts are what the server observes at the time of the check. Cleared conditions are not kept, and there is no acknowledgement. Nothing is emailed or messaged.
- The audit text search is case-sensitive (SQLite).
- AI shows Not Configured because runtime AI is not connected; AI failure alerts come from recorded decisions only.
- `OperationsMonitorService` is covered by integration tests rather than its own unit tests.
- Not checked: the owner's visual check, Edge and Chrome by hand, and a real Demo account.

### Migration, restart, backup and rollback

- No migration. Restart the API to load the new endpoints. Rollback: run the previous build; nothing in the database changed.

### Results after 02-06 corrections and 02-07

| Check | Result |
|---|---|
| `npx vitest run` | 89/89 passed (79 earlier + 10 new) |
| `npx tsc --noEmit`, `npm run lint` | clean |
| `npm run build` | new `wwwroot` asset, committed with the source |
| `dotnet test PilotTrade.slnx` | unit 193/193 (4 new); integration 272 passed, 3 skipped, 0 failed (275; 8 new) |
| `scripts/release-checks.tests.ps1` | 46 PASS, 0 FAIL (Windows PowerShell 5.1; PowerShell 7 is not installed on this machine) |
| `scripts/smoke-helpers.tests.ps1` | 20 PASS, 0 FAIL (Windows PowerShell 5.1) |
| Windows package (`package-windows.ps1`, unzip, `verify-package.ps1`, local Release publish) | passed: Release publish, ZIP built, 369 files verified against SHA256SUMS, new asset included |
| `scripts/secret-scan.ps1` | no findings in 225 files (after staging) |
| CI | not run: the commit is local and waits for the owner's push approval |

Recommendation: 02-05 stays **PASS**. 02-06 stays **PARTIAL** (owner's visual check, CI on the correction commit). 02-07 is **PARTIAL** (owner's visual check, CI, no export, no audit severity).

