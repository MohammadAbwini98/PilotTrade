# Capital.com Demo Read-Only Gate 1–7 Acceptance

Tested commit: `24b43604f5da6f4b827957108d54e63d05f0b2c6`, on `feature/capital-demo-readonly-hardening`, clean tree. Runs: 2026-10-31, 21:27 to 21:28 UTC (a Saturday).

**Result: read-only Demo is NO-GO. Not ready for Demo order-execution tests.** Gates 1, 2, 3 and 6 pass. Gates 4, 5 and 7 are blocked: gate 4 by closed FX and gold markets, gates 5 and 7 by owner review. No gate failed. Outside gates 1–7, the npm audit is not clean, so gate 10 fails.

No order was placed, and Live trading and order submission were not enabled.

## Test and build results

| Check | Result |
|---|---|
| .NET build, Release, no-incremental | Succeeded, 0 warnings, 0 errors |
| Unit tests | 49 total: 49 passed, 0 failed, 0 skipped |
| Integration tests | 195 total: 192 passed, 0 failed, 3 skipped (macOS only) |
| Frontend build | Succeeded, 31 modules |
| `scripts/smoke-test.ps1` | Not run. It belongs to gate 8 |

## Demo evidence results

All files are in `docs/phase3/` and are ignored by git. All four record adapter `CapitalComBrokerAdapter`, host `demo-api-capital.backend-capital.com`, environment Demo, build `24b4360`, and `OrderEndpointsCalled=false`.

| File suffix | Epic and broker name | Class, status | Quote age | Latest candle close age | Result |
|---|---|---|---|---|---|
| `btcusd-24b4360` | BTCUSD, Bitcoin/USD | CryptoCfd, Open | 3 s (limit 60 s) | 128 s (limit 900 s) | All 8 steps passed |
| `eurusd-24b4360` | EURUSD, EUR/USD | Fx, Closed | 88050 s | 88038 s | 6 passed. Quote and candles failed closed |
| `gold-24b4360` | GOLD, Gold | Commodity, Closed | 88110 s | 88048 s | 6 passed. Quote and candles failed closed |
| `wrongaccount-24b4360` | BTCUSD, with a made-up account ID | Not reached | Not applicable | Not applicable | Refused as intended: `AccountNotFound` |

Common results on the three instrument runs:

- **Account:** one account, source `autoSelected`, and `activeAccountMatched=True` against the broker's `GET /session`.
- **Financials:** currency and available funds present, equity proven, margin used and realized P&L null.
- **Exposure:** 0 positions and 0 working orders.
- **Candles:** 50 ordered and valid. The closed-market candles end at 21:00:00 UTC on Friday, and the BTCUSD candle closed at 21:25:00 UTC, a completed 5-minute bar.

The closed-market runs are not counted as freshness passes.

**Wrong-account run:** the account steps and every account-scoped step failed with `AccountNotFound`. Its quote and candle steps also failed, because the market status stayed `Unknown`. This is the first real-API evidence of the refusal.

**Limits of this evidence:**

- It holds no prices, spreads, balances or timestamps of quotes, by design. Bid, offer and account values were validated by the probe but cannot be compared from these files.
- "Real Demo API" is inferred from the adapter, its host check and the session headers. There is no wire capture.
- EPICs were verified by direct lookup: the broker returned the same epic and the name above. The `searchTerm` discovery route was not run, because the evidence runner has no search option.
- `AccountSource=configured` with the real account ID has not run. Only the owner should set that ID.

## Gate matrix

"Auto" means verified by the probe or scans. "Manual" means it waits for the owner.

