# Dependency audit (task 01-04)

Run on 2026-11-04 on the working tree above `ff0f37d`, on Windows 10 19045 with Node 18.16.0 and npm 9.5.1. CI repeats each check on Node 20 in the `npm_audit`, `npm_audit_dev` and `nuget_audit` steps.

| Check | Command | Result |
|---|---|---|
| NuGet, all six projects, transitive | `dotnet list PilotTrade.slnx package --vulnerable --include-transitive` | No vulnerable packages |
| npm, production | `npm audit --omit=dev` | 0 vulnerabilities |
| npm, development | `scripts/npm-audit-policy.ps1` | Not clean: 12 packages flagged, 1 advisory, covered by an exception that still needs owner approval |

## The open advisory

GHSA-g3hv-75wg-m249, moderate, in `minimatch` (ReDoS on crafted glob patterns). It reaches the build only through the lint tools: `eslint` 9.39.5, `@eslint/eslintrc` 3.3.7, `@eslint/config-array` 0.21.2 and `@typescript-eslint/typescript-estree` 8.72.1. That makes up the 12 flagged packages. The fix is in `minimatch` 11.0.1.

**No compatible update exists.** As of 2026-11-04 the latest releases still want older `minimatch` lines: `eslint` 10.14 wants `^10.2.6`, `typescript-estree` wants `^10.2.2`, `@eslint/eslintrc` 3.3.7 wants `^3.1.5` and `config-array` wants `^10.2.4`. Forcing 11 with an `overrides` entry would be an untested major-version override, and eslint 9 calls `minimatch` 3 as a function, which 11 does not export. `npm audit fix --force` was not run.

**Exposure.** The glob patterns come from this repository's own `eslint.config.js`, not from users or the network. None of these packages ships: the portable build holds the compiled `wwwroot` bundle and the .NET app only, and the production audit is clean.

**Exception.** `security/npm-audit-exceptions.json` records it with status `pending-owner-approval`, recorded 2026-11-04 and expiring 2027-02-04. It is not approved: `approvedBy` and `approvedUtc` are empty, and only the owner may fill them. Until then CI passes the step with a warning and reports it as not clean. After 2027-02-04 the step fails whether or not it was approved.

To approve, the owner sets `"status": "approved"`, `"approvedBy"` and `"approvedUtc"` in that file and commits it. To close it, update the lint tools once a release accepts `minimatch` 11, and delete the entry.

## Packages added for 01-05

`vitest` 4.1.12, `jsdom` 26.1.0, `@testing-library/react` 16.3.3, `@testing-library/dom` 10.4.2 and `@testing-library/user-event` 14.6.8, all exact and development only.

`vitest` 3.2.7 was installed first and rejected. It brought GHSA-85c8-ppgw-ccpr and GHSA-5gmw-xhrv-c9v3 (critical, in `tinypool`), GHSA-82fw-gwwq-j7x9 (moderate, `vitest`) and GHSA-qxg6-89mg-rqv8 (moderate, `@vitest/mocker`), and the new policy failed on all four. 4.1.12 has none of them and does not depend on `tinypool`. `@testing-library/jest-dom` 6.10 was not used because it needs Node 22.

**Local tooling limits.** npm 9.5.1 on this machine crashes in its peer resolver (`Cannot read properties of null (reading 'edgesOut')`) when adding `vitest` 4, as does npm 9.9.4. The lockfile was therefore written with `npm install --legacy-peer-deps`; `npm ci` accepts it locally, and the CI `npm_install` step on Node 20 is the check that counts. `vitest` 4 declares Node `^20 || ^22 || >=24`; it ran on Node 18.16 here, but only the Node 20 CI run is evidence. Fetching a Node 20 runtime locally was refused by this session's permissions.
