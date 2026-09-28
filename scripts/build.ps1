param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw '需要 Windows 10/11 的 .NET Framework 4.x。' }
if (-not $OutputPath) { $OutputPath = Join-Path $pluginRoot 'CodexConnectionMonitor.exe' }
$buildArguments = @('/nologo','/target:winexe','/platform:x64','/optimize+','/codepage:65001',"/out:$OutputPath",'/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Core.dll','/r:System.Web.Extensions.dll',"/win32manifest:$pluginRoot\src\app.manifest")
if (Test-Path -LiteralPath "$pluginRoot\assets\icon.ico") { $buildArguments += "/win32icon:$pluginRoot\assets\icon.ico" }
$buildArguments += (Get-ChildItem -LiteralPath "$pluginRoot\src" -Filter '*.cs').FullName
& $compilerPath @buildArguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Write-Output "Built: $OutputPath"
