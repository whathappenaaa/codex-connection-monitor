param([string]$Version='1.3.0')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid version' }
& (Join-Path $PSScriptRoot 'test.ps1')
$package = Join-Path $root "artifacts\release\codex-connection-monitor-v$Version-win-x64-portable"
New-Item -ItemType Directory -Path $package -Force | Out-Null
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath (Join-Path $package 'CodexConnectionMonitor.exe')
$actual = (Get-Item -LiteralPath (Join-Path $package 'CodexConnectionMonitor.exe')).VersionInfo.FileVersion
if ($actual -ne "$Version.0") { throw "Assembly version $actual does not match $Version" }
foreach ($file in @('使用说明.md','LICENSE','README.en.md')) { Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package -Force }
$zip = "$package.zip"
# Package an explicit allowlist so running a prior build cannot publish settings or diagnostics.
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$stream = [IO.File]::Open($zip, [IO.FileMode]::Create)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in @('CodexConnectionMonitor.exe','使用说明.md','LICENSE','README.en.md')) {
        $entry = (Split-Path $package -Leaf) + '/' + $file
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $package $file), $entry, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose(); $stream.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path (Split-Path $zip) 'SHA256SUMS.txt'), ($hash + '  ' + (Split-Path $zip -Leaf) + "`n"), (New-Object Text.UTF8Encoding($false)))
Get-Item -LiteralPath $zip | Select-Object FullName,Length
