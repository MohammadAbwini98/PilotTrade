# Demo Release Go/No-Go

Status on 2026-10-31:

- **Read-only Demo: NO-GO.** The owner's run on 2026-10-31 at 19:02 UTC, on commit `926fb93`, passed all 7 steps on an open crypto market, so the quote fix now works against the Demo API. Criteria 1, 5, 7, 8 and 9 are still open. The probe has changed since that run, so criteria 1 to 7 must be repeated on the new commit before release. See `phase3/DEMO_READONLY_BTCUSD_EVIDENCE_REVIEW.md`.
- **Demo execution: NO-GO, and disabled in code.** It needs separate authorization after the read-only gate passes.
- **Live: LOCKED.** Any request for Live stops startup.
- **macOS arm64: unverified.** It publishes from Windows, but has not run on a Mac. CI covers this once the branch is pushed.

## Read-only gate

Every item must pass. Criteria 1 to 6 come from sanitized evidence files made by `--demo-readonly-evidence` against the real Demo API, one per epic (see `DEMO_AUTHENTICATION_AND_LOCAL_SETUP.md`). Statuses below rest on three runs on `926fb93`: crypto at 19:02 UTC (all steps passed), FX at 18:56 UTC and commodity at 18:56 UTC (market closed, so quote and candle steps failed closed, as designed).

| # | Criterion | Evidence needed | Status |
|---|---|---|---|
| 1 | Account identity: the session's active account equals the requested account, and a mismatch is refused | Probe session and account steps pass | **Partial.** The match passed on the only account (1 account). Refusal and switching are mock tested only |
| 2 | Accounts: financial fields parse, and values the broker does not send stay null | Probe account step passes | **Passed on `926fb93`, parsing only.** Balance, deposit, P&L and available were numeric, or the step would have failed. Margin used and realized P&L stay null. No values are recorded, so none were compared |
| 3 | Instrument metadata: currency, precision, status, sizes, increment and stop limits parse for at least 2 instruments of different asset classes | Probe metadata step passes for each epic (one run per epic) | **Passed on `926fb93`.** Fx, Commodity and CryptoCfd each passed. The files do not record the epic requested |
| 4 | Market data: quote and candle timestamps, spread and freshness validate during market hours | Probe quote and candle steps pass | **Passed on `926fb93`, crypto only.** Open market, quote age 1 s (limit 60 s), latest candle close age 121 s (limit 900 s). Spread is recorded only as within the 5% policy. FX and commodity have not run on an open market |
| 5 | Exposure: positions and working orders match the Capital.com Demo web platform at the same moment | Probe steps pass, and the owner compares the two by eye | **Not done.** 0 positions and 0 working orders returned, so non-empty parsing is unproven, and the owner has not compared them |
| 6 | No order endpoint called | `OrderEndpointsCalled` is `false` in the evidence | **Passed on `926fb93`.** The value is measured by the adapter's write counter, and the order methods send nothing. There is no separate wire capture |
| 7 | The evidence file holds no credential, token or full account ID | Owner review and `scripts/secret-scan.ps1` | **Scan clean, owner review pending.** The scan and a manual read of the 4 evidence files found no credential or full ID. The masked ID keeps the last 4 digits, so the files stay ignored by git |
| 8 | CI passes on Windows and macOS, including the owner flow in the smoke test | Green run for the release commit | Needs a push |
| 9 | Credentials work from the macOS keychain on a real Mac | Evidence run or `--show-owner-token` plus the macOS tests | Not run |
| 10 | No known vulnerable dependency | NuGet and npm audits clean | Pass on Windows. Audits were not repeated on `926fb93`; no dependency file changed since `c3adf2d` |

Any failure, or any evidence older than the commit being released, means NO-GO. A probe change or any later code commit therefore means repeating criteria 1 to 7.

## Still inconclusive

- **Which crypto epic ran on `926fb93`.** That evidence records `epicMatched=True` and `assetClass=CryptoCfd`, but not the epic. BTCUSD rests on the file name and the command the owner ran. New evidence records `RequestedEpic` and the broker's `instrumentName`.
- **Quote time zone on other instruments.** The 1 s quote age shows the `timezoneOffset` conversion worked for this crypto run, and the closed FX and commodity quotes dated just before the 21:00 UTC close agree. Capital.com does not state the meaning, and a daylight saving change has not been observed.
- **Evidence fields that cannot change.** On `926fb93`, `accountMatched` and `epicMatched` compared a value the adapter copied from its own argument, so they were always true when reached. Both are removed: the new `activeSessionAccount` step compares the broker's `GET /session` answer, `AccountSource` says whether the account was configured or auto-selected, and a wrong epic fails as `MalformedResponse: instrument.epic`. `marginUsedPresent` and `realizedPnLPresent` remain always false, because the adapter sets both to null. `currencyPresent` and `availablePresent` remain always true on this adapter, because its readers refuse a missing field first.
- **Equity.** It is derived, not reported: balance counts as equity when balance equals deposit plus P&L. This has only been seen with no open positions.
- **Application results.** The probe calls the adapter directly. The API routes and `MarketDataService` have not run against the real API, including the 1h candles the snapshot also requests.
- **Multiple accounts**, because the Demo login has only one.

## Before the Demo execution phase

These do not block the read-only gate, but execution stays NO-GO until they are resolved:

1. Capital.com's documented account response has no margin-used or realized-P&L field. The risk engine rejects when they are null, so every Capital order is rejected today. This is deliberate. A proven source or an owner-approved risk policy change is needed; the values must not be invented.
2. Order submission and confirmation are tested against documented shapes in mocks only. Real confirmation behaviour is unobserved.
3. The owner must authorize minimal-size Demo orders explicitly.
