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
}
finally {
    $env:STEP_RESULTS = ''
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}

# The explicit exit 0 matters: the last check above leaves a non-zero $LASTEXITCODE, and GitHub Actions exits with it.
if ($script:failures -gt 0) { exit 1 }
exit 0
