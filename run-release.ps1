#!/usr/bin/env pwsh
# run-release.ps1 —— 运行已发布（Release）exe
#
# 用法： .\run-release.ps1 <CLI 参数...>
# 例：
#   .\run-release.ps1 火遮眼
#   .\run-release.ps1 download "D:\Videos" -l zh-CN --top-only
#
# 运行 publish\win-x64\IcedSub.exe。若该 exe 不存在，或 src 下有源码/配置比 exe 新，
# 则先按 README 的参数重新 publish。所有参数与退出码原样透传。

$ErrorActionPreference = 'Stop'

$outDir = Join-Path $PSScriptRoot 'publish\win-x64'
$exe = Join-Path $outDir 'IcedSub.exe'
$project = Join-Path $PSScriptRoot 'src\IcedSub.Cli\IcedSub.Cli.csproj'

$needsPublish = -not (Test-Path -LiteralPath $exe)
$reason = "未找到 $exe"

if (-not $needsPublish) {
    $exeTime = (Get-Item -LiteralPath $exe).LastWriteTimeUtc
    $newer = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Recurse -File `
                -Include *.cs,*.csproj,*.json -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTimeUtc -gt $exeTime } |
             Select-Object -First 1
    if ($newer) {
        $needsPublish = $true
        $reason = "源码有更新（$($newer.Name)）"
    }
}

if ($needsPublish) {
    Write-Host "$reason，重新 publish ..."
    & dotnet publish $project `
        -c Release -r win-x64 `
        -p:DebugType=None -p:DebugSymbols=false `
        -o $outDir
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& $exe @args
exit $LASTEXITCODE
