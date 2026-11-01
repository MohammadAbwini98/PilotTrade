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

## Rollback

1. Stop the app.
2. Run `Start-PilotTrade.cmd` from the earlier unzipped folder.
3. If the newer version upgraded the database, restore the copy taken before the update.

Check any copy with `Verify-Package.ps1`, and check the ZIP against `PilotTrade-win-x64.zip.sha256` and the `packageSha256` in the manifest.

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
- [ ] Rolling back to an earlier folder starts the earlier version

## CI evidence

Run 41357066494 on `22d9503`, on Windows 10.0.26100 (the hosted runner, which is not evidence for a Windows 11 desktop): green. The downloaded ZIP is 51.4 MB, SHA-256 `6b1447172d7129c7aec5a0d414496c979be3357c3bf80551c5bfb471d497c842`. It matches its `.sha256` file and the manifest's `packageSha256`, and all 368 listed files verify. The build names its commit, and the working tree stayed clean.

The checklist above has not been run, so compatibility with a standard user on Windows 10 and Windows 11 is unproven.
