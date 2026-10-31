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

Live values stop startup. Order submission stays disabled in every mode.

## Read-only evidence run

This logs in with the stored Demo credentials, and uses session and read endpoints only. Run it during market hours, once per instrument:

```bash
dotnet run --project src/PilotTrade.Api -- --demo-readonly-evidence "$PWD/docs/phase3/demo-readonly-evidence.json" --epic EURUSD
```

Review the file before sharing or committing it. It should hold only a masked account ID and step results.
