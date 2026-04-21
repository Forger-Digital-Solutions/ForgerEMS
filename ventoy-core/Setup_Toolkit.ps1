<#
.SYNOPSIS
Creates the ForgerEMS Ventoy toolkit layout and shipping support files.

.DESCRIPTION
Canonical implementation for the Ventoy core setup pass. This script resolves
the selected root, creates the toolkit folder layout, writes URL shortcuts and
manual note files, generates local documentation, and can seed the bundled
update manifest. For new human-facing usage, prefer Setup-ForgerEMS.ps1, which
delegates here without changing behavior.

.PARAMETER DriveLetter
Drive letter for the target USB or toolkit root, such as D.

.PARAMETER UsbRoot
Full path to the target toolkit location. If you point at the release bundle
folder itself, the script uses the USB drive root so the toolkit is created at
the top of the device.

.PARAMETER OwnerName
Optional owner name written into generated README content.

.PARAMETER ManifestName
Relative manifest path to seed under the selected root when -SeedManifest is
used. Defaults to ForgerEMS.updates.json.

.PARAMETER OpenCorePages
Open core official download pages after setup completes.

.PARAMETER OpenManualPages
Open manual/community download pages after setup completes.

.PARAMETER SeedManifest
Copy the bundled manifest into the selected root if it does not already exist.

.PARAMETER ForceManifestOverwrite
Overwrite an existing target manifest when used together with -SeedManifest.

.PARAMETER ShowVersion
Display the Ventoy core version/build metadata from the bundled manifest and
exit without making changes.

.EXAMPLE
.\Setup-ForgerEMS.ps1 -DriveLetter D -SeedManifest

.EXAMPLE
.\Setup_Toolkit.ps1 -UsbRoot "D:\" -OwnerName "Edward"

.EXAMPLE
.\Setup-ForgerEMS.ps1 -UsbRoot "H:\" -WhatIf

.EXAMPLE
.\Setup-ForgerEMS.ps1 -ShowVersion

.NOTES
Public PowerShell entrypoint. Safe to rerun. Supports -WhatIf.
#>

#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DriveLetter,
    [string]$UsbRoot = "",
    [string]$OwnerName = "",
    [string]$ManifestName = "ForgerEMS.updates.json",
    [switch]$OpenCorePages,
    [switch]$OpenManualPages,
    [switch]$SeedManifest,
    [switch]$ForceManifestOverwrite,
    [switch]$ShowVersion
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

$script:LogFile = $null

function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO","OK","WARN","ERROR")][string]$Level = "INFO"
    )

    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$ts][$Level] $Message"

    switch ($Level) {
        "INFO"  { Write-Host $line -ForegroundColor Cyan }
        "OK"    { Write-Host $line -ForegroundColor Green }
        "WARN"  { Write-Host $line -ForegroundColor Yellow }
        "ERROR" { Write-Host $line -ForegroundColor Red }
    }

    if ($script:LogFile) {
        $logParent = Split-Path -Parent $script:LogFile
        if ($logParent -and (Test-Path -LiteralPath $logParent)) {
            Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
        }
    }
}

function J {
    param(
        [Parameter(Mandatory)][string]$A,
        [Parameter(Mandatory)][string]$B
    )
    Join-Path -Path $A -ChildPath $B
}

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Get-PathDriveRoot {
    param([Parameter(Mandatory)][string]$Path)

    try {
        $fullPath = [IO.Path]::GetFullPath($Path)
        $driveRoot = [IO.Path]::GetPathRoot($fullPath)
        if ([string]::IsNullOrWhiteSpace($driveRoot)) {
            return $null
        }

        return $driveRoot.TrimEnd('\')
    }
    catch {
        return $null
    }
}

function Test-IsReleaseBundleRoot {
    param([Parameter(Mandatory)][string]$Path)

    foreach ($marker in @("RELEASE-BUNDLE.txt", "VERSION.txt", "ForgerEMS.updates.json")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $marker))) {
            return $false
        }
    }

    return $true
}

function Find-ReleaseBundleRoot {
    param([Parameter(Mandatory)][string]$Path)

    $current = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')

    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-IsReleaseBundleRoot -Path $current) {
            return $current
        }

        $parentInfo = [IO.Directory]::GetParent($current + '\')
        if ($null -eq $parentInfo) {
            break
        }

        $parent = $parentInfo.FullName.TrimEnd('\')
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $null
}

function Test-PathWithinRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $normalizedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')

    return ($normalizedPath -eq $normalizedRoot) -or $normalizedPath.StartsWith($normalizedRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsReleaseBundleScratchPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$BundleRoot
    )

    $scratchRoot = [IO.Path]::GetFullPath((Join-Path $BundleRoot ".verify")).TrimEnd('\')
    $normalizedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')

    return Test-PathWithinRoot -Path $normalizedPath -Root $scratchRoot
}

function Resolve-SelectedUsbRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Source
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')
    $bundleRoot = Find-ReleaseBundleRoot -Path $resolvedPath
    if (-not $bundleRoot) {
        $bundleRoot = Find-ReleaseBundleRoot -Path $PSScriptRoot
    }

    if ($bundleRoot -and (Test-PathWithinRoot -Path $resolvedPath -Root $bundleRoot)) {
        if (Test-IsReleaseBundleScratchPath -Path $resolvedPath -BundleRoot $bundleRoot) {
            return $resolvedPath
        }

        $driveRoot = Get-PathDriveRoot -Path $resolvedPath
        if ($driveRoot -and $resolvedPath -ne $driveRoot) {
            Write-Host ("{0} '{1}' is inside the release bundle. Using USB root '{2}' instead." -f $Source, $resolvedPath, $driveRoot) -ForegroundColor Yellow
            return $driveRoot
        }
    }

    return $resolvedPath
}

function Resolve-RootChildPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "Relative path cannot be empty."
    }

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Relative path must stay relative to the selected root. Path='$RelativePath'"
    }

    $normalizedRoot = Get-NormalizedPath -Path $Root
    $fullPath = [IO.Path]::GetFullPath((Join-Path $normalizedRoot $RelativePath))
    $isUnderRoot = $fullPath.StartsWith($normalizedRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)

    if (($fullPath.TrimEnd('\') -ne $normalizedRoot) -and -not $isUnderRoot) {
        throw "Resolved path escapes the selected root. Root='$normalizedRoot' Relative='$RelativePath' Resolved='$fullPath'"
    }

    return $fullPath
}

function Get-BundledManifestTemplatePath {
    $candidates = @(
        (Join-Path $PSScriptRoot "ForgerEMS.updates.json"),
        (Join-Path $PSScriptRoot "manifests\ForgerEMS.updates.json"),
        (Join-Path (Split-Path -Parent $PSScriptRoot) "manifests\ForgerEMS.updates.json")
    )

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Bundled manifest template not found. Checked: $($candidates -join '; ')"
}

function Assert-ManifestStringField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value) { return }

    if ([string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "$FieldName must be a non-empty JSON string."
    }
}

