# Demo Release Go/No-Go

Status on 2026-10-31:

- **Read-only Demo: NO-GO.** The owner's authenticated run on 2026-10-31 at 18:38 UTC, on commit `c3adf2d`, recorded 6 of 7 steps passing and the quote step failing. The cause is a bug, now fixed: the adapter expected `markets`, but the documented `epics` response is `marketDetails`. The fix has not run against the real API. The market was closed, so freshness is unproven either way.
- **Demo execution: NO-GO, and disabled in code.** It needs separate authorization after the read-only gate passes.
- **Live: LOCKED.** Any request for Live stops startup.
- **macOS arm64: unverified.** It publishes from Windows, but has not run on a Mac. CI covers this once the branch is pushed.

## Read-only gate

Every item must pass. Criteria 1 to 6 come from one sanitized evidence file made by `--demo-readonly-evidence` against the real Demo API (see `DEMO_AUTHENTICATION_AND_LOCAL_SETUP.md`).

| # | Criterion | Evidence needed | Status |
|---|---|---|---|
| 1 | Account identity: the session's active account equals the requested account, and a mismatch is refused | Probe session and account steps pass | **Partial.** The match passed on the only account (1 account). Refusal and switching are mock tested only |
| 2 | Accounts: financial fields parse, and values the broker does not send stay null | Probe account step passes | **Passed on `c3adf2d`, repeat needed.** Available and equity present; margin used and realized P&L null. Presence only: the evidence holds no values, and none were compared |
| 3 | Instrument metadata: currency, precision, status, sizes, increment and stop limits parse for at least 2 instruments of different asset classes | Probe metadata step passes for each epic (one run per epic) | **Incomplete.** One epic (EURUSD) passed on presence checks; a second asset class has not run |
| 4 | Market data: quote and candle timestamps, spread and freshness validate during market hours | Probe quote and candle steps pass | **Failed.** Quote failed as `MalformedResponse`. 50 candles returned, but their freshness was not checked and the market was closed |
| 5 | Exposure: positions and working orders match the Capital.com Demo web platform at the same moment | Probe steps pass, and the owner compares the two by eye | **Not done.** 0 positions and 0 working orders returned, so non-empty parsing is unproven, and the owner has not compared them |
| 6 | No order endpoint called | `OrderEndpointsCalled` is `false` in the evidence | **Unproven by evidence.** The `c3adf2d` value was a hardcoded `false`. It is now measured in the adapter, so the next run proves it |
| 7 | The evidence file holds no credential, token or full account ID | Owner review and `scripts/secret-scan.ps1` | **Scan clean, owner review pending.** The file holds a masked ID and step results. The masked ID keeps the last 4 digits, so the file is ignored by git |
| 8 | CI passes on Windows and macOS, including the owner flow in the smoke test | Green run for the release commit | Needs a push |
| 9 | Credentials work from the macOS keychain on a real Mac | Evidence run or `--show-owner-token` plus the macOS tests | Not run |
| 10 | No known vulnerable dependency | NuGet and npm audits clean | Pass on Windows |

Any failure, or any evidence older than the commit being released, means NO-GO. The recorded run is older than the current code, so all of criteria 1 to 7 must be repeated.

## Still inconclusive

- **Quote time zone.** The documented `marketDetails` snapshot has only a local `updateTime`, so the adapter subtracts `timezoneOffset` from `GET /session`. That meaning is inferred from documented samples, not stated by Capital.com. A wrong conversion fails closed as stale or future, and an open-market run must confirm it.
- **Quote failure cause on the real API.** The recorded file does not name the failing field, because it recorded the error kind only. The documentation gives the cause. Failures now name the field.
- **Adapter and host in the recorded run.** The file does not record them. The code path (`CreateCapitalDemo`, which refuses non-Demo hosts) and a real account in the results point to the real Demo adapter. New evidence records the adapter, host and build.
- **Multiple accounts**, because the Demo login has only one.

## Before the Demo execution phase

These do not block the read-only gate, but execution stays NO-GO until they are resolved:

1. Capital.com's documented account response has no margin-used or realized-P&L field. The risk engine rejects when they are null, so every Capital order is rejected today. This is deliberate. A proven source or an owner-approved risk policy change is needed; the values must not be invented.
2. Order submission and confirmation are tested against documented shapes in mocks only. Real confirmation behaviour is unobserved.
3. The owner must authorize minimal-size Demo orders explicitly.
