<#
.SYNOPSIS
Builds a complete ForgerEMS release staging folder.

.DESCRIPTION
Publishes the .NET 8 WPF app, builds and stages the PowerShell backend bundle,
copies public manifests, optionally compiles the Inno Setup installer, produces a
versioned ZIP bundle for beta distribution, and writes SHA256 checksums under
release\current\.

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

    $trim = $Value.Trim()
    # Strip semver prerelease / metadata for Windows four-part version (e.g. 1.1.12-rc.1 -> 1.1.12.0).
    $numericCore = $trim -replace '-[^+]*(?:\+.*)?$', ''
    $match = [regex]::Match($numericCore, '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<build>\d+))?$')
    if (-not $match.Success) {
        throw "Version '$Value' could not be mapped to a Windows four-part version (numeric core: '$numericCore')."
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
    $outFull = [IO.Path]::GetFullPath($OutputPath)
    $lines = Get-ChildItem -LiteralPath $Root -File -Recurse |
        Where-Object { $_.FullName -ne $outFull } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($rootFull.Length + 1).Replace('\', '/')
            $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
            "{0} *{1}" -f $hash, $relative
        }

    Set-Content -LiteralPath $OutputPath -Value $lines -Encoding ASCII
}

function Get-DistributionChecksumText {
    param(
        [Parameter(Mandatory)][string]$InstallerPath,
        [Parameter(Mandatory)][string]$ZipPath,
        [Parameter(Mandatory)][string]$ReleaseJsonPath,
        [Parameter(Mandatory)][string]$DownloadBetaPath,
        [Parameter(Mandatory)][string]$InstallerRelativeName,
        [Parameter(Mandatory)][string]$ZipRelativeName,
        [string]$ZipBetaPath = "",
        [string]$ZipBetaRelativeName = ""
    )

    $installerHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $InstallerPath).Hash.ToLowerInvariant()
    $zipHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipPath).Hash.ToLowerInvariant()
    $jsonHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ReleaseJsonPath).Hash.ToLowerInvariant()
    $downloadBetaHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $DownloadBetaPath).Hash.ToLowerInvariant()
    # Parenthesize each -f expression: comma binds tighter than -f inside @( ), which otherwise splits the array wrong.
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add(("{0} *{1}" -f $installerHash, ($InstallerRelativeName -replace '\\', '/')))
    $lines.Add(("{0} *{1}" -f $zipHash, ($ZipRelativeName -replace '\\', '/')))
    if (-not [string]::IsNullOrWhiteSpace($ZipBetaPath)) {
        if (-not (Test-Path -LiteralPath $ZipBetaPath)) {
            throw "Beta alias ZIP was not found: $ZipBetaPath"
        }
        $zipBetaHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $ZipBetaPath).Hash.ToLowerInvariant()
        if ($zipBetaHash -ne $zipHash) {
            throw "Beta alias ZIP SHA256 does not match primary ZIP (copy step failed)."
        }
        if ([string]::IsNullOrWhiteSpace($ZipBetaRelativeName)) {
            throw "ZipBetaRelativeName is required when ZipBetaPath is set."
        }
        $lines.Add(("{0} *{1}" -f $zipBetaHash, ($ZipBetaRelativeName -replace '\\', '/')))
    }
    $lines.Add(("{0} *release.json" -f $jsonHash))
    $lines.Add(("{0} *DOWNLOAD_BETA.txt" -f $downloadBetaHash))
    return ($lines -join "`n") + "`n"
}

function Get-PackageLooseFilesChecksumText {
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string]$InstallerInZipName,
        [Parameter(Mandatory)][string]$StartHereName,
        [Parameter(Mandatory)][string]$VerifyName,
        [Parameter(Mandatory)][string]$ReleaseJsonName
    )

    $root = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
    $pairs = @(
        @{ Path = (Join-Path $root $InstallerInZipName); Rel = ($InstallerInZipName -replace '\\', '/') },
        @{ Path = (Join-Path $root $StartHereName); Rel = ($StartHereName -replace '\\', '/') },
        @{ Path = (Join-Path $root $VerifyName); Rel = ($VerifyName -replace '\\', '/') },
        @{ Path = (Join-Path $root $ReleaseJsonName); Rel = ($ReleaseJsonName -replace '\\', '/') }
    )
    $lines = foreach ($p in $pairs) {
        if (-not (Test-Path -LiteralPath $p.Path)) {
            throw "Package file missing for checksums: $($p.Path)"
        }
        $h = (Get-FileHash -Algorithm SHA256 -LiteralPath $p.Path).Hash.ToLowerInvariant()
        ("{0} *{1}" -f $h, $p.Rel)
    }
    return ($lines -join "`n") + "`n"
}

