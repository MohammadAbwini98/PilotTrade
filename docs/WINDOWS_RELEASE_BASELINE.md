# Windows Release Baseline

Task 00-01 of the Windows-only delivery plan. Recorded 2026-11-01 on `feature/capital-demo-readonly-hardening`, commit `455a2fd1bb82e1bf08ab5ee5e524ae2732427714`, clean working tree, Windows 10 Enterprise x64 (19045).

**Result: read-only Demo NO-GO, Demo execution NO-GO and disabled, Live locked.** Nothing here passes a gate. Earlier gate results stay as history until they are re-run on the release commit.

## Scope

- **Supported:** Windows 10 and 11 x64, normal user with no administrator rights, local-first .NET 10 and React with SQLite, secrets in Windows Credential Manager, loopback listener only.
- **Not supported:** macOS. It is no longer a release criterion, and former gate 9 is N/A. The macOS source and old evidence stay in place.
- **AI:** Claude and JEV only.
- **Instruments:** chosen by the owner across supported asset classes, not fixed to BTCUSD.

## Three separate decisions

A GO on one of these never implies a GO on the next.

| Decision | Status | What it allows | Decided at |
|---|---|---|---|
| Read-only Demo | NO-GO | Reading accounts, instruments, quotes and holdings. No orders | Task 03-05 |
| Demo execution | NO-GO, disabled in code | Minimal-size Demo orders, only inside an envelope the owner authorizes afresh | Readiness at 04-08, orders from 05-01, decision at 05-04 |
| Live | LOCKED | Nothing. Out of scope for this plan | Not applicable |

## Measured on this commit

| Check | Command | Result |
|---|---|---|
| .NET build | `dotnet build PilotTrade.slnx -c Release` | Succeeded, 0 warnings, 0 errors |
| Unit tests | `dotnet test PilotTrade.slnx -c Release --no-build` | 100 total: 100 passed, 0 failed, 0 skipped |
| Integration tests | Same command | 227 total: 224 passed, 0 failed, 3 skipped |
| Frontend install | `npm ci` | Succeeded |
| Frontend lint | `npm run lint` | Clean |
| Frontend build | `npm run build` | Succeeded |
| NuGet audit | `dotnet list PilotTrade.slnx package --vulnerable --include-transitive` | No vulnerable packages |
| npm audit, production | `npm audit --omit=dev` | 0 vulnerabilities |
| npm audit, all | `npm audit` | **12 moderate**, 0 high, 0 critical. Twelve packages are named, all in the ESLint tooling, with `minimatch` as the source |
| Secret scan | `scripts/secret-scan.ps1` under Windows PowerShell 5.1 | No findings in 141 files. This excludes publish output, which was not built |

### Not run on this commit

- Published win-x64 smoke test, anonymous or owner flow.
- CI. `gh run list` shows no run for `455a2fd`; the newest is 41341674927 on `2db8417`.
- Any call to the real Capital.com Demo API, so gates 1 to 6 have no evidence here.
- The UI in a browser.

### Local environment limits

- PowerShell 7 is **not installed** here, only Windows PowerShell 5.1. CI uses `pwsh`.
- Local Node is 18.16.0, while CI and the README use Node 20.
- `node_modules` was missing and was restored with `npm ci`. No tracked file changed.

## Gate register

Former numbers are kept, and nothing is renumbered. "Current evidence" means evidence on `455a2fd`.

| Gate | Requirement | Latest result | Current evidence | Owner | Task | Closure check |
|---|---|---|---|---|---|---|
| 1 | Active account equals the requested account, and a mismatch is refused | Passed on `09244cf` | None, needs a re-run | Engineering, with the owner setting `TRADING_ACCOUNT_ID` | 03-01, 03-02 | `activeAccountMatched=True` on the release commit, wrong ID refused, one run with the real configured ID |
| 2 | Financial fields parse, and unsent values stay null | Passed on `09244cf`, parsing only | None, needs a re-run | Engineering | 03-04 | Probe account step passes on the release commit |
| 3 | Metadata parses for 2 or more asset classes | Passed on `09244cf` | None, needs a re-run | Engineering | 03-01 | Probe metadata step passes per epic on the release commit |
| 4 | Quote, candles, spread and freshness during market hours | **Blocked** on `09244cf`: BTCUSD passed, EURUSD and GOLD closed | None | Engineering, needs open markets | 03-01 | BTCUSD, EURUSD and GOLD all pass on one clean commit while the broker reports Open |
| 5 | Identity, funds and holdings match the Demo web platform | **Blocked** on the owner | None | **Owner** | 03-02 | Owner completes `DEMO_GATE5_OWNER_CHECKLIST.md` for the release commit |
| 6 | No order endpoint called | Passed on `09244cf` | None, needs a re-run | Engineering | 03-01 | `OrderEndpointsCalled=false` in all evidence on the release commit |
| 7 | Evidence holds no credential, token or personal data | Scan clean, **owner review pending** | Tracked-file scan clean, evidence not re-made | Engineering, then **owner** | 03-03 | Scan clean on the release evidence, and the owner has read the sanitized report |
| 8 | CI passes on Windows, including the owner flow | **Not passed on the release commit.** Passed on `2db8417` under the old two-system rule | None, no run on `455a2fd` | Engineering, with the owner approving each push | 01-01, 01-02 | Whole Windows job green on the release commit |
| 9 | Former: credentials from the macOS keychain on a real Mac | **N/A, Windows only** | Not applicable | None | None | None. Not a failure |
| 10 | No known vulnerable dependency | **Fail** strictly | Measured above: NuGet clean, production npm clean, 12 moderate in development tooling | Engineering, and the **owner** if an exception is wanted | 01-04 | `npm audit` clean, or a written owner-approved exception |

