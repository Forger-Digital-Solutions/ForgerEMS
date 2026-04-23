<#
.SYNOPSIS
Assembles a clean distributable Ventoy core release bundle.

.DESCRIPTION
Validates the canonical manifest, reads version/build metadata from the single
source of truth in manifests\ForgerEMS.updates.json, and assembles a
deterministic release folder under release\ventoy-core\<coreVersion>\. The
script copies only the required release files, excluding verification scratch
space and unrelated workspace artifacts. Optionally creates a zip archive of
the built bundle.

.PARAMETER ZipOutput
Create a zip archive alongside the built release folder.

.PARAMETER ShowVersion
Display the Ventoy core version/build metadata from the canonical manifest and
exit without building.

.EXAMPLE
.\Tools\build-release.ps1

.EXAMPLE
.\Tools\build-release.ps1 -ZipOutput
#>

#requires -Version 5.1

[CmdletBinding()]
param(
    [switch]$ZipOutput,
    [switch]$ShowVersion
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$powerShellExe = Join-Path $PSHOME "powershell.exe"
if (-not (Test-Path -LiteralPath $powerShellExe)) {
    $powerShellExe = "powershell.exe"
}

function Write-Status {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO", "OK", "WARN", "ERROR")][string]$Level = "INFO"
    )

    switch ($Level) {
        "INFO"  { Write-Host $Message -ForegroundColor Cyan }
        "OK"    { Write-Host $Message -ForegroundColor Green }
        "WARN"  { Write-Host $Message -ForegroundColor Yellow }
        "ERROR" { Write-Host $Message -ForegroundColor Red }
    }
}

function Ensure-Dir {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    [IO.Path]::GetFullPath($Path).TrimEnd('\')
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

function Format-BuildTimestamp {
    param([AllowNull()]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return "untracked"
    }

    if ($Value -is [DateTime] -or $Value -is [DateTimeOffset]) {
        return ([DateTimeOffset]$Value).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    }

    return [string]$Value
}

function Assert-PositiveIntegerValue {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "$FieldName is required."
    }

    if (-not ([string]$Value -match '^\d+$')) {
        throw "$FieldName must be a positive integer."
    }

    if ([int64]$Value -lt 1) {
        throw "$FieldName must be greater than or equal to 1."
    }
}

function Assert-ReleaseTypeValue {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "$FieldName is required."
    }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("dev", "candidate", "stable")) {
        throw "$FieldName must be 'dev', 'candidate', or 'stable'."
    }
}

function Assert-ManagedChecksumPolicyValue {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "$FieldName is required."
    }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("warn", "require-for-release")) {
        throw "$FieldName must be 'warn' or 'require-for-release'."
    }
}

function Assert-Sha256Value {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    if (-not ([string]$Value -match '^[a-fA-F0-9]{64}$')) {
        throw "$FieldName must be a 64-character SHA-256 hex string."
    }
}

function Get-ManagedChecksumPolicy {
    param([Parameter(Mandatory)]$Manifest)

    $policy = if ($Manifest.managedChecksumPolicy) {
        ([string]$Manifest.managedChecksumPolicy).Trim().ToLowerInvariant()
    }
    else {
        "warn"
    }

    Assert-ManagedChecksumPolicyValue -Value $policy -FieldName "managedChecksumPolicy"
    return $policy
}

function Assert-ManagedChecksumReleaseDiscipline {
    param(
        [Parameter(Mandatory)][string]$ReleaseType,
        [Parameter(Mandatory)][string]$ManagedChecksumPolicy
    )

    if (($ReleaseType -in @("candidate", "stable")) -and ($ManagedChecksumPolicy -ne "require-for-release")) {
        throw "managedChecksumPolicy must be 'require-for-release' for $ReleaseType releases."
    }
}

