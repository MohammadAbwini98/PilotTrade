# Phase 01 final report

Written 2026-11-04. Evidence states are Verified, Partial, Blocked or Failed. The work for 01-03 to 01-05 below is in the working tree above `ff0f37d` and is **not committed or run in CI yet**, so its local results are not release evidence.

## Decisions

| Decision | Result | Why |
|---|---|---|
| Windows build and package | **Not GO for the new work.** The last validated package is CI run 41357066494 on `22d9503`. | The new commit needs a green Windows CI run, including `frontend_tests`, `npm_audit_dev` and the release manifest. |
| Phase 01 | **NOT PASSED** | 01-03, 01-04 and 01-05 are Partial; see below. |
| Read-only Capital.com Demo | **NO-GO** | Decided in Phase 03 (03-05), not here. |
| Automated Demo execution | **DISABLED** | Unchanged. No order endpoint was added or called. |
| Live trading | **LOCKED** | Unchanged. |

## Acceptance checklist

| Task | Item | State | Evidence |
|---|---|---|---|
| 01-01 | PowerShell 7 owner-flow smoke test | Verified | CI run 41344790900 on `89d78a7`, 18/18 |
| 01-02 | Windows CI, unconditional secret scan, signed-off artifacts | Verified | CI run 41350159296 on `9c8cce6`; failure drill run 41346793135 on `b12ac43` |
| 01-03 | Portable ZIP builds, verifies, smoke-tests after unzipping | Verified | CI run 41357066494 on `22d9503`, 27/27 |
| 01-03 | Selection persists; restore cannot revive an old version, snapshot, AI decision or authorization | Partial | `RestartPersistenceTests` (5) and `HardeningTests` pass locally; no CI run |
| 01-03 | Owner-authorized read-only no-order evidence | Partial | `GET /api/evidence/no-orders`, owner only, configured account only; tests pass locally; not run against the owner's Demo account |
| 01-03 | Backup and restore procedure | Partial | `Backup-Data.ps1`, 8 release checks pass locally (46/46 total) |
| 01-03 | Rollback to an earlier package | Blocked | No earlier compatible package exists; procedure in `docs/WINDOWS_PORTABLE.md` |
| 01-03 | Standard-user checklist, Windows 10 and Windows 11 | Blocked | Needs the owner on both systems; not run |
| 01-04 | NuGet audit | Partial | No vulnerable packages locally; CI `nuget_audit` on the new commit pending |
| 01-04 | npm production audit | Partial | 0 locally; CI pending |
| 01-04 | npm development advisories | Partial | `minimatch` GHSA-g3hv-75wg-m249 open, no compatible fix; exception **pending owner approval**, expires 2027-02-04. Not a clean audit |
| 01-05 | Automated frontend suite | Partial | 30/30 locally on Node 18.16, below the declared Node 20; three mutations each failed tests; CI pending |
| 01-05 | Edge and Chrome smoke, redacted screenshots | Blocked | Not run: needs the owner's token in a real session |
| 01-05 | 100%, 150%, 200% scaling, keyboard-only and screen-reader pass | Blocked | Not run; listed in the `docs/WINDOWS_PORTABLE.md` checklist |

## Local results on the working tree above `ff0f37d`

- `npm test`: 30 passed. `tsc -b` and `eslint`: no errors.
- `dotnet test`: 102 unit passed; 234 integration passed, 3 skipped, 237 total.
- `scripts/release-checks.tests.ps1` on Windows PowerShell 5.1: 46 passed.
- `npm audit --omit=dev`: 0. `scripts/npm-audit-policy.ps1`: not clean, covered by a pending exception. NuGet: none.

No Windows 11 system, Edge or Chrome session was used. Nothing here claims they were tested.

## Defect register

| # | Defect | State |
|---|---|---|
| D1 | The instrument selection was lost on restart | Fixed, uncommitted |
| D2 | Selection versions started over after a restart, so an old snapshot or decision could match again | Fixed: the version continues above every stored selection, decision, AI analysis and order intent |
| D3 | The UI did not show the restored selection after Connect | Fixed, and the instrument is checked with the broker again first |
| D4 | The owner-token hint named a developer flag instead of `Show-Owner-Token.cmd` | Fixed |
| D5 | An empty search showed nothing; Connect could be pressed twice; no visible focus ring | Fixed |
| D6 | `vitest` 3.2.7 brought critical `tinypool` advisories | Rejected; 4.1.12 used |
| D7 | Local npm 9.5.1 crashes adding `vitest` 4; lockfile written with `--legacy-peer-deps` | Open; CI `npm ci` on Node 20 is the check |
| D8 | `minimatch` moderate advisory in the lint tools | Open; exception pending owner approval |
| D9 | Rollback cannot be tested | Blocked until a second package exists |
| D10 | Standard-user checklist, browser smoke and scaling not run | Blocked; needs the owner |

## Security findings

- No new write endpoint. The evidence endpoint is a GET behind owner authorization and serves only the configured account. `RestartPersistenceTests` checks that it refuses a request without the owner token and one for another account. Loopback binding applies to it as to every route, and was not changed.
- CSRF, Origin checks, loopback binding, the audit log, the journal and the persistent kill switch are unchanged. No test disables them.
- The UI retries a GET once after a network error and never repeats a POST.
- Backups hold the database only. Credentials and the owner token stay in Windows Credential Manager and are never copied. A restore checks SHA-256 sums first and moves, never deletes, the files it replaces.
- A restored selection is re-issued under a new id and a higher version, so restored data cannot re-authorize anything.
- Production npm dependencies and NuGet packages: no known advisories. Development: one moderate advisory, not shipped in the package.
- No credential, owner token or account data is in the changed files. Raw `docs/phase3/demo-readonly-evidence*.json` files stay local and ignored.

## What the owner needs to do to finish Phase 01

1. Approve pushing the commit set, so CI runs on it.
2. Decide on the `minimatch` exception: approve it in `security/npm-audit-exceptions.json`, or leave it pending.
3. Run the standard-user checklist in `docs/WINDOWS_PORTABLE.md` on Windows 10 and Windows 11, including Edge and Chrome, keyboard-only use and 100–200% scaling.
4. Rollback stays Blocked until the next package is built.
