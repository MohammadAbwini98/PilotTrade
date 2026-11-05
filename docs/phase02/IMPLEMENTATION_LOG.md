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