function Get-ManagedItemsMissingChecksumCoverage {
    param([Parameter(Mandatory)]$Manifest)

    $missing = New-Object System.Collections.Generic.List[object]

    foreach ($item in @($Manifest.items)) {
        if ($null -eq $item) { continue }

        $itemType = if ($item.type) { ([string]$item.type).Trim().ToLowerInvariant() } else { "file" }
        if ($itemType -ne "file") { continue }

        $isEnabled = $true
        if ($null -ne $item.enabled) {
            $isEnabled = [bool]$item.enabled
        }

        if (-not $isEnabled) { continue }

        $hasSha256 = -not [string]::IsNullOrWhiteSpace([string]$item.sha256)
        $hasSha256Url = -not [string]::IsNullOrWhiteSpace([string]$item.sha256Url)
        if ($hasSha256 -or $hasSha256Url) { continue }

        $missing.Add([PSCustomObject]@{
            Name = [string]$item.name
            Dest = [string]$item.dest
        })
    }

    return $missing.ToArray()
}

function Assert-VendorInventoryContract {
    param(
        [Parameter(Mandatory)]$Inventory,
        [Parameter(Mandatory)][string]$SourceName,
        [Parameter(Mandatory)][string]$ExpectedCoreVersion,
        [Parameter(Mandatory)][string]$ExpectedReleaseType
    )

    if ($null -eq $Inventory) {
        throw "Vendor inventory '$SourceName' is empty or invalid."
    }

    Assert-PositiveIntegerValue -Value $Inventory.inventoryVersion -FieldName "inventoryVersion"

    if ([string]::IsNullOrWhiteSpace([string]$Inventory.coreVersion)) {
        throw "Vendor inventory '$SourceName' must declare coreVersion."
    }

    if ([string]$Inventory.coreVersion -ne $ExpectedCoreVersion) {
        throw "Vendor inventory coreVersion does not match the canonical manifest."
    }

    if ([string]::IsNullOrWhiteSpace([string]$Inventory.buildTimestampUtc)) {
        throw "Vendor inventory '$SourceName' must declare buildTimestampUtc."
    }

    try {
        [DateTimeOffset]::Parse([string]$Inventory.buildTimestampUtc) | Out-Null
    }
    catch {
        throw "Vendor inventory buildTimestampUtc must be an ISO-like date/time string."
    }

    Assert-ReleaseTypeValue -Value $Inventory.releaseType -FieldName "releaseType"
    if ([string]$Inventory.releaseType -ne $ExpectedReleaseType) {
        throw "Vendor inventory releaseType does not match the canonical manifest."
    }

    $items = @($Inventory.items)
    if ($items.Count -eq 0) {
        throw "Vendor inventory '$SourceName' must contain at least one item."
    }

    $seenNames = @{}
    $seenPaths = @{}

    for ($i = 0; $i -lt $items.Count; $i++) {
        $entry = $items[$i]
        $prefix = "items[$i]"

        if ($null -eq $entry) {
            throw "$prefix cannot be null."
        }

        $name = [string]$entry.name
        $path = [string]$entry.path
        $sourceUrl = [string]$entry.sourceUrl
        $version = [string]$entry.version
        $sourceTrust = if ($entry.source_trust) { ([string]$entry.source_trust).Trim().ToLowerInvariant() } else { "" }

        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "$prefix.name is required."
        }

        if ([string]::IsNullOrWhiteSpace($path)) {
            throw "$prefix.path is required."
        }

        if ([string]::IsNullOrWhiteSpace($version)) {
            throw "$prefix.version is required."
        }

        if ($entry.managed -isnot [bool]) {
            throw "$prefix.managed must be a JSON boolean."
        }

        if ($entry.verified -isnot [bool]) {
            throw "$prefix.verified must be a JSON boolean."
        }

        if ($sourceTrust -notin @("official", "community", "manual")) {
            throw "$prefix.source_trust must be 'official', 'community', or 'manual'."
        }

        Assert-Sha256Value -Value $entry.checksum -FieldName "$prefix.checksum"

        if (-not [string]::IsNullOrWhiteSpace($sourceUrl) -and -not [Uri]::IsWellFormedUriString($sourceUrl, [UriKind]::Absolute)) {
            throw "$prefix.sourceUrl must be an absolute URL when present."
        }

        if ($seenNames.ContainsKey($name)) {
            throw "Vendor inventory contains a duplicate item name: $name"
        }

        if ($seenPaths.ContainsKey($path)) {
            throw "Vendor inventory contains a duplicate item path: $path"
        }

        $seenNames[$name] = $true
        $seenPaths[$path] = $true
    }
}

