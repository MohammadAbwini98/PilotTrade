# PilotTrade — Capital.com Multi-Instrument AI Trading Platform

Local-first .NET 10 + React trading platform. DEMO-first. LIVE locked/unarmed in initial delivery.

## Prerequisites
- .NET 10 SDK
- Node 18+ (for web build)
- Windows 10/11 x64 or macOS Apple Silicon

## Build & Run
```
dotnet build PilotTrade.sln
dotnet run --project src/PilotTrade.Api --urls http://127.0.0.1:5099
```
Open http://127.0.0.1:5099

## Tests
```
dotnet test tests/PilotTrade.UnitTests
```

## Configuration
Copy .env.example to .env (not committed). Secrets stored via OS secret store (DPAPI on Windows). Never commit credentials.

## Safety
- LIVE execution disabled by default; requires authenticated owner activation workflow (not implemented for arming in initial release).
- Risk gateway is sole execution authority.
- Stale decisions / missing data => NO_TRADE.