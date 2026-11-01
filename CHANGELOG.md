# Changelog

## Unreleased (2026-10-31)
- Capital.com Demo read-only hardening: explicit broker modes, account proof, real instrument metadata, market data validation, macOS keychain store, owner authorization, strict Origin and CSRF, patched Microsoft.OpenApi, expanded tests and CI.
- Fixed quote parsing: `GET /markets?epics=` answers with `marketDetails`, not `markets`. Found by the owner's real Demo run, and confirmed on an open crypto market since.
- Stricter read-only probe: records the adapter, host and build, measures order-endpoint use, and cannot pass freshness on a closed market. The output path is checked before any broker request.
- The probe records the requested epic, the broker's instrument name and the account source, and checks the active account against `GET /session`. The always-true `accountMatched` and `epicMatched` fields are gone.
- Gate acceptance on `24b4360`: gates 1, 2, 3 and 6 pass. Gate 4 is blocked by closed FX and gold markets, gates 5 and 7 wait for the owner, and gate 10 fails on development-only npm advisories, including a high one in `vite`. Read-only Demo stays NO-GO.
- Demo execution stays disabled and Live stays locked.

## 0.1.0 (2026-10-31)
- Initial implementation: domain, broker adapter (simulator + Capital.com skeleton), market data, risk engine, decision coordinator, execution gateway, audit, API, React dashboard, tests, docs.
- LIVE execution locked/unarmed.