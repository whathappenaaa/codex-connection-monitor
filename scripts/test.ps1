param([switch]$IncludeUi)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe = Join-Path $out 'CodexConnectionMonitor.exe'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath $exe
function Invoke-MonitorTest([string]$Mode, [string]$ResultPath) {
    $testProcess = Start-Process -FilePath $exe -ArgumentList @($Mode, ('"' + $ResultPath + '"')) -WindowStyle Hidden -PassThru
    if (-not $testProcess.WaitForExit(60000)) { $testProcess.Kill(); throw "$Mode timed out" }
    $testProcess.Refresh()
    if ($testProcess.ExitCode -ne 0) { throw "$Mode failed; inspect $ResultPath" }
}
$result = Join-Path $out 'self-test.txt'
Invoke-MonitorTest '--self-test' $result
Get-Content -LiteralPath $result
if ($IncludeUi) {
    $uiResult = Join-Path $out 'ui-test.txt'
    Invoke-MonitorTest '--ui-test' $uiResult
    Get-Content -LiteralPath $uiResult
    $layouts = Join-Path $out 'layouts'
    Invoke-MonitorTest '--layout-test' $layouts
    Get-Content -LiteralPath (Join-Path $layouts 'result.txt')
}
