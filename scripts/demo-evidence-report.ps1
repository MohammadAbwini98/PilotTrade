# Checks that Demo read-only evidence belongs to one clean commit and writes a sanitized report that can be shared.
# Raw evidence stays local. Only values on the allowlist below reach the report, so an unexpected field is withheld, not copied.
# A closed market is reported as blocked, never as a pass. Exit codes: 0 checks passed, 1 a check failed, 2 bad usage.
param(
    [Parameter(Mandatory)][string]$Sha,
    [string[]]$Instruments = @('btcusd', 'eurusd', 'gold'),
    [string]$EvidenceDir = 'docs/phase3',
    [string]$OutputDir = 'docs/phase3'
)
$ErrorActionPreference = 'Stop'

$output = Join-Path $OutputDir "DEMO_EVIDENCE_SANITIZED_$Sha.md"
if (Test-Path $output) { Write-Output "Report already exists and is never replaced: $output"; exit 2 }
$fullSha = (git rev-parse --verify "$Sha^{commit}" 2>$null)
if (-not $fullSha) { Write-Output "Unknown commit: $Sha"; exit 2 }

$sensitive = [ordered]@{
    'API key or token value' = 'sk-ant-[A-Za-z0-9_-]{16,}|gh[pousr]_[A-Za-z0-9]{30,}|eyJ[A-Za-z0-9_-]{10,}\.'
    'Session or credential header' = '(?i)X-CAP-API-KEY|X-SECURITY-TOKEN|\bCST\b|\bBearer\b|\bAuthorization\b|\bpassword\b|\bapi_?key\b'
    'Email address' = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}'
    'Home folder path' = '(?i)[A-Z]:(\\\\?|/)Users(\\\\?|/)|/home/[a-z]|/Users/[a-z]'
    'Long number that could be an account ID' = '(?<![0-9a-fA-F.])\d{8,}(?![0-9a-fA-F])'
    'Unmasked identifier' = '(?i)\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b'
}
$maskedAccount = '\*{4}\d{4}'
$allowedKeys = 'marketStatus|ageSeconds|fresh|pricesValid|spreadPercent|spreadLimitPercent|spreadWithinPolicy|health|latestCloseAgeSeconds|ordered|valuesValid|enoughHistory|missingBars|instrumentName|assetClass|status|precision|currencyPresent|dealingRulesValid|availablePresent|equityProven|marginUsedPresent|realizedPnLPresent|activeAccountMatched|foundEpic'

# Each part of a step's detail is kept only if it has a known shape. Counts of accounts and holdings are private, so they are withheld.
function Sanitize([string]$detail) {
    $parts = foreach ($part in ($detail -split ';\s*')) {
        if ($part -match "^($allowedKeys)=[A-Za-z0-9 ./_-]{1,40}$") { $part }
        elseif ($part -match '^\d+ (candle|result)\(s\)$|^(5m|1h)=\d+ closed candle\(s\)$|^none closed$|^no quote$|^Not measured$') { $part }
        elseif ($part -match '^\d+ (account|position|working order)\(s\)$') { 'count withheld' }
        elseif ($part -match '^(Authentication|RateLimited|Unavailable|MalformedResponse|AccountNotFound|AccountMismatch|NotFound|InvalidRequest|CredentialsMissing|ExecutionDisabled)$') { $part }
        else { '(withheld)' }
    }
    return ($parts -join '; ')
}

$problems = New-Object System.Collections.ArrayList
$findings = @{}
$rows = New-Object System.Collections.ArrayList
$sections = New-Object System.Collections.ArrayList
$masked = 0

