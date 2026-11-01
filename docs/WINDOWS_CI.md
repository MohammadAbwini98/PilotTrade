# Windows CI and release artifacts

The workflow is `.github/workflows/build.yml`. It has one job, on `windows-latest`. The macOS job is gone, and the macOS source stays in the tree, unsupported.

## What a run means

Each check is its own step, and the job summary lists every step's outcome. Any failed step fails the job.

| Area | Steps |
|---|---|
| Build | `npm_install`, `lint`, `frontend_build`, `publish`, `package` |
| Dependency audit | `npm_audit`, `nuget_audit` |
| Tests | `credential_tests`, `full_tests`, `helpers_pwsh`, `helpers_powershell`, `release_checks` |
| Smoke | `smoke`: a disposable database and the simulator, with no broker key, live account or real order |
| Secret scan | `secret_scan`: runs even when an earlier step failed |

A green secret scan does not prove a secret is absent. It finds known shapes only.

## What gets uploaded

| Artifact | When | Contents | Kept for |
|---|---|---|---|
| `win-x64-<sha>` | Every step passed | Portable ZIP, its `.sha256` file and manifest | 30 days |
| `diagnostics-<sha>` | A step failed and the secret scan passed | `.trx` test results and manifest | 14 days |

If the scan fails, nothing is uploaded, so its findings have to be read in the step log. That log shows file paths and never the matched text. App logs from the smoke test are deleted when it ends and are not uploaded.

## The manifest

`release/win-x64.manifest.json` records the commit, ref, run ID, OS, runtime, .NET SDK and Node versions, each step's outcome, a SHA-256 for each file, a build hash over the sorted file list, and the ZIP's SHA-256. Check a file against it with:

```powershell
Get-FileHash PilotTrade-win-x64.zip -Algorithm SHA256
```

The manifest step fails if the working tree changed during the build, or if `PilotTrade.Api.dll` does not name the commit in its product version.

## Failure drill

To show that a failing smoke test still runs the scan and leaves the run red, push a throwaway branch whose `scripts/smoke-test.ps1` throws right after its `param` block. Expect `smoke` to fail, `secret_scan` to run, no `win-x64-<sha>` artifact and a `diagnostics-<sha>` artifact. Delete the branch afterwards.
