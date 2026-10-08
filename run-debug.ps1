#!/usr/bin/env pwsh
# run-debug.ps1 —— 开发运行（Debug）
#
# 用法： .\run-debug.ps1 <CLI 参数...>
# 例：
#   .\run-debug.ps1 火遮眼
#   .\run-debug.ps1 search 火遮眼
#   .\run-debug.ps1 download "D:\Videos" -l zh-CN --top-only
#
# 等价于在仓库根目录执行：
#   dotnet run --project src\IcedSub.Cli -- <CLI 参数...>
# 所有参数原样透传给 IcedSub，退出码同样透传。

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\IcedSub.Cli\IcedSub.Cli.csproj'

if (-not (Test-Path -LiteralPath $project)) {
    Write-Error "找不到项目文件：$project"
    exit 1
}

& dotnet run --project $project -- @args
exit $LASTEXITCODE
