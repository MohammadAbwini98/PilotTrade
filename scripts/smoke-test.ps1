# Runtime smoke test of a published build, in simulation mode with a throwaway database. No broker is contacted.
# -AnonymousOnly skips the owner flow, which needs the owner token: use it on a machine holding a real token.
param(
    [Parameter(Mandatory)][string]$Executable,
    [int]$Port = 5100,
    [switch]$AnonymousOnly
)
$ErrorActionPreference = 'Stop'
$base = "http://127.0.0.1:$Port"
$db = Join-Path ([IO.Path]::GetTempPath()) "pilottrade-smoke-$([guid]::NewGuid().ToString('N')).db"
$Executable = (Resolve-Path $Executable).Path
$workDir = Split-Path $Executable

$env:ConnectionStrings__Default = "Data Source=$db"
$env:BROKER_MODE = 'simulation'
$env:TRADING_ENVIRONMENT = 'demo'
$env:TRADING_ACCOUNT_ID = ''

function Check($name, [bool]$passed) {
    if (-not $passed) { throw "FAIL: $name" }
    Write-Output "PASS: $name"
}

function Status($uri, $headers = @{}) {
    try { return (Invoke-WebRequest -Uri $uri -Headers $headers -UseBasicParsing -TimeoutSec 15).StatusCode }
    catch [System.Net.WebException] { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } throw }
    catch { if ($_.Exception.Response) { return [int]$_.Exception.Response.StatusCode } throw }
}

$env:TRADING_ENVIRONMENT = 'live'
$live = Start-Process -FilePath $Executable -ArgumentList '--urls', "http://127.0.0.1:$($Port + 1)" -WorkingDirectory $workDir -PassThru -RedirectStandardOutput "$db.live.out" -RedirectStandardError "$db.live.err"
if (-not $live.WaitForExit(30000)) { $live.Kill(); throw 'FAIL: Live configuration did not stop startup' }
$liveRefused = $true
try { Invoke-WebRequest -Uri "http://127.0.0.1:$($Port + 1)/health" -UseBasicParsing -TimeoutSec 2 | Out-Null; $liveRefused = $false } catch { }
Check 'Live configuration stops startup' $liveRefused
$env:TRADING_ENVIRONMENT = 'demo'

$app = Start-Process -FilePath $Executable -ArgumentList '--urls', $base -WorkingDirectory $workDir -PassThru -RedirectStandardOutput "$db.out" -RedirectStandardError "$db.err"
try {
    $ready = $false
    foreach ($i in 1..30) {
        try { if ((Invoke-WebRequest -Uri "$base/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { $ready = $true; break } } catch { Start-Sleep -Seconds 1 }
    }
    Check '/health responds' $ready

    $index = Invoke-WebRequest -Uri "$base/" -UseBasicParsing -TimeoutSec 15
    Check 'UI is served' ($index.Content -match 'id="root"')
    $asset = [regex]::Match($index.Content, 'src="(/assets/[^"]+\.js)"').Groups[1].Value
    Check 'UI script is served' ($asset -and (Status "$base$asset") -eq 200)

    foreach ($path in '/api/runtime', '/api/accounts', '/api/demo/credential-status', '/api/security/csrf-token') {
        Check "$path refuses anonymous callers" ((Status "$base$path") -eq 403)
    }

    if (-not $AnonymousOnly) {
        $token = (& $Executable --show-owner-token | Select-Object -Last 1).Trim()
        $owner = @{ 'X-PilotTrade-Owner-Token' = $token }

        $runtime = Invoke-RestMethod -Uri "$base/api/runtime" -Headers $owner
        # The simulator fills orders in memory, so executionEnabled is true here. The Capital.com adapter's false is covered by SecurityEndpointTests.
        Check 'Runs on the simulator, not a broker' ($runtime.mode -eq 'Simulation')
        Check 'Live is locked' ([bool]$runtime.liveLocked)

        $accounts = @(Invoke-RestMethod -Uri "$base/api/accounts" -Headers $owner)
        $accountId = $accounts[0].accountId
        Check 'Accounts are listed' ([bool]$accountId)
        $found = @(Invoke-RestMethod -Uri "$base/api/instruments/search?q=&limit=5" -Headers $owner)
        Check 'Instruments are listed' ($found.Count -gt 0)

        function Guarded($path, $body) {
            $csrf = (Invoke-RestMethod -Uri "$base/api/security/csrf-token" -Headers $owner).csrfToken
            $headers = $owner + @{ 'X-PilotTrade-CSRF' = $csrf; 'Origin' = $base }
            Invoke-RestMethod -Method Post -Uri "$base$path" -Headers $headers -ContentType 'application/json' -Body ($body | ConvertTo-Json)
        }
        $selected = Guarded '/api/instruments/select' @{ accountId = $accountId; epic = $found[0].epic }
        Check 'Instrument can be selected' ($selected.selection.selectionVersion -ge 1)
        $outdated = Guarded '/api/market/snapshot' @{ accountId = $accountId; epic = $found[0].epic; selectionVersion = $selected.selection.selectionVersion + 1 }
        Check 'Outdated selection is NO_TRADE' ($outdated.outcome -eq 'NO_TRADE')
    }
}
finally {
    if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit(10000) | Out-Null }
    foreach ($suffix in '', '-wal', '-shm', '.out', '.err', '.live.out', '.live.err') { Remove-Item "$db$suffix" -ErrorAction SilentlyContinue }
}
