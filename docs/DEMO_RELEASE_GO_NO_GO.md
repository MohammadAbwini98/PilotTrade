# Demo Release Go/No-Go

Windows 10 and 11 x64 only. Status on 2026-11-01, with probe evidence from commit `09244cf`. The baseline on `455a2fd`, with blocker owners and closure checks, is in `WINDOWS_RELEASE_BASELINE.md`.

- **Read-only Demo: NO-GO.** Criteria 1, 2, 3 and 6 pass on `09244cf`. Criterion 4 is blocked by closed FX and gold markets, 5 and 7 wait for the owner, 8 has no green run on the release commit, 9 is N/A, and 10 fails. See `phase3/DEMO_ACCEPTANCE_09244cf.md`.
- **Demo execution: NO-GO, and disabled in code.** It needs separate authorization after the read-only gate passes.
- **Live: LOCKED.** Any request for Live stops startup.
- **macOS: not supported, and not a release criterion.** The code and old evidence stay in place. CI on `2db8417` last passed there, using a throwaway keychain.

## Read-only gate

Every item must pass. Criteria 1 to 6 come from sanitized evidence files made by `--demo-readonly-evidence` against the real Demo API, one per epic (see `DEMO_AUTHENTICATION_AND_LOCAL_SETUP.md`). Statuses below rest on four runs on `09244cf`: BTCUSD (all steps passed), EURUSD and GOLD (market closed, so quote, candle and snapshot steps failed closed, as designed), and a made-up account ID (refused).

| # | Criterion | Evidence needed | Status |
|---|---|---|---|
| 1 | Account identity: the session's active account equals the requested account, and a mismatch is refused | Probe session and account steps pass | **Passed on `09244cf`.** `activeAccountMatched=True` against `GET /session` on all three instrument runs, and a made-up ID was refused with `AccountNotFound`. Switching between accounts is mock tested only, because the login has 1 account |
| 2 | Accounts: financial fields parse, and values the broker does not send stay null | Probe account step passes | **Passed on `09244cf`, parsing only.** Margin used and realized P&L stay null. No values are recorded, so none were compared |
| 3 | Instrument metadata: currency, precision, status, sizes, increment and stop limits parse for at least 2 instruments of different asset classes | Probe metadata step passes for each epic (one run per epic) | **Passed on `09244cf`.** BTCUSD (CryptoCfd), EURUSD (Fx) and GOLD (Commodity), with the epic and broker name recorded |
| 4 | Market data: quote and candle timestamps, spread and freshness validate during market hours | Probe quote and candle steps pass | **Blocked.** Crypto passed on an open market: quote age 0 s (limit 60 s), latest candle close age 132 s (limit 900 s), spread 0.0641% (limit 1%). FX and gold were closed, so their freshness is unproven |
| 5 | Exposure: positions and working orders match the Capital.com Demo web platform at the same moment | Probe steps pass, and the owner compares the two by eye | **Blocked on the owner.** Use `DEMO_GATE5_OWNER_CHECKLIST.md`. 0 positions and 0 working orders returned, so non-empty parsing is unproven |
| 6 | No order endpoint called | `OrderEndpointsCalled` is `false` in the evidence | **Passed on `09244cf`.** The value is measured by the adapter's write counter, and the order methods send nothing. There is no separate wire capture |
| 7 | The evidence file holds no credential, token or full account ID | Owner review and `scripts/secret-scan.ps1` | **Scan clean, blocked on owner review.** The scans of all raw files found no credential, session header, personal detail or full ID. The masked ID keeps the last 4 digits, so the raw files stay ignored by git. `phase3/DEMO_EVIDENCE_SANITIZED_09244cf.md` is the version to share once the owner has read it |
| 8 | CI passes on Windows, including the owner flow in the smoke test | Green Windows job for the release commit | **Not passed on the release commit.** `2db8417` passed under the old two-system rule: run 41341674927 is green on both systems. Windows: unit 100 passed, integration 224 passed, 3 skipped. macOS: unit 100 passed, integration 223 passed, 4 skipped. The smoke test with the owner flow passed, and the secret scan found nothing in 511 and 507 files, including publish output. Three earlier runs failed on faults in the two scripts, not in the application. `455a2fd` and later commits have no run yet |
| 9 | Former criterion: credentials work from the macOS keychain on a real Mac | None | **N/A, Windows only.** It never ran, and it is not a failure |
| 10 | No known vulnerable dependency | NuGet and npm audits clean | **Fail.** NuGet is clean and production npm dependencies have 0 advisories, but the full npm audit reports 12 moderate in development tooling, all from `minimatch` under ESLint. No compatible fix exists yet. The owner may accept this residual, but it is not a pass |

Any failure, or any evidence older than the commit being released, means NO-GO. A probe change or any later code commit therefore means repeating criteria 1 to 7.

## Still inconclusive

- **Quote time zone on other instruments.** The BTCUSD quote age shows the `timezoneOffset` conversion worked for crypto, and the closed FX and commodity quotes dated just before the 21:00 UTC close agree. Capital.com does not state the meaning, and a daylight saving change has not been observed.
- **Evidence fields that cannot change.** `marginUsedPresent` and `realizedPnLPresent` remain always false, because the adapter sets both to null. `currencyPresent` and `availablePresent` remain always true on this adapter, because its readers refuse a missing field first.
- **Equity.** It is derived, not reported: balance counts as equity when balance equals deposit plus P&L. This has only been seen with no open positions.
- **Application results.** `MarketDataService` has run against the real API on BTCUSD, including 1h candles. The HTTP API routes have not. The owner's gate 5 comparison exercises them.
- **Configured account.** All passing runs used `AccountSource=autoSelected`. The real ID has not been set through `TRADING_ACCOUNT_ID`, though a made-up configured ID was refused.
- **Closed-market spread.** EURUSD read 0.0627% against its 0.05% limit while closed. Open-market spreads are unmeasured, so the default limits need owner review after the open-market run.
- **Multiple accounts**, because the Demo login has only one.

## Before the Demo execution phase

These do not block the read-only gate, but execution stays NO-GO until they are resolved:

1. Capital.com's documented account response has no margin-used or realized-P&L field. The risk engine rejects when they are null, so every Capital order is rejected today. This is deliberate. A proven source or an owner-approved risk policy change is needed; the values must not be invented.
2. Order submission, confirmation and non-empty holdings are tested against documented shapes in mocks only. Real behaviour is unobserved.
3. `ExecutionGateway` is not registered in the API and has no check that an intent's account is the configured one.
4. The journal has no closed state, so a legitimately closed deal stays flagged as missing at the broker and blocks reconciliation.
5. Startup holdings comparison does not run while execution is disabled, so it first runs in the execution phase.
6. Working orders are not in the exposure sum, and are capped only by the maximum pending orders.
7. The owner must authorize minimal-size Demo orders explicitly.