function Get-ReleaseChecksumRelativePaths {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][string]$CoreVersion
    )

    $paths = New-Object System.Collections.Generic.List[string]

    foreach ($path in @(
        "Setup-ForgerEMS.ps1",
        "Setup_USB_Toolkit.ps1",
        "Setup_Toolkit.ps1",
        "Update-ForgerEMS.ps1",
        "Verify-VentoyCore.ps1",
        "ForgerEMS.Runtime.ps1",
        "ForgerEMS.updates.json",
        "manifests/ForgerEMS.updates.schema.json",
        "manifests/vendor.inventory.json",
        "manifests/vendor.inventory.schema.json",
        "VERSION.txt",
        "RELEASE-BUNDLE.txt"
    )) {
        [void]$paths.Add($path)
    }

    foreach ($optionalPath in @(
        "docs/DOWNLOAD-CATALOG.txt",
        "docs/ForgerEMS-Quick-Start.txt",
        "docs/MANAGED-DOWNLOAD-MAINTENANCE.txt",
        "docs/README.txt",
        "docs/ReadME.html",
        "docs/ScriptCommands.txt",
        "tools/ScriptCommands.txt"
    )) {
        $fullOptionalPath = Join-Path $TargetRoot ($optionalPath -replace '/', '\')
        if (Test-Path -LiteralPath $fullOptionalPath) {
            [void]$paths.Add($optionalPath)
        }
    }

    foreach ($optionalPath in @(
        "docs/Release-Verification-History/README.txt",
        ("docs/Release-Verification-History/{0}/RELEASE-VERIFICATION-STATUS.txt" -f $CoreVersion),
        ("docs/Release-Verification-History/{0}/managed-download-summary.txt" -f $CoreVersion),
        ("docs/Release-Verification-History/{0}/managed-download-revalidation.txt" -f $CoreVersion),
        ("docs/Release-Verification-History/{0}/managed-download-revalidation.csv" -f $CoreVersion)
    )) {
        $fullOptionalPath = Join-Path $TargetRoot ($optionalPath -replace '/', '\')
        if (Test-Path -LiteralPath $fullOptionalPath) {
            [void]$paths.Add($optionalPath)
        }
    }

    return $paths.ToArray()
}

function Write-ReleaseChecksums {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][string]$CoreVersion,
        [Parameter(Mandatory)][string]$OutputPath
    )

    $lines = foreach ($relativePath in (Get-ReleaseChecksumRelativePaths -TargetRoot $TargetRoot -CoreVersion $CoreVersion)) {
        $fullPath = Join-Path $TargetRoot ($relativePath -replace '/', '\')
        if (-not (Test-Path -LiteralPath $fullPath)) {
            throw "Cannot generate checksum for missing release file: $fullPath"
        }

        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash.ToLowerInvariant()
        "{0} *{1}" -f $hash, $relativePath
    }

    Set-Content -LiteralPath $OutputPath -Value $lines -Encoding ASCII
}

function Write-ReleaseSignature {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][string]$OutputPath,
        [Parameter(Mandatory)][string]$CoreVersion,
        [Parameter(Mandatory)][string]$BuildTimestampUtc,
        [Parameter(Mandatory)][string]$ReleaseType
    )

    $checksumsPath = Join-Path $TargetRoot "CHECKSUMS.sha256"
    $manifestPath = Join-Path $TargetRoot "ForgerEMS.updates.json"

    if (-not (Test-Path -LiteralPath $checksumsPath)) {
        throw "Cannot generate a signature without CHECKSUMS.sha256."
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Cannot generate a signature without ForgerEMS.updates.json."
    }

    $checksumsSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $checksumsPath).Hash.ToLowerInvariant()
    $manifestSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash.ToLowerInvariant()

    $content = @"
