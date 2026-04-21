<#
.SYNOPSIS
Stages a minimal installed-mode backend bundle from a verified release bundle.

.DESCRIPTION
Copies only the verified backend release-bundle files needed by the WPF app into
an installer staging folder. This keeps installed mode self-contained without
bundling large payloads, ISOs, Drivers, or portable tool archives.

.PARAMETER FrontendVersion
Frontend version that the staged backend must match.

.PARAMETER ReleaseBundleRoot
Optional explicit source release-bundle root. When omitted, the newest verified
folder under ..\release\ventoy-core\ is used.

.PARAMETER OutputRoot
Optional destination root. Defaults to .\dist\backend-stage\backend
#>

[CmdletBinding()]
param(
    [string]$FrontendVersion = "",
    [string]$ReleaseBundleRoot = "",
    [string]$OutputRoot = ""
)

$ErrorActionPreference = "Stop"

$appRoot = Split-Path -Parent $PSScriptRoot
$workspaceRoot = Split-Path -Parent $appRoot
$defaultOutputRoot = Join-Path $appRoot "dist\backend-stage\backend"
$releaseFamilyRoot = Join-Path $workspaceRoot "release\ventoy-core"
$csprojPath = Join-Path $appRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
$metadataFileName = "ForgerEMS.bundled-backend.json"

$rootFilesToCopy = @(
    "Verify-VentoyCore.ps1",
    "Setup-ForgerEMS.ps1",
    "Update-ForgerEMS.ps1",
    "Setup_Toolkit.ps1",
    "Setup_USB_Toolkit.ps1",
    "ForgerEMS.updates.json",
    "VERSION.txt",
    "RELEASE-BUNDLE.txt",
    "CHECKSUMS.sha256",
    "SIGNATURE.txt"
)

$directoriesToCopy = @(
    "manifests",
    "docs",
    "tools"
)

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

function Resolve-ReleaseBundleRoot {
    param(
        [Parameter(Mandatory)][string]$ReleaseFamilyRoot,
        [string]$ExplicitRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitRoot)) {
        $resolvedExplicitRoot = Get-NormalizedPath -Path $ExplicitRoot
        if (-not (Test-Path -LiteralPath $resolvedExplicitRoot)) {
            throw "Explicit release bundle root was not found: $resolvedExplicitRoot"
        }

        return $resolvedExplicitRoot
    }

    if (-not (Test-Path -LiteralPath $ReleaseFamilyRoot)) {
        throw "Release bundle family root was not found: $ReleaseFamilyRoot"
    }

    $candidate = Get-ChildItem -LiteralPath $ReleaseFamilyRoot -Directory |
        Where-Object {
            (Test-Path -LiteralPath (Join-Path $_.FullName "Verify-VentoyCore.ps1")) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName "Setup-ForgerEMS.ps1")) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName "Update-ForgerEMS.ps1")) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName "VERSION.txt")) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName "RELEASE-BUNDLE.txt"))
        } |
        Sort-Object Name -Descending |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw "No verified release-bundle folder was found under $ReleaseFamilyRoot"
    }

    return $candidate.FullName
}

function Get-BackendVersion {
    param([Parameter(Mandatory)][string]$VersionFilePath)

    $line = Get-Content -LiteralPath $VersionFilePath |
        Where-Object { $_ -match '^Version:' } |
        Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($line)) {
        throw "Could not read backend version from $VersionFilePath"
    }

    return ($line -split ':', 2)[1].Trim()
}

if ([string]::IsNullOrWhiteSpace($FrontendVersion)) {
    $FrontendVersion = Get-ProjectVersion -ProjectPath $csprojPath
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = $defaultOutputRoot
}

$ReleaseBundleRoot = Resolve-ReleaseBundleRoot -ReleaseFamilyRoot $releaseFamilyRoot -ExplicitRoot $ReleaseBundleRoot
$OutputRoot = Get-NormalizedPath -Path $OutputRoot

$stageFamilyRoot = Join-Path $appRoot "dist\backend-stage"
Ensure-Dir -Path $stageFamilyRoot
Assert-ChildPath -Parent $stageFamilyRoot -Child $OutputRoot

foreach ($requiredFile in $rootFilesToCopy) {
    $sourcePath = Join-Path $ReleaseBundleRoot $requiredFile
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Required release-bundle file was not found: $sourcePath"
    }
}

foreach ($requiredDirectory in $directoriesToCopy) {
    $sourceDirectory = Join-Path $ReleaseBundleRoot $requiredDirectory
    if (-not (Test-Path -LiteralPath $sourceDirectory)) {
        throw "Required release-bundle directory was not found: $sourceDirectory"
    }
}

Ensure-Dir -Path $OutputRoot
Get-ChildItem -LiteralPath $OutputRoot -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force

foreach ($fileName in $rootFilesToCopy) {
    Copy-Item -LiteralPath (Join-Path $ReleaseBundleRoot $fileName) -Destination (Join-Path $OutputRoot $fileName) -Force
}

foreach ($directoryName in $directoriesToCopy) {
    Copy-Item -LiteralPath (Join-Path $ReleaseBundleRoot $directoryName) -Destination (Join-Path $OutputRoot $directoryName) -Recurse -Force
}

$backendVersion = Get-BackendVersion -VersionFilePath (Join-Path $ReleaseBundleRoot "VERSION.txt")
$metadata = [ordered]@{
    schemaVersion    = 1
    frontendVersion  = $FrontendVersion
    backendVersion   = $backendVersion
    bundleSourceRoot = (Split-Path -Leaf $ReleaseBundleRoot)
    generatedUtc     = (Get-Date).ToUniversalTime().ToString("o")
}

$metadataPath = Join-Path $OutputRoot $metadataFileName
$metadata | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $metadataPath -Encoding UTF8

Write-Host "Bundled backend staged:" -ForegroundColor Green
Write-Host "  Source   : $ReleaseBundleRoot"
Write-Host "  Frontend : $FrontendVersion"
Write-Host "  Backend  : $backendVersion"
Write-Host "  Output   : $OutputRoot"
