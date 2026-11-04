# Windows portable package

`scripts/package-windows.ps1` turns win-x64 self-contained publish output into `PilotTrade-win-x64.zip`. CI builds it, verifies it, and runs the smoke test from the unzipped copy. The instructions for users are in `packaging/windows/README.txt`, which ships in the ZIP.

## Behaviour of a packaged build

A build counts as packaged when `wwwroot/index.html` sits beside the executable. Development runs keep their old defaults.

| Item | Packaged build | Override |
|---|---|---|
| Address | `http://127.0.0.1:5099` | `--urls` or `ASPNETCORE_URLS` |
| Database | `%LOCALAPPDATA%\PilotTrade\pilottrade.db` | `ConnectionStrings__Default` |
| UI and settings | Beside the executable, from any working directory | None |
| Broker mode | Simulation | `appsettings.Production.json` or environment |

**Migration:** a packaged build no longer uses a `pilottrade.db` in its working directory. An earlier published build's journal, audit log and kill-switch state do not carry over by themselves, and the app logs a warning at startup if it finds such a file beside the executable or in its working directory. To keep them, stop the app and copy that file to the per-user path before the first launch.

## Backup, restore and rollback

The database holds the order journal, the audit log, the persistent kill switch and the selected instrument. Credentials and the owner token stay in Windows Credential Manager and are never in a backup. `scripts/backup-data.ps1`, shipped in the package as `Backup-Data.ps1`, does both directions; it refuses to run on the default folder while `PilotTrade.Api.exe` is running.

**Before every update:**

1. Close the PilotTrade window.
2. `powershell -ExecutionPolicy Bypass -File Backup-Data.ps1` (in the PilotTrade folder) copies `pilottrade.db` and its `-wal` and `-shm` files to `%LOCALAPPDATA%\PilotTrade\backups\<UTC time>\` with a `SHA256SUMS.txt`.
3. Keep the earlier unzipped folder; unzip the new version beside it, not over it.

**Rollback:**

1. Close the PilotTrade window.
2. If the newer version changed the database, `powershell -ExecutionPolicy Bypass -File Backup-Data.ps1 -Restore <backup folder>`. It checks the backup against its checksums first and changes nothing if they differ. The files it replaces move to `%LOCALAPPDATA%\PilotTrade\replaced-<UTC time>\`; nothing is deleted.
3. Run `Start-PilotTrade.cmd` from the earlier folder, and check that the version it shows is the earlier one.
4. Check that the kill switch and journal look as they did before the update, and select the instrument again if needed.

A restore cannot bring back authority for anything that was decided before the backup. At startup, the saved instrument selection is re-issued under a new id, marked `restored`, with a version above every stored selection, decision, AI analysis and order intent. A snapshot, AI decision or execution authorization for an earlier version is refused (`RestartPersistenceTests`).

Check any copy with `Verify-Package.ps1`, and check the ZIP against `PilotTrade-win-x64.zip.sha256` and the `packageSha256` in the manifest.

**Status: BLOCKED.** The backup and restore script is checked in CI on a stand-in folder (`scripts/release-checks.tests.ps1`). A rollback to an earlier package has not been run, because no earlier compatible portable package exists: the first one was built on `22d9503`. It can be run once a second package exists, using the steps above.

## Manual checklist

Run this on Windows 10 x64 and on Windows 11 x64, each under a standard, non-administrator account with no .NET SDK. Record the OS build, ZIP hash and result for each. If a system is unavailable, record it as BLOCKED.

- [ ] The ZIP's SHA-256 matches its `.sha256` file, and `Verify-Package.ps1` passes
- [ ] `Start-PilotTrade.cmd` starts the app without an administrator prompt and opens the browser
- [ ] `Show-Owner-Token.cmd` shows the token, and the browser accepts it
- [ ] The UI shows simulation mode and that Live is locked
- [ ] The database appears under `%LOCALAPPDATA%\PilotTrade`, and nothing new appears in the package folder
- [ ] After closing and restarting, a setting made before the restart, such as the selected instrument, is still there
- [ ] Another device on the network cannot reach port 5099
- [ ] No order was submitted, as the journal in the UI shows
- [ ] After deleting the folder, the data under `%LOCALAPPDATA%\PilotTrade` remains
- [ ] A backup taken with `backup-data.ps1` restores, and rolling back to an earlier folder starts the earlier version (BLOCKED until a second package exists)
- [ ] Keyboard only: Tab reaches every control with a visible focus ring, and Enter works them
- [ ] At 100%, 150% and 200% display scaling in Edge, nothing is cut off and no horizontal scrolling is needed

## CI evidence

Run 41357066494 on `22d9503`, on Windows 10.0.26100 (the hosted runner, which is not evidence for a Windows 11 desktop): green. The downloaded ZIP is 51.4 MB, SHA-256 `6b1447172d7129c7aec5a0d414496c979be3357c3bf80551c5bfb471d497c842`. It matches its `.sha256` file and the manifest's `packageSha256`, and all 368 listed files verify. The build names its commit, and the working tree stayed clean.

The checklist above has not been run, so compatibility with a standard user on Windows 10 and Windows 11 is unproven.
