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
if (-not (Test-Path -LiteralPath (Join-Path $stageDir "app\SimpleVideoCompressor.exe"))) {
    & (Join-Path $PSScriptRoot "build-portable.ps1") -Configuration $Configuration
}

$iscc = Get-Command "iscc.exe" -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
if (-not $iscc) {
    $candidates = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe"
    )
    $iscc = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $iscc) {
    throw "Inno Setup compiler (ISCC.exe) not found."
}

$issPath = Join-Path $repoRoot "packaging\installer\SimpleVideoCompressor.iss"
$outBase = "SimpleVideoCompressor-Setup-v$version"

& $iscc `
    "/DMyAppVersion=$version" `
    "/DMyStageDir=$stageDir" `
    "/DMyOutputDir=$artifactsDir" `
    "/DMyOutputBaseFilename=$outBase" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

$setupPath = Join-Path $artifactsDir "$outBase.exe"
"Built $setupPath ($((Get-Item -LiteralPath $setupPath).Length) bytes)"
