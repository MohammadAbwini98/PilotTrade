# Demo Authentication and Local Setup

1. Set TRADING_ENVIRONMENT=demo.
2. Store secrets via OS store keys: capital:Demo:api_key, capital:Demo:identifier, capital:Demo:password.
   Windows: DPAPI under %LOCALAPPDATA%\PilotTrade\secrets.
3. Run app; verify /health and /api/account.
4. First Demo submission must be owner-initiated and explicitly approved.
Never paste credentials into chat or commit.