function Assert-ManifestTimestampField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    try {
        [DateTimeOffset]::Parse([string]$Value) | Out-Null
    }
    catch {
        throw "$FieldName must be an ISO-like date/time string."
    }
}

function Assert-ManifestReleaseTypeField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("dev", "candidate", "stable")) {
        throw "$FieldName must be 'dev', 'candidate', or 'stable'."
    }
}

function Assert-ManifestChecksumPolicyField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("warn", "require-for-release")) {
        throw "$FieldName must be 'warn' or 'require-for-release'."
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

function Assert-ManifestBooleanField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value) { return }

    if ($Value -isnot [bool]) {
        throw "$FieldName must be a JSON boolean."
    }
}

function Assert-ManifestPositiveIntegerField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName,
        [int]$Minimum = 1
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    if (-not ([string]$Value -match '^\d+$')) {
        throw "$FieldName must be a whole-number JSON value."
    }

    if ([int64]$Value -lt $Minimum) {
        throw "$FieldName must be greater than or equal to $Minimum."
    }
}

function Assert-ManifestSha256Field {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    if (-not ([string]$Value -match '^[a-fA-F0-9]{64}$')) {
        throw "$FieldName must be a 64-character SHA-256 hex string."
    }
}

function Assert-ManifestSourceTypeField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("sourceforge", "github-release", "official-mirror", "official-version-path")) {
        throw "$FieldName must be 'sourceforge', 'github-release', 'official-mirror', or 'official-version-path'."
    }
}

function Assert-ManifestFragilityLevelField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("low", "medium", "high")) {
        throw "$FieldName must be 'low', 'medium', or 'high'."
    }
}

function Assert-ManifestContract {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$SourceName
    )

    if ($null -eq $Manifest) {
        throw "Manifest '$SourceName' is empty or invalid."
    }

    if ($null -eq $Manifest.settings) {
        Add-Member -InputObject $Manifest -MemberType NoteProperty -Name settings -Value ([PSCustomObject]@{}) -Force
    }

    if ($null -eq $Manifest.items) {
        throw "Manifest '$SourceName' must contain an items array."
    }

    $settings = $Manifest.settings

    foreach ($folderField in @("downloadFolder", "archiveFolder", "logFolder")) {
        $folderValue = $settings.$folderField
        if ($null -ne $folderValue -and -not [string]::IsNullOrWhiteSpace([string]$folderValue)) {
            Resolve-RootChildPath -Root $Root -RelativePath ([string]$folderValue) | Out-Null
        }
    }

    Assert-ManifestPositiveIntegerField -Value $Manifest.manifestVersion -FieldName "manifestVersion"
    Assert-ManifestStringField -Value $Manifest.coreName -FieldName "coreName"
    Assert-ManifestStringField -Value $Manifest.coreVersion -FieldName "coreVersion"
    Assert-ManifestTimestampField -Value $Manifest.buildTimestampUtc -FieldName "buildTimestampUtc"
    Assert-ManifestReleaseTypeField -Value $Manifest.releaseType -FieldName "releaseType"
    Assert-ManifestChecksumPolicyField -Value $Manifest.managedChecksumPolicy -FieldName "managedChecksumPolicy"
    Assert-ManifestPositiveIntegerField -Value $settings.timeoutSec -FieldName "settings.timeoutSec"
    Assert-ManifestPositiveIntegerField -Value $settings.retryCount -FieldName "settings.retryCount"
    Assert-ManifestPositiveIntegerField -Value $settings.maxArchivePerItem -FieldName "settings.maxArchivePerItem"

    $items = @($Manifest.items)
    if ($items.Count -eq 0) {
        throw "Manifest '$SourceName' must contain at least one item."
    }

    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $prefix = "items[$i]"

        if ($null -eq $item) {
            throw "$prefix cannot be null."
        }

        $name = [string]$item.name
        $url = [string]$item.url
        $dest = [string]$item.dest
        $type = if ($item.type) { ([string]$item.type).Trim().ToLowerInvariant() } else { "file" }

        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "$prefix.name is required."
        }

        if ([string]::IsNullOrWhiteSpace($url)) {
            throw "$prefix.url is required."
        }

        if ([string]::IsNullOrWhiteSpace($dest)) {
            throw "$prefix.dest is required."
        }

        if ($type -notin @("file", "page")) {
            throw "$prefix.type must be 'file' or 'page'."
        }

        Resolve-RootChildPath -Root $Root -RelativePath $dest | Out-Null
        Assert-ManifestBooleanField -Value $item.enabled -FieldName "$prefix.enabled"
        Assert-ManifestBooleanField -Value $item.archive -FieldName "$prefix.archive"
        Assert-ManifestPositiveIntegerField -Value $item.timeoutSec -FieldName "$prefix.timeoutSec"
        Assert-ManifestSha256Field -Value $item.sha256 -FieldName "$prefix.sha256"
        Assert-ManifestSourceTypeField -Value $item.sourceType -FieldName "$prefix.sourceType"
        Assert-ManifestFragilityLevelField -Value $item.fragilityLevel -FieldName "$prefix.fragilityLevel"
        Assert-ManifestStringField -Value $item.fallbackRule -FieldName "$prefix.fallbackRule"
        Assert-ManifestPositiveIntegerField -Value $item.maintenanceRank -FieldName "$prefix.maintenanceRank"
        Assert-ManifestBooleanField -Value $item.borderline -FieldName "$prefix.borderline"

        if ($null -ne $item.sha256Url -and -not [string]::IsNullOrWhiteSpace([string]$item.sha256Url)) {
            if ($type -ne "file") {
                throw "$prefix.sha256Url is only valid for file items."
            }
        }

        $hasResilienceMetadata = (
            ($null -ne $item.sourceType -and -not [string]::IsNullOrWhiteSpace([string]$item.sourceType)) -or
            ($null -ne $item.fragilityLevel -and -not [string]::IsNullOrWhiteSpace([string]$item.fragilityLevel)) -or
            ($null -ne $item.fallbackRule -and -not [string]::IsNullOrWhiteSpace([string]$item.fallbackRule)) -or
            ($null -ne $item.maintenanceRank -and -not [string]::IsNullOrWhiteSpace([string]$item.maintenanceRank)) -or
            ($null -ne $item.borderline)
        )

        if ($hasResilienceMetadata -and $type -ne "file") {
            throw "$prefix.sourceType, $prefix.fragilityLevel, $prefix.fallbackRule, $prefix.maintenanceRank, and $prefix.borderline are only valid for file items."
        }
    }
}

