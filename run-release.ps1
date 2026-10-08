#!/usr/bin/env pwsh
# run-release.ps1 —— 运行已发布（Release）exe
#
# 用法： .\run-release.ps1 <CLI 参数...>
# 例：
#   .\run-release.ps1 火遮眼
#   .\run-release.ps1 download "D:\Videos" -l zh-CN --top-only
#
# 运行 publish\win-x64\IcedSub.exe；若该 exe 不存在，则先按 README 的参数
# 执行一次 publish。所有参数与退出码原样透传。

$ErrorActionPreference = 'Stop'

$outDir = Join-Path $PSScriptRoot 'publish\win-x64'
$exe = Join-Path $outDir 'IcedSub.exe'
$project = Join-Path $PSScriptRoot 'src\IcedSub.Cli\IcedSub.Cli.csproj'

if (-not (Test-Path -LiteralPath $exe)) {
    Write-Host "未找到 $exe，先执行 publish ..."
    & dotnet publish $project `
        -c Release -r win-x64 `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $outDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& $exe @args
exit $LASTEXITCODE
