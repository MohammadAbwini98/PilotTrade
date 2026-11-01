@echo off
rem Starts PilotTrade on this machine only, waits until it answers, then opens the browser.
cd /d "%~dp0"
curl.exe -s -f -o nul http://127.0.0.1:5099/health && goto open
start "PilotTrade" "%~dp0PilotTrade.Api.exe"
for /l %%i in (1,1,30) do (
  curl.exe -s -f -o nul http://127.0.0.1:5099/health && goto open
  ping -n 2 127.0.0.1 >nul
)
echo PilotTrade did not answer within 30 seconds. Check its window for the reason.
pause
exit /b 1
:open
start "" http://127.0.0.1:5099/
