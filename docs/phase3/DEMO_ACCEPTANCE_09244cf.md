# Capital.com Demo Acceptance: Commit 09244cf

Tested commit: `09244cf22769e0efba966643a3f962fd643bfa64`, on `feature/capital-demo-readonly-hardening`, clean working tree. Runs: 2026-11-01, 17:07 to 17:10 UTC (a Sunday, before the FX and gold open). This report and its documents follow in a docs-only commit, and no code changed after `09244cf`.

**Result: read-only Demo is NO-GO. Not ready for controlled Demo execution.** Gates 1, 2, 3 and 6 pass. Gate 4 is blocked by closed FX and gold markets. Gates 5 and 7 wait for the owner. Gates 8 and 9 have not run. Gate 10 still fails strictly on 12 moderate development-only npm advisories.

No order was placed. Demo order submission stays disabled and Live stays locked. Nothing was pushed.

## Test and build results

| Check | Result | Kind |
|---|---|---|
| .NET Release build | Succeeded, 0 warnings, 0 errors | Verified, Windows |
| Unit tests | 100 total: 100 passed, 0 failed, 0 skipped (baseline 49) | Verified, Windows, mocks |
| Integration tests | 227 total: 224 passed, 0 failed, 3 skipped, all macOS only (baseline 195) | Verified, Windows, mocks |
| Frontend lint and build, Vite 6.4.5 | Lint clean, build succeeded | Verified, Windows |
| `scripts/smoke-test.ps1 -AnonymousOnly` on a published win-x64 build | 8 of 8 checks passed | Verified. The owner flow was left out locally, to leave this machine's owner token untouched, and runs in CI |
| Published UI in a browser, simulation mode | Connect screen renders, script and stylesheet load with 200, no console errors | Verified up to the token prompt. Screens behind it were not opened |
| Frontend automated tests | None exist in the project | Gap |

| Platform | Status |
|---|---|
| Windows, local | Verified as above. CI has not run |
| macOS | **Partly verified.** CI on `dc377bd`: unit 100 of 100, integration 223 passed, 4 skipped, publish passed, smoke test failed on a wrong check. The keychain check still needs a real Mac |

## Instrument results on the real Demo API

Raw files are in `docs/phase3/`, local and ignored by git. All record `CapitalComBrokerAdapter`, host `demo-api-capital.backend-capital.com`, environment Demo, commit `09244cf`, a clean working tree and `OrderEndpointsCalled=false`. The shareable version is [DEMO_EVIDENCE_SANITIZED_09244cf.md](DEMO_EVIDENCE_SANITIZED_09244cf.md).

| Instrument | Class, broker status | Quote age | Bid and ask valid | Spread, limit | Candle close age, missing bars | Steps | Result |
|---|---|---|---|---|---|---|---|
| BTCUSD | CryptoCfd, Open | 0 s (limit 60 s) | Yes | 0.0641%, 1% | 132 s (limit 900 s), 0 | 10 of 10 | **PASS** |
| EURUSD | Fx, Closed | 158851 s | Yes | 0.0627%, 0.05% | 158838 s, 0 | 7 of 10 | **BLOCKED.** Market closed, freshness not tested |
| GOLD | Commodity, Closed | 158906 s | Yes | 0.0129%, 0.2% | 158844 s, 0 | 7 of 10 | **BLOCKED.** Market closed, freshness not tested |
| Made-up account ID, BTCUSD | Not reached | Not applicable | Not applicable | Not applicable | Not applicable | Not applicable | Refused as intended: `AccountNotFound` |

Common to the three instrument runs: 1 account, source `autoSelected`, `activeAccountMatched=True`, currency and available funds present, equity proven, margin used and realized P&L absent, 0 positions and 0 working orders.

- **The stale source timestamp now shows.** On both closed markets the quote health reads `Stale`. At the baseline it read `Healthy`.
- **`MarketDataService` ran against the real API.** On BTCUSD the snapshot built with 199 closed candles each on 5m and 1h. On the closed markets it refused with `NO_TRADE`, which the sanitized report shows as withheld because that text is outside its allowlist.
- **EURUSD's spread is above its limit** in this closed-market reading. That says nothing about open-market spreads, but 0.05% may prove too tight. Check it in the open-market run. The limits are defaults that need owner review.

The file set named `-NEWSHA` was not made in this work and appears to be owner-run. It records build `24b4360`, so it is not counted as acceptance evidence, and it was kept.

## Gate matrix

| # | Requirement | Status | Type | Evidence | Outstanding |
|---|---|---|---|---|---|
| 1 | Active account equals the requested account, and a mismatch is refused | **PASS** | Verified | `btcusd-`, `eurusd-`, `gold-`, `wrongaccount-09244cf` | Real account ID through `TRADING_ACCOUNT_ID` not run. Only the owner sets it. Switching accounts is mock only, as the login has one account |
| 2 | Financial fields parse, and unsent values stay null | **PASS**, parsing only | Verified | Same three instrument files | Values are compared under gate 5 |
| 3 | Metadata parses for 2 or more asset classes | **PASS**, 3 classes | Verified | Same three instrument files | Dealing rules not confirmed against the platform |
| 4 | Quote, candles, spread and freshness in market hours | **BLOCKED** | Verified for crypto. FX and gold unproven | BTCUSD passed. EURUSD and GOLD closed | Rerun all three on one commit once the broker reports Open |
| 5 | Identity, balance, equity, funds, positions and working orders match the web platform | **BLOCKED** | User-dependent | Probe side only. [Checklist](../DEMO_GATE5_OWNER_CHECKLIST.md) | Owner fills the checklist. With no holdings, non-empty mapping stays mock only |
| 6 | No order endpoint called | **PASS** | Verified | All four files | Measured by the adapter's write counter, with no wire capture |
| 7 | Evidence holds no credential, token, session header or personal data | **Automated checks pass, owner review pending** | Verified, then user-dependent | Scan results below, sanitized report | Owner reads the sanitized report before sharing it |
| 8 | CI on Windows and macOS, including the owner flow | **FAIL** | Run 41331400074 on `dc377bd`: tests and publish passed on both systems | None | The smoke test expected `executionEnabled` false in simulation, where the simulator reports true, so it stopped at its first owner step. The later owner steps and the published-output secret scan did not run. The check is corrected and needs another push |
| 9 | Credentials from the macOS keychain on a real Mac | **PENDING** | Needs a Mac | None | Steps below |
| 10 | NuGet and npm audits clean | **FAIL** strictly | Verified | Below | Owner decides whether to accept the residual, or waits for an upstream fix |