foreach ($instrument in $Instruments) {
    $name = "demo-readonly-evidence-$instrument-$Sha.json"
    $path = Join-Path $EvidenceDir $name
    if (-not (Test-Path $path)) {
        [void]$problems.Add("$($name): no evidence file")
        [void]$rows.Add("| $($instrument.ToUpperInvariant()) | none | none | no evidence | NOT RUN |")
        continue
    }

    $raw = [IO.File]::ReadAllText((Resolve-Path $path).Path)
    foreach ($category in $sensitive.Keys) {
        # Categories and file names only, so a failed scan cannot leak what it found.
        if ($raw -match $sensitive[$category]) { $findings["$category in $name"] = $true }
    }
    if ($raw -match $maskedAccount) { $masked++ }

    $e = $raw | ConvertFrom-Json
    if ($e.GitSha -ne $fullSha) { [void]$problems.Add("$($name): recorded commit differs from $Sha") }
    if ($e.WorkingTree -ne 'clean') { [void]$problems.Add("$($name): built from a working tree recorded as $($e.WorkingTree)") }
    if ($e.Environment -ne 'Demo') { [void]$problems.Add("$($name): environment is not Demo") }
    if ($e.Adapter -ne 'CapitalComBrokerAdapter') { [void]$problems.Add("$($name): not the Capital.com adapter") }
    if ($e.ApiHost -notmatch '^demo-api') { [void]$problems.Add("$($name): API host is not the Demo host") }
    if ($e.OrderEndpointsCalled -ne $false) { [void]$problems.Add("$($name): order endpoints were called") }
    if ($e.InstrumentEpic -ne $e.RequestedEpic) { [void]$problems.Add("$($name): instrument differs from the one requested") }

    $metadata = @($e.Steps | Where-Object Name -eq 'instrumentMetadata')[0]
    $status = if ($metadata -and $metadata.Detail -match '(?:^|; )status=(\w+)') { $Matches[1] } else { 'unknown' }
    $result = if ($status -ne 'Open') { 'BLOCKED: market not open, freshness not tested' }
              elseif ($e.AllPassed) { 'PASS' }
              else { 'FAIL' }
    $failed = @($e.Steps | Where-Object { -not $_.Passed } | ForEach-Object Name) -join ', '
    if (-not $failed) { $failed = 'none' }
    [void]$rows.Add("| $($e.RequestedEpic) | $($e.RunUtc) | $status | $failed | $result |")

    [void]$sections.Add("## $($e.RequestedEpic)")
    [void]$sections.Add('')
    [void]$sections.Add("Run $($e.RunUtc). Commit $($e.GitSha), working tree $($e.WorkingTree). Account source $($e.AccountSource). Host $($e.ApiHost). Order endpoints called: $($e.OrderEndpointsCalled).")
    [void]$sections.Add('')
    [void]$sections.Add('| Step | Passed | Sanitized detail |')
    [void]$sections.Add('| --- | --- | --- |')
    foreach ($step in $e.Steps) { [void]$sections.Add("| $($step.Name) | $($step.Passed) | $(Sanitize $step.Detail) |") }
    [void]$sections.Add('')
}

$changed = @(git diff --name-only $Sha HEAD | Where-Object { $_ -notmatch '^docs/' })
if ($changed.Count -gt 0) { [void]$problems.Add("HEAD differs from $Sha outside docs/ in $($changed.Count) file(s)") }
if (git status --porcelain) { [void]$problems.Add('Working tree is not clean') }
foreach ($finding in $findings.Keys) { [void]$problems.Add("Sensitive shape: $finding") }

$lines = New-Object System.Collections.ArrayList
[void]$lines.Add("# Sanitized Demo evidence for $Sha")
[void]$lines.Add('')
[void]$lines.Add('Generated by `scripts/demo-evidence-report.ps1`. Raw evidence stays local and is ignored by git. Account digits, account and holdings counts, balances, prices, tokens and personal details are not in this report. PASS needs the broker to report the market Open and every step to pass.')
[void]$lines.Add('')
[void]$lines.Add('| Instrument | Run (UTC) | Broker status | Failed steps | Result |')
[void]$lines.Add('| --- | --- | --- | --- | --- |')
foreach ($row in $rows) { [void]$lines.Add($row) }
[void]$lines.Add('')
[void]$lines.Add('## Checks')
[void]$lines.Add('')
if ($problems.Count -eq 0) {
    [void]$lines.Add("- Every file records commit $fullSha and a clean working tree, and the same code is at HEAD.")
    [void]$lines.Add('- Every file records the Demo environment, the Capital.com adapter, the Demo host and no order endpoint calls.')
    [void]$lines.Add('- The scan found no credential, token, session header, email address, home path or long account-like number. It covers known shapes only, so it is a safety net, not proof of absence.')
}
else { foreach ($problem in $problems) { [void]$lines.Add("- FAILED: $problem") } }
[void]$lines.Add("- Masked account digits were present in $masked raw file(s) and were left out of this report.")
[void]$lines.Add('')
foreach ($section in $sections) { [void]$lines.Add($section) }
$text = ($lines -join "`n").TrimEnd() + "`n"

if ($text -match $maskedAccount) { Write-Output 'Refusing to write a report containing masked account digits.'; exit 1 }
foreach ($category in $sensitive.Keys) {
    # Commit hashes are hexadecimal, and the guidance sentences above name the categories in words, not shapes.
    if ($category -ne 'Session or credential header' -and $text -match $sensitive[$category]) { Write-Output "Refusing to write a report containing: $category"; exit 1 }
}

$stream = New-Object IO.FileStream($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($output), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
$writer = New-Object IO.StreamWriter($stream, (New-Object Text.UTF8Encoding($false)))
try { $writer.Write($text) } finally { $writer.Dispose() }

Write-Output "Wrote $output"
foreach ($row in $rows) { Write-Output $row }
if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Output "FAILED: $_" }; exit 1 }
Write-Output 'All checks passed.'
