# Changelog

## Unreleased (2026-10-31)
- Capital.com Demo read-only hardening: explicit broker modes, account proof, real instrument metadata, market data validation, macOS keychain store, owner authorization, strict Origin and CSRF, patched Microsoft.OpenApi, expanded tests and CI.
- Fixed quote parsing: `GET /markets?epics=` answers with `marketDetails`, not `markets`. Found by the owner's real Demo run, and confirmed on an open crypto market since.
- Stricter read-only probe: records the adapter, host and build, measures order-endpoint use, and cannot pass freshness on a closed market. The output path is checked before any broker request.
- The probe records the requested epic, the broker's instrument name and the account source, and checks the active account against `GET /session`. The always-true `accountMatched` and `epicMatched` fields are gone.
- Gate acceptance on `24b4360`: gates 1, 2, 3 and 6 pass. Gate 4 is blocked by closed FX and gold markets, gates 5 and 7 wait for the owner, and gate 10 fails on development-only npm advisories, including a high one in `vite`. Read-only Demo stays NO-GO.
- Evidence records the Git commit, working-tree state, actual epic and account source, and never overwrites an earlier file. `scripts/demo-evidence-report.ps1` checks that a set belongs to one clean commit and writes a sanitized report.
- Feed health follows the source timestamp, and spread limits depend on the asset class. Unknown position data rejects exposure, and only the configured account is served or selected.
- Startup compares positions and working orders with the journal. Ambiguous confirmations stay Unknown, and a journal or audit failure after a broker answer suspends trading.
- Upgraded `vite` to 6.4.5 and `typescript-eslint` to 8.72.1, which leaves 12 moderate development-only npm advisories from `minimatch`.
- Acceptance on `09244cf`: gates 1, 2, 3 and 6 pass. Gate 4 is blocked by closed FX and gold markets, gates 5 and 7 wait for the owner, gates 8 and 9 have not run, and gate 10 still fails. Read-only Demo stays NO-GO.
- First CI run, on `dc377bd`: tests passed on Windows and macOS, and the smoke test failed on a wrong check. It expected `executionEnabled` to be false in simulation, and now checks that the run is on the simulator.
- Second CI run, on `347ad92`: tests passed on both systems, and the smoke test failed with a 400 on instrument selection. The likely cause is PowerShell passing the JSON list as one object, so the script sent every epic in one request. It now unrolls the list, which CI has yet to confirm.
- Demo execution stays disabled and Live stays locked.

## 0.1.0 (2026-10-31)
- Initial implementation: domain, broker adapter (simulator + Capital.com skeleton), market data, risk engine, decision coordinator, execution gateway, audit, API, React dashboard, tests, docs.
- LIVE execution locked/unarmed.