## Security and dependency results

- `scripts/secret-scan.ps1` over tracked files and `docs/phase3`: no findings in 158 files.
- All 15 raw evidence files, scanned for tokens, session and credential headers, email addresses, home paths, long account-like numbers and GUIDs: 0 findings. All hold the masked account ID with its last 4 digits, so they stay local. These searches catch known shapes only.
- No evidence file has ever been tracked in any commit, and no tracked document holds a masked account ID.
- NuGet, including transitive packages: no vulnerable packages.
- npm, production dependencies: 0 advisories.
- npm, all dependencies: **12 moderate, 0 high** (baseline 14, with 1 high). `vite` went from 5.4.21 to 6.4.5 and `typescript-eslint` to 8.72.1, which cleared the `vite` and `esbuild` advisories. `npm audit fix --force` was not used.
- All 12 trace to `minimatch` below 11.0.1 in ESLint's tooling. ESLint 10 still depends on a flagged range and needs Node 20.19, audit's suggested fix downgrades `typescript-eslint` to 8.14, and an override would jump `minimatch` across a breaking API. They run only on the project's own lint patterns and do not ship.

## Defects

| Blocker | State | Coverage |
|---|---|---|
| Margin and exposure calculations | Fixed. Unknown position data rejects, and missing broker fields stay null | Mock. Real data held no positions |
| Market health | Fixed. Health follows the source timestamp | **Real** |
| Instrument-dependent spread limits | Fixed, by asset class | **Real** values on 3 classes. Limits need owner review |
| Capital.com adapter selection | Fixed. A test resolves the real adapter through the application | Integration test. Real for the probe path, not HTTP routes |
| MarketDataService quotes and candles | Verified, no defect left | **Real** on BTCUSD, and real refusal on the two closed markets |
| Account selection | Fixed. Only the configured account is served, otherwise 403 | Integration test, and real refusal of a made-up ID |
| Position and working-order reconciliation | Fixed. Startup compares the broker with the journal, and a failed read never looks empty | Mock only. Real responses were empty |
| Order confirmation | Fixed. Accepted, rejected, pending, ambiguous and multi-deal results are classified, and the gateway fails closed | Mock only. No order was sent |
| Evidence stamp and overwrite protection | Fixed | **Real** |

### Remaining blockers before Demo execution

1. **Every Capital order is rejected today.** The documented account response has no margin-used or realized-P&L field, and the risk engine rejects null. This needs a proven source or an owner-approved risk policy change. The values must not be invented.
2. **Confirmation and non-empty holdings have never been seen on real data.** That includes positions opened from a working order, and size times level ignoring contract size.
3. **`ExecutionGateway` is not registered in the API and has no check that an intent's account is the configured one.** Add that check when execution is wired up.
4. **The journal has no closed state,** so a legitimately closed deal stays flagged as missing at the broker and blocks reconciliation. This fails closed, but needs a design before real trading.
5. **Startup holdings comparison does not run while execution is disabled,** so reconciliation stays incomplete in read-only use. It first runs in the execution phase.
6. **Working orders are not in the exposure sum.** They are capped only by the maximum pending orders.
7. **The kill switch persists a generic reason.** The specific reason is held in memory only.
8. Quote time-zone handling has not been observed across a daylight saving change.

## Remaining steps

**Gate 4, once the broker reports Open.** The FX and gold markets usually open around 22:00 UTC on Sunday. Every run must share one clean commit, so rebuild first, then run each instrument with a file name for the current commit:

```powershell
dotnet build PilotTrade.slnx -c Release --nologo -v q
$sha = git rev-parse --short HEAD
foreach ($e in 'BTCUSD','EURUSD','GOLD') { dotnet run --no-build -c Release --project src/PilotTrade.Api -- --demo-readonly-evidence "$PWD/docs/phase3/demo-readonly-evidence-$($e.ToLower())-$sha.json" --epic $e }
powershell -NoProfile -File scripts/demo-evidence-report.ps1 -Sha $sha
```

**Gate 5.** Fill in [the checklist](../DEMO_GATE5_OWNER_CHECKLIST.md). Setting the real `TRADING_ACCOUNT_ID` for this also closes the configured-account item under gate 1.

**Gate 8.** After owner approval, push the branch. `.github/workflows/build.yml` then runs both platforms, including the owner flow in the smoke test.

**Gate 9, on a Mac:** `dotnet test PilotTrade.slnx --filter "Category=Credentials|Category=OwnerAuthorization"`, then store the Demo credentials in the login keychain and run the BTCUSD command above, or `PilotTrade.Api --show-owner-token`.

## Decision

- **Read-only Demo: NO-GO.** Gates 4, 5, 7, 8, 9 and 10 have not passed.
- **Controlled Demo execution: not ready.** It needs the read-only gate, blockers 1 to 3 at least, and separate owner authorization.
- **Live: locked.** No move to automated trading without explicit owner authorization after all gates pass.
