# Fails on files that must never ship and on text shaped like a credential. This is a safety net for known shapes,
# not proof that a secret is absent. Paths are the tracked files plus any folders passed in, such as publish output.
param([string[]]$ExtraPaths = @())
$ErrorActionPreference = 'Stop'

$files = @(git ls-files | Where-Object { Test-Path $_ -PathType Leaf } | ForEach-Object { Get-Item $_ })
foreach ($path in $ExtraPaths) { $files += Get-ChildItem $path -Recurse -File }

$forbiddenName = '(^|[\\/])\.env$|\.db(-wal|-shm)?$|\.pfx$|\.pem$|\.key$|\.log$'
$textFile = '\.(cs|ts|tsx|js|css|html|json|md|yml|yaml|xml|csproj|config|txt|ps1|sh|env|example)$'
$patterns = @(
    'sk-ant-[A-Za-z0-9_-]{16,}',
    '-----BEGIN [A-Z ]*PRIVATE KEY-----',
    'gh[pousr]_[A-Za-z0-9]{30,}',
    '(?i)(X-CAP-API-KEY|X-SECURITY-TOKEN|\bCST)["'']?\s*[:=]\s*["''][A-Za-z0-9+/=_-]{16,}["'']',
    '(?i)(api_?key|api_?password|password)["'']?\s*[:=]\s*["''][A-Za-z0-9+/=_-]{16,}["'']'
)

$problems = @()
foreach ($file in $files) {
    if ($file.FullName -match $forbiddenName) { $problems += "forbidden file: $($file.FullName)"; continue }
    if ($file.Name -notmatch $textFile -or $file.Length -gt 5MB) { continue }
    $text = [IO.File]::ReadAllText($file.FullName)
    foreach ($pattern in $patterns) {
        # The match itself is never printed, so a failed scan cannot leak what it found.
        if ($text -match $pattern) { $problems += "credential-shaped text: $($file.FullName)"; break }
    }
}

if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Output $_ }; exit 1 }
Write-Output "No findings in $($files.Count) files."
