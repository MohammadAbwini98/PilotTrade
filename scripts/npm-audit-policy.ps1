# Checks development npm advisories against security/npm-audit-exceptions.json.
# Fails on a high or critical advisory, on one with no exception, and on an expired exception.
# An exception that the owner has not approved does not fail the run, but the result is never reported as clean.
param(
    [string]$WebPath = (Join-Path $PSScriptRoot '..\src\PilotTrade.Web'),
    [string]$ExceptionsPath = (Join-Path $PSScriptRoot '..\security\npm-audit-exceptions.json'),
    [string]$AuditJson,
    [string]$Today = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd')
)
$ErrorActionPreference = 'Stop'

if ($AuditJson) { $raw = Get-Content $AuditJson -Raw }
else {
    Push-Location $WebPath
    try { $raw = (& npm audit --json 2>$null) -join "`n" } finally { Pop-Location }
}
$audit = $raw | ConvertFrom-Json
$exceptions = @((Get-Content $ExceptionsPath -Raw | ConvertFrom-Json).exceptions)

# Each advisory once, keyed by its GHSA id; entries that only point at other packages add nothing new.
$advisories = @{}
$flagged = 0
if ($audit.vulnerabilities) {
    foreach ($entry in $audit.vulnerabilities.PSObject.Properties) {
        $flagged++
        foreach ($via in @($entry.Value.via)) {
            if ($via -is [string]) { continue }
            $id = ($via.url -split '/')[-1]
            $advisories[$id] = [pscustomobject]@{ Id = $id; Package = $via.name; Severity = $via.severity }
        }
    }
}

$failures = @()
$pending = @()
foreach ($a in $advisories.Values) {
    if ($a.Severity -in 'high', 'critical') { $failures += "$($a.Id) ($($a.Package)) is $($a.Severity); no exception can cover it"; continue }
    $ex = $exceptions | Where-Object { $_.advisory -eq $a.Id -and $_.package -eq $a.Package } | Select-Object -First 1
    if (-not $ex) { $failures += "$($a.Id) ($($a.Package), $($a.Severity)) has no exception"; continue }
    if ([string]::CompareOrdinal($Today, [string]$ex.expires) -gt 0) { $failures += "$($a.Id) ($($a.Package)): the exception expired on $($ex.expires)"; continue }
    if ($ex.status -ne 'approved' -or -not $ex.approvedBy) { $pending += "$($a.Id) ($($a.Package), $($a.Severity)): exception pending owner approval, expires $($ex.expires)" }
}

Write-Output "npm audit (development): $flagged package(s) flagged, $($advisories.Count) distinct advisory(ies)"
foreach ($f in $failures) { Write-Output "FAIL: $f" }
foreach ($p in $pending) { Write-Output "PENDING: $p"; if ($env:GITHUB_ACTIONS) { Write-Output "::warning::$p" } }
if ($failures.Count -gt 0) { exit 1 }
if ($advisories.Count -eq 0) { Write-Output 'RESULT: clean' }
elseif ($pending.Count -gt 0) { Write-Output 'RESULT: not clean; covered by exceptions that still need owner approval' }
else { Write-Output 'RESULT: not clean; covered by owner-approved exceptions' }
exit 0
