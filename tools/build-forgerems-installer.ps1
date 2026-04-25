<#
.SYNOPSIS
Builds the portable publish output, stages a bundled backend, and compiles the
Inno Setup installer.

.DESCRIPTION
This helper keeps the installer workflow reproducible:
1. publish the self-contained single-file win-x64 frontend
2. stage a version-matched bundled backend from a verified release bundle
3. compile installer\ForgerEMS.iss with Inno Setup

.PARAMETER Version
Installer/app version. Defaults to the WPF project version.

.PARAMETER SkipPublish
Skip dotnet publish and reuse the existing publish output.

.PARAMETER ReleaseBundleRoot
Optional explicit release-bundle root to stage into the installer. When omitted,
the newest verified folder under ..\release\ventoy-core\ is used.

.EXAMPLE
.\tools\build-forgerems-installer.ps1

.EXAMPLE
.\tools\build-forgerems-installer.ps1 -SkipPublish
#>

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipPublish,
    [string]$ReleaseBundleRoot = ""
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $repoRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
$publishDir = Join-Path $repoRoot "src\ForgerEMS.Wpf\bin\Release\net8.0-windows\win-x64\publish"
$issPath = Join-Path $repoRoot "installer\ForgerEMS.iss"
$stageScriptPath = Join-Path $repoRoot "tools\stage-bundled-backend.ps1"
$backendStageRoot = Join-Path $repoRoot "dist\backend-stage\backend"
$outputDir = Join-Path $repoRoot "dist\installer"
$appExePath = Join-Path $publishDir "ForgerEMS.exe"

function Get-ProjectVersion {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$versionNode)) {
        throw "Could not read <Version> from $ProjectPath"
    }

    return [string]$versionNode
}

function Resolve-IsccPath {
    $candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6, then rerun this script."
}

if (-not (Test-Path -LiteralPath $csprojPath)) {
    throw "Project file not found: $csprojPath"
}

if (-not (Test-Path -LiteralPath $issPath)) {
    throw "Installer script not found: $issPath"
}

if (-not (Test-Path -LiteralPath $stageScriptPath)) {
    throw "Bundled backend stage script not found: $stageScriptPath"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -ProjectPath $csprojPath
}

if (-not $SkipPublish) {
    Write-Host "Publishing ForgerEMS..." -ForegroundColor Cyan
    dotnet publish $csprojPath -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:Version=$Version /p:InformationalVersion=$Version
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $appExePath)) {
    throw "Published executable not found: $appExePath"
}

Write-Host "Staging bundled backend..." -ForegroundColor Cyan
& $stageScriptPath `
    -FrontendVersion $Version `
    -ReleaseBundleRoot $ReleaseBundleRoot `
    -OutputRoot $backendStageRoot

if (-not (Test-Path -LiteralPath (Join-Path $backendStageRoot "Verify-VentoyCore.ps1"))) {
    throw "Bundled backend staging completed, but Verify-VentoyCore.ps1 was not found at $backendStageRoot"
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$isccPath = Resolve-IsccPath

Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $isccPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
    "/DBackendBundleDir=$backendStageRoot" `
    "/DOutputDir=$outputDir" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$expectedInstaller = Join-Path $outputDir ("ForgerEMS-Setup-v{0}.exe" -f $Version)
if (Test-Path -LiteralPath $expectedInstaller) {
    Write-Host "Installer ready: $expectedInstaller" -ForegroundColor Green
}
else {
    Write-Warning "Installer compilation completed, but the expected output file was not found: $expectedInstaller"
}
