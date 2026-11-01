# Checks an unzipped package against its SHA256SUMS.txt: every listed file must match, and nothing extra may be present.
param([string]$Folder = $PSScriptRoot)
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $Folder).Path.TrimEnd('\', '/')
$sums = Join-Path $root 'SHA256SUMS.txt'
if (-not (Test-Path $sums)) { Write-Output 'SHA256SUMS.txt is missing.'; exit 1 }

$problems = @()
$listed = @{}
foreach ($line in Get-Content $sums) {
    $hash, $path = $line -split '  ', 2
    $listed[$path] = $true
    $file = Join-Path $root $path
    if (-not (Test-Path $file -PathType Leaf)) { $problems += "missing: $path"; continue }
    if ((Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hash) { $problems += "changed: $path" }
}
foreach ($file in Get-ChildItem $root -Recurse -File -Force) {
    $path = $file.FullName.Substring($root.Length + 1).Replace('\', '/')
    # appsettings.Production.json is the one file a user is told to add, so it is named and not treated as tampering.
    if ($path -eq 'appsettings.Production.json') { Write-Output 'Not part of the package: appsettings.Production.json (your settings)'; continue }
    if ($path -ne 'SHA256SUMS.txt' -and -not $listed.ContainsKey($path)) { $problems += "extra: $path" }
}

if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Output $_ }; exit 1 }
Write-Output "Package verified: $($listed.Count) files match."
exit 0