# ForgerEMS Ventoy Core integrity seal
# This is an integrity catalog signature, not a cryptographic publisher signature.
SignatureType: checksum-catalog-sha256
Algorithm: SHA256
CoreVersion: $CoreVersion
BuildTimestampUtc: $BuildTimestampUtc
ReleaseType: $ReleaseType
SignedFile: CHECKSUMS.sha256
SignedFileSha256: $checksumsSha256
ManifestFile: ForgerEMS.updates.json
ManifestSha256: $manifestSha256
"@

    Set-Content -LiteralPath $OutputPath -Value $content -Encoding ASCII
}

function Write-ReleaseVerificationHistory {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][string]$CoreVersion,
        [Parameter(Mandatory)][string]$BuildTimestampUtc,
        [Parameter(Mandatory)][string]$ReleaseType
    )

    $historyRoot = Join-Path $TargetRoot "docs\Release-Verification-History"
    $releaseHistoryRoot = Join-Path $historyRoot $CoreVersion
    $historyReadmePath = Join-Path $historyRoot "README.txt"
    $statusPath = Join-Path $releaseHistoryRoot "RELEASE-VERIFICATION-STATUS.txt"
    $managedSourceRoot = Join-Path $RepoRoot ".verify\managed-download-revalidation\latest"
    $managedArtifactNames = @(
        "managed-download-summary.txt",
        "managed-download-revalidation.txt",
        "managed-download-revalidation.csv"
    )

    Ensure-Dir -Path $historyRoot
    Ensure-Dir -Path $releaseHistoryRoot

    $hasFullManagedSnapshot = $true
    foreach ($artifactName in $managedArtifactNames) {
        if (-not (Test-Path -LiteralPath (Join-Path $managedSourceRoot $artifactName))) {
            $hasFullManagedSnapshot = $false
            break
        }
    }

    $includedFiles = New-Object System.Collections.Generic.List[string]
    $managedArtifactMode = "operator-generated-after-build"
    $snapshotGeneratedUtc = "not bundled"

    if ($hasFullManagedSnapshot) {
        foreach ($artifactName in $managedArtifactNames) {
            Copy-Item -LiteralPath (Join-Path $managedSourceRoot $artifactName) -Destination (Join-Path $releaseHistoryRoot $artifactName) -Force
            [void]$includedFiles.Add($artifactName)
        }

        $managedArtifactMode = "included-current-verification-artifacts"
        $snapshotGeneratedUtc = (Get-Item -LiteralPath (Join-Path $managedSourceRoot "managed-download-summary.txt")).LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
    }
    else {
        $hasTrackedManagedSnapshot = $true
        foreach ($artifactName in $managedArtifactNames) {
            if (-not (Test-Path -LiteralPath (Join-Path $releaseHistoryRoot $artifactName))) {
                $hasTrackedManagedSnapshot = $false
                break
            }
        }

        if ($hasTrackedManagedSnapshot) {
            foreach ($artifactName in $managedArtifactNames) {
                [void]$includedFiles.Add($artifactName)
            }

            $managedArtifactMode = "included-tracked-verification-artifacts"
            $snapshotGeneratedUtc = (Get-Item -LiteralPath (Join-Path $releaseHistoryRoot "managed-download-summary.txt")).LastWriteTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")
        }
    }

    $historyReadme = @"
Release Verification History
============================

Purpose:
- show what was verified for a release
- show when it was verified
- show what the managed download status looked like at ship time

Per-release folder:
- docs\Release-Verification-History\<coreVersion>\

Managed download artifact mode:
- included-current-verification-artifacts
  Current managed-download summary/revalidation files were copied from
  .verify\managed-download-revalidation\latest at build time.
- included-tracked-verification-artifacts
  Tracked release-history summary/revalidation files were bundled because no
  fresher .verify\managed-download-revalidation\latest snapshot was present.
- operator-generated-after-build
  No current managed-download snapshot was bundled. Run
  .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads after build and record
  the new files here before shipping a candidate/stable release.

