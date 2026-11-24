param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src/SimpleVideoCompressor/SimpleVideoCompressor.csproj"
[xml]$xml = Get-Content -Path $csproj
$versionNode = $xml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1
$version = $versionNode.Version.Trim()

$artifactsDir = Join-Path $repoRoot "artifacts"
$stageDir = Join-Path $artifactsDir "portable-stage"
$appDir = Join-Path $stageDir "app"
$zipPath = Join-Path $artifactsDir "SimpleVideoCompressor-Portable-$version.zip"

if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

dotnet publish $csproj `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:DebugType=none `
    -p:DebugSymbols=false `
    -o $appDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$launcher = @"
@echo off
start "" "%~dp0app\SimpleVideoCompressor.exe"
"@
[System.IO.File]::WriteAllText((Join-Path $stageDir "启动极简视频压缩.bat"), $launcher, [System.Text.Encoding]::ASCII)

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $stageDir "*") -DestinationPath $zipPath -CompressionLevel Optimal
"Built $zipPath"
