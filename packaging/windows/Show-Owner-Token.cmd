@echo off
rem Shows the owner token on this screen only. It stays in Windows Credential Manager and is never written to a file.
"%~dp0PilotTrade.Api.exe" --show-owner-token
pause