function Get-BundledManifestTemplate {
    param([Parameter(Mandatory)][string]$Root)

    $manifestPath = Get-BundledManifestTemplatePath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-ManifestContract -Manifest $manifest -Root $Root -SourceName $manifestPath
    return $manifest
}

function Get-VentoyCoreVersionInfo {
    $manifestPath = Get-BundledManifestTemplatePath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    return [PSCustomObject]@{
        Name              = if ($manifest.coreName) { [string]$manifest.coreName } else { "ForgerEMS Ventoy Core" }
        Version           = if ($manifest.coreVersion) { [string]$manifest.coreVersion } else { "0.0.0-dev" }
        BuildTimestampUtc = Format-BuildTimestamp -Value $manifest.buildTimestampUtc
        ReleaseType       = if ($manifest.releaseType) { ([string]$manifest.releaseType).Trim().ToLowerInvariant() } else { "dev" }
        ManifestPath      = $manifestPath
    }
}

function Show-VentoyCoreVersionInfo {
    $info = Get-VentoyCoreVersionInfo
    Write-Host ("{0} {1} ({2})" -f $info.Name, $info.Version, $info.BuildTimestampUtc) -ForegroundColor Cyan
    Write-Host ("Release: " + $info.ReleaseType) -ForegroundColor DarkCyan
    Write-Host ("Manifest: " + $info.ManifestPath) -ForegroundColor DarkCyan
}

function Get-ShortcutDefinitionsFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $manifest = Get-BundledManifestTemplate -Root $Root
    $links = @()

    foreach ($item in $manifest.items) {
        $enabled = $true
        if ($null -ne $item.enabled) {
            $enabled = [bool]$item.enabled
        }

        $type = if ($item.type) { [string]$item.type } else { "file" }
        if (-not $enabled -or $type.Trim().ToLowerInvariant() -ne "page") {
            continue
        }

        $destPath = Resolve-RootChildPath -Root $Root -RelativePath ([string]$item.dest)
        $links += [PSCustomObject]@{
            Folder = Split-Path -Parent $destPath
            Name   = Split-Path -Leaf $destPath
            Url    = [string]$item.url
        }
    }

    return $links
}

function Get-ManagedCatalogDataFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $manifest = Get-BundledManifestTemplate -Root $Root
    $auto = New-Object System.Collections.Generic.List[object]
    $manual = New-Object System.Collections.Generic.List[object]
    $review = New-Object System.Collections.Generic.List[object]

    foreach ($item in @($manifest.items)) {
        if ($null -eq $item) { continue }

        $isEnabled = $true
        if ($null -ne $item.enabled) {
            $isEnabled = [bool]$item.enabled
        }

        if (-not $isEnabled) { continue }

        $type = if ($item.type) { ([string]$item.type).Trim().ToLowerInvariant() } else { "file" }
        $notes = if ($item.notes) { ([string]$item.notes).Trim().ToLowerInvariant() } else { "" }

        if ($type -eq "file") {
            [void]$auto.Add($item)
            continue
        }

        if ($notes.StartsWith("manual only:")) {
            [void]$manual.Add($item)
            continue
        }

        if ($notes.StartsWith("review first:")) {
            [void]$review.Add($item)
        }
    }

    return [PSCustomObject]@{
        Manifest = $manifest
        Auto     = $auto.ToArray()
        Manual   = $manual.ToArray()
        Review   = $review.ToArray()
    }
}

function Get-ManagedDownloadRankingFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $catalogData = Get-ManagedCatalogDataFromManifest -Root $Root
    return @(
        $catalogData.Auto |
        Sort-Object `
            @{ Expression = {
                    if ($null -ne $_.maintenanceRank -and -not [string]::IsNullOrWhiteSpace([string]$_.maintenanceRank)) {
                        [int]$_.maintenanceRank
                    }
                    else {
                        [int]::MaxValue
                    }
                }
            },
            @{ Expression = { [string]$_.name } }
    )
}

function Get-ManagedDownloadChecksumPostureFromItem {
    param([Parameter(Mandatory)]$Item)

    $hasPinnedChecksum = -not [string]::IsNullOrWhiteSpace([string]$Item.sha256)
    $hasChecksumUrl = -not [string]::IsNullOrWhiteSpace([string]$Item.sha256Url)

    if ($hasPinnedChecksum -and $hasChecksumUrl) { return "pinned+remote" }
    if ($hasPinnedChecksum) { return "pinned-only" }
    if ($hasChecksumUrl) { return "remote-only" }
    return "none"
}

function Get-ManagedDownloadStatusFromItem {
    param([Parameter(Mandatory)]$Item)

    $checksumPosture = Get-ManagedDownloadChecksumPostureFromItem -Item $Item
    if ($checksumPosture -eq "pinned-only") { return "OK-LIMITED" }
    return "OK"
}

function Test-ManagedDownloadBorderline {
    param([Parameter(Mandatory)]$Item)

    if ($null -eq $Item.borderline) {
        return $false
    }

    return [bool]$Item.borderline
}

function Get-ManagedDownloadSummaryFromItems {
    param([Parameter(Mandatory)][object[]]$Items)

    $rankedItems = @(
        $Items |
        Sort-Object `
            @{ Expression = {
                    if ($null -ne $_.maintenanceRank -and -not [string]::IsNullOrWhiteSpace([string]$_.maintenanceRank)) {
                        [int]$_.maintenanceRank
                    }
                    else {
                        [int]::MaxValue
                    }
                }
            },
            @{ Expression = { [string]$_.name } }
    )

    $borderlineItems = @($rankedItems | Where-Object { Test-ManagedDownloadBorderline -Item $_ })
    $nextCycleItems = @($rankedItems | Select-Object -First ([Math]::Min(5, $rankedItems.Count)))
    $topPriorityItems = @($rankedItems | Where-Object { [int]$_.maintenanceRank -le 7 })

    return [PSCustomObject]@{
        RankedItems         = $rankedItems
        TotalCount          = $rankedItems.Count
        HighCount           = @($rankedItems | Where-Object { ([string]$_.fragilityLevel).Trim().ToLowerInvariant() -eq "high" }).Count
        MediumCount         = @($rankedItems | Where-Object { ([string]$_.fragilityLevel).Trim().ToLowerInvariant() -eq "medium" }).Count
        LowCount            = @($rankedItems | Where-Object { ([string]$_.fragilityLevel).Trim().ToLowerInvariant() -eq "low" }).Count
        PinnedOnlyCount     = @($rankedItems | Where-Object { (Get-ManagedDownloadChecksumPostureFromItem -Item $_) -eq "pinned-only" }).Count
        PinnedRemoteCount   = @($rankedItems | Where-Object { (Get-ManagedDownloadChecksumPostureFromItem -Item $_) -eq "pinned+remote" }).Count
        RemoteOnlyCount     = @($rankedItems | Where-Object { (Get-ManagedDownloadChecksumPostureFromItem -Item $_) -eq "remote-only" }).Count
        OkCount             = @($rankedItems | Where-Object { (Get-ManagedDownloadStatusFromItem -Item $_) -eq "OK" }).Count
        OkLimitedCount      = @($rankedItems | Where-Object { (Get-ManagedDownloadStatusFromItem -Item $_) -eq "OK-LIMITED" }).Count
        BorderlineItems     = $borderlineItems
        NextCycleItems      = $nextCycleItems
        TopPriorityItems    = $topPriorityItems
    }
}