## Blockers from the Phase 3 report

Each one comes from `phase3/DEMO_ACCEPTANCE_09244cf.md`. That report stays unchanged as the historical record.

| # | Blocker | Owner | Task | Closure check | Status |
|---|---|---|---|---|---|
| 1 | Every Capital order is rejected, because margin used and realized P&L have no documented source | Engineering, with an **owner** policy decision | 04-01 | Proven source or owner-approved policy, and tests that nothing is invented | Open, fails closed |
| 2 | Confirmation and non-empty holdings never seen on real data, including contract size | Engineering, then **owner** authorization | 03-04, 04-05, 05-02 | Contract tests, then real Demo observation | Open, mock only |
| 3 | `ExecutionGateway` not registered, and no configured-account check on intents | Engineering | 04-02 | Integration test refuses an intent for another account | Open |
| 4 | Journal has no closed state, so a closed deal blocks reconciliation | Engineering | 04-03 | Test closes a deal and reconciliation stays clean | Open, fails closed |
| 5 | Startup holdings comparison does not run while execution is disabled | Engineering | 04-03 | Comparison runs and is tested in the read-only path, or the limit is accepted in writing | Open |
| 6 | Working orders are not in the exposure sum | Engineering | 04-04 | Risk test counts a working order | Open |
| 7 | Kill switch persists only a generic reason | Engineering | 04-07 | Specific reason survives a restart in a test | Open |
| 8 | Quote time zone unobserved across a daylight saving change | Engineering | 04-08 | Test with both offsets, plus an observation when a change occurs | Open |
| 9 | No automated frontend tests | Engineering | 01-05 | Test suite runs in CI | Open |
| 10 | UI behind the token prompt, and the HTTP routes, not exercised against the real API | Engineering, with the **owner** | 02-10, 03-02 | Authenticated end-to-end run, plus the gate 5 comparison | Open |
| 11 | Spread limits are unreviewed defaults, and EURUSD read above its limit while closed | **Owner** | 03-01 | Open-market readings reviewed by the owner | Open |

## Backlog

| Area | Tasks |
|---|---|
| Windows CI and gate 8 | 01-01, 01-02 |
| Packaging | 01-03 |
| Dependencies and gate 10 | 01-04 |
| Frontend tests | 01-05 |
| Dashboard and settings | 02-01 to 02-10 |
| Gate 4 market data | 03-01 |
| Gate 5 owner parity | 03-02 |
| Gate 7 privacy | 03-03 |
| Broker read contracts | 03-04 |
| Read-only decision | 03-05 |
| Risk and execution readiness, with submission still disabled | 04-01 to 04-08 |
| Controlled Demo orders, after fresh owner authorization | 05-01 to 05-04 |
| Analytics and handover | 06-01 to 06-03 |

## Verified read-only features

These have been seen working against the real Demo API, on `09244cf`:

- Session and account proof, and refusal of a made-up account ID.
- Instrument metadata on crypto, FX and commodity.
- Quote and candle validation on open BTCUSD, and stale refusal on closed EURUSD and GOLD.
- `MarketDataService` snapshots on BTCUSD.
- Evidence stamping and overwrite protection.

The API's HTTP routes, the authenticated UI and non-empty holdings have **not** been seen against real data.

## Risk register

| Risk | Severity | Likelihood | Preconditions | Owner decision | Validation | Rollback |
|---|---|---|---|---|---|---|
| A green run or old report is read as permission to trade | Critical | Medium | Mixed-up decisions | None. Orders always need fresh authorization | Three decisions tracked apart, submission disabled in code | Revert the claim, keep submission disabled |
| Live becomes reachable | Critical | Low | A code or configuration change | Live is out of scope | Smoke test proves Live stops startup | Revert the commit |
| Secret leaks into git, logs or evidence | High | Low | New logging or evidence fields | None | Secret scan, including publish output, and owner review | Rotate the credential, purge the evidence |
| No frontend tests, so UI regressions go unnoticed during Phase 02 | High | High | Dashboard work starts first | None | 01-05 before 02-01 | Revert the UI commit |
| Risk engine has no margin or realized P&L source | High | Certain | Capital.com's documented response | Policy decision at 04-01 | Tests that it fails closed | Keep rejecting orders |
| Real order behaviour differs from the mocks | High | Medium | Mocks follow documentation only | Authorization at 05-01 | Minimal Demo orders under observation | Kill switch, disable submission |
| Evidence goes stale as code moves on | Medium | High | Any later code commit | None | Evidence carries commit and tree state, and 03-05 freezes it | Re-run on the release commit |
| Development npm advisories stay unresolved | Medium | Medium | `minimatch` under ESLint, no compatible fix when last checked | Accept in writing, or wait | `npm audit` in 01-04 | Pin the previous lockfile |
| Local checks differ from CI, as PowerShell 7 is absent and Node is 18 | Medium | Medium | This machine's toolchain | Approve each push | Windows CI run on the release commit | Not applicable |
| Removing the macOS CI job loses coverage of macOS code that stays in the tree | Low | High | Windows-only scope | Accepted by the scope change | macOS code is unsupported and marked so | Restore the job from git |

## Rollback

This task changes documents only, with no migration or restart. Revert its commit to restore the earlier wording.
