# Demo Release Go/No-Go

Status on 2026-10-31, 21:28 UTC, on commit `24b4360`:

- **Read-only Demo: NO-GO.** Criteria 1, 2, 3 and 6 pass. Criterion 4 is blocked by closed FX and gold markets, 5 and 7 wait for the owner, 8 and 9 have not run, and 10 fails. See `phase3/DEMO_READONLY_GATE_ACCEPTANCE_24b4360.md`.
- **Demo execution: NO-GO, and disabled in code.** It needs separate authorization after the read-only gate passes.
- **Live: LOCKED.** Any request for Live stops startup.
- **macOS arm64: unverified.** It publishes from Windows, but has not run on a Mac. CI covers this once the branch is pushed.

## Read-only gate

Every item must pass. Criteria 1 to 6 come from sanitized evidence files made by `--demo-readonly-evidence` against the real Demo API, one per epic (see `DEMO_AUTHENTICATION_AND_LOCAL_SETUP.md`). Statuses below rest on four runs on `24b4360`: BTCUSD (all steps passed), EURUSD and GOLD (market closed, so quote and candle steps failed closed, as designed), and a made-up account ID (refused).

| # | Criterion | Evidence needed | Status |
|---|---|---|---|
| 1 | Account identity: the session's active account equals the requested account, and a mismatch is refused | Probe session and account steps pass | **Passed on `24b4360`.** `activeAccountMatched=True` against `GET /session` on all three instrument runs, and a made-up ID was refused with `AccountNotFound`. Switching between accounts is mock tested only, because the login has 1 account |
| 2 | Accounts: financial fields parse, and values the broker does not send stay null | Probe account step passes | **Passed on `24b4360`, parsing only.** Margin used and realized P&L stay null. No values are recorded, so none were compared |
| 3 | Instrument metadata: currency, precision, status, sizes, increment and stop limits parse for at least 2 instruments of different asset classes | Probe metadata step passes for each epic (one run per epic) | **Passed on `24b4360`.** BTCUSD (CryptoCfd), EURUSD (Fx) and GOLD (Commodity), with the epic and broker name recorded |
| 4 | Market data: quote and candle timestamps, spread and freshness validate during market hours | Probe quote and candle steps pass | **Blocked.** Crypto passed on an open market: quote age 3 s (limit 60 s), latest candle close age 128 s (limit 900 s). FX and gold were closed, so their freshness is unproven. Spread is recorded only as within the 5% policy |
| 5 | Exposure: positions and working orders match the Capital.com Demo web platform at the same moment | Probe steps pass, and the owner compares the two by eye | **Blocked on the owner.** 0 positions and 0 working orders returned, so non-empty parsing is unproven |
| 6 | No order endpoint called | `OrderEndpointsCalled` is `false` in the evidence | **Passed on `24b4360`.** The value is measured by the adapter's write counter, and the order methods send nothing. There is no separate wire capture |
| 7 | The evidence file holds no credential, token or full account ID | Owner review and `scripts/secret-scan.ps1` | **Scan clean, blocked on owner review.** The scan and a manual read of the 4 new files found no credential or full ID. The masked ID keeps the last 4 digits, so the files stay ignored by git |
| 8 | CI passes on Windows and macOS, including the owner flow in the smoke test | Green run for the release commit | Needs a push |
| 9 | Credentials work from the macOS keychain on a real Mac | Evidence run or `--show-owner-token` plus the macOS tests | Not run |
| 10 | No known vulnerable dependency | NuGet and npm audits clean | **Fail.** NuGet is clean and production npm dependencies have 0 advisories, but the full npm audit reports 14 in development tooling: 1 high (`vite`) and 13 moderate |

Any failure, or any evidence older than the commit being released, means NO-GO. A probe change or any later code commit therefore means repeating criteria 1 to 7.

## Still inconclusive

- **Dirty-tree runs.** The build hash comes from HEAD and does not show uncommitted changes. A run at 21:15:37 UTC holds the new fields but is labelled `926fb93`, so it is excluded.
- **EPIC discovery.** Epics were verified by direct lookup, with the broker returning the same epic and name. The `searchTerm` route has not run against the real API.
- **Quote time zone on other instruments.** The BTCUSD quote age shows the `timezoneOffset` conversion worked for crypto, and the closed FX and commodity quotes dated just before the 21:00 UTC close agree. Capital.com does not state the meaning, and a daylight saving change has not been observed.
- **Evidence fields that cannot change.** `marginUsedPresent` and `realizedPnLPresent` remain always false, because the adapter sets both to null. `currencyPresent` and `availablePresent` remain always true on this adapter, because its readers refuse a missing field first.
- **Equity.** It is derived, not reported: balance counts as equity when balance equals deposit plus P&L. This has only been seen with no open positions.
- **Application results.** The probe calls the adapter directly. The API routes and `MarketDataService` have not run against the real API, including the 1h candles the snapshot also requests.
- **Configured account.** All passing runs used `AccountSource=autoSelected`. The real ID has not been set through `TRADING_ACCOUNT_ID`.
- **Multiple accounts**, because the Demo login has only one.

## Before the Demo execution phase

These do not block the read-only gate, but execution stays NO-GO until they are resolved:

1. Capital.com's documented account response has no margin-used or realized-P&L field. The risk engine rejects when they are null, so every Capital order is rejected today. This is deliberate. A proven source or an owner-approved risk policy change is needed; the values must not be invented.
2. Order submission and confirmation are tested against documented shapes in mocks only. Real confirmation behaviour is unobserved.
3. The owner must authorize minimal-size Demo orders explicitly.
