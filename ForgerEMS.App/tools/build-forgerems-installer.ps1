<#
.SYNOPSIS
Builds the portable publish output and compiles the Inno Setup installer.

.DESCRIPTION
This helper keeps the installer workflow reproducible:
1. publish the self-contained single-file win-x64 frontend
2. compile installer\ForgerEMS.iss with Inno Setup

.PARAMETER Version
Installer/app version. Defaults to 1.0.0.

.PARAMETER SkipPublish
Skip dotnet publish and reuse the existing publish output.

.EXAMPLE
.\tools\build-forgerems-installer.ps1

.EXAMPLE
.\tools\build-forgerems-installer.ps1 -SkipPublish
#>

[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $repoRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
$publishDir = Join-Path $repoRoot "src\ForgerEMS.Wpf\bin\Release\net8.0-windows\win-x64\publish"
$issPath = Join-Path $repoRoot "installer\ForgerEMS.iss"
$outputDir = Join-Path $repoRoot "dist\installer"
$appExePath = Join-Path $publishDir "ForgerEMS.exe"

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

if (-not $SkipPublish) {
    Write-Host "Publishing ForgerEMS..." -ForegroundColor Cyan
    dotnet publish $csprojPath -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $appExePath)) {
    throw "Published executable not found: $appExePath"
}

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$isccPath = Resolve-IsccPath

Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $isccPath `
    "/DAppVersion=$Version" `
    "/DPublishDir=$publishDir" `
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
