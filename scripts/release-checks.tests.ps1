# Framework-free checks of the secret scan and the release manifest. Run them from the repository root.
$ErrorActionPreference = 'Stop'
$script:failures = 0
$scripts = $PSScriptRoot
$shell = (Get-Process -Id $PID).Path
$temp = Join-Path ([IO.Path]::GetTempPath()) "pilottrade-checks-$([guid]::NewGuid().ToString('N'))"

function Check($name, [bool]$passed) {
    if ($passed) { Write-Output "PASS: $name" } else { Write-Output "FAIL: $name"; $script:failures++ }
}

# Each script runs in its own process, because they end with exit codes.
function Run($script, [string[]]$arguments) {
    $output = & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts $script) @arguments
    return [pscustomobject]@{ Code = $LASTEXITCODE; Text = ($output -join "`n") }
}

function NewFolder($name, $files) {
    $path = Join-Path $temp $name
    New-Item -ItemType Directory -Force $path | Out-Null
    foreach ($file in $files.Keys) { Set-Content (Join-Path $path $file) $files[$file] -Encoding utf8 }
    return $path
}

try {
    $good = NewFolder 'good' @{ 'app.json' = '{"name":"pilot"}' }
    Check 'Scan passes on a harmless folder' ((Run 'secret-scan.ps1' @('-ExtraPaths', $good)).Code -eq 0)

    $evidence = NewFolder 'evidence' @{ 'demo-readonly-evidence-1.json' = '{}' }
    Check 'Scan refuses raw evidence' ((Run 'secret-scan.ps1' @('-ExtraPaths', $evidence)).Code -ne 0)

    # Built in two parts, so that this file does not hold text the scan refuses.
    $key = 'sk-ant-' + ('a' * 20)
    $results = NewFolder 'results' @{ 'run.trx' = "<Output>$key</Output>" }
    $found = Run 'secret-scan.ps1' @('-ExtraPaths', $results)
    Check 'Scan reads test results' ($found.Code -ne 0)
    Check 'Scan does not print what it found' (-not $found.Text.Contains($key))

    $env:STEP_RESULTS = '{"publish":{"outcome":"skipped"},"smoke":{"outcome":"failure"},"secret_scan":{"outcome":"success"}}'
    $out = Join-Path $temp 'failed\manifest.json'
    $run = Run 'release-manifest.ps1' @('-PublishPath', (Join-Path $temp 'missing'), '-Runtime', 'win-x64', '-OutFile', $out)
    Check 'A failed run still gets a manifest' ($run.Code -eq 0 -and (Test-Path $out))
    $failed = Get-Content $out -Raw | ConvertFrom-Json
    Check 'A failed run is not marked as passed' (-not $failed.allStepsPassed)
    Check 'The failed steps are named' (@($failed.failedSteps) -contains 'smoke' -and @($failed.failedSteps) -contains 'publish')
    Check 'The manifest names the commit' ($failed.commit -eq (git rev-parse HEAD).Trim())

    $env:STEP_RESULTS = ''
    $one = NewFolder 'one' @{ 'a.txt' = 'alpha'; 'b.txt' = 'beta' }
    $outOne = Join-Path $temp 'one.json'
    Run 'release-manifest.ps1' @('-PublishPath', $one, '-Runtime', 'win-x64', '-OutFile', $outOne) | Out-Null
    $first = Get-Content $outOne -Raw | ConvertFrom-Json
    Check 'No steps means not passed' (-not $first.allStepsPassed)
    Check 'Each file has a SHA-256' (@($first.files).Count -eq 2 -and @($first.files | Where-Object { $_.sha256 -notmatch '^[0-9a-f]{64}$' }).Count -eq 0)
    Run 'release-manifest.ps1' @('-PublishPath', $one, '-Runtime', 'win-x64', '-OutFile', $outOne) | Out-Null
    Check 'The build hash is repeatable' ((Get-Content $outOne -Raw | ConvertFrom-Json).buildHash -eq $first.buildHash)
    Set-Content (Join-Path $one 'b.txt') 'changed' -Encoding utf8
    Run 'release-manifest.ps1' @('-PublishPath', $one, '-Runtime', 'win-x64', '-OutFile', $outOne) | Out-Null
    Check 'A changed file changes the build hash' ((Get-Content $outOne -Raw | ConvertFrom-Json).buildHash -ne $first.buildHash)

    $untraceable = NewFolder 'untraceable' @{ 'PilotTrade.Api.dll' = 'not a real assembly' }
    $env:STEP_RESULTS = '{"publish":{"outcome":"success"}}'
    $refused = Run 'release-manifest.ps1' @('-PublishPath', $untraceable, '-Runtime', 'win-x64', '-OutFile', (Join-Path $temp 'u.json'))
    Check 'A build that does not name its commit fails' ($refused.Code -ne 0)

    $env:STEP_RESULTS = ''
    $publish = NewFolder 'publish' @{ 'PilotTrade.Api.exe' = 'app'; 'PilotTrade.Api.pdb' = 'symbols'; 'appsettings.Development.json' = '{}'; 'appsettings.json' = '{}'; 'createdump.exe' = 'tool'; 'web.config' = 'iis' }
    $packaged = Join-Path $temp 'package'
    Check 'Packaging succeeds' ((Run 'package-windows.ps1' @('-PublishPath', $publish, '-OutDir', $packaged)).Code -eq 0)
    $zip = Join-Path $packaged 'PilotTrade-win-x64.zip'
    $zipHash = (Get-FileHash $zip -Algorithm SHA256).Hash
    Run 'package-windows.ps1' @('-PublishPath', $publish, '-OutDir', $packaged) | Out-Null
    Check 'The same input gives the same ZIP' ((Get-FileHash $zip -Algorithm SHA256).Hash -eq $zipHash)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $unzipped = Join-Path $temp 'unzipped'
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, $unzipped)
    $folder = Join-Path $unzipped 'PilotTrade-win-x64'
    foreach ($name in 'PilotTrade.Api.exe', 'Start-PilotTrade.cmd', 'Show-Owner-Token.cmd', 'README.txt', 'appsettings.example.json', 'Verify-Package.ps1', 'Backup-Data.ps1', 'SHA256SUMS.txt') {
        Check "The package holds $name" (Test-Path (Join-Path $folder $name))
    }
    Check 'Debug symbols do not ship' (-not (Test-Path (Join-Path $folder 'PilotTrade.Api.pdb')))
    Check 'Development settings do not ship' (-not (Test-Path (Join-Path $folder 'appsettings.Development.json')))
    Check 'The crash-dump tool does not ship' (-not (Test-Path (Join-Path $folder 'createdump.exe')))
    Check 'IIS configuration does not ship' (-not (Test-Path (Join-Path $folder 'web.config')))
    Check 'An untouched package verifies' ((Run 'verify-package.ps1' @('-Folder', $folder)).Code -eq 0)
    Set-Content (Join-Path $folder 'appsettings.Production.json') '{}'
    Check 'The settings file a user adds does not fail verification' ((Run 'verify-package.ps1' @('-Folder', $folder)).Code -eq 0)
    Remove-Item (Join-Path $folder 'appsettings.Production.json')
    Set-Content (Join-Path $folder 'extra.db') 'x'
    Check 'An extra file fails verification' ((Run 'verify-package.ps1' @('-Folder', $folder)).Code -ne 0)
    Remove-Item (Join-Path $folder 'extra.db')
    Set-Content (Join-Path $folder 'PilotTrade.Api.exe') 'tampered'
    Check 'A changed file fails verification' ((Run 'verify-package.ps1' @('-Folder', $folder)).Code -ne 0)

    # The development audit policy, run on recorded audit output rather than the network.
    function AuditFile($name, $severity, $ghsa) {
        $json = '{"vulnerabilities":{"minimatch":{"via":[{"name":"minimatch","severity":"' + $severity + '","url":"https://github.com/advisories/' + $ghsa + '"}]},"eslint":{"via":["minimatch"]}}}'
        $path = Join-Path $temp "$name.json"
        Set-Content $path $json -Encoding utf8
        return $path
    }
    function ExceptionFile($name, $expires, $status, $approvedBy) {
        $by = if ($approvedBy) { '"' + $approvedBy + '"' } else { 'null' }
        $json = '{"exceptions":[{"advisory":"GHSA-g3hv-75wg-m249","package":"minimatch","expires":"' + $expires + '","status":"' + $status + '","approvedBy":' + $by + '}]}'
        $path = Join-Path $temp "$name.json"
        Set-Content $path $json -Encoding utf8
        return $path
    }
    $listed = AuditFile 'audit-listed' 'moderate' 'GHSA-g3hv-75wg-m249'
    $pendingEx = ExceptionFile 'ex-pending' '2027-02-04' 'pending-owner-approval' $null
    $pendingRun = Run 'npm-audit-policy.ps1' @('-AuditJson', $listed, '-ExceptionsPath', $pendingEx, '-Today', '2026-11-04')
    Check 'A pending exception does not fail the run' ($pendingRun.Code -eq 0)
    Check 'A pending exception is never reported as clean' ($pendingRun.Text -match 'not clean' -and $pendingRun.Text -match 'PENDING')
    Check 'An expired exception fails' ((Run 'npm-audit-policy.ps1' @('-AuditJson', $listed, '-ExceptionsPath', $pendingEx, '-Today', '2027-02-05')).Code -ne 0)
    $other = AuditFile 'audit-other' 'moderate' 'GHSA-0000-0000-0000'
    Check 'An advisory with no exception fails' ((Run 'npm-audit-policy.ps1' @('-AuditJson', $other, '-ExceptionsPath', $pendingEx, '-Today', '2026-11-04')).Code -ne 0)
    $high = AuditFile 'audit-high' 'high' 'GHSA-g3hv-75wg-m249'
    Check 'A high advisory fails even with an exception' ((Run 'npm-audit-policy.ps1' @('-AuditJson', $high, '-ExceptionsPath', $pendingEx, '-Today', '2026-11-04')).Code -ne 0)
    $approvedEx = ExceptionFile 'ex-approved' '2027-02-04' 'approved' 'owner'
    $approvedRun = Run 'npm-audit-policy.ps1' @('-AuditJson', $listed, '-ExceptionsPath', $approvedEx, '-Today', '2026-11-04')
    Check 'An approved exception passes and is still not clean' ($approvedRun.Code -eq 0 -and $approvedRun.Text -match 'owner-approved' -and $approvedRun.Text -notmatch 'PENDING')
    Set-Content (Join-Path $temp 'audit-none.json') '{"vulnerabilities":{}}' -Encoding utf8
    Check 'No advisories is clean' ((Run 'npm-audit-policy.ps1' @('-AuditJson', (Join-Path $temp 'audit-none.json'), '-ExceptionsPath', $pendingEx)).Text -match 'RESULT: clean')

    # Backup and restore of the local database, on a stand-in data folder.
    $data = Join-Path $temp 'data'
    New-Item -ItemType Directory -Force $data | Out-Null
    Check 'A backup with no database fails' ((Run 'backup-data.ps1' @('-DataFolder', $data)).Code -ne 0)
    Set-Content (Join-Path $data 'pilottrade.db') 'journal v1' -Encoding ascii
    Set-Content (Join-Path $data 'pilottrade.db-wal') 'wal v1' -Encoding ascii
    Check 'A backup succeeds' ((Run 'backup-data.ps1' @('-DataFolder', $data)).Code -eq 0)
    $backup = @(Get-ChildItem (Join-Path $data 'backups') -Directory)[0].FullName
    Check 'The backup holds the database, its log and checksums' ((Test-Path (Join-Path $backup 'pilottrade.db')) -and (Test-Path (Join-Path $backup 'pilottrade.db-wal')) -and (Test-Path (Join-Path $backup 'SHA256SUMS.txt')))
    Set-Content (Join-Path $data 'pilottrade.db') 'journal v2' -Encoding ascii
    Check 'A restore succeeds' ((Run 'backup-data.ps1' @('-DataFolder', $data, '-Restore', $backup)).Code -eq 0)
    Check 'A restore brings the backed-up database back' ((Get-Content (Join-Path $data 'pilottrade.db') -Raw).Trim() -eq 'journal v1')
    $kept = @(Get-ChildItem $data -Directory -Filter 'replaced-*')
    Check 'A restore keeps the files it replaced' ($kept.Count -eq 1 -and (Get-Content (Join-Path $kept[0].FullName 'pilottrade.db') -Raw).Trim() -eq 'journal v2')
    Set-Content (Join-Path $backup 'pilottrade.db') 'tampered' -Encoding ascii
    Set-Content (Join-Path $data 'pilottrade.db') 'journal v3' -Encoding ascii
    Check 'A changed backup is refused' ((Run 'backup-data.ps1' @('-DataFolder', $data, '-Restore', $backup)).Code -ne 0)
    Check 'A refused restore changes nothing' ((Get-Content (Join-Path $data 'pilottrade.db') -Raw).Trim() -eq 'journal v3')
}
finally {
    $env:STEP_RESULTS = ''
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

# The explicit exit 0 matters: the last check above leaves a non-zero $LASTEXITCODE, and GitHub Actions exits with it.
if ($script:failures -gt 0) { exit 1 }
exit 0
