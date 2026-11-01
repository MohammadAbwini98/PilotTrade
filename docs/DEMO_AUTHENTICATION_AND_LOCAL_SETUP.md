# Demo Authentication and Local Setup

Never paste credentials into chat or commit them.

## Storage

| Platform | Store | Targets |
|---|---|---|
| Windows | Credential Manager | `CapitalTrading/Demo/ApiKey`, `ApiPassword`, `Identifier` |
| macOS | Login keychain, no administrator rights needed | The same names, as generic passwords |

Demo and Live use separate targets. Protected targets can only be changed through the owner-authorized credential endpoint, and a change that fails restores the previous values. Other platforms refuse to run the credential features.

## Owner access

All `/api` routes need the owner token, a loopback caller and a loopback Host. Print the token in your own terminal:

```bash
PilotTrade.Api --show-owner-token
```

The UI asks for it and keeps it in memory only. Browser writes also need an allowed Origin and a single-use CSRF token.

## Modes

Simulation is the default. The real adapter needs all of:

```
BROKER_MODE=capital_demo
TRADING_ENVIRONMENT=demo
TRADING_ACCOUNT_ID=<your Demo account ID>
```

Live values stop startup. The Capital.com adapter refuses to submit orders. Simulation only fills orders in memory, so `/api/runtime` reports `executionEnabled` as true there, and no broker is contacted.

## Read-only evidence run

This logs in with the stored Demo credentials, and uses session and read endpoints only. Run it during market hours, once per instrument:

```bash
dotnet run --project src/PilotTrade.Api -- --demo-readonly-evidence "$PWD/docs/phase3/demo-readonly-evidence.json" --epic EURUSD
```

Before any broker request, the run checks the output path and exits with code 2 if it is unusable. Exit code 0 means every step passed, 1 means a step failed, and 3 means the file could not be written, in which case the sanitized evidence is printed instead.

A closed market fails the quote and candle steps, because freshness cannot be proven then. Check that `RequestedEpic` and `instrumentName` show the instrument you meant, and that `AccountSource` is `configured` if you set `TRADING_ACCOUNT_ID`. Files named `docs/phase3/demo-readonly-evidence*.json` are ignored by git; use a different name per epic and commit, such as `demo-readonly-evidence-gold-<sha>.json`. An existing file is never replaced, and the run exits with code 2 instead.

Do not share the raw file: the masked ID keeps the last 4 digits. To check that a set of files belongs to one clean commit and get a version that can be shared, once `demo-readonly-evidence-btcusd-`, `-eurusd-` and `-gold-<sha>.json` exist:

```bash
powershell -NoProfile -File scripts/demo-evidence-report.ps1 -Sha <sha>
```

Rebuild after committing and before a run, because the commit and working-tree state are stamped at build time. Then compare the account with the web platform using `DEMO_GATE5_OWNER_CHECKLIST.md`.
