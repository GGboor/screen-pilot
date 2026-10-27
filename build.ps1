param([string]$OutputName = '屏幕管家.exe')
$ErrorActionPreference = 'Stop'
$taskDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 的 .NET Framework 4.x C# 编译器。' }
& $compiler /nologo /target:exe /r:System.Drawing.dll "/out:$taskDir\BuildIcon.exe" (Join-Path $taskDir 'BuildIcon.cs')
if ($LASTEXITCODE -ne 0) { throw '图标构建工具编译失败' }
& (Join-Path $taskDir 'BuildIcon.exe') $taskDir
if ($LASTEXITCODE -ne 0) { throw '图标构建失败' }
$sources = @('NativeDisplay.cs','Recovery.cs','ScreenPilot.cs','RoundedControls.cs','MonitorLayoutView.cs','WindowRecovery.cs','LayoutMemory.cs','AssemblyInfo.cs') | ForEach-Object { Join-Path $taskDir $_ }
& $compiler /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/win32icon:$taskDir\AppIcon.ico" "/resource:$taskDir\AppIcon.ico,ScreenPilot.AppIcon.ico" "/out:$taskDir\$OutputName" $sources
if ($LASTEXITCODE -ne 0) { throw '程序编译失败' }
& $compiler /nologo /codepage:65001 /target:exe /platform:x64 /main:ScreenPilot.Tests /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll "/resource:$taskDir\AppIcon.ico,ScreenPilot.AppIcon.ico" "/out:$taskDir\ScreenPilot.Tests.exe" $sources (Join-Path $taskDir 'Tests.cs')
if ($LASTEXITCODE -ne 0) { throw '测试程序编译失败' }
Write-Output "已生成 $taskDir\$OutputName"