function Write-DownloadBetaTxt {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version
    )
    $zipPrimary = "ForgerEMS-v{0}.zip" -f $Version
    $zipBeta = "ForgerEMS-Beta-v{0}.zip" -f $Version
    $content = @"
================================================================================
  DOWNLOAD THE ZIP  —  NOT THE EXE  (read this first)
================================================================================

On the GitHub Release -> Assets list, download ONE of these (same contents):
  PRIMARY:  $zipPrimary
  ALIAS:    $zipBeta   (easier to spot in a long asset list)

Do NOT download ForgerEMS-Setup-v$Version.exe first unless you are an advanced user.
Chrome and Edge often warn harder on a raw .exe from the browser. The ZIP flow is the supported beta path.

INCOMPLETE DOWNLOADS
- If the filename ends in .crdownload (Chrome) or looks like a partial/temp name, the download is NOT finished.
- Do NOT rename a .crdownload to .zip and do NOT run it.
- Wait until the file name ends in .zip, or cancel and retry on a stable connection.

AFTER YOU HAVE A REAL .ZIP
1. Extract the ZIP (Right-click -> Extract All). Do not run from inside the zip viewer.
2. Open the extracted folder: ForgerEMS-v$Version
3. Double-click START_HERE.bat
4. If Windows SmartScreen appears, only choose More info -> Run anyway if this ZIP came from the official GitHub release and you verified hashes.

VERIFY INTEGRITY
Use CHECKSUMS.sha256 from the same release page. Full steps: GitHub repo -> docs/DOWNLOAD_TROUBLESHOOTING.md
"@
    Set-Content -LiteralPath $Path -Value $content -Encoding utf8
}

function Write-StartHereBat {
    param([Parameter(Mandatory)][string]$Path)
    $content = @"
@echo off
title ForgerEMS Installer
echo.
echo Starting ForgerEMS Installer...
echo.
echo If Windows shows SmartScreen, choose More info -^> Run anyway ONLY if this
echo folder came from the official ForgerEMS GitHub release and you verified CHECKSUMS.sha256.
echo.
start "" "%~dp0ForgerEMS Installer.exe"
"@
    Set-Content -LiteralPath $Path -Value $content -Encoding ascii
}

function Normalize-ChecksumText {
    param([Parameter(Mandatory)][string]$Text)
    $t = $Text -replace "`r`n", "`n"
    $t = $t.TrimEnd("`n") + "`n"
    return $t
}

function Set-ChecksumFileLf {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Text
    )
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($Path, (Normalize-ChecksumText -Text $Text), $utf8)
}

