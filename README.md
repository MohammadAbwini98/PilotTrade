# PilotTrade — Capital.com Multi-Instrument AI Trading Platform

Local-first .NET 10 + React trading platform. Simulation by default, Capital.com Demo read-only by explicit configuration, Demo execution disabled, Live locked.

Current release status: [docs/DEMO_RELEASE_GO_NO_GO.md](docs/DEMO_RELEASE_GO_NO_GO.md).

## Prerequisites
- .NET 10 SDK
- Node 20 (for the web build)
- Windows 10/11 x64 or macOS Apple Silicon

## Build & Run
```
npm ci --prefix src/PilotTrade.Web
npm run build --prefix src/PilotTrade.Web
dotnet build PilotTrade.slnx
dotnet run --project src/PilotTrade.Api --urls http://127.0.0.1:5099
```
Open http://127.0.0.1:5099 and enter the owner token, printed by:
```
dotnet run --project src/PilotTrade.Api -- --show-owner-token
```

## Tests
```
dotnet test PilotTrade.slnx
npm run lint --prefix src/PilotTrade.Web
```

## Configuration
Copy `.env.example` for the available settings. Secrets live in Windows Credential Manager or the macOS keychain; see [the setup guide](docs/DEMO_AUTHENTICATION_AND_LOCAL_SETUP.md). Never commit credentials.

## Safety
- Live requests stop startup, and Capital.com order submission is disabled.
- The execution gateway is the sole execution path, behind the risk engine.
- Stale decisions, or missing or invalid data, result in NO_TRADE.
- Uncertain order outcomes are reconciled and never resubmitted automatically.
