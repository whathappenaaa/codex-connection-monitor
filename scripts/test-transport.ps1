$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'artifacts\transport-tests'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe = Join-Path $out 'CodexConnectionMonitor.exe'
& (Join-Path $PSScriptRoot 'build.ps1') -OutputPath $exe
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$stub = Join-Path $out 'codex.exe'
& $compiler /nologo /target:exe /r:System.Web.Extensions.dll "/out:$stub" (Join-Path $root 'tests\StubCodex.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture compilation failed' }
foreach ($case in @('success','login','unsupported','timeout')) {
    $resultPath = Join-Path $out "$case.json"
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $exe
    $info.Arguments = '--usage-check "' + $resultPath + '"'
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.EnvironmentVariables['PATH'] = $out + ';' + $env:PATH
    $info.EnvironmentVariables['MONITOR_TEST_CASE'] = $case
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $testProcess = [Diagnostics.Process]::Start($info)
    if (-not $testProcess.WaitForExit(15000)) { $testProcess.Kill(); throw "Transport $case exceeded 15s" }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $expected = if ($case -eq 'success') { '' } else { $case }
    if ($result.status -ne $expected) { throw "Transport $case returned $($result.status)" }
    if ($case -eq 'success' -and ($result.windows[0].Remaining -ne 75 -or $testProcess.ExitCode -ne 0)) { throw 'Wrong success result' }
    if ($case -ne 'success' -and $testProcess.ExitCode -eq 0) { throw 'Failure not reflected in exit code' }
    $children = Get-Process -Name codex -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $stub }
    if ($children) { throw 'Owned helper process was left running' }
    Write-Output "PASS transport $case ($([Math]::Round($timer.Elapsed.TotalSeconds,1))s); no child left running"
}