| # | Requirement | Test performed | Status | Type | Evidence | Outstanding, remediation |
|---|---|---|---|---|---|---|
| 1 | Active account equals the requested account, and a mismatch is refused | Probe compared with `GET /session`. Made-up ID run | **PASS** | Auto | `btcusd-`, `eurusd-`, `gold-`, `wrongaccount-24b4360` | Switching between accounts is mock tested only, because the login has one account. Owner confirms the ID on the platform under gate 5 |
| 2 | Financial fields parse, and unsent values stay null | Probe account step, 3 runs | **PASS**, parsing only | Auto | `btcusd-`, `eurusd-`, `gold-24b4360` | Values are compared under gate 5 |
| 3 | Metadata parses for 2 or more asset classes | Probe metadata step | **PASS**, 3 classes | Auto | `btcusd-`, `eurusd-`, `gold-24b4360` | `dealingRulesValid` is not independently confirmed against the platform |
| 4 | Quote, candles, spread and freshness validate in market hours | Probe on 3 instruments | **BLOCKED** | Auto | Crypto passed in `btcusd-24b4360`. FX and gold closed | Rerun EURUSD and GOLD after the FX open on Sunday evening UTC |
| 5 | Positions and working orders match the web platform at the same moment | Probe side only: 0 and 0 | **BLOCKED** | Manual | All three instrument files | Owner compares with the platform. Non-empty parsing stays mock tested |
| 6 | No order endpoint called | Adapter write counter, 4 runs | **PASS** | Auto | All four files | Measured in the adapter, with no separate wire capture |
| 7 | Evidence holds no credential, token or full account ID | Secret scan and a read of all 4 new files | **BLOCKED** | Auto done, manual pending | Scan below | Owner review. The masked ID keeps the last 4 digits, so the files stay out of git and need that removed before outside sharing |

## Security scan results

- `scripts/secret-scan.ps1` over tracked files and `docs/phase3`: no findings in 146 files.
- The same credential patterns across all 14 commits: 0 hits. No evidence file, `.log`, `.env`, `.pfx` or `.pem` has ever been tracked. The masked account ID appears in no tracked file. These searches only catch known shapes. They do not close the older open item on build logs in 5 public commits.
- All evidence files are ignored by git.
- NuGet, including transitive packages: no vulnerable packages.
- npm, production dependencies: 0 advisories.
- npm, all dependencies: **14 advisories, 1 high (`vite`) and 13 moderate**, all in development tooling (`vite`, `esbuild`, ESLint and `typescript-eslint` chains). They do not ship in the built app, but the `vite` fix needs a major version upgrade to 8.3.5, and several others have no fix available.

## Remaining defects

1. **Gate 10 fails.** Its criterion is clean audits, and the npm audit is not clean. Earlier documents recorded a pass. No dependency file changed since `926fb93`, so the difference comes from the audit data or from the earlier check. Upgrade `vite` and retest, as a separate change.
2. **The build hash does not show a dirty tree.** `demo-readonly-evidence-btcusd.json` holds a run at 21:15:37 UTC with the new fields, labelled `926fb93`, 17 seconds before `24b4360` was committed. It came from uncommitted code, so it is excluded here. Record dirty state in the evidence, or check for it before a run. That run also overwrote the 19:02 UTC file the earlier review cites.
3. `health=Healthy` appears beside quotes more than 24 hours old, because health only reflects `delayTime`. Freshness still fails correctly.
4. The 5% spread limit is too loose to say much about BTC, and no spread value is recorded.
5. The API routes and `MarketDataService`, including the 1h candles, have not run against the real API.
6. `currencyPresent`, `availablePresent`, `marginUsedPresent` and `realizedPnLPresent` cannot change on this adapter.
7. Quote time-zone handling has not been observed across a daylight saving change.

Gates 8 (CI, needs a push) and 9 (macOS keychain) remain open.

## Remediation made

Documents only: this report, `docs/DEMO_RELEASE_GO_NO_GO.md`, a note in `docs/phase3/DEMO_READONLY_BTCUSD_EVIDENCE_REVIEW.md`, and `CHANGELOG.md`. No code changed, so no tests needed rerunning. These changes are uncommitted.

## Next actions

1. Owner: commit or review these documents, then rerun all three instruments on one commit after the FX open on Sunday evening UTC. Check that the markets show `Open`, and use the file names for that commit.
2. Owner: compare the account, positions and working orders with the web platform during that run (gate 5). The checklist is in `DEMO_READONLY_BTCUSD_EVIDENCE_REVIEW.md`.
3. Owner: review the evidence files (gate 7).
4. Run the API routes and a market snapshot with the real Demo adapter.
5. Owner: decide on the `vite` upgrade, and on the push for gates 8 and 9.

## Decision

- **Demo read-only: NO-GO.** Gates 4, 5, 7, 8, 9 and 10 are not passed.
- **Demo order-execution tests: not ready.** They need the read-only gate to pass and separate owner authorization. Every Capital order is also rejected today because margin used and realized P&L are null, and order confirmation has only been tested against mocks.
- **Live: locked.**