function Get-DownloadCatalogTextFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $catalogData = Get-ManagedCatalogDataFromManifest -Root $Root
    $summary = Get-ManagedDownloadSummaryFromItems -Items @($catalogData.Auto)
    $lines = New-Object System.Collections.Generic.List[string]

    [void]$lines.Add("ForgerEMS Download Catalog")
    [void]$lines.Add("==========================")
    [void]$lines.Add("")
    [void]$lines.Add("The Ventoy core uses three practical catalog buckets:")
    [void]$lines.Add("")
    [void]$lines.Add("- auto-download safe")
    [void]$lines.Add("- manual only")
    [void]$lines.Add("- review-first")
    [void]$lines.Add("")
    [void]$lines.Add("Note: 'safe' still depends on upstream availability.")
    [void]$lines.Add("It means the manifest points at an official direct")
    [void]$lines.Add("artifact with checksum coverage today. It does not")
    [void]$lines.Add("guarantee that the upstream will stay reachable or")
    [void]$lines.Add("unchanged forever.")
    [void]$lines.Add("")
    [void]$lines.Add("Health snapshot")
    [void]$lines.Add("---------------")
    [void]$lines.Add(("Total safe items: " + $summary.TotalCount))
    [void]$lines.Add(("Fragility: high " + $summary.HighCount + " | medium " + $summary.MediumCount + " | low " + $summary.LowCount))
    [void]$lines.Add(("Checksum posture: pinned-only " + $summary.PinnedOnlyCount + " | pinned+remote " + $summary.PinnedRemoteCount + " | remote-only " + $summary.RemoteOnlyCount))
    [void]$lines.Add(("Status: OK " + $summary.OkCount + " | OK-LIMITED " + $summary.OkLimitedCount + " | DRIFT 0"))
    [void]$lines.Add(("Borderline: " + (($summary.BorderlineItems | ForEach-Object { [string]$_.name }) -join "; ")))
    [void]$lines.Add(("Inspect first next cycle: " + (($summary.NextCycleItems | ForEach-Object { "[" + [string]$_.maintenanceRank + "] " + [string]$_.name }) -join "; ")))
    [void]$lines.Add("")
    [void]$lines.Add("Auto-download safe")
    [void]$lines.Add("------------------")
    [void]$lines.Add("- manifest-managed file item")
    [void]$lines.Add("- official direct artifact URL")
    [void]$lines.Add("- no gated/account/clickthrough flow")
    [void]$lines.Add("- checksum coverage in the manifest")
    [void]$lines.Add("- status class is baseline metadata, not a live URL probe")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    foreach ($item in @($summary.RankedItems)) {
        $status = Get-ManagedDownloadStatusFromItem -Item $item
        $checksumPosture = Get-ManagedDownloadChecksumPostureFromItem -Item $item
        [void]$lines.Add(("[{0}] {1} | {2} | {3} | {4} | {5}" -f [int]$item.maintenanceRank, $status, [string]$item.fragilityLevel, [string]$item.sourceType, $checksumPosture, [string]$item.name))
        if (Test-ManagedDownloadBorderline -Item $item) {
            [void]$lines.Add("  borderline: yes")
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("Manual only")
    [void]$lines.Add("-----------")
    [void]$lines.Add("- manifest-managed page shortcut only")
    [void]$lines.Add("- licensing, redistribution, or install flow makes")
    [void]$lines.Add("  automation inappropriate in the stable baseline")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    foreach ($item in @($catalogData.Manual)) {
        [void]$lines.Add("- " + [string]$item.name)
    }
    [void]$lines.Add("")
    [void]$lines.Add("Review-first")
    [void]$lines.Add("------------")
    [void]$lines.Add("- manifest-managed page shortcut only")
    [void]$lines.Add("- still manual today because checksum coverage,")
    [void]$lines.Add("  provenance, or operational stability is not yet")
    [void]$lines.Add("  good enough for the stable baseline")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    foreach ($item in @($catalogData.Review)) {
        [void]$lines.Add("- " + [string]$item.name)
    }
    [void]$lines.Add("")
    [void]$lines.Add("See MANAGED-DOWNLOAD-MAINTENANCE.txt for fragility")
    [void]$lines.Add("ranking, fallback rules, cadence, and recovery steps.")

    return ($lines -join [Environment]::NewLine)
}

function Get-ManagedDownloadMaintenanceTextFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $catalogData = Get-ManagedCatalogDataFromManifest -Root $Root
    $summary = Get-ManagedDownloadSummaryFromItems -Items @($catalogData.Auto)
    $rankedItems = @($summary.RankedItems)
    $lines = New-Object System.Collections.Generic.List[string]

    [void]$lines.Add("ForgerEMS Managed Download Maintenance")
    [void]$lines.Add("======================================")
    [void]$lines.Add("")
    [void]$lines.Add("Scope")
    [void]$lines.Add("-----")
    [void]$lines.Add("This guide applies to the 16 manifest-managed")
    [void]$lines.Add("auto-download-safe file entries only.")
    [void]$lines.Add("")
    [void]$lines.Add("Safe still depends on upstream availability.")
    [void]$lines.Add("If a vendor moves or removes an official artifact or")
    [void]$lines.Add("checksum source, the entry may need repair or demotion.")
    [void]$lines.Add("")
    [void]$lines.Add("Health snapshot")
    [void]$lines.Add("---------------")
    [void]$lines.Add(("Total safe items: " + $summary.TotalCount))
    [void]$lines.Add(("Fragility: high " + $summary.HighCount + " | medium " + $summary.MediumCount + " | low " + $summary.LowCount))
    [void]$lines.Add(("Checksum posture: pinned-only " + $summary.PinnedOnlyCount + " | pinned+remote " + $summary.PinnedRemoteCount + " | remote-only " + $summary.RemoteOnlyCount))
    [void]$lines.Add(("Status: OK " + $summary.OkCount + " | OK-LIMITED " + $summary.OkLimitedCount + " | DRIFT 0"))
    [void]$lines.Add(("Borderline: " + (($summary.BorderlineItems | ForEach-Object { [string]$_.name }) -join "; ")))
    [void]$lines.Add(("Inspect first next cycle: " + (($summary.NextCycleItems | ForEach-Object { "[" + [string]$_.maintenanceRank + "] " + [string]$_.name }) -join "; ")))
    [void]$lines.Add("")
    [void]$lines.Add("Status meanings")
    [void]$lines.Add("---------------")
    [void]$lines.Add("- OK -> URL plus remote checksum coverage should participate")
    [void]$lines.Add("  fully in live revalidation")
    [void]$lines.Add("- OK-LIMITED -> URL can be probed live, but checksum")
    [void]$lines.Add("  confirmation still depends on the pinned manifest hash")
    [void]$lines.Add("")
    [void]$lines.Add("Revalidation workflow")
    [void]$lines.Add("---------------------")
    [void]$lines.Add("Run:")
    [void]$lines.Add("  .\\Verify-VentoyCore.ps1 -RevalidateManagedDownloads")
    [void]$lines.Add("")
    [void]$lines.Add("This checks:")
    [void]$lines.Add("- each enabled managed file URL still resolves")
    [void]$lines.Add("- each remote checksum source still resolves")
    [void]$lines.Add("- drift is reported without changing the manifest")
    [void]$lines.Add("")
    [void]$lines.Add("Archive outputs are written under:")
    [void]$lines.Add("- .\\.verify\\managed-download-revalidation\\<timestamp>\\")
    [void]$lines.Add("- .\\.verify\\managed-download-revalidation\\latest\\")
    [void]$lines.Add("")
    [void]$lines.Add("Expected files:")
    [void]$lines.Add("- managed-download-summary.txt")
    [void]$lines.Add("- managed-download-revalidation.csv")
    [void]$lines.Add("- managed-download-revalidation.txt")
    [void]$lines.Add("")
    [void]$lines.Add("Failure guidance")
    [void]$lines.Add("----------------")
    [void]$lines.Add("If a managed download breaks:")
    [void]$lines.Add("1. Confirm whether the artifact moved on the official")
    [void]$lines.Add("   project page, release index, or official release feed.")
    [void]$lines.Add("2. Confirm checksum coverage cleanly from an official")
    [void]$lines.Add("   checksum file, asset digest, or a freshly verified")
    [void]$lines.Add("   hash from the live official artifact.")
    [void]$lines.Add("3. Patch the manifest only when the new URL is still an")
    [void]$lines.Add("   official direct artifact with the same legal/operational")
    [void]$lines.Add("   safety profile.")
    [void]$lines.Add("4. Demote back to review-first when the flow becomes page-")
    [void]$lines.Add("   only, account-gated, EULA-sensitive, checksum-poor, or")
    [void]$lines.Add("   provenance becomes ambiguous.")
    [void]$lines.Add("5. Do not patch around a broken upstream with third-party")
    [void]$lines.Add("   mirrors, repacks, scraper URLs, or unofficial checksum")
    [void]$lines.Add("   sources just to keep automation alive.")
    [void]$lines.Add("")
    [void]$lines.Add("Demote immediately when")
    [void]$lines.Add("----------------------")
    [void]$lines.Add("- the URL no longer resolves cleanly from the official source")
    [void]$lines.Add("- the artifact can no longer be matched confidently to the")
    [void]$lines.Add("  intended versioned payload")
    [void]$lines.Add("- checksum coverage can no longer be confirmed or pinned")
    [void]$lines.Add("  safely from the official artifact/source")
    [void]$lines.Add("- the upstream flow becomes gated, clickthrough-based,")
    [void]$lines.Add("  account-bound, or otherwise ambiguous")
    [void]$lines.Add("")
    [void]$lines.Add("Decision guide")
    [void]$lines.Add("--------------")
    [void]$lines.Add("- high-fragility item drifts -> stop automation for that")
    [void]$lines.Add("  item, confirm the official replacement path/checksum,")
    [void]$lines.Add("  then patch or demote before release use")
    [void]$lines.Add("- pinned-only item changes upstream -> recompute the hash")
    [void]$lines.Add("  from the live official artifact, update the pin, and")
    [void]$lines.Add("  demote if the artifact match is no longer confident")
    [void]$lines.Add("- checksum source disappears -> rely only on a safely")
    [void]$lines.Add("  recomputed pinned hash if the official artifact is still")
    [void]$lines.Add("  clear; otherwise demote")
    [void]$lines.Add("- formerly safe item must be demoted -> convert it back to")
    [void]$lines.Add("  a review-first page entry, preserve the official page")
    [void]$lines.Add("  shortcut, and remove automated download claims")
    [void]$lines.Add("")
    [void]$lines.Add("Maintenance cadence")
    [void]$lines.Add("-------------------")
    [void]$lines.Add("- monthly: run the revalidation workflow")
    [void]$lines.Add("- before shipping or rebuilding a toolkit USB")
    [void]$lines.Add("- after any manifest URL/checksum edit")
    [void]$lines.Add("- quarterly: manually review high-fragility items first")
    [void]$lines.Add("")
    [void]$lines.Add("Retention policy")
    [void]$lines.Add("----------------")
    [void]$lines.Add("- timestamped revalidation snapshots -> keep 12 months")
    [void]$lines.Add("  plus any snapshot tied to a shipped release")
    [void]$lines.Add("- release verification artifacts -> keep for every shipped")
    [void]$lines.Add("  candidate/stable release while that release is retained")
    [void]$lines.Add("- summary reports -> latest\\ is rolling only; timestamped")
    [void]$lines.Add("  summaries follow the same 12-month + shipped-release rule")
    [void]$lines.Add("")
    [void]$lines.Add("Pre-release gate")
    [void]$lines.Add("----------------")
    [void]$lines.Add("Before any candidate/stable build intended for distribution:")
    [void]$lines.Add("1. Run .\\Verify-VentoyCore.ps1")
    [void]$lines.Add("2. Run .\\Verify-VentoyCore.ps1 -RevalidateManagedDownloads")
    [void]$lines.Add("3. Review managed-download-summary.txt")
    [void]$lines.Add("4. Ensure maintenance ranks 1-7 have no unresolved drift")
    [void]$lines.Add("   or provenance/checksum ambiguity")
    [void]$lines.Add("")
    [void]$lines.Add("Priority ranking")
    [void]$lines.Add("----------------")
    foreach ($item in $rankedItems) {
        $status = Get-ManagedDownloadStatusFromItem -Item $item
        $checksumPosture = Get-ManagedDownloadChecksumPostureFromItem -Item $item
        [void]$lines.Add(("[{0}] {1}" -f [int]$item.maintenanceRank, [string]$item.name))
        [void]$lines.Add(("  status: " + $status + " | fragility: " + [string]$item.fragilityLevel + " | source: " + [string]$item.sourceType + " | checksum: " + $checksumPosture))
        if (Test-ManagedDownloadBorderline -Item $item) {
            [void]$lines.Add("  borderline: yes")
        }
        [void]$lines.Add(("  fallback: " + [string]$item.fallbackRule))
        [void]$lines.Add("")
    }

    return ($lines -join [Environment]::NewLine)
}