function Compress-PackageFolderZip {
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string]$EntryFolderName,
        [Parameter(Mandatory)][string]$DestinationZipPath
    )

    Add-Type -AssemblyName System.IO.Compression
    if (-not ("System.IO.Compression.ZipFile" -as [type])) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }

    $packageFull = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
    $destFull = [IO.Path]::GetFullPath($DestinationZipPath)
    if (Test-Path -LiteralPath $destFull) {
        Remove-Item -LiteralPath $destFull -Force
    }
    $fixedTime = [DateTimeOffset]::Parse("2000-01-01T00:00:00Z", [System.Globalization.CultureInfo]::InvariantCulture)
    $stream = [IO.File]::Open($destFull, [IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            Get-ChildItem -LiteralPath $packageFull -File -Recurse |
                Sort-Object FullName |
                ForEach-Object {
                    $relative = $_.FullName.Substring($packageFull.Length + 1).Replace('\', '/')
                    $entryName = ($EntryFolderName.TrimEnd('/') + "/" + $relative)
                    $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $fixedTime
                    $writer = $entry.Open()
                    try {
                        $fs = [IO.File]::OpenRead($_.FullName)
                        try {
                            $fs.CopyTo($writer)
                        } finally {
                            $fs.Dispose()
                        }
                    } finally {
                        $writer.Dispose()
                    }
                }
        } finally {
            $zip.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Write-VerifyTxt {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version
    )
    $content = @"
ForgerEMS Official Beta Package
Version: $Version

Official source only:
https://github.com/Forger-Digital-Solutions/ForgerEMS/releases

You should have extracted a .zip from that release page. If you only have a single
.exe from email, Discord, or another site, STOP — it is not this verified package.

DO NOT RUN
- Files ending in .crdownload (Chrome incomplete download) — wait or re-download.
- Partial / tmp download names — do not rename them to .exe or .zip.
- Random installers from chat or unofficial mirrors.

Expected files inside this folder:
- ForgerEMS Installer.exe
- START_HERE.bat
- VERIFY.txt
- CHECKSUMS.sha256 (hashes for the files inside this folder)
- release.json

How to install:
1. You must extract the ZIP first (do not run from the zip preview alone).
2. Double-click START_HERE.bat.
3. If Windows SmartScreen appears, use More info -> Run anyway only for this official release.

Verify this folder (PowerShell, run inside the extracted folder):
  Get-FileHash ".\ForgerEMS Installer.exe" -Algorithm SHA256
Compare the Hash line to CHECKSUMS.sha256 in this folder for ForgerEMS Installer.exe.

The GitHub release page also publishes a root CHECKSUMS.sha256 for the standalone
installer, both ZIP variants, release.json, and DOWNLOAD_BETA.txt.

Support:
ForgerDigitalSolutions@outlook.com

Security notice:
Never send API keys, passwords, serial numbers, private documents, or sensitive personal files.
"@
    Set-Content -LiteralPath $Path -Value $content -Encoding utf8
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
$releaseRoot = Join-Path $repoRoot "release\current"
$releaseAppRoot = Join-Path $releaseRoot "app"
$releaseBackendRoot = Join-Path $releaseAppRoot "backend"
$releaseManifestRoot = Join-Path $releaseAppRoot "manifests"
$checksumsPath = Join-Path $releaseRoot "CHECKSUMS.sha256"
$installerOutputDir = Join-Path $distRoot "installer"
$installerReleaseName = "ForgerEMS-Setup-v{0}.exe" -f $Version
$installerReleasePath = Join-Path $releaseRoot $installerReleaseName
$releaseIdentifierLabel = [string]::Concat("ForgerEMS Beta v", $Version, " ", [char]0x2014, " Beta readiness: ZIP-first download, dual ZIP assets, first-run guidance")

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

if ($DryRun -or $SkipInstaller) {
    Write-Step "Skipping installer compilation"
}
else {
    Write-Step "Compiling installer"
    Ensure-Dir -Path $installerOutputDir
    $isccPath = Resolve-IsccPath -ExplicitPath $InnoCompilerPath
    $appVersionInfo = ConvertTo-WindowsVersion -Value $Version
    & $isccPath `
        "/DAppVersion=$Version" `
        "/DAppVersionInfo=$appVersionInfo" `
        ("/DReleaseIdentifier=$releaseIdentifierLabel") `
        "/DPublishDir=$publishDir" `
        "/DBackendBundleDir=$backendStageRoot" `
        "/DOutputDir=$installerOutputDir" `
        $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

    $versionedInstallerPath = Join-Path $installerOutputDir ("ForgerEMS-Setup-v{0}.exe" -f $Version)
    if (-not (Test-Path -LiteralPath $versionedInstallerPath)) {
        throw "Expected installer output was not found: $versionedInstallerPath"
    }

    Copy-Item -LiteralPath $versionedInstallerPath -Destination (Join-Path $releaseRoot (Split-Path -Leaf $versionedInstallerPath)) -Force
}

Write-Step "Writing release metadata"
$metadata = [ordered]@{
    product = "ForgerEMS"
    publisher = "Forger Digital Solutions"
    version = $Version
    releaseIdentifier = $releaseIdentifierLabel
    channel = "Beta"
    backendVersion = $backendVersion
    runtime = $Runtime
    configuration = $Configuration
    dryRun = [bool]$DryRun
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot "release.json") -Encoding UTF8

Write-Step "Copying beta readme if present"
$betaReadmePath = Join-Path $distRoot ("beta\README-BETA-v{0}.txt" -f $Version)
if (Test-Path -LiteralPath $betaReadmePath) {
    Copy-Item -LiteralPath $betaReadmePath -Destination (Join-Path $releaseRoot (Split-Path -Leaf $betaReadmePath)) -Force
}

$releaseJsonPath = Join-Path $releaseRoot "release.json"

if ($DryRun -or $SkipInstaller) {
    Write-Step "Generating SHA256 checksums"
    Write-Checksums -Root $releaseRoot -OutputPath $checksumsPath
}
else {
    Write-Step "Creating ZIP distribution bundle"
    $zipBundleName = "ForgerEMS-v{0}.zip" -f $Version
    $zipBundlePath = Join-Path $releaseRoot $zipBundleName
    $packageParent = Join-Path $releaseRoot "package"
    $packageDirName = "ForgerEMS-v{0}" -f $Version
    $packageRoot = Join-Path $packageParent $packageDirName

    if (-not (Test-Path -LiteralPath $installerReleasePath)) {
        throw "Installer not found for bundling: $installerReleasePath"
    }

    if (Test-Path -LiteralPath $packageParent) {
        Remove-Item -LiteralPath $packageParent -Recurse -Force
    }
    Ensure-Dir -Path $packageRoot

    Copy-Item -LiteralPath $installerReleasePath -Destination (Join-Path $packageRoot "ForgerEMS Installer.exe") -Force
    Copy-Item -LiteralPath $releaseJsonPath -Destination (Join-Path $packageRoot "release.json") -Force
    Write-StartHereBat -Path (Join-Path $packageRoot "START_HERE.bat")
    Write-VerifyTxt -Path (Join-Path $packageRoot "VERIFY.txt") -Version $Version

    $packageChecksumText = Get-PackageLooseFilesChecksumText `
        -PackageRoot $packageRoot `
        -InstallerInZipName "ForgerEMS Installer.exe" `
        -StartHereName "START_HERE.bat" `
        -VerifyName "VERIFY.txt" `
        -ReleaseJsonName "release.json"
    Set-ChecksumFileLf -Path (Join-Path $packageRoot "CHECKSUMS.sha256") -Text $packageChecksumText

    Compress-PackageFolderZip -PackageRoot $packageRoot -EntryFolderName $packageDirName -DestinationZipPath $zipBundlePath

    $zipBetaBundleName = "ForgerEMS-Beta-v{0}.zip" -f $Version
    $zipBetaBundlePath = Join-Path $releaseRoot $zipBetaBundleName
    Copy-Item -LiteralPath $zipBundlePath -Destination $zipBetaBundlePath -Force

    $downloadBetaPath = Join-Path $releaseRoot "DOWNLOAD_BETA.txt"
    Write-DownloadBetaTxt -Path $downloadBetaPath -Version $Version

    $distributionChecksumText = Get-DistributionChecksumText `
        -InstallerPath $installerReleasePath `
        -ZipPath $zipBundlePath `
        -ReleaseJsonPath $releaseJsonPath `
        -DownloadBetaPath $downloadBetaPath `
        -InstallerRelativeName $installerReleaseName `
        -ZipRelativeName $zipBundleName `
        -ZipBetaPath $zipBetaBundlePath `
        -ZipBetaRelativeName $zipBetaBundleName
    Set-ChecksumFileLf -Path $checksumsPath -Text $distributionChecksumText
    if (Test-Path -LiteralPath $packageParent) {
        Remove-Item -LiteralPath $packageParent -Recurse -Force
    }
}

Write-Host "ForgerEMS current release folder ready: $releaseRoot" -ForegroundColor Green
