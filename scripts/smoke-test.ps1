# Runtime smoke test of a published build, in simulation mode with a throwaway database. No broker is contacted.
# -AnonymousOnly skips the owner flow, which needs the owner token: use it on a machine holding a real token.
param(
    [Parameter(Mandatory)][string]$Executable,
    [int]$Port = 5100,
    [switch]$AnonymousOnly
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'smoke-helpers.ps1')
$base ="http://127.0.0.1:$Port"
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

        # Web errors are reported through Format-HttpFailure, so that a failure cannot print a header or a token.
        function Send($method, $path, $headers, $json) {
            $request = @{ Method = $method; Uri = "$base$path"; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 15 }
            if ($json) { $request.ContentType = 'application/json'; $request.Body = $json }
            try {
                $response = Invoke-WebRequest @request
                return [pscustomobject]@{ Status = [int]$response.StatusCode; Body = [string]$response.Content }
            }
            catch {
                if (-not $_.Exception.Response) { throw "FAIL: $method $($path -replace '\?.*$', '') got no response" }
                $text = if ($_.ErrorDetails) { $_.ErrorDetails.Message } else { '' }
                return [pscustomobject]@{ Status = [int]$_.Exception.Response.StatusCode; Body = [string]$text }
            }
        }
        function Read($path) {
            $answer = Send 'Get' $path $owner $null
            if ($answer.Status -ne 200) { throw "FAIL: $(Format-HttpFailure 'GET' $path $answer.Status $answer.Body)" }
            return $answer.Body | ConvertFrom-Json
        }
        function Guarded($path, $json) {
            $headers = $owner + @{ 'X-PilotTrade-CSRF' = (Read '/api/security/csrf-token').csrfToken; 'Origin' = $base }
            return Send 'Post' $path $headers $json
        }
        function GuardedOk($path, $json) {
            $answer = Guarded $path $json
            if ($answer.Status -ne 200) { throw "FAIL: $(Format-HttpFailure 'POST' $path $answer.Status $answer.Body)" }
            return $answer.Body | ConvertFrom-Json
        }

        $runtime = Read '/api/runtime'
        # The simulator fills orders in memory, so executionEnabled is true here. The Capital.com adapter's false is covered by SecurityEndpointTests.
        Check 'Runs on the simulator, not a broker' ($runtime.mode -eq 'Simulation')
        Check 'Live is locked' ([bool]$runtime.liveLocked)

        $accounts = @(ConvertTo-FlatList -InputObject (Read '/api/accounts'))
        Check 'Accounts are listed' ($accounts.Count -gt 0)
        $accountId = $accounts[0].accountId
        $found = @(ConvertTo-FlatList -InputObject (Read '/api/instruments/search?q=&limit=5'))
        Check 'Instruments are listed' ($found.Count -gt 0)
        $epic = $found[0].epic
        $selectBody = New-SelectionBody $accountId $epic

        $noCsrf = Send 'Post' '/api/instruments/select' ($owner + @{ 'Origin' = $base }) $selectBody
        Check 'Select without a CSRF token is refused' ($noCsrf.Status -eq 403)
        $listBody = (@{ accountId = $accountId; epic = @($epic, $epic) } | ConvertTo-Json -Compress)
        Check 'Select with a list as the epic is a 400' ((Guarded '/api/instruments/select' $listBody).Status -eq 400)

        $selected = GuardedOk '/api/instruments/select' $selectBody
        Check 'Instrument can be selected' ($selected.selection.selectionVersion -ge 1)
        $snapshotBody = (@{ accountId = $accountId; epic = $epic; selectionVersion = $selected.selection.selectionVersion + 1 } | ConvertTo-Json -Compress)
        $outdated = GuardedOk '/api/market/snapshot' $snapshotBody
        Check 'Outdated selection is NO_TRADE' ($outdated.outcome -eq 'NO_TRADE')

        # A point-in-time check only. That the simulator sends nothing to a broker rests on the mode check above.
        $remote = @(Get-NetTCPConnection -OwningProcess $app.Id -State Established -ErrorAction SilentlyContinue | Where-Object { $_.RemoteAddress -notin '127.0.0.1', '::1' })
        Check 'No connection outside this machine is open' ($remote.Count -eq 0)

        $app.Kill(); $app.WaitForExit(10000) | Out-Null
        $logs = (Get-Content "$db.out", "$db.err" -Raw -ErrorAction SilentlyContinue) -join ''
        Check 'Owner token is not in the app logs' (-not $logs.Contains($token))
        # Read as Latin-1, so that binaries are searched byte for byte. This finds the token as plain text only.
        $latin1 = [Text.Encoding]::GetEncoding(28591)
        $holding = @(Get-ChildItem $workDir -Recurse -File -Force | Where-Object { [IO.File]::ReadAllText($_.FullName, $latin1).Contains($token) })
        Check 'Owner token is not in the published files' ($holding.Count -eq 0)
    }
}
finally {
    if (-not $app.HasExited) { $app.Kill(); $app.WaitForExit(10000) | Out-Null }
    foreach ($suffix in '', '-wal', '-shm', '.out', '.err', '.live.out', '.live.err') { Remove-Item "$db$suffix" -ErrorAction SilentlyContinue }
}
