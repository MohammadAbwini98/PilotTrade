# Backs up or restores PilotTrade's local database: the order journal, audit log, kill-switch state and instrument selection.
# Credentials and the owner token are in Windows Credential Manager, not in this folder, so they are never copied.
#
#   Backup:  powershell -ExecutionPolicy Bypass -File backup-data.ps1
#   Restore: powershell -ExecutionPolicy Bypass -File backup-data.ps1 -Restore <backup folder>
#
# PilotTrade must be stopped. A restore never deletes the current files: they are moved to a "replaced-..." folder first.
# After a restore, the app re-issues the saved instrument selection under a new id and a higher version, so no snapshot,
# AI decision or execution authorization made before the backup is accepted again.
param(
    [string]$DataFolder = (Join-Path $env:LOCALAPPDATA 'PilotTrade'),
    [string]$Restore
)
$ErrorActionPreference = 'Stop'
$names = 'pilottrade.db', 'pilottrade.db-wal', 'pilottrade.db-shm'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')

# A packaged PilotTrade only ever uses the default folder, so a running app matters only there.
$default = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'PilotTrade')).TrimEnd('\')
if ([IO.Path]::GetFullPath($DataFolder).TrimEnd('\') -eq $default -and (Get-Process -Name 'PilotTrade.Api' -ErrorAction SilentlyContinue)) {
    Write-Output 'FAIL: PilotTrade is running. Close its window, then run this again.'
    exit 1
}

function Hashes($folder) {
    Get-ChildItem $folder -File | Where-Object { $names -contains $_.Name } | Sort-Object Name |
        ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" }
}

if (-not $Restore) {
    if (-not (Test-Path (Join-Path $DataFolder 'pilottrade.db'))) { Write-Output "FAIL: no database in $DataFolder"; exit 1 }
    $target = Join-Path $DataFolder "backups\$stamp"
    New-Item -ItemType Directory -Force $target | Out-Null
    foreach ($n in $names) { $f = Join-Path $DataFolder $n; if (Test-Path $f) { Copy-Item $f $target } }
    Set-Content (Join-Path $target 'SHA256SUMS.txt') (Hashes $target) -Encoding ascii
    Write-Output "Backed up to $target"
    exit 0
}

$sums = Join-Path $Restore 'SHA256SUMS.txt'
if (-not (Test-Path (Join-Path $Restore 'pilottrade.db')) -or -not (Test-Path $sums)) { Write-Output "FAIL: $Restore is not a PilotTrade backup"; exit 1 }
$expected = @(Get-Content $sums | Where-Object { $_ })
$actual = @(Hashes $Restore)
if (($expected -join "`n") -ne ($actual -join "`n")) { Write-Output 'FAIL: the backup does not match its SHA256SUMS.txt; nothing was changed'; exit 1 }

$replaced = Join-Path $DataFolder "replaced-$stamp"
New-Item -ItemType Directory -Force $replaced | Out-Null
foreach ($n in $names) { $f = Join-Path $DataFolder $n; if (Test-Path $f) { Move-Item $f $replaced } }
foreach ($n in $names) { $f = Join-Path $Restore $n; if (Test-Path $f) { Copy-Item $f $DataFolder } }
Write-Output "Restored from $Restore. The files it replaced are in $replaced."
exit 0