Retention:
- keep one history folder for every shipped candidate/stable release
- keep timestamped repo-side revalidation snapshots for the last 12 months
- keep latest\ as the rolling working view only
"@
    Set-Content -LiteralPath $historyReadmePath -Value $historyReadme -Encoding UTF8

    $statusLines = New-Object System.Collections.Generic.List[string]
    [void]$statusLines.Add("Release Verification Status")
    [void]$statusLines.Add("=========================")
    [void]$statusLines.Add(("CoreVersion: " + $CoreVersion))
    [void]$statusLines.Add(("BuildTimestampUtc: " + $BuildTimestampUtc))
    [void]$statusLines.Add(("ReleaseType: " + $ReleaseType))
    [void]$statusLines.Add(("ManagedDownloadArtifactMode: " + $managedArtifactMode))
    [void]$statusLines.Add(("ManagedDownloadSnapshotGeneratedUtc: " + $snapshotGeneratedUtc))
    [void]$statusLines.Add(("HistoryFolder: docs\\Release-Verification-History\\" + $CoreVersion))
    [void]$statusLines.Add("")

    if ($includedFiles.Count -gt 0) {
        [void]$statusLines.Add("Bundled files:")
        foreach ($includedFile in $includedFiles) {
            [void]$statusLines.Add(("- " + $includedFile))
        }
    }
    else {
        [void]$statusLines.Add("Bundled files:")
        [void]$statusLines.Add("- none")
    }

    [void]$statusLines.Add("")
    [void]$statusLines.Add("Operator action:")
    if ($managedArtifactMode -in @("included-current-verification-artifacts", "included-tracked-verification-artifacts")) {
        [void]$statusLines.Add("- review the bundled managed-download snapshot before ship")
        [void]$statusLines.Add("- regenerate after build if a fresher verification pass is required")
    }
    else {
        [void]$statusLines.Add("- run .\\Verify-VentoyCore.ps1 -RevalidateManagedDownloads after build")
        [void]$statusLines.Add("- place or retain the generated managed-download files in this release history folder before ship")
    }

    Set-Content -LiteralPath $statusPath -Value $statusLines -Encoding UTF8

    return [PSCustomObject]@{
        HistoryRoot               = $historyRoot
        ReleaseHistoryRoot        = $releaseHistoryRoot
        ManagedArtifactMode       = $managedArtifactMode
        ManagedArtifactNames      = $managedArtifactNames
        IncludedFiles             = $includedFiles.ToArray()
        SnapshotGeneratedUtc      = $snapshotGeneratedUtc
        StatusPath                = $statusPath
        HistoryReadmePath         = $historyReadmePath
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$canonicalScriptRoot = Join-Path $repoRoot "ventoy-core"
$manifestRoot = Join-Path $repoRoot "manifests"
$bundleDocsRoot = Join-Path $repoRoot "Docs\ventoy-core\bundle"
$releaseFamilyRoot = Join-Path $repoRoot "release\ventoy-core"
$manifestPath = Join-Path $manifestRoot "ForgerEMS.updates.json"
$schemaPath = Join-Path $manifestRoot "ForgerEMS.updates.schema.json"
$vendorInventoryPath = Join-Path $manifestRoot "vendor.inventory.json"
$vendorInventorySchemaPath = Join-Path $manifestRoot "vendor.inventory.schema.json"
$updateScript = Join-Path $canonicalScriptRoot "Update-ForgerEMS.ps1"

foreach ($requiredPath in @($canonicalScriptRoot, $manifestPath, $schemaPath, $vendorInventoryPath, $vendorInventorySchemaPath, $bundleDocsRoot, $updateScript)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required release input not found: $requiredPath"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$coreName = if ($manifest.coreName) { [string]$manifest.coreName } else { "ForgerEMS Ventoy Core" }
$coreVersion = if ($manifest.coreVersion) { [string]$manifest.coreVersion } else { throw "Canonical manifest is missing coreVersion." }
$buildTimestampUtc = if ($manifest.buildTimestampUtc) { Format-BuildTimestamp -Value $manifest.buildTimestampUtc } else { throw "Canonical manifest is missing buildTimestampUtc." }
$releaseType = if ($manifest.releaseType) { ([string]$manifest.releaseType).Trim().ToLowerInvariant() } else { "dev" }
 $managedChecksumPolicy = Get-ManagedChecksumPolicy -Manifest $manifest
Assert-ReleaseTypeValue -Value $releaseType -FieldName "releaseType"
Assert-ManagedChecksumReleaseDiscipline -ReleaseType $releaseType -ManagedChecksumPolicy $managedChecksumPolicy

$missingManagedChecksumItems = Get-ManagedItemsMissingChecksumCoverage -Manifest $manifest
if ($missingManagedChecksumItems.Count -gt 0) {
    $missingSummary = $missingManagedChecksumItems | ForEach-Object { "{0} -> {1}" -f $_.Name, $_.Dest }
    $missingMessage = "Managed file items are missing checksum coverage: " + ($missingSummary -join "; ")

    if ($releaseType -in @("candidate", "stable")) {
        throw $missingMessage
    }

    Write-Status $missingMessage "WARN"
}

$vendorInventory = Get-Content -LiteralPath $vendorInventoryPath -Raw | ConvertFrom-Json
Assert-VendorInventoryContract `
    -Inventory $vendorInventory `
    -SourceName $vendorInventoryPath `
    -ExpectedCoreVersion $coreVersion `
    -ExpectedReleaseType $releaseType

if ($ShowVersion) {
    Write-Host ("{0} {1} ({2})" -f $coreName, $coreVersion, $buildTimestampUtc) -ForegroundColor Cyan
    Write-Host ("Release: " + $releaseType) -ForegroundColor DarkCyan
    Write-Host ("Manifest: " + $manifestPath) -ForegroundColor DarkCyan
    exit 0
}

Ensure-Dir -Path $releaseFamilyRoot

$targetRoot = Join-Path $releaseFamilyRoot $coreVersion
$zipPath = Join-Path $releaseFamilyRoot ($coreVersion + ".zip")

Assert-ChildPath -Parent $releaseFamilyRoot -Child $targetRoot
Assert-ChildPath -Parent $releaseFamilyRoot -Child $zipPath

$validationStamp = (Get-Date -Format "yyyyMMdd_HHmmss_fff") + "_" + ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$validationRoot = Join-Path $repoRoot (".verify\build-release-validate-" + $validationStamp)
Ensure-Dir -Path $validationRoot
$validationLog = Join-Path $validationRoot "validate-manifest.log"

Write-Status ("Validating manifest with canonical updater: " + $manifestPath) "INFO"
$validationOutput = & $powerShellExe -NoProfile -ExecutionPolicy Bypass -File $updateScript -UsbRoot $validationRoot -ManifestName $manifestPath -WhatIf 2>&1 | Out-String
$validationExitCode = $LASTEXITCODE
Set-Content -LiteralPath $validationLog -Value $validationOutput -Encoding UTF8

if ($validationExitCode -ne 0) {
    throw "Canonical manifest validation failed. See $validationLog"
}

if (Test-Path -LiteralPath $targetRoot) {
    Write-Status ("Refreshing existing release output: " + $targetRoot) "INFO"
    Get-ChildItem -LiteralPath $targetRoot -Force | Remove-Item -Recurse -Force
}
else {
    Ensure-Dir -Path $targetRoot
}

Ensure-Dir -Path (Join-Path $targetRoot "docs")
Ensure-Dir -Path (Join-Path $targetRoot "manifests")
Ensure-Dir -Path (Join-Path $targetRoot "tools")

foreach ($scriptName in @(
    "Setup-ForgerEMS.ps1",
    "Setup_USB_Toolkit.ps1",
    "Setup_Toolkit.ps1",
    "Update-ForgerEMS.ps1",
    "Verify-VentoyCore.ps1",
    "ForgerEMS.Runtime.ps1"
)) {
    Copy-Item -LiteralPath (Join-Path $canonicalScriptRoot $scriptName) -Destination (Join-Path $targetRoot $scriptName) -Force
}

Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $targetRoot "ForgerEMS.updates.json") -Force
Copy-Item -LiteralPath $schemaPath -Destination (Join-Path $targetRoot "manifests\ForgerEMS.updates.schema.json") -Force
Copy-Item -LiteralPath $vendorInventoryPath -Destination (Join-Path $targetRoot "manifests\vendor.inventory.json") -Force
Copy-Item -LiteralPath $vendorInventorySchemaPath -Destination (Join-Path $targetRoot "manifests\vendor.inventory.schema.json") -Force
Copy-Item -Path (Join-Path $bundleDocsRoot "*") -Destination (Join-Path $targetRoot "docs") -Recurse -Force
Copy-Item -LiteralPath (Join-Path $bundleDocsRoot "ScriptCommands.txt") -Destination (Join-Path $targetRoot "tools\ScriptCommands.txt") -Force

$releaseVerificationHistory = Write-ReleaseVerificationHistory `
    -RepoRoot $repoRoot `
    -TargetRoot $targetRoot `
    -CoreVersion $coreVersion `
    -BuildTimestampUtc $buildTimestampUtc `
    -ReleaseType $releaseType

$versionFile = @"
$coreName
Version: $coreVersion
Build:   $buildTimestampUtc
Release: $releaseType
Source:  manifests\ForgerEMS.updates.json
"@
Set-Content -LiteralPath (Join-Path $targetRoot "VERSION.txt") -Value $versionFile -Encoding UTF8

$bundleReadme = @"
Shippable Bundle: $coreName
========================================

Version: $coreVersion
Build:   $buildTimestampUtc
Release: $releaseType

Primary public entrypoints:
- Setup-ForgerEMS.ps1
- Update-ForgerEMS.ps1

Compatibility/support entrypoints:
- Setup_USB_Toolkit.ps1
- Setup_Toolkit.ps1
- Verify-VentoyCore.ps1
- ForgerEMS.Runtime.ps1

Included support content:
- docs\
- manifests\
- tools\ScriptCommands.txt
- CHECKSUMS.sha256
- SIGNATURE.txt
- docs\Release-Verification-History\$coreVersion\

Supported by this bundle:
- ventoy-core scripts and wrappers included here
- ForgerEMS.updates.json and manifest-driven setup/update lifecycle
- release-bundle docs and verification entrypoint

Not fully managed by this bundle:
- Tools\Portable payloads
- Drivers payloads
- MediCat.USB
- vendor binaries without a tracked source/build workflow

This bundle is intended to ship as a whole release folder.

Managed download verification history:
- mode: $($releaseVerificationHistory.ManagedArtifactMode)
- status file: docs\Release-Verification-History\$coreVersion\RELEASE-VERIFICATION-STATUS.txt
- if bundled, review the included managed-download summary/revalidation files
- if not bundled, run .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads after build
"@
Set-Content -LiteralPath (Join-Path $targetRoot "RELEASE-BUNDLE.txt") -Value $bundleReadme -Encoding UTF8

$checksumsPath = Join-Path $targetRoot "CHECKSUMS.sha256"
Write-ReleaseChecksums -TargetRoot $targetRoot -CoreVersion $coreVersion -OutputPath $checksumsPath
Write-Status ("Checksums written: " + $checksumsPath) "OK"

$signaturePath = Join-Path $targetRoot "SIGNATURE.txt"
Write-ReleaseSignature `
    -TargetRoot $targetRoot `
    -OutputPath $signaturePath `
    -CoreVersion $coreVersion `
    -BuildTimestampUtc $buildTimestampUtc `
    -ReleaseType $releaseType
Write-Status ("Signature written: " + $signaturePath) "OK"

if ($ZipOutput) {
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -LiteralPath $targetRoot -DestinationPath $zipPath -Force
    Write-Status ("Zip created: " + $zipPath) "OK"
}

Write-Status ("Release folder ready: " + $targetRoot) "OK"
Write-Status ("Bundle version: " + $coreVersion) "OK"
Write-Status ("Release classification: " + $releaseType) "OK"
Write-Status ("Managed download artifact mode: " + $releaseVerificationHistory.ManagedArtifactMode) "INFO"
Write-Status ("Release verification history: " + $releaseVerificationHistory.ReleaseHistoryRoot) "INFO"