function Ensure-Folder {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, "Create directory")) {
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
            Write-Log "Created folder: $Path" "OK"
        }
        else {
            Write-Log "Would create folder: $Path" "INFO"
        }
    }
    else {
        Write-Log "Exists: $Path" "INFO"
    }
}

function Resolve-UsbRoot {
    param(
        [string]$Drive,
        [string]$Root
    )

    if ($Drive -and $Root) {
        throw "Use either -DriveLetter or -UsbRoot, not both."
    }

    if ($Root) {
        $candidate = $Root.Trim()
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Path not found: $candidate"
        }
        return Resolve-SelectedUsbRoot -Path $candidate -Source "-UsbRoot"
    }

    if ($Drive) {
        $letter = $Drive.Trim().TrimEnd(":").ToUpper()
        if (-not $letter) {
            throw "Invalid drive letter."
        }

        $candidate = "$letter`:\"
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Drive not found: $candidate"
        }

        return $candidate.TrimEnd('\')
    }

    $currentBundleRoot = Find-ReleaseBundleRoot -Path $PSScriptRoot
    if ($currentBundleRoot) {
        $scriptDriveRoot = Get-PathDriveRoot -Path $currentBundleRoot
        if ($scriptDriveRoot) {
            Write-Host ("Detected the release bundle at '{0}'. Using USB root '{1}'." -f $currentBundleRoot, $scriptDriveRoot) -ForegroundColor Cyan
            return $scriptDriveRoot
        }
    }

    Write-Host ""
    Write-Host "=== ForgerEMS Unified Builder ===" -ForegroundColor Cyan
    Write-Host "Enter either a drive letter (D) or a full path on the target USB." -ForegroundColor Cyan
    Write-Host "If you choose this release bundle folder, the script will use the USB root." -ForegroundColor Cyan
    Write-Host ""

    $entered = Read-Host "Enter USB drive letter or target path"
    if (-not $entered) {
        throw "No drive/root provided."
    }

    $entered = $entered.Trim()

    if ($entered -match '^[A-Za-z]:?$') {
        $letter = $entered.TrimEnd(':').ToUpper()
        $candidate = "$letter`:\"
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Drive not found: $candidate"
        }
        return $candidate.TrimEnd('\')
    }

    if (-not (Test-Path -LiteralPath $entered)) {
        throw "Path not found: $entered"
    }

    return Resolve-SelectedUsbRoot -Path $entered -Source "Requested path"
}

