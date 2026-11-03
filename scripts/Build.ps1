[CmdletBinding()]
param([switch]$Test)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$tokens = $null
$parseErrors = $null
[System.Management.Automation.Language.Parser]::ParseFile((Join-Path $PSScriptRoot 'Install-Components.ps1'), [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$outDir = Join-Path $projectRoot 'dist\TwinLink-1.3.1'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
& $compiler /nologo /utf8output /target:winexe /platform:anycpu /optimize+ /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.Web.Extensions.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll ('/out:' + (Join-Path $outDir 'TwinLink.exe')) $sources
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY.md') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\VALIDATION.md') -Destination $outDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\TwinLink.exe.config') -Destination $outDir -Force
New-Item -ItemType Directory -Path (Join-Path $outDir 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-Components.ps1') -Destination (Join-Path $outDir 'scripts') -Force
if ($Test) {
    $testDir = Join-Path $projectRoot 'tests\output'
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    & $compiler /nologo /utf8output /target:exe /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll /r:Microsoft.CSharp.dll /main:TwinLink.Tests ('/out:' + (Join-Path $testDir 'Tests.exe')) $sources (Join-Path $projectRoot 'tests\Tests.cs')
    if ($LASTEXITCODE -ne 0) { throw '测试编译失败。' }
    $env:TWINLINK_HOME = Join-Path $testDir 'profile'
    & (Join-Path $testDir 'Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw '测试失败。' }
    $render = Start-Process -FilePath (Join-Path $outDir 'TwinLink.exe') -ArgumentList @('--render-check', ('"' + (Join-Path $testDir 'window.png') + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($render.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $testDir 'window.png'))) { throw '界面渲染失败。' }
    Remove-Item Env:\TWINLINK_HOME
}
$zip = Join-Path $projectRoot 'dist\TwinLink-Windows.zip'
Compress-Archive -LiteralPath $outDir -DestinationPath $zip -Force
Write-Output "已生成：$outDir"
Write-Output "分发包：$zip"
