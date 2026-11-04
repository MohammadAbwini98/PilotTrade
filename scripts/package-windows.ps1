# Builds the portable ZIP from publish output. The same input gives the same ZIP: entries are sorted and carry a fixed time.
param(
    [Parameter(Mandatory)][string]$PublishPath,
    [Parameter(Mandatory)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$name = 'PilotTrade-win-x64'
$repo = Split-Path $PSScriptRoot
$publish = (Resolve-Path $PublishPath).Path.TrimEnd('\', '/')
New-Item -ItemType Directory -Force $OutDir | Out-Null
$out = (Resolve-Path $OutDir).Path
$stage = Join-Path $out $name
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }

# Debug symbols, development settings, the crash-dump tool and IIS configuration do not ship.
New-Item -ItemType Directory $stage | Out-Null
Copy-Item (Join-Path $publish '*') $stage -Recurse -Force
Get-ChildItem $stage -Recurse -File -Force | Where-Object { $_.Name -like '*.pdb' -or $_.Name -in 'appsettings.Development.json', 'createdump.exe', 'web.config' } | Remove-Item -Force
Copy-Item (Join-Path $repo 'packaging\windows\*') $stage
Copy-Item (Join-Path $PSScriptRoot 'verify-package.ps1') (Join-Path $stage 'Verify-Package.ps1')
Copy-Item (Join-Path $PSScriptRoot 'backup-data.ps1') (Join-Path $stage 'Backup-Data.ps1')

function RelativePaths {
    $found = [string[]]@(Get-ChildItem $stage -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($stage.Length + 1).Replace('\', '/') })
    [Array]::Sort($found, [StringComparer]::Ordinal)
    return $found
}

$sums = @(RelativePaths | ForEach-Object { "$((Get-FileHash (Join-Path $stage $_) -Algorithm SHA256).Hash.ToLowerInvariant())  $_" })
[IO.File]::WriteAllText((Join-Path $stage 'SHA256SUMS.txt'), (($sums -join "`n") + "`n"))

$zipPath = Join-Path $out "$name.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$fixedTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($path in RelativePaths) {
        $entry = $zip.CreateEntry("$name/$path", [IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixedTime
        $target = $entry.Open()
        $source = [IO.File]::OpenRead((Join-Path $stage $path))
        try { $source.CopyTo($target) } finally { $source.Dispose(); $target.Dispose() }
    }
}
finally { $zip.Dispose() }

$zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zipPath.sha256", "$zipHash  $name.zip`n")
Write-Output "$name.zip $zipHash"
