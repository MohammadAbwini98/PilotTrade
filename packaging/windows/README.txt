PilotTrade, portable build for Windows 10 and 11 x64

No installer, administrator rights or .NET install are needed.

START
  Unzip this folder anywhere you can read it, then run Start-PilotTrade.cmd.
  The app answers on http://127.0.0.1:5099, on this machine only.
  It starts in simulation mode. Close its window to stop it.

OWNER TOKEN
  Run Show-Owner-Token.cmd and enter the token in the browser.
  Do not paste it into chat, tickets or screenshots.

SETTINGS
  Copy appsettings.example.json to appsettings.Production.json in this folder and edit the copy.
  Do not put credentials in it. They belong in Windows Credential Manager.
  Live trading is locked.

YOUR DATA
  %LOCALAPPDATA%\PilotTrade\pilottrade.db
  This holds the order journal, audit log, kill-switch state and selected instrument. It is not in this folder.
  If an earlier build left a pilottrade.db elsewhere, this build does not use it and warns at startup.
  To keep that history, stop the app and copy the file to the path above before the first launch.

CHECK THE DOWNLOAD
  powershell -ExecutionPolicy Bypass -File Verify-Package.ps1
  Compare the ZIP itself with: Get-FileHash PilotTrade-win-x64.zip -Algorithm SHA256

BACK UP, UPDATE OR GO BACK
  Close the PilotTrade window first. Before updating, back up your data:
    powershell -ExecutionPolicy Bypass -File Backup-Data.ps1
  Keep the earlier unzipped folder and unzip the new version beside it.
  To go back, close the app, restore the backup if the newer version changed the database:
    powershell -ExecutionPolicy Bypass -File Backup-Data.ps1 -Restore <backup folder>
  then run Start-PilotTrade.cmd from the earlier folder. A restore keeps the files it replaces.
  A restored instrument selection gets a new version, so nothing decided before the backup is reused.

REMOVE
  Stop the app and delete this folder. Your data stays.
  To remove the data too, delete %LOCALAPPDATA%\PilotTrade.
  The owner token and any broker credentials stay in Windows Credential Manager until you remove them there.
