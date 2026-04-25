<#
.SYNOPSIS
Stages a root-level PreRelease folder for manual installed-mode testing.

.DESCRIPTION
Creates a single repo-root PreRelease folder that isolates:
- the current installer executable and installer build sources
- the bundled backend script set used by the app in installed mode

This does not move or rewrite the canonical source locations. It stages a
fresh backend bundle and copies the current installer output into a predictable
test folder.

.PARAMETER OutputRoot
Optional destination root. Defaults to ..\PreRelease at the workspace root.

.PARAMETER BuildInstaller
Force a fresh installer build before staging. If omitted, an existing matching
installer is reused when available and built only if missing.

.PARAMETER ReleaseBundleRoot
Optional explicit release-bundle root passed through to backend staging and
installer build operations.
#>

[CmdletBinding()]
param(
    [string]$OutputRoot = "",
    [switch]$BuildInstaller,
    [string]$ReleaseBundleRoot = ""
)

$ErrorActionPreference = "Stop"

$appRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $appRoot
$defaultOutputRoot = Join-Path $workspaceRoot "PreRelease"
$csprojPath = Join-Path $appRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
$buildInstallerScriptPath = Join-Path $appRoot "tools\build-forgerems-installer.ps1"
$stageBackendScriptPath = Join-Path $appRoot "tools\stage-bundled-backend.ps1"
$installerScriptPath = Join-Path $appRoot "installer\ForgerEMS.iss"
$installedReadmePath = Join-Path $appRoot "installer\ForgerEMS-Installed-README.txt"
$installedModeDocPath = Join-Path $appRoot "docs\FORGEREMS-INSTALLED-MODE-V2.md"
$packagingDocPath = Join-Path $appRoot "docs\FORGEREMS-WPF-PACKAGING.md"
$backendStageRoot = Join-Path $appRoot "dist\backend-stage\backend"
$installerDistRoot = Join-Path $appRoot "dist\installer"

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Assert-ChildPath {
    param(
        [Parameter(Mandatory)][string]$Parent,
        [Parameter(Mandatory)][string]$Child
    )

    $normalizedParent = Get-NormalizedPath -Path $Parent
    $normalizedChild = Get-NormalizedPath -Path $Child

    if (($normalizedChild -ne $normalizedParent) -and -not $normalizedChild.StartsWith($normalizedParent + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes expected parent. Parent='$normalizedParent' Child='$normalizedChild'"
    }
}

function Ensure-Dir {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-ProjectVersion {
    param([Parameter(Mandatory)][string]$ProjectPath)

    [xml]$projectXml = Get-Content -LiteralPath $ProjectPath -Raw
    $versionNode = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$versionNode)) {
        throw "Could not read <Version> from $ProjectPath"
    }

    return [string]$versionNode
}

if (-not (Test-Path -LiteralPath $csprojPath)) {
    throw "Project file not found: $csprojPath"
}

if (-not (Test-Path -LiteralPath $buildInstallerScriptPath)) {
    throw "Installer build script not found: $buildInstallerScriptPath"
}

if (-not (Test-Path -LiteralPath $stageBackendScriptPath)) {
    throw "Backend stage script not found: $stageBackendScriptPath"
}

if (-not (Test-Path -LiteralPath $installerScriptPath)) {
    throw "Installer script not found: $installerScriptPath"
}

$version = Get-ProjectVersion -ProjectPath $csprojPath
$expectedInstallerPath = Join-Path $installerDistRoot ("ForgerEMS-Setup-v{0}.exe" -f $version)

Write-Host "Refreshing bundled backend stage..." -ForegroundColor Cyan
& $stageBackendScriptPath `
    -FrontendVersion $version `
    -ReleaseBundleRoot $ReleaseBundleRoot `
    -OutputRoot $backendStageRoot

if (-not (Test-Path -LiteralPath (Join-Path $backendStageRoot "Verify-VentoyCore.ps1"))) {
    throw "Bundled backend stage is missing Verify-VentoyCore.ps1 at $backendStageRoot"
}

if ($BuildInstaller -or -not (Test-Path -LiteralPath $expectedInstallerPath)) {
    Write-Host "Building installer..." -ForegroundColor Cyan
    & $buildInstallerScriptPath -Version $version -ReleaseBundleRoot $ReleaseBundleRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $expectedInstallerPath)) {
    throw "Installer executable not found: $expectedInstallerPath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $defaultOutputRoot
}

$OutputRoot = Get-NormalizedPath -Path $OutputRoot
Assert-ChildPath -Parent $workspaceRoot -Child $OutputRoot

Ensure-Dir -Path $OutputRoot
Get-ChildItem -LiteralPath $OutputRoot -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

$installerOutputRoot = Join-Path $OutputRoot "installer"
$backendOutputRoot = Join-Path $OutputRoot "backend"
$docsOutputRoot = Join-Path $OutputRoot "docs"

Ensure-Dir -Path $installerOutputRoot
Ensure-Dir -Path $backendOutputRoot
Ensure-Dir -Path $docsOutputRoot

Copy-Item -LiteralPath $expectedInstallerPath -Destination (Join-Path $installerOutputRoot (Split-Path -Leaf $expectedInstallerPath)) -Force
Copy-Item -LiteralPath $installerScriptPath -Destination (Join-Path $installerOutputRoot "ForgerEMS.iss") -Force
Copy-Item -LiteralPath $buildInstallerScriptPath -Destination (Join-Path $installerOutputRoot "build-forgerems-installer.ps1") -Force
Copy-Item -LiteralPath $stageBackendScriptPath -Destination (Join-Path $installerOutputRoot "stage-bundled-backend.ps1") -Force

Get-ChildItem -LiteralPath $backendStageRoot -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $backendOutputRoot $_.Name) -Recurse -Force
}

if (Test-Path -LiteralPath $installedReadmePath) {
    Copy-Item -LiteralPath $installedReadmePath -Destination (Join-Path $docsOutputRoot "ForgerEMS-Installed-README.txt") -Force
}

if (Test-Path -LiteralPath $installedModeDocPath) {
    Copy-Item -LiteralPath $installedModeDocPath -Destination (Join-Path $docsOutputRoot "FORGEREMS-INSTALLED-MODE-V2.md") -Force
}

if (Test-Path -LiteralPath $packagingDocPath) {
    Copy-Item -LiteralPath $packagingDocPath -Destination (Join-Path $docsOutputRoot "FORGEREMS-WPF-PACKAGING.md") -Force
}

$readmePath = Join-Path $OutputRoot "README.txt"
$readme = @"
ForgerEMS PreRelease Test Folder
================================

This folder is generated for manual installed-mode testing.
Do not edit files here as a source of truth.

Contents
--------
- installer\
  - ForgerEMS-Setup-v$version.exe
  - ForgerEMS.iss
  - build-forgerems-installer.ps1
  - stage-bundled-backend.ps1
- backend\
  - Minimal bundled backend release-bundle used by the app in installed mode
- docs\
  - Installed-mode and packaging notes copied from the app workspace

Notes
-----
- The installer executable is copied from: $expectedInstallerPath
- The backend bundle is staged from the verified release-bundle flow and copied from:
  $backendStageRoot
- The app expects the bundled backend at <install_root>\backend\
- Third-party payloads are not bundled here
"@

$readme | Set-Content -LiteralPath $readmePath -Encoding UTF8

Write-Host "PreRelease folder ready:" -ForegroundColor Green
Write-Host "  Root      : $OutputRoot"
Write-Host "  Installer : $installerOutputRoot"
Write-Host "  Backend   : $backendOutputRoot"
Write-Host "  Docs      : $docsOutputRoot"