function Write-UrlShortcut {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$Url
    )

    $content = @"
[InternetShortcut]
URL=$Url
"@

    if ($PSCmdlet.ShouldProcess($ShortcutPath, "Write URL shortcut")) {
        Set-Content -LiteralPath $ShortcutPath -Value $content -Encoding ASCII
        Write-Log "Shortcut written: $ShortcutPath" "OK"
    }
    else {
        Write-Log "Would write shortcut: $ShortcutPath" "INFO"
    }
}

function Add-ManualLinkFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][string]$Url,
        [string]$Notes = ""
    )

    $content = @"
$Title
$Url

$Notes
"@

    if ($PSCmdlet.ShouldProcess($Path, "Write manual note file")) {
        Set-Content -LiteralPath $Path -Value $content -Encoding UTF8
        Write-Log "Manual note written: $Path" "OK"
    }
    else {
        Write-Log "Would write manual note: $Path" "INFO"
    }
}

function Open-UrlsIfRequested {
    param(
        [Parameter(Mandatory)][string[]]$Urls,
        [switch]$Enabled
    )

    if (-not $Enabled) { return }

    foreach ($u in $Urls) {
        try {
            if ($PSCmdlet.ShouldProcess($u, "Open page in browser")) {
                Start-Process $u | Out-Null
                Write-Log "Opened page: $u" "INFO"
            }
            else {
                Write-Log "Would open page: $u" "INFO"
            }
        }
        catch {
            Write-Log "Failed to open page: $u :: $($_.Exception.Message)" "WARN"
        }
    }
}

if ($ShowVersion) {
    Show-VentoyCoreVersionInfo
    return
}

$root = Resolve-UsbRoot -Drive $DriveLetter -Root $UsbRoot

$versionInfo = Get-VentoyCoreVersionInfo

