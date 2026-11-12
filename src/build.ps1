<#
.SYNOPSIS
    Minecraft 統合版 実績維持・統合リソースパック オフラインビルド＆検証スクリプト
.DESCRIPTION
    .NET 10 標準ライブラリ (C#) のみで構成された RpBuilder をオフライン実行し、
    テクスチャアトラス生成・3Dジオメトリ生成・整合性/数式検証・.mcpackパッケージングを一括実行します。
    外部ネットワーク通信およびサードパーティパッケージ取得は一切行いません。
#>
[CmdletBinding()]
param(
    [ValidateSet('all', 'atlases', 'geometries', 'validate', 'pack', 'report')]
    [string]$Mode = 'all'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# .NET CLI のテレメトリ通信・初回キャッシュ展開を完全無効化
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:NUGET_XMLDOC_MODE = 'skip'

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot 'tools\RpBuilder\RpBuilder.csproj'
$nugetConfig = Join-Path $repoRoot 'tools\NuGet.Config'

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "RpBuilder project file not found: $projectPath"
}

Write-Host "[build.ps1] Restoring & building RpBuilder (.NET 10 BCL ONLY, Offline Mode)..." -ForegroundColor Cyan
& dotnet build $projectPath -c Release --nologo --configfile $nugetConfig
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

Write-Host "[build.ps1] Running RpBuilder ($Mode)..." -ForegroundColor Cyan
& dotnet run --project $projectPath -c Release --no-build -- $Mode
if ($LASTEXITCODE -ne 0) {
    throw "RpBuilder ($Mode) failed with exit code $LASTEXITCODE"
}

Write-Host "[build.ps1] Completed successfully." -ForegroundColor Green
