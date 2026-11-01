# Writes a manifest that ties a published build to its commit: versions, the outcome of each CI step and a SHA-256 for each file.
# It holds hashes, paths and versions only. It always writes a manifest, so a failed run can be read too.
# It fails only when a published build cannot be traced to its commit, or when -RequireClean finds a changed working tree.
param(
    [Parameter(Mandatory)][string]$PublishPath,
    [Parameter(Mandatory)][string]$Runtime,
    [Parameter(Mandatory)][string]$OutFile,
    [string]$PackagePath,
    [switch]$RequireClean
)
$ErrorActionPreference = 'Stop'

function ToolVersion($tool) {
    try { return ((& $tool --version) | Select-Object -First 1).Trim() } catch { return 'unknown' }
}

$commit = (git rev-parse HEAD).Trim()
$changes = @(git status --porcelain)
$clean = $changes.Count -eq 0

# The step results come from the environment, because the quotes in JSON do not survive being passed as a process argument.
$outcomes = [ordered]@{}
$stepResults = if ($env:STEP_RESULTS) { $env:STEP_RESULTS } else { '{}' }
($stepResults | ConvertFrom-Json).PSObject.Properties | Sort-Object Name | ForEach-Object { $outcomes[$_.Name] = $_.Value.outcome }
$failed = @($outcomes.Keys | Where-Object { $outcomes[$_] -ne 'success' })

$files = @()
if (Test-Path $PublishPath) {
    $root = (Resolve-Path $PublishPath).Path.TrimEnd('\', '/')
    $files = @(Get-ChildItem $root -Recurse -File -Force | ForEach-Object {
        [pscustomobject]@{
            path   = $_.FullName.Substring($root.Length + 1).Replace('\', '/')
            bytes  = $_.Length
            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
}

# The build hash is the SHA-256 of the sorted file list, so it does not depend on the order files were found in.
$lines = [string[]]@($files | ForEach-Object { "$($_.sha256)  $($_.path)" })
[Array]::Sort($lines, [StringComparer]::Ordinal)
$buildHash = $null
if ($lines.Count -gt 0) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $buildHash = ([BitConverter]::ToString($sha.ComputeHash($bytes)) -replace '-', '').ToLowerInvariant()
}

$assembly = Join-Path $PublishPath 'PilotTrade.Api.dll'
$productVersion = if (Test-Path $assembly) { (Get-Item $assembly).VersionInfo.ProductVersion } else { $null }
$traceable = [bool]($productVersion -and $productVersion.Contains($commit))

$manifest = [ordered]@{
    commit            = $commit
    ref               = $env:GITHUB_REF
    ciRunId           = $env:GITHUB_RUN_ID
    workingTreeClean  = [bool]$clean
    os                = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    runtime           = $Runtime
    dotnetSdk         = ToolVersion 'dotnet'
    node              = ToolVersion 'node'
    productVersion    = $productVersion
    traceableToCommit = $traceable
    buildHash         = $buildHash
    packageSha256     = $(if ($PackagePath -and (Test-Path $PackagePath)) { (Get-FileHash $PackagePath -Algorithm SHA256).Hash.ToLowerInvariant() } else { $null })
    steps             = $outcomes
    failedSteps       = $failed
    allStepsPassed    = ($outcomes.Count -gt 0 -and $failed.Count -eq 0)
    files             = $files
}
$outDir = Split-Path $OutFile
if ($outDir) { New-Item -ItemType Directory -Force $outDir | Out-Null }
$manifest | ConvertTo-Json -Depth 5 | Set-Content $OutFile -Encoding utf8

if ($env:GITHUB_STEP_SUMMARY) {
    $summary = @("Commit ``$commit``, build hash ``$buildHash``", '', '| Step | Outcome |', '|---|---|')
    $summary += @($outcomes.Keys | ForEach-Object { "| $_ | $($outcomes[$_]) |" })
    $summary | Add-Content $env:GITHUB_STEP_SUMMARY -Encoding utf8
}

Write-Output "Manifest for $commit, $($files.Count) files, failed steps: $($failed.Count)."
if ($RequireClean -and -not $clean) { Write-Output 'The working tree has changes, so this build does not match its commit:'; $changes | ForEach-Object { Write-Output $_ }; exit 1 }
if ($outcomes['publish'] -eq 'success' -and -not $traceable) { Write-Output "The published version '$productVersion' does not name commit $commit."; exit 1 }
