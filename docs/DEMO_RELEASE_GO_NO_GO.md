# Demo Release Go/No-Go

Status on 2026-10-31:

- **Read-only Demo: NO-GO.** The code is ready for the evidence run, but no evidence exists: the first attempt crashed on its output path after the probe, losing the results. "Not run" below means no recorded result.
- **Demo execution: NO-GO, and disabled in code.** It needs separate authorization after the read-only gate passes.
- **Live: LOCKED.** Any request for Live stops startup.
- **macOS arm64: unverified.** It publishes from Windows, but has not run on a Mac. CI covers this once the branch is pushed.

## Read-only gate

Every item must pass. Criteria 1 to 6 come from one sanitized evidence file made by `--demo-readonly-evidence` against the real Demo API (see `DEMO_AUTHENTICATION_AND_LOCAL_SETUP.md`).

| # | Criterion | Evidence needed | Status |
|---|---|---|---|
| 1 | Account identity: the session's active account equals the requested account, and a mismatch is refused | Probe session and account steps pass | Not run |
| 2 | Accounts: financial fields parse, and values the broker does not send stay null | Probe account step passes | Not run |
| 3 | Instrument metadata: currency, precision, status, sizes, increment and stop limits parse for at least 2 instruments of different asset classes | Probe metadata step passes for each epic (one run per epic) | Not run |
| 4 | Market data: quote and candle timestamps, spread and freshness validate during market hours | Probe quote and candle steps pass | Not run |
| 5 | Exposure: positions and working orders match the Capital.com Demo web platform at the same moment | Probe steps pass, and the owner compares the two by eye | Not run |
| 6 | No order endpoint called | `OrderEndpointsCalled` is `false` in the evidence | Not run |
| 7 | The evidence file holds no credential, token or full account ID | Owner review and `scripts/secret-scan.ps1` | Not run |
| 8 | CI passes on Windows and macOS, including the owner flow in the smoke test | Green run for the release commit | Needs a push |
| 9 | Credentials work from the macOS keychain on a real Mac | Evidence run or `--show-owner-token` plus the macOS tests | Not run |
| 10 | No known vulnerable dependency | NuGet and npm audits clean | Pass on Windows |

Any failure, or any evidence older than the commit being released, means NO-GO.

## Before the Demo execution phase

These do not block the read-only gate, but execution stays NO-GO until they are resolved:

1. Capital.com's documented account response has no margin-used or realized-P&L field. The risk engine rejects when they are null, so every Capital order is rejected today. This is deliberate. A proven source or an owner-approved risk policy change is needed; the values must not be invented.
2. Order submission and confirmation are tested against documented shapes in mocks only. Real confirmation behaviour is unobserved.
3. The owner must authorize minimal-size Demo orders explicitly.