$logDir = J $root "_logs"
Ensure-Folder -Path $logDir
$script:LogFile = Join-Path $logDir ("setup_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".log")

Write-Log ("Ventoy core: {0} {1} ({2})" -f $versionInfo.Name, $versionInfo.Version, $versionInfo.BuildTimestampUtc) "INFO"
Write-Log ("Release: " + $versionInfo.ReleaseType) "INFO"
Write-Log "Using root: $root" "INFO"

$paths = @(
    (J $root "ISO"),
    (J $root "ISO\Linux"),
    (J $root "ISO\Tools"),
    (J $root "ISO\Windows"),

    (J $root "Tools"),
    (J $root "Tools\Portable"),
    (J $root "Tools\Portable\Disk"),
    (J $root "Tools\Portable\Hardware"),
    (J $root "Tools\Portable\Network"),
    (J $root "Tools\Portable\System"),
    (J $root "Tools\Portable\Remote"),
    (J $root "Tools\Portable\USB"),
    (J $root "Tools\Portable\GPU"),
    (J $root "Tools\Portable\Security"),

    (J $root "Drivers"),
    (J $root "Drivers\Chipset_Intel"),
    (J $root "Drivers\Intel_LAN"),
    (J $root "Drivers\Intel_WiFi"),
    (J $root "Drivers\Realtek_LAN"),
    (J $root "Drivers\Storage_NVMe"),
    (J $root "Drivers\Touchpad_ELAN"),
    (J $root "Drivers\Touchpad_Synaptics"),

    (J $root "ForgerTools"),
    (J $root "ForgerTools\DisplayForger"),
    (J $root "ForgerTools\HardwareForger"),
    (J $root "ForgerTools\EncryptionForge"),
    (J $root "ForgerTools\QuickToolsHealthCheck"),

    (J $root "_logs"),
    (J $root "_reports"),
    (J $root "_downloads"),
    (J $root "_archive"),

    (J $root "Docs"),
    (J $root "IfScriptFails(ManualSetup)"),
    (J $root "MediCat.USB")
)

foreach ($p in $paths) {
    Ensure-Folder -Path $p
}

$links = Get-ShortcutDefinitionsFromManifest -Root $root

foreach ($item in $links) {
    Ensure-Folder -Path $item.Folder
    $shortcut = Join-Path $item.Folder $item.Name
    Write-UrlShortcut -ShortcutPath $shortcut -Url $item.Url
}

$manualItems = @(
    @{
        Title = "Windows 11 Official Download"
        Url = "https://www.microsoft.com/software-download/windows11"
        Note = (J $root "IfScriptFails(ManualSetup)\Windows11_Official.txt")
        Notes = "Microsoft changes download flow often. Use official page."
    },
    @{
        Title = "Windows 10 Official Download"
        Url = "https://www.microsoft.com/software-download/windows10ISO"
        Note = (J $root "IfScriptFails(ManualSetup)\Windows10_Official.txt")
        Notes = "Microsoft changes download flow often. Use official page."
    },
    @{
        Title = "Ventoy Official Download"
        Url = "https://www.ventoy.net/en/download.html"
        Note = (J $root "IfScriptFails(ManualSetup)\Ventoy_Official.txt")
        Notes = "Install Ventoy manually with Ventoy2Disk before using this USB as a boot device."
    },
    @{
        Title = "MediCat USB"
        Url = "https://medicatusb.com/"
        Note = (J $root "IfScriptFails(ManualSetup)\MediCat_Manual.txt")
        Notes = "Manual/folder-based toolkit. Distribution and size vary."
    },
    @{
        Title = "Sergei Strelec"
        Url = "https://sergeistrelec.name/"
        Note = (J $root "IfScriptFails(ManualSetup)\SergeiStrelec_Manual.txt")
        Notes = "Community project; use your preferred known-good source/workflow."
    },
    @{
        Title = "Hiren's BootCD PE"
        Url = "https://www.hirensbootcd.org/download/"
        Note = (J $root "IfScriptFails(ManualSetup)\Hirens_Manual.txt")
        Notes = "Keep a known-good copy from the official page."
    }
)

foreach ($item in $manualItems) {
    Add-ManualLinkFile -Path $item.Note -Title $item.Title -Url $item.Url -Notes $item.Notes
}

if (-not $OwnerName -and -not $WhatIfPreference) {
    $OwnerName = Read-Host "Optional: Enter your name for README (or press Enter to skip)"
}

$ownerText = if ($OwnerName -and $OwnerName.Trim().Length -gt 0) { $OwnerName.Trim() } else { "Not set" }
$readmePath = Join-Path $root "README.txt"
$date = (Get-Date).ToString("yyyy-MM-dd")

$readme = @"
=====================================================
                FORGEREMS TECHBENCH USB
=====================================================

Owner:   $ownerText
Created: $date
Bundle:  $($versionInfo.Version)
Build:   $($versionInfo.BuildTimestampUtc)
Release: $($versionInfo.ReleaseType)
Purpose: System repair, recovery, diagnostics, OS install, portable bench tools
Status:  Verify after major updates

BOOT (Ventoy)
-------------
$root\ISO\Windows -> Windows 10/11 ISOs + WinPE tools
$root\ISO\Linux   -> Ubuntu, Mint, Kali, SystemRescue
$root\ISO\Tools   -> Clonezilla, Rescuezilla, GParted, MemTest, Hiren's PE, UBCD

PORTABLE APPS (Run in Windows / WinPE)
--------------------------------------
$root\Tools\Portable\Disk
$root\Tools\Portable\Hardware
$root\Tools\Portable\Network
$root\Tools\Portable\System
$root\Tools\Portable\Remote
$root\Tools\Portable\USB
$root\Tools\Portable\GPU
$root\Tools\Portable\Security

DRIVERS
-------
$root\Drivers

FORGER APPS
-----------
$root\ForgerTools\DisplayForger
$root\ForgerTools\HardwareForger
$root\ForgerTools\EncryptionForge
$root\ForgerTools\QuickToolsHealthCheck

WORKING FOLDERS
---------------
$root\_logs
$root\_reports
$root\_downloads
$root\_archive

SUPPORTED VS NOT FULLY MANAGED
------------------------------
Supported by the Ventoy core lifecycle:
- items listed in ForgerEMS.updates.json
- generated DOWNLOAD/INFO shortcuts
- _downloads, _archive, and _logs workflow data

Catalog split:
- auto-download safe -> manifest-managed file items
- manual only -> manifest-managed page shortcuts only
- review-first -> manifest-managed page shortcuts only
- see Docs\ForgerEMS-Download-Catalog.txt for the current bucket list
- see Docs\ForgerEMS-Managed-Download-Maintenance.txt for revalidation,
  fragility ranking, and fallback rules

Not fully managed in this repo baseline:
- portable third-party tools copied into Tools\Portable
- offline driver bundles under Drivers
- MediCat.USB
- packaged/vendor ForgerTools content without its source repo

NOTES
-----
- Install Ventoy first with Ventoy2Disk.
- Copy ISO files into the matching ISO folders.
- Run Update-ForgerEMS.ps1 to fetch the auto-download-safe bucket.
- Use the remaining DOWNLOAD shortcuts for manual-only and review-first items.
- "Safe" still depends on upstream availability.
- Revalidate managed downloads before rebuild/shipping:
  .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads
- Review the latest summary at:
  .\.verify\managed-download-revalidation\latest\managed-download-summary.txt
- MediCat is folder-based; place it at root if you use it.
- Run Update-ForgerEMS.ps1 later to refresh manifest-managed items.
- Portable apps, drivers, and bundled vendor folders remain manual unless a maintained source/update workflow is added.
- Re-running this script is safe.
=====================================================
"@

if ($PSCmdlet.ShouldProcess($readmePath, "Write README")) {
    Set-Content -LiteralPath $readmePath -Value $readme -Encoding UTF8
    Write-Log "README written: $readmePath" "OK"
}
else {
    Write-Log "Would write README: $readmePath" "INFO"
}

$bootstrapNotesPath = J $root "Docs\ForgerEMS-Bootstrap-Notes.txt"
$bootstrapNotes = @"
ForgerEMS Bootstrap Notes
=========================

Created: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
Root:    $root
Bundle:  $($versionInfo.Version)
Build:   $($versionInfo.BuildTimestampUtc)
Release: $($versionInfo.ReleaseType)

This script:
- creates the full folder structure
- creates URL shortcuts
- creates manual-download note files
- writes README and inventory CSV
- optionally seeds the manifest
- uses _logs / _downloads / _archive / _reports consistently

Important:
- Ventoy must still be installed separately
- Use Update-ForgerEMS.ps1 for the auto-download-safe bucket
- Manual-only and review-first items still use official page shortcuts
- Drivers are intentionally kept at the root, not under ISO
- Run Update-ForgerEMS.ps1 for manifest-driven refreshes
- "Safe" still depends on upstream availability; revalidate before rebuilds
- Run .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads for URL/checksum drift
- Review .\.verify\managed-download-revalidation\latest\managed-download-summary.txt
  before any distribution-intended rebuild
- Safe to rerun

Supported scope:
- manifest-listed files and URL shortcuts
- manifest and generated inventory/readme files
- _downloads, _archive, and _logs workflow folders
- Docs\ForgerEMS-Download-Catalog.txt category guide
- Docs\ForgerEMS-Managed-Download-Maintenance.txt maintenance guide

Not fully managed:
- portable third-party binaries copied into Tools\Portable
- offline driver packs copied under Drivers
- MediCat.USB and other large community bundles
- packaged vendor content without a tracked source/build pipeline

Core folders:
- ISO\Windows
- ISO\Linux
- ISO\Tools
- Drivers
- Tools\Portable
- ForgerTools
- _logs
- _downloads
- _archive
- _reports
"@

if ($PSCmdlet.ShouldProcess($bootstrapNotesPath, "Write bootstrap notes")) {
    Set-Content -LiteralPath $bootstrapNotesPath -Value $bootstrapNotes -Encoding UTF8
    Write-Log "Bootstrap notes written: $bootstrapNotesPath" "OK"
}
else {
    Write-Log "Would write bootstrap notes: $bootstrapNotesPath" "INFO"
}

$downloadCatalogPath = J $root "Docs\ForgerEMS-Download-Catalog.txt"
$downloadCatalog = Get-DownloadCatalogTextFromManifest -Root $root

if ($PSCmdlet.ShouldProcess($downloadCatalogPath, "Write download catalog")) {
    Set-Content -LiteralPath $downloadCatalogPath -Value $downloadCatalog -Encoding UTF8
    Write-Log "Download catalog written: $downloadCatalogPath" "OK"
}
else {
    Write-Log "Would write download catalog: $downloadCatalogPath" "INFO"
}

$maintenanceGuidePath = J $root "Docs\ForgerEMS-Managed-Download-Maintenance.txt"
$maintenanceGuide = Get-ManagedDownloadMaintenanceTextFromManifest -Root $root

if ($PSCmdlet.ShouldProcess($maintenanceGuidePath, "Write managed download maintenance guide")) {
    Set-Content -LiteralPath $maintenanceGuidePath -Value $maintenanceGuide -Encoding UTF8
    Write-Log "Managed download maintenance guide written: $maintenanceGuidePath" "OK"
}
else {
    Write-Log "Would write managed download maintenance guide: $maintenanceGuidePath" "INFO"
}

$inventoryPath = J $root "Docs\ForgerEMS-Link-Inventory.csv"

$inventoryRows = @()

foreach ($item in $links) {
    $inventoryRows += [PSCustomObject]@{
        Type   = "Shortcut"
        Folder = $item.Folder
        Name   = $item.Name
        Url    = $item.Url
        Notes  = ""
    }
}

foreach ($item in $manualItems) {
    $inventoryRows += [PSCustomObject]@{
        Type   = "ManualNote"
        Folder = Split-Path -Parent $item.Note
        Name   = Split-Path -Leaf $item.Note
        Url    = $item.Url
        Notes  = $item.Notes
    }
}

if ($PSCmdlet.ShouldProcess($inventoryPath, "Write inventory CSV")) {
    $inventoryRows | Export-Csv -LiteralPath $inventoryPath -NoTypeInformation -Encoding UTF8
    Write-Log "Inventory written: $inventoryPath" "OK"
}
else {
    Write-Log "Would write inventory CSV: $inventoryPath" "INFO"
}

if ($SeedManifest) {
    $manifestPath = Resolve-RootChildPath -Root $root -RelativePath $ManifestName
    $bundledManifestPath = Get-BundledManifestTemplatePath
    Get-BundledManifestTemplate -Root $root | Out-Null

    if ((Test-Path -LiteralPath $manifestPath) -and -not $ForceManifestOverwrite) {
        Write-Log "Manifest already exists, skipping seed: $manifestPath" "WARN"
    }
    else {
        if ($PSCmdlet.ShouldProcess($manifestPath, "Write manifest JSON")) {
            Copy-Item -LiteralPath $bundledManifestPath -Destination $manifestPath -Force
            Write-Log "Manifest written: $manifestPath" "OK"
        }
        else {
            Write-Log "Would write manifest: $manifestPath" "INFO"
        }
    }
}

Open-UrlsIfRequested -Urls @(
    "https://www.ventoy.net/en/download.html",
    "https://www.microsoft.com/software-download/windows11",
    "https://www.microsoft.com/software-download/windows10ISO"
) -Enabled:$OpenCorePages

Open-UrlsIfRequested -Urls @(
    "https://sergeistrelec.name/",
    "https://www.hirensbootcd.org/download/",
    "https://medicatusb.com/"
) -Enabled:$OpenManualPages

Write-Log "Done. ForgerEMS layout created and shortcuts/docs added." "OK"
Write-Log "Next: install Ventoy, add ISOs/tools, then run Update-ForgerEMS.ps1." "OK"
if ($script:LogFile -and (Test-Path -LiteralPath (Split-Path -Parent $script:LogFile))) {
    Write-Log "Log saved: $script:LogFile" "OK"
}
else {
    Write-Log "Log file was not created because the run was previewed only." "INFO"
}
