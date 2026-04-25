<#
.SYNOPSIS
Builds a complete ForgerEMS release staging folder.

.DESCRIPTION
Publishes the .NET 8 WPF app, builds and stages the PowerShell backend bundle,
copies public manifests, optionally compiles the Inno Setup installer, and
generates SHA256 checksums under release\<version>\.

.PARAMETER Version
Release version. Defaults to the WPF project <Version>.

.PARAMETER DryRun
Runs all CI-safe build and validation work, but skips Inno Setup compilation.

.PARAMETER SkipInstaller
Skips installer compilation even outside dry-run mode.

.PARAMETER Configuration
.NET build configuration. Defaults to Release.

.PARAMETER Runtime
.NET runtime identifier. Defaults to win-x64.
#>

#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$DryRun,
    [switch]$SkipInstaller,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$InnoCompilerPath = ""
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "ForgerEMS.sln"
$projectPath = Join-Path $repoRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
$backendBuildScript = Join-Path $repoRoot "tools\build-backend-release.ps1"
$stageBackendScript = Join-Path $repoRoot "tools\stage-bundled-backend.ps1"
$installerScript = Join-Path $repoRoot "installer\ForgerEMS.iss"
$manifestRoot = Join-Path $repoRoot "manifests"
$distRoot = Join-Path $repoRoot "dist"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[ForgerEMS] $Message" -ForegroundColor Cyan
}

function Ensure-Dir {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-ProjectVersion {
    param([Parameter(Mandatory)][string]$Path)
    [xml]$xml = Get-Content -LiteralPath $Path -Raw
    $value = $xml.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace([string]$value)) {
        throw "Could not read <Version> from $Path"
    }
    return [string]$value
}

function Resolve-IsccPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path -LiteralPath $ExplicitPath) { return $ExplicitPath }
        throw "Inno Setup compiler was not found at: $ExplicitPath"
    }

    $candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6 or rerun with -DryRun."
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

function Copy-CleanDirectory {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Required source directory was not found: $Source"
    }

    Ensure-Dir -Path $Destination
    Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Write-Checksums {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$OutputPath
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $lines = Get-ChildItem -LiteralPath $Root -File -Recurse |
        Where-Object { $_.FullName -ne $OutputPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($rootFull.Length + 1).Replace('\', '/')
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
            "{0} *{1}" -f $hash, $relative
        }

    Set-Content -LiteralPath $OutputPath -Value $lines -Encoding ASCII
}

foreach ($required in @($solutionPath, $projectPath, $backendBuildScript, $stageBackendScript, $installerScript, $manifestRoot)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required release input not found: $required"
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-ProjectVersion -Path $projectPath
}

$publishDir = Join-Path $distRoot "publish\$Runtime"
$backendStageRoot = Join-Path $distRoot "backend-stage\backend"
$releaseRoot = Join-Path $repoRoot ("release\{0}" -f $Version)
$releaseAppRoot = Join-Path $releaseRoot "app"
$releaseBackendRoot = Join-Path $releaseRoot "backend"
$releaseManifestRoot = Join-Path $releaseRoot "manifests"
$releaseInstallerRoot = Join-Path $releaseRoot "installer"
$checksumsPath = Join-Path $releaseRoot "CHECKSUMS.sha256"

Write-Step "Release version: $Version"
Write-Step "Restoring solution"
dotnet restore $solutionPath
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }

Write-Step "Building solution"
dotnet build $solutionPath -c $Configuration --no-restore
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }

Write-Step "Publishing WPF app"
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true /p:Version=$Version /p:InformationalVersion=$Version /p:PublishDir="$publishDir\"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

Write-Step "Building backend release bundle"
& $backendBuildScript

$backendManifest = Get-Content -LiteralPath (Join-Path $manifestRoot "ForgerEMS.updates.json") -Raw | ConvertFrom-Json
$backendVersion = [string]$backendManifest.coreVersion
if ([string]::IsNullOrWhiteSpace($backendVersion)) {
    throw "manifests\ForgerEMS.updates.json is missing coreVersion."
}

$backendReleaseRoot = Join-Path $repoRoot ("release\ventoy-core\{0}" -f $backendVersion)
if (-not (Test-Path -LiteralPath $backendReleaseRoot)) {
    throw "Expected backend release bundle was not found: $backendReleaseRoot"
}

Write-Step "Staging bundled backend"
& $stageBackendScript -FrontendVersion $Version -ReleaseBundleRoot $backendReleaseRoot -OutputRoot $backendStageRoot

Write-Step "Preparing release folder"
Ensure-Dir -Path (Split-Path -Parent $releaseRoot)
if (Test-Path -LiteralPath $releaseRoot) {
    Get-ChildItem -LiteralPath $releaseRoot -Force | Remove-Item -Recurse -Force
}
Ensure-Dir -Path $releaseRoot
Copy-CleanDirectory -Source $publishDir -Destination $releaseAppRoot
Copy-CleanDirectory -Source $backendStageRoot -Destination $releaseBackendRoot
Copy-CleanDirectory -Source $manifestRoot -Destination $releaseManifestRoot
Ensure-Dir -Path $releaseInstallerRoot

if ($DryRun -or $SkipInstaller) {
    Write-Step "Skipping installer compilation"
}
else {
    Write-Step "Compiling installer"
    $isccPath = Resolve-IsccPath -ExplicitPath $InnoCompilerPath
    $appVersionInfo = ConvertTo-WindowsVersion -Value $Version
    & $isccPath `
        "/DAppVersion=$Version" `
        "/DAppVersionInfo=$appVersionInfo" `
        "/DPublishDir=$publishDir" `
        "/DBackendBundleDir=$backendStageRoot" `
        "/DOutputDir=$releaseInstallerRoot" `
        $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
}

Write-Step "Writing release metadata"
$metadata = [ordered]@{
    product = "ForgerEMS"
    publisher = "Forger Digital Solutions"
    version = $Version
    channel = "Beta"
    backendVersion = $backendVersion
    runtime = $Runtime
    configuration = $Configuration
    dryRun = [bool]$DryRun
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot "release.json") -Encoding UTF8

Write-Step "Generating SHA256 checksums"
Write-Checksums -Root $releaseRoot -OutputPath $checksumsPath

Write-Host "ForgerEMS release folder ready: $releaseRoot" -ForegroundColor Green
