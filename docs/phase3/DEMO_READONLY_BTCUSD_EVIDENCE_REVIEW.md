# Capital.com Demo Crypto Evidence Review

Date: 2026-10-31. Evidence: `docs/phase3/demo-readonly-evidence-btcusd.json` (ignored by git), run at 19:02:01 UTC on `926fb93`.

**Result: the run is valid and all 7 steps passed, but read-only Demo stays NO-GO.** Gates 1, 5, 7, 8 and 9 are open. Demo execution is disabled and Live is locked.

The evidence holds presence flags, counts and ages by design. It holds no prices, spread, balances or raw broker responses, so those values could not be checked here.

## Review results

| Check | Result | Basis |
|---|---|---|
| File integrity | Pass | Valid JSON. The file write time (19:02:02 UTC) matches `RunUtc`. The build hash equals HEAD, and the run came 8 minutes after that commit. The build does not record whether the tree was clean |
| Real Demo API, not simulated | Pass, inferred | Adapter is `CapitalComBrokerAdapter`, and its constructor refuses any other host. The session needs real `CST` and `X-SECURITY-TOKEN` headers. No handler override is registered. `ApiHost` comes from configuration, and broker request logging is off, so no wire record exists |
| Exact epic is BTCUSD | **Not proven** | The epic is not recorded. Only the file name says BTC, and the default epic is EURUSD |
| Crypto classification | Pass | `assetClass=CryptoCfd`, mapped only from the broker's `CRYPTOCURRENCIES` type |
| Market status | Pass | `Open`, mapped only from `TRADEABLE`. FX and gold were `Closed` minutes earlier, which fits a Saturday |
| Quote | Pass | Age 1 s, limit 60 s, not future-dated |
| Spread | Pass, weak | Only `spreadWithinPolicy=True`. The limit is 5%, which is very wide for BTC, and the value is not recorded |
| Candles | Pass | 50 candles, ordered and valid. Latest close was 19:00:00 UTC, an exact 5-minute boundary, at age 121 s against a 900 s limit |
| Account identity | Partial | One account. The real checks ran inside the adapter (active account through `GET /session`, unique ID). `accountMatched` itself cannot be false |
| Balance, available | Parsed, values unverified | Required numeric fields, so a missing one would have failed the step. Nothing is treated as zero |
| Equity | Derived, values unverified | Calculated from balance, deposit and P&L, seen only with no open positions |
| Margin used, realized P&L | Null, correct | Not sent to zero. These flags are constants in the adapter, not an observation of the broker |
| Positions, working orders | Parsed as empty | Required arrays were present with 0 items. Non-empty parsing is mock tested only |
| Stale, future or fabricated values | None found | Ages are non-negative. The closed FX and gold candles both end at exactly 21:00:00 UTC on Friday, and their quotes shortly before it |
| No trading | Pass | `OrderEndpointsCalled=false` comes from a counter on every non-session write. Submit, cancel and close send nothing. The evidence path exits before the app starts |
| Sensitive data | No credential found | See below |

## Sensitive data

- `scripts/secret-scan.ps1` found nothing in 146 files: tracked files, the 4 evidence files and 8 logs. It only catches known shapes, and its default run skips ignored files.
- The evidence holds no tokens, keys, passwords, emails or full account ID. A count of emails and long digit runs in the logs found none.
- No files needed changing. Before sharing outside the project, remove these:
  - the last 4 account digits in `AccountIdMasked`, which this report omits;
  - the Windows user name, which appears in file paths in all 8 non-empty logs.
- No log exists for this run. `run.txt` is from about 16:10 UTC, before the run, so it is not evidence for it. It shows earlier 401 and 429 session responses.
- Build logs in 5 older public commits remain an open item from the hardening report.

## Gates

| # | Gate | Status |
|---|---|---|
| 1 | Account identity | Partial |
| 2 | Account parsing | Passed, parsing only |
| 3 | Instrument metadata, 2 or more asset classes | Passed |
| 4 | Market data | Passed for crypto only |
| 5 | Exposure matches the web platform | Not done |
| 6 | No order endpoint | Passed |
| 7 | Evidence is safe | Scan clean, owner review pending |
| 8 | CI on Windows and macOS | Needs a push |
| 9 | macOS keychain | Not run |
| 10 | Dependencies | Pass on Windows, not repeated on `926fb93` |

The full criteria are in `docs/DEMO_RELEASE_GO_NO_GO.md`.

## Remaining issues

1. This evidence does not record the requested epic, so a BTC run cannot be told from another crypto run. Fixed in the probe after this run; a new run must confirm it.
2. In this evidence, `accountMatched` and `epicMatched` cannot be false, and it does not say whether the account was configured or auto-selected. Fixed in the probe after this run; a new run must confirm it.
3. `health=Healthy` appears next to quotes 22 hours old, because health only reflects `delayTime`. Those steps still fail correctly through `fresh=False`.
4. The 5% spread limit is too loose to say much about BTC.
5. The API routes and `MarketDataService` have not run against the real API.

## Manual comparison on the Capital.com web platform

Compare these while logged in to the same Demo account, with a new evidence run or API response at the same moment:

- account ID, currency, balance and available funds, plus equity and margin as the platform shows them;
- open positions and working orders, both expected to be 0;
- BTC/USD epic, market status, bid and offer, minimum and maximum size, size step, stop distances and decimal places;
- latest 5-minute candle against the platform chart;
- the activity history, which should show no orders or deals from the run.

## Next actions

1. Owner: do the manual comparison above and record the result for gate 5.
2. Done after this run: the probe records `RequestedEpic`, `AccountSource` and `instrumentName`, and adds the `activeSessionAccount` step. Commit this change.
3. Owner: repeat the evidence run on that commit with `--epic BTCUSD`, then for EURUSD and gold after the FX open on Sunday evening UTC.
4. Run the API routes and a market snapshot with the real Demo adapter, and compare them with the web platform.
5. Owner: review the evidence files for gate 7, then decide on pushing so CI can cover gates 8 and 9, and run the macOS check on a real Mac.
6. Owner: decide on the 5% spread limit for crypto.

Do not start Demo execution until the read-only gate passes and the owner authorizes it separately.
