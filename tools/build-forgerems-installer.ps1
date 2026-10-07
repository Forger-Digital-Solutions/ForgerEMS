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
Installer/app version. Defaults to the repository VERSION file. A different
explicit value is refused: VERSION is authoritative.

.PARAMETER SkipPublish
Skip dotnet publish and reuse the existing publish output.

.PARAMETER ReleaseBundleRoot
Optional explicit release-bundle root to stage into the installer. When omitted,
the newest verified folder under ..\release\ventoy-core\ is used.

.PARAMETER UnsignedCandidate
Required. This adjunct script only produces unsigned local candidates. Pass it
explicitly; production packaging belongs to tools\build-release.ps1 (canonical,
signed). A sidecar *.candidate.json is emitted next to the installer marking
unsignedCandidate=true, productionEligible=false, signed=false with the source
HEAD and dirty-file count.


.EXAMPLE
.\tools\build-forgerems-installer.ps1 -UnsignedCandidate

.EXAMPLE
.\tools\build-forgerems-installer.ps1 -UnsignedCandidate -SkipPublish
#>

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$SkipPublish,
    [string]$ReleaseBundleRoot = "",
    [switch]$UnsignedCandidate
)

$ErrorActionPreference = "Stop"

if (-not $UnsignedCandidate) {
    throw "build-forgerems-installer.ps1 only produces unsigned local candidates - pass -UnsignedCandidate explicitly. For production packaging use the canonical tools\build-release.ps1 (signed, gated)."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $repoRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
$publishDir = Join-Path $repoRoot "src\ForgerEMS.Wpf\bin\Release\net8.0-windows\win-x64\publish"
$issPath = Join-Path $repoRoot "installer\ForgerEMS.iss"
$stageScriptPath = Join-Path $repoRoot "tools\stage-bundled-backend.ps1"
$backendStageRoot = Join-Path $repoRoot "dist\backend-stage\backend"
$outputDir = Join-Path $repoRoot "dist\installer"
$appExePath = Join-Path $publishDir "ForgerEMS.exe"

function Get-RepoVersion {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $versionFile = Join-Path $RepoRoot "VERSION"
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "Authoritative version file not found: $versionFile"
    }

    $value = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "VERSION file is empty: $versionFile"
    }

    return $value
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

function ConvertTo-WindowsVersion {
    param([Parameter(Mandatory)][string]$Value)

    $match = [regex]::Match($Value.Trim(), '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<build>\d+))?')
    if (-not $match.Success) {
        throw "Version '$Value' must start with a semantic numeric core like 1.2.3."
    }

    $build = if ($match.Groups["build"].Success) { $match.Groups["build"].Value } else { "0" }
    return "{0}.{1}.{2}.{3}" -f $match.Groups["major"].Value, $match.Groups["minor"].Value, $match.Groups["patch"].Value, $build
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

$authoritativeVersion = Get-RepoVersion -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $authoritativeVersion
}
elseif ($Version.Trim() -ne $authoritativeVersion) {
    throw "Refusing to override authoritative VERSION ($authoritativeVersion) with '$Version'."
}

$displayVersionLabel = "ForgerEMS v$Version"
$releaseIdentifierLabel = $displayVersionLabel

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
$appVersionInfo = ConvertTo-WindowsVersion -Value $Version

Write-Host "Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $isccPath `
    "/DAppVersion=$Version" `
    "/DAppVersionInfo=$appVersionInfo" `
    ("/DDisplayVersion=$displayVersionLabel") `
    ("/DReleaseIdentifier=$releaseIdentifierLabel") `
    "/DUnsignedCandidate=1" `
    "/DPublishDir=$publishDir" `
    "/DBackendBundleDir=$backendStageRoot" `
    "/DOutputDir=$outputDir" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

$expectedInstaller = Join-Path $outputDir ("ForgerEMS-Setup-v{0}.exe" -f $Version)
if (Test-Path -LiteralPath $expectedInstaller) {
    $candidateSourceHead = (git -C $repoRoot rev-parse HEAD 2>$null)
    $candidateDirtyCount = @(git -C $repoRoot status --porcelain 2>$null | Where-Object { $_ }).Count
    $candidateSidecar = [ordered]@{
        unsignedCandidate     = $true
        productionEligible    = $false
        signed                = $false
        sourceHead            = $candidateSourceHead
        sourceDirtyFileCount  = $candidateDirtyCount
        generatedUtc          = (Get-Date).ToUniversalTime().ToString("o")
    }
    $candidateSidecar | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath ($expectedInstaller -replace '\.exe$', '.candidate.json') -Encoding UTF8
    Write-Host "Installer ready: $expectedInstaller" -ForegroundColor Green
    Write-Warning "UNSIGNED LOCAL CANDIDATE: this installer is not Authenticode-signed and is marked non-production. Do not publish."
}
else {
    Write-Warning "Installer compilation completed, but the expected output file was not found: $expectedInstaller"
}
