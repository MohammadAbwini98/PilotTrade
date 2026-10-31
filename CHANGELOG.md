# Changelog

## Unreleased (2026-10-31)
- Capital.com Demo read-only hardening: explicit broker modes, account proof, real instrument metadata, market data validation, macOS keychain store, owner authorization, strict Origin and CSRF, patched Microsoft.OpenApi, expanded tests and CI.
- Fixed quote parsing: `GET /markets?epics=` answers with `marketDetails`, not `markets`. Found by the owner's real Demo run; the fix has not run against the real API yet.
- Stricter read-only probe: records the adapter, host and build, measures order-endpoint use, and cannot pass freshness on a closed market. The output path is checked before any broker request.
- The probe records the requested epic, the broker's instrument name and the account source, and checks the active account against `GET /session`. The always-true `accountMatched` and `epicMatched` fields are gone.
- Demo execution stays disabled and Live stays locked. Not validated against the real Demo API: the one recorded run failed its quote step.

## 0.1.0 (2026-10-31)
- Initial implementation: domain, broker adapter (simulator + Capital.com skeleton), market data, risk engine, decision coordinator, execution gateway, audit, API, React dashboard, tests, docs.
- LIVE execution locked/unarmed.