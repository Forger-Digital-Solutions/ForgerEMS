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

.PARAMETER LayoutOnly
Create the USB layout and supporting docs without automatically launching the
managed download/update pass.

.PARAMETER WaitForManagedDownloads
Keep setup attached to the managed download/update pass instead of launching it
in the background after layout creation.

.PARAMETER NonInteractive
Disable interactive prompts. Required for attached GUI runs so setup never
blocks waiting for stdin.

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
.\Setup-ForgerEMS.ps1 -UsbRoot "D:\" -LayoutOnly

.EXAMPLE
.\Setup-ForgerEMS.ps1 -UsbRoot "D:\" -WaitForManagedDownloads

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
    [switch]$LayoutOnly,
    [switch]$WaitForManagedDownloads,
    [switch]$NonInteractive,
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

function Get-ManifestDestinationKey {
    param([AllowNull()][string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return ""
    }

    return ([string]$RelativePath).Trim().ToLowerInvariant()
}

function Normalize-ManifestMatchText {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    return ([regex]::Replace($Text.ToLowerInvariant(), '[^a-z0-9]+', ' ')).Trim()
}

function Get-PlaceholderDisplayLabelFromDestination {
    param([AllowNull()][string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return ""
    }

    $leafName = [IO.Path]::GetFileNameWithoutExtension($RelativePath)
    if ([string]::IsNullOrWhiteSpace($leafName)) {
        return ""
    }

    return (($leafName -replace '^(download|info)\s*-\s*', '').Trim())
}

function Test-ManagedPlaceholderShadowMatch {
    param(
        [Parameter(Mandatory)]$PageItem,
        [Parameter(Mandatory)]$ManagedItem
    )

    $pageDest = ([string]$(if ($PageItem.dest) { $PageItem.dest } else { "" })).Trim()
    $managedDest = ([string]$(if ($ManagedItem.dest) { $ManagedItem.dest } else { "" })).Trim()

    if ([string]::IsNullOrWhiteSpace($pageDest) -or [string]::IsNullOrWhiteSpace($managedDest)) {
        return $false
    }

    $pageDir = Split-Path -Parent $pageDest
    $managedDir = Split-Path -Parent $managedDest

    if (-not [string]::Equals($pageDir, $managedDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $pageLabels = @(
        (Normalize-ManifestMatchText -Text (Get-PlaceholderDisplayLabelFromDestination -RelativePath $pageDest))
        (Normalize-ManifestMatchText -Text ([string]$(if ($PageItem.name) { $PageItem.name } else { "" })))
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    $managedTargets = @(
        (Normalize-ManifestMatchText -Text ([string]$(if ($ManagedItem.name) { $ManagedItem.name } else { "" })))
        (Normalize-ManifestMatchText -Text ([IO.Path]::GetFileNameWithoutExtension($managedDest)))
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($pageLabel in $pageLabels) {
        foreach ($managedTarget in $managedTargets) {
            if ($managedTarget.Contains($pageLabel) -or $pageLabel.Contains($managedTarget)) {
                return $true
            }
        }
    }

    return $false
}

function Get-ActiveManagedPlaceholderPlanFromManifest {
    param([Parameter(Mandatory)]$Manifest)

    $enabledManagedFileItems = @(
        @($Manifest.items) | Where-Object {
            $itemEnabled = $true
            if ($null -ne $_.enabled) {
                $itemEnabled = [bool]$_.enabled
            }

            $itemType = ([string]$(if ($_.type) { $_.type } else { "file" })).Trim().ToLowerInvariant()
            $itemEnabled -and $itemType -eq "file"
        }
    )

    $byPlaceholderDest = @{}

    foreach ($item in @($Manifest.items)) {
        if ($null -eq $item) { continue }

        $itemEnabled = $true
        if ($null -ne $item.enabled) {
            $itemEnabled = [bool]$item.enabled
        }

        $itemType = ([string]$(if ($item.type) { $item.type } else { "file" })).Trim().ToLowerInvariant()
        if (-not $itemEnabled -or $itemType -ne "page") {
            continue
        }

        $matchedManagedItem = @(
            $enabledManagedFileItems | Where-Object {
                Test-ManagedPlaceholderShadowMatch -PageItem $item -ManagedItem $_
            }
        ) | Select-Object -First 1

        if ($null -eq $matchedManagedItem) {
            continue
        }

        $destKey = Get-ManifestDestinationKey -RelativePath ([string]$item.dest)
        if (-not $byPlaceholderDest.ContainsKey($destKey)) {
            $byPlaceholderDest[$destKey] = [PSCustomObject]@{
                PlaceholderItem = $item
                ManagedItem     = $matchedManagedItem
            }
        }
    }

    return $byPlaceholderDest
}

function Get-ShortcutDefinitionsFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $manifest = Get-BundledManifestTemplate -Root $Root
    $links = @()
    $suppressedPaths = @()
    $activeManagedPlaceholderPlan = Get-ActiveManagedPlaceholderPlanFromManifest -Manifest $manifest

    foreach ($item in $manifest.items) {
        $enabled = $true
        if ($null -ne $item.enabled) {
            $enabled = [bool]$item.enabled
        }

        $type = if ($item.type) { [string]$item.type } else { "file" }
        if (-not $enabled -or $type.Trim().ToLowerInvariant() -ne "page") {
            continue
        }

        $destRel = ([string]$item.dest).Trim()
        $destKey = Get-ManifestDestinationKey -RelativePath $destRel
        if ($activeManagedPlaceholderPlan.ContainsKey($destKey)) {
            Write-Log "Skipped placeholder creation because item is active managed download: $destRel" "INFO"
            $suppressedPaths += Resolve-RootChildPath -Root $Root -RelativePath $destRel
            continue
        }

        $destPath = Resolve-RootChildPath -Root $Root -RelativePath $destRel
        $links += [PSCustomObject]@{
            Folder = Split-Path -Parent $destPath
            Name   = Split-Path -Leaf $destPath
            Url    = [string]$item.url
        }
    }

    return [PSCustomObject]@{
        Links           = $links
        SuppressedPaths = $suppressedPaths
    }
}

function Get-ManagedCatalogDataFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $manifest = Get-BundledManifestTemplate -Root $Root
    $activeManaged = New-Object System.Collections.Generic.List[object]
    $disabledEligible = New-Object System.Collections.Generic.List[object]
    $info = New-Object System.Collections.Generic.List[object]
    $placeholder = New-Object System.Collections.Generic.List[object]
    $manual = New-Object System.Collections.Generic.List[object]
    $review = New-Object System.Collections.Generic.List[object]
    $otherPages = New-Object System.Collections.Generic.List[object]

    foreach ($item in @($manifest.items)) {
        if ($null -eq $item) { continue }

        $isEnabled = $true
        if ($null -ne $item.enabled) {
            $isEnabled = [bool]$item.enabled
        }

        $type = if ($item.type) { ([string]$item.type).Trim().ToLowerInvariant() } else { "file" }
        $notes = if ($item.notes) { ([string]$item.notes).Trim().ToLowerInvariant() } else { "" }

        if ($type -eq "file") {
            if ($isEnabled) {
                [void]$activeManaged.Add($item)
            }
            else {
                [void]$disabledEligible.Add($item)
            }
            continue
        }

        if (-not $isEnabled) { continue }

        if ($notes.StartsWith("info shortcut:")) {
            [void]$info.Add($item)
            continue
        }

        if ($notes.StartsWith("placeholder only:")) {
            [void]$placeholder.Add($item)
            continue
        }

        if ($notes.StartsWith("manual only:")) {
            [void]$manual.Add($item)
            continue
        }

        if ($notes.StartsWith("review first:")) {
            [void]$review.Add($item)
            continue
        }

        [void]$otherPages.Add($item)
    }

    return [PSCustomObject]@{
        Manifest          = $manifest
        ActiveManaged     = $activeManaged.ToArray()
        DisabledEligible  = $disabledEligible.ToArray()
        Info              = $info.ToArray()
        Placeholder       = $placeholder.ToArray()
        Manual            = $manual.ToArray()
        Review            = $review.ToArray()
        OtherPages        = $otherPages.ToArray()
    }
}

function Get-ManagedDownloadRankingFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $catalogData = Get-ManagedCatalogDataFromManifest -Root $Root
    return @(
        $catalogData.ActiveManaged |
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
    $summary = Get-ManagedDownloadSummaryFromItems -Items @($catalogData.ActiveManaged)
    $lines = New-Object System.Collections.Generic.List[string]
    $borderlineText = if (@($summary.BorderlineItems).Count -gt 0) {
        ($summary.BorderlineItems | ForEach-Object { [string]$_.name }) -join "; "
    }
    else {
        "none"
    }
    $nextCycleText = if (@($summary.NextCycleItems).Count -gt 0) {
        ($summary.NextCycleItems | ForEach-Object { "[" + [string]$_.maintenanceRank + "] " + [string]$_.name }) -join "; "
    }
    else {
        "none"
    }

    [void]$lines.Add("ForgerEMS Download Catalog")
    [void]$lines.Add("==========================")
    [void]$lines.Add("")
    [void]$lines.Add("The Ventoy core uses explicit catalog roles so the")
    [void]$lines.Add("manifest, folder contents, and UX all describe the")
    [void]$lines.Add("same business rule:")
    [void]$lines.Add("")
    [void]$lines.Add("- active managed autodownload")
    [void]$lines.Add("- disabled but eligible for re-promotion")
    [void]$lines.Add("- info shortcut")
    [void]$lines.Add("- placeholder-only")
    [void]$lines.Add("- manual only")
    [void]$lines.Add("- review-first")
    [void]$lines.Add("")
    [void]$lines.Add("Note: managed still depends on upstream availability.")
    [void]$lines.Add("It means the manifest points at an official direct")
    [void]$lines.Add("artifact with checksum coverage today. It does not")
    [void]$lines.Add("guarantee that the upstream will stay reachable or")
    [void]$lines.Add("unchanged forever.")
    [void]$lines.Add("")
    [void]$lines.Add("Health snapshot")
    [void]$lines.Add("---------------")
    [void]$lines.Add(("Active managed items: " + $summary.TotalCount))
    [void]$lines.Add(("Disabled-but-eligible items: " + @($catalogData.DisabledEligible).Count))
    [void]$lines.Add(("Shortcut roles: info " + @($catalogData.Info).Count + " | placeholder-only " + @($catalogData.Placeholder).Count + " | manual-only " + @($catalogData.Manual).Count + " | review-first " + @($catalogData.Review).Count))
    [void]$lines.Add(("Fragility: high " + $summary.HighCount + " | medium " + $summary.MediumCount + " | low " + $summary.LowCount))
    [void]$lines.Add(("Checksum posture: pinned-only " + $summary.PinnedOnlyCount + " | pinned+remote " + $summary.PinnedRemoteCount + " | remote-only " + $summary.RemoteOnlyCount))
    [void]$lines.Add(("Status: OK " + $summary.OkCount + " | OK-LIMITED " + $summary.OkLimitedCount + " | DRIFT 0"))
    [void]$lines.Add(("Borderline: " + $borderlineText))
    [void]$lines.Add(("Inspect first next cycle: " + $nextCycleText))
    [void]$lines.Add("")
    [void]$lines.Add("Active managed autodownload")
    [void]$lines.Add("---------------------------")
    [void]$lines.Add("- manifest-managed file item")
    [void]$lines.Add("- official direct artifact URL")
    [void]$lines.Add("- no gated/account/clickthrough flow")
    [void]$lines.Add("- checksum coverage in the manifest")
    [void]$lines.Add("- setup/update should leave only the real payload after success")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    if (@($summary.RankedItems).Count -eq 0) {
        [void]$lines.Add("- none")
    }
    else {
        foreach ($item in @($summary.RankedItems)) {
            $status = Get-ManagedDownloadStatusFromItem -Item $item
            $checksumPosture = Get-ManagedDownloadChecksumPostureFromItem -Item $item
            [void]$lines.Add(("[{0}] {1} | {2} | {3} | {4} | {5}" -f [int]$item.maintenanceRank, $status, [string]$item.fragilityLevel, [string]$item.sourceType, $checksumPosture, [string]$item.name))
            if (Test-ManagedDownloadBorderline -Item $item) {
                [void]$lines.Add("  borderline: yes")
            }
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("Disabled but eligible for re-promotion")
    [void]$lines.Add("--------------------------------------")
    [void]$lines.Add("- manifest-managed file item")
    [void]$lines.Add("- currently not active in this manifest")
    [void]$lines.Add("- should be re-enabled only when the same official")
    [void]$lines.Add("  source/checksum/verifier rules still hold")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    if (@($catalogData.DisabledEligible).Count -eq 0) {
        [void]$lines.Add("- none")
    }
    else {
        foreach ($item in @(
            $catalogData.DisabledEligible |
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
        )) {
            $checksumPosture = Get-ManagedDownloadChecksumPostureFromItem -Item $item
            [void]$lines.Add(("[{0}] {1} | {2} | {3} | {4}" -f [int]$item.maintenanceRank, [string]$item.fragilityLevel, [string]$item.sourceType, $checksumPosture, [string]$item.name))
            if (Test-ManagedDownloadBorderline -Item $item) {
                [void]$lines.Add("  borderline: yes")
            }
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("Info shortcuts")
    [void]$lines.Add("--------------")
    [void]$lines.Add("- manifest-managed page shortcut paired with an active")
    [void]$lines.Add("  managed autodownload item")
    [void]$lines.Add("- setup/update suppresses the shortcut while the real")
    [void]$lines.Add("  managed payload owns the slot")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    if (@($catalogData.Info).Count -eq 0) {
        [void]$lines.Add("- none")
    }
    else {
        foreach ($item in @($catalogData.Info | Sort-Object @{ Expression = { [string]$_.name } })) {
            [void]$lines.Add("- " + [string]$item.name + " -> " + [string]$item.dest)
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("Placeholder-only")
    [void]$lines.Add("----------------")
    [void]$lines.Add("- manifest-managed page shortcut only")
    [void]$lines.Add("- used when an item is intentionally not active managed")
    [void]$lines.Add("  today, but is not excluded forever")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    if (@($catalogData.Placeholder).Count -eq 0) {
        [void]$lines.Add("- none")
    }
    else {
        foreach ($item in @($catalogData.Placeholder | Sort-Object @{ Expression = { [string]$_.name } })) {
            [void]$lines.Add("- " + [string]$item.name + " -> " + [string]$item.dest)
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("Manual only")
    [void]$lines.Add("-----------")
    [void]$lines.Add("- manifest-managed page shortcut only")
    [void]$lines.Add("- excluded from managed autodownload by legal,")
    [void]$lines.Add("  corporate, or safety policy")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    if (@($catalogData.Manual).Count -eq 0) {
        [void]$lines.Add("- none")
    }
    else {
        foreach ($item in @($catalogData.Manual | Sort-Object @{ Expression = { [string]$_.name } })) {
            [void]$lines.Add("- " + [string]$item.name)
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("Review-first")
    [void]$lines.Add("------------")
    [void]$lines.Add("- manifest-managed page shortcut only")
    [void]$lines.Add("- not approved for managed autodownload until checksum,")
    [void]$lines.Add("  provenance, or operational review is good enough")
    [void]$lines.Add("")
    [void]$lines.Add("Current items:")
    if (@($catalogData.Review).Count -eq 0) {
        [void]$lines.Add("- none")
    }
    else {
        foreach ($item in @($catalogData.Review | Sort-Object @{ Expression = { [string]$_.name } })) {
            [void]$lines.Add("- " + [string]$item.name)
        }
    }
    [void]$lines.Add("")
    [void]$lines.Add("See MANAGED-DOWNLOAD-MAINTENANCE.txt for fragility")
    [void]$lines.Add("ranking, fallback rules, cadence, and recovery steps.")

    return ($lines -join [Environment]::NewLine)
}

function Get-ManagedDownloadMaintenanceTextFromManifest {
    param([Parameter(Mandatory)][string]$Root)

    $catalogData = Get-ManagedCatalogDataFromManifest -Root $Root
    $summary = Get-ManagedDownloadSummaryFromItems -Items @($catalogData.ActiveManaged)
    $rankedItems = @($summary.RankedItems)
    $lines = New-Object System.Collections.Generic.List[string]
    $borderlineText = if (@($summary.BorderlineItems).Count -gt 0) {
        ($summary.BorderlineItems | ForEach-Object { [string]$_.name }) -join "; "
    }
    else {
        "none"
    }
    $nextCycleText = if (@($summary.NextCycleItems).Count -gt 0) {
        ($summary.NextCycleItems | ForEach-Object { "[" + [string]$_.maintenanceRank + "] " + [string]$_.name }) -join "; "
    }
    else {
        "none"
    }

    [void]$lines.Add("ForgerEMS Managed Download Maintenance")
    [void]$lines.Add("======================================")
    [void]$lines.Add("")
    [void]$lines.Add("Scope")
    [void]$lines.Add("-----")
    [void]$lines.Add(("This guide applies to the " + $summary.TotalCount + " active"))
    [void]$lines.Add("manifest-managed autodownload file entries only.")
    [void]$lines.Add(("Disabled-but-eligible managed candidates: " + @($catalogData.DisabledEligible).Count))
    [void]$lines.Add("")
    [void]$lines.Add("Managed still depends on upstream availability.")
    [void]$lines.Add("If a vendor moves or removes an official artifact or")
    [void]$lines.Add("checksum source, the entry may need repair or demotion.")
    [void]$lines.Add("")
    [void]$lines.Add("Health snapshot")
    [void]$lines.Add("---------------")
    [void]$lines.Add(("Active managed items: " + $summary.TotalCount))
    [void]$lines.Add(("Disabled-but-eligible items: " + @($catalogData.DisabledEligible).Count))
    [void]$lines.Add(("Fragility: high " + $summary.HighCount + " | medium " + $summary.MediumCount + " | low " + $summary.LowCount))
    [void]$lines.Add(("Checksum posture: pinned-only " + $summary.PinnedOnlyCount + " | pinned+remote " + $summary.PinnedRemoteCount + " | remote-only " + $summary.RemoteOnlyCount))
    [void]$lines.Add(("Status: OK " + $summary.OkCount + " | OK-LIMITED " + $summary.OkLimitedCount + " | DRIFT 0"))
    [void]$lines.Add(("Borderline: " + $borderlineText))
    [void]$lines.Add(("Inspect first next cycle: " + $nextCycleText))
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
    [void]$lines.Add("- %LOCALAPPDATA%\\ForgerEMS\\.verify\\managed-download-revalidation\\<timestamp>\\")
    [void]$lines.Add("- %LOCALAPPDATA%\\ForgerEMS\\.verify\\managed-download-revalidation\\latest\\")
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
    [void]$lines.Add("4. If scope must be narrowed temporarily while the item is")
    [void]$lines.Add("   still otherwise acceptable, keep the official page")
    [void]$lines.Add("   shortcut and classify it as disabled-but-eligible.")
    [void]$lines.Add("5. Demote to review-first or manual-only when the flow")
    [void]$lines.Add("   becomes page-only, account-gated, EULA-sensitive,")
    [void]$lines.Add("   checksum-poor, or provenance becomes ambiguous.")
    [void]$lines.Add("6. Do not patch around a broken upstream with third-party")
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
    [void]$lines.Add("- formerly active item pauses for scope reasons -> keep the")
    [void]$lines.Add("  official page shortcut, classify it as disabled-but-")
    [void]$lines.Add("  eligible, and remove active managed claims")
    [void]$lines.Add("- formerly active item becomes questionable -> convert it to")
    [void]$lines.Add("  review-first or manual-only, preserve the official page")
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
        [string]$Root,
        [switch]$NonInteractive
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

    if ($NonInteractive) {
        throw "A USB target must be supplied with -DriveLetter or -UsbRoot when -NonInteractive is used."
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

function Remove-PathIfPresent {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Description = "Remove legacy generated item",
        [switch]$Recurse
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    if ($PSCmdlet.ShouldProcess($Path, $Description)) {
        Remove-Item -LiteralPath $Path -Recurse:$Recurse -Force
        Write-Log "Removed legacy item: $Path" "OK"
    }
    else {
        Write-Log "Would remove legacy item: $Path" "INFO"
    }

    return $true
}

function Remove-DirectoryIfEmpty {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string]$Description = "Remove empty legacy directory"
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }

    $children = @(Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue)
    if ($children.Count -gt 0) {
        return $false
    }

    return Remove-PathIfPresent -Path $Path -Description $Description -Recurse
}

function Remove-LegacyGeneratedTree {
    param(
        [Parameter(Mandatory)][string]$Path,
        [string[]]$AllowedFileNames = @(),
        [string[]]$AllowedDirectoryNames = @(),
        [string]$Description = "Remove legacy generated tree"
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return $false
    }

    $unexpectedFiles = @(
        Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin $AllowedFileNames }
    )

    if ($unexpectedFiles.Count -gt 0) {
        Write-Log "Retained legacy folder with unexpected files: $Path" "WARN"
        return $false
    }

    $unexpectedDirectories = @(
        Get-ChildItem -LiteralPath $Path -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -notin $AllowedDirectoryNames }
    )

    if ($unexpectedDirectories.Count -gt 0) {
        Write-Log "Retained legacy folder with unexpected subfolders: $Path" "WARN"
        return $false
    }

    return Remove-PathIfPresent -Path $Path -Description $Description -Recurse
}

function Test-InteractivePromptAvailable {
    try {
        if (-not [Environment]::UserInteractive) {
            return $false
        }
    }
    catch {
        return $false
    }

    try {
        if ([Console]::IsInputRedirected) {
            return $false
        }
    }
    catch {
        return $false
    }

    try {
        if ([Console]::IsOutputRedirected -or [Console]::IsErrorRedirected) {
            return $false
        }
    }
    catch {
        return $false
    }

    return $true
}

function Get-PowerShellExePath {
    $candidate = Join-Path $PSHOME "powershell.exe"
    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
        return $candidate
    }

    return "powershell.exe"
}

function Start-ManagedDownloadPassInBackground {
    param(
        [Parameter(Mandatory)][string]$UpdateScriptPath,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManifestName,
        [Parameter(Mandatory)][string]$LogDirectory
    )

    $powerShellExe = Get-PowerShellExePath
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $stdoutPath = J $LogDirectory ("update-launcher_" + $stamp + ".stdout.log")
    $stderrPath = J $LogDirectory ("update-launcher_" + $stamp + ".stderr.log")

    $process = Start-Process `
        -FilePath $powerShellExe `
        -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $UpdateScriptPath, "-UsbRoot", $Root, "-ManifestName", $ManifestName) `
        -RedirectStandardOutput $stdoutPath `
        -RedirectStandardError $stderrPath `
        -WindowStyle Hidden `
        -PassThru

    return [PSCustomObject]@{
        ProcessId  = $process.Id
        StdoutPath = $stdoutPath
        StderrPath = $stderrPath
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

$root = Resolve-UsbRoot -Drive $DriveLetter -Root $UsbRoot -NonInteractive:$NonInteractive

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
    (J $root "Drivers\Audio"),
    (J $root "Drivers\Bluetooth"),
    (J $root "Drivers\Chipset"),
    (J $root "Drivers\Graphics"),
    (J $root "Drivers\Input"),
    (J $root "Drivers\Network"),
    (J $root "Drivers\Storage"),
    (J $root "Drivers\Wireless"),

    (J $root "_logs"),
    (J $root "_reports"),
    (J $root "_downloads"),
    (J $root "_archive"),

    (J $root "Docs"),
    (J $root "MediCat.USB")
)

foreach ($p in $paths) {
    Ensure-Folder -Path $p
}

$legacyGeneratedManualFolder = J $root "IfScriptFails(ManualSetup)"
Remove-LegacyGeneratedTree -Path $legacyGeneratedManualFolder -AllowedFileNames @(
    "Windows11_Official.txt",
    "Windows10_Official.txt",
    "Ventoy_Official.txt",
    "MediCat_Manual.txt",
    "SergeiStrelec_Manual.txt",
    "Hirens_Manual.txt"
) -Description "Remove legacy manual fallback notes folder" | Out-Null

$legacyForgerToolsFolder = J $root "ForgerTools"
Remove-LegacyGeneratedTree -Path $legacyForgerToolsFolder -AllowedDirectoryNames @(
    "DisplayForger",
    "HardwareForger",
    "EncryptionForge",
    "QuickToolsHealthCheck"
) -Description "Remove legacy ForgerTools placeholder folder" | Out-Null

foreach ($legacyDriverFolder in @(
    (J $root "Drivers\Chipset_Intel"),
    (J $root "Drivers\Intel_LAN"),
    (J $root "Drivers\Intel_WiFi"),
    (J $root "Drivers\Realtek_LAN"),
    (J $root "Drivers\Storage_NVMe"),
    (J $root "Drivers\Touchpad_ELAN"),
    (J $root "Drivers\Touchpad_Synaptics")
)) {
    Remove-DirectoryIfEmpty -Path $legacyDriverFolder -Description "Remove empty legacy driver folder" | Out-Null
}

foreach ($legacyShortcut in @(
    (J $root "DOWNLOAD - MediCat.url"),
    (J $root "Drivers\DRIVERS - Intel Download Center.url"),
    (J $root "Drivers\DRIVERS - Realtek Downloads.url")
)) {
    Remove-PathIfPresent -Path $legacyShortcut -Description "Remove legacy generated shortcut" | Out-Null
}

$shortcutPlan = Get-ShortcutDefinitionsFromManifest -Root $root

foreach ($suppressedPath in @($shortcutPlan.SuppressedPaths | Sort-Object -Unique)) {
    Remove-PathIfPresent -Path $suppressedPath -Description "Remove placeholder shortcut for active managed download" | Out-Null
}

$links = @($shortcutPlan.Links)

foreach ($item in $links) {
    Ensure-Folder -Path $item.Folder
    $shortcut = Join-Path $item.Folder $item.Name
    Write-UrlShortcut -ShortcutPath $shortcut -Url $item.Url
}

if (-not $OwnerName -and -not $WhatIfPreference -and -not $NonInteractive -and (Test-InteractivePromptAvailable)) {
    $OwnerName = Read-Host "Optional: Enter your name for README (or press Enter to skip)"
}
elseif (-not $OwnerName -and -not $WhatIfPreference -and $NonInteractive) {
    Write-Log "Non-interactive mode is active. README owner prompt was skipped." "INFO"
}
elseif (-not $OwnerName) {
    Write-Log "Owner name was not supplied. README owner will remain unset." "INFO"
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
$root\ISO\Linux   -> Ubuntu, Mint, Kali, Fedora, Endless OS, SystemRescue
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
$root\Drivers\Audio
$root\Drivers\Bluetooth
$root\Drivers\Chipset
$root\Drivers\Graphics
$root\Drivers\Input
$root\Drivers\Network
$root\Drivers\Storage
$root\Drivers\Wireless

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
- active managed autodownload -> enabled manifest-managed file items
- disabled but eligible -> disabled manifest-managed file items
- info / placeholder / manual / review-first -> manifest-managed page shortcuts
- see Docs\ForgerEMS-Download-Catalog.txt for the current bucket list
- see Docs\ForgerEMS-Managed-Download-Maintenance.txt for revalidation,
  fragility ranking, and fallback rules

Not fully managed in this repo baseline:
- portable third-party tools copied into Tools\Portable
- offline driver bundles under Drivers
- MediCat.USB

NOTES
-----
- Install Ventoy first with Ventoy2Disk.
- Copy ISO files into the matching ISO folders.
- Setup now auto-starts the managed autodownload pass unless -LayoutOnly is used.
- By default the managed download pass launches in the background.
- Use -WaitForManagedDownloads if you want Setup to stay attached until downloads finish.
- Use the remaining DOWNLOAD shortcuts for placeholder/manual/review-first items.
- Managed autodownload still depends on upstream availability.
- Revalidate managed downloads before rebuild/shipping:
  .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads
- Review the latest summary at:
  %LOCALAPPDATA%\ForgerEMS\.verify\managed-download-revalidation\latest\managed-download-summary.txt
- MediCat is folder-based; place it at root if you use it.
- Run Update-ForgerEMS.ps1 later to refresh manifest-managed items.
- Portable apps and driver bundles remain manual unless a maintained source/update workflow is added.
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
- writes README and inventory CSV
- optionally seeds the manifest
- uses _logs / _downloads / _archive / _reports consistently

Important:
- Ventoy must still be installed separately
- Setup auto-starts the managed autodownload pass unless -LayoutOnly is used
- Setup launches managed downloads in the background by default
- Use -WaitForManagedDownloads to keep Setup attached until downloads finish
- Placeholder/manual/review-first items still use official page shortcuts
- Drivers are intentionally kept at the root, not under ISO
- Run Update-ForgerEMS.ps1 for manifest-driven refreshes
- Managed autodownload still depends on upstream availability; revalidate before rebuilds
- Run .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads for URL/checksum drift
- Review %LOCALAPPDATA%\ForgerEMS\.verify\managed-download-revalidation\latest\managed-download-summary.txt
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

Core folders:
- ISO\Windows
- ISO\Linux
- ISO\Tools
- Drivers
- Tools\Portable
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

$driverGuidePath = J $root "Drivers\README.txt"
$driverGuide = @"
ForgerEMS Driver Staging
========================

Use these folders to keep driver packs tidy:
- Audio
- Bluetooth
- Chipset
- Graphics
- Input
- Network
- Storage
- Wireless

Tips
----
- Prefer OEM support packages for laptops and all-in-ones.
- Drop extracted INF folders or vendor installers into the best matching category.
- Use the DOWNLOAD shortcuts in Drivers\* as starting points for official vendor pages.
- Keep large vendor packs unzipped only when needed to save space.
- Archive superseded driver bundles in _archive if you want local rollback copies.
"@

if ($PSCmdlet.ShouldProcess($driverGuidePath, "Write driver guide")) {
    Set-Content -LiteralPath $driverGuidePath -Value $driverGuide -Encoding UTF8
    Write-Log "Driver guide written: $driverGuidePath" "OK"
}
else {
    Write-Log "Would write driver guide: $driverGuidePath" "INFO"
}

$driverCategoryNotes = [ordered]@{
    "Audio" = @"
ForgerEMS Audio Driver Staging
==============================

Store OEM audio packages here.

Recommended:
- prefer the laptop or motherboard vendor support page first
- use Realtek packages only when they match the hardware and OEM workflow
- keep extracted INF folders here when Windows Setup or Device Manager needs them
"@
    "Bluetooth" = @"
ForgerEMS Bluetooth Driver Staging
==================================

Store OEM Bluetooth packages here.

Recommended:
- prefer OEM support pages for laptops and combo Wi-Fi/Bluetooth adapters
- Intel Bluetooth packages are a good fallback for Intel wireless chipsets
- keep adapter-specific installers or extracted INF folders here
"@
    "Chipset" = @"
ForgerEMS Chipset Driver Staging
================================

Store chipset, ME, and related platform packages here.

Recommended:
- OEM support page first for platform-tuned packages
- Intel generic packages are useful when OEM support is stale
- archive older chipset bundles in _archive if you keep rollback copies
"@
    "Graphics" = @"
ForgerEMS Graphics Driver Staging
=================================

Store GPU driver packages here.

Recommended:
- keep Intel, AMD, and NVIDIA packages separated by vendor subfolder if needed
- prefer OEM graphics packages on laptops with switchable graphics
- keep clean-install helpers such as DDU in Tools\Portable\GPU instead
"@
    "Input" = @"
ForgerEMS Input Driver Staging
==============================

Store touchpad, keyboard, card-reader, pen, or other input packages here.

Recommended:
- ELAN and Synaptics drivers are commonly OEM-customized
- use the PC vendor support page first for touchpad packages
- keep extracted INF folders here for manual install scenarios
"@
    "Network" = @"
ForgerEMS Network Driver Staging
================================

Store wired LAN or Ethernet packages here.

Recommended:
- Realtek and Intel Ethernet packages are the most common starting points
- keep extracted INF folders here for offline recovery installs
- archive superseded NIC packs in _archive if you keep rollback copies
"@
    "Storage" = @"
ForgerEMS Storage Driver Staging
================================

Store NVMe, RAID, VMD, and storage controller packages here.

Recommended:
- use OEM packages first when Windows Setup cannot see a drive
- Intel RST / VMD packages are common on modern Intel platforms
- keep extracted load-driver folders here for Windows install media use
"@
    "Wireless" = @"
ForgerEMS Wireless Driver Staging
=================================

Store Wi-Fi packages here.

Recommended:
- use OEM Wi-Fi packages first on laptops
- Intel Wi-Fi packages are useful for Intel adapters when OEM support is outdated
- keep extracted INF folders here for offline installs
"@
}

foreach ($driverCategory in $driverCategoryNotes.Keys) {
    $driverCategoryReadmePath = J $root ("Drivers\" + $driverCategory + "\README.txt")
    $driverCategoryReadme = $driverCategoryNotes[$driverCategory]

    if ($PSCmdlet.ShouldProcess($driverCategoryReadmePath, "Write driver category guide")) {
        Set-Content -LiteralPath $driverCategoryReadmePath -Value $driverCategoryReadme -Encoding UTF8
        Write-Log "Driver category guide written: $driverCategoryReadmePath" "OK"
    }
    else {
        Write-Log "Would write driver category guide: $driverCategoryReadmePath" "INFO"
    }
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

if (-not $LayoutOnly) {
    $updateScriptPath = Join-Path $PSScriptRoot "Update-ForgerEMS.ps1"
    if (-not (Test-Path -LiteralPath $updateScriptPath -PathType Leaf)) {
        throw "Managed download pass could not start because the updater was not found: $updateScriptPath"
    }

    $updateParameters = @{
        UsbRoot      = $root
        ManifestName = $ManifestName
    }

    try {
        if ($WhatIfPreference) {
            $updateParameters["WhatIf"] = $true
            Write-Log "Setup preview will include the managed download pass." "INFO"
            & $updateScriptPath @updateParameters
            Write-Log "Managed download pass completed in preview mode." "OK"
        }
        elseif ($WaitForManagedDownloads) {
            Write-Log "Starting managed download pass in attached mode..." "INFO"
            & $updateScriptPath @updateParameters
            Write-Log "Managed download pass completed." "OK"
        }
        else {
            $logsRoot = J $root "_logs"
            Write-Log "Starting managed download pass in background..." "INFO"
            try {
                $launchInfo = Start-ManagedDownloadPassInBackground `
                    -UpdateScriptPath $updateScriptPath `
                    -Root $root `
                    -ManifestName $ManifestName `
                    -LogDirectory $logsRoot

                Write-Log "Managed download pass started in background (PID $($launchInfo.ProcessId))." "OK"
                Write-Log "Watch $logsRoot for update_*.log and launcher output if you want live progress." "INFO"
                Write-Log "Launcher stdout: $($launchInfo.StdoutPath)" "INFO"
                Write-Log "Launcher stderr: $($launchInfo.StderrPath)" "INFO"
            }
            catch {
                Write-Log "Background launch failed. Falling back to attached mode: $($_.Exception.Message)" "WARN"
                & $updateScriptPath @updateParameters
                Write-Log "Managed download pass completed." "OK"
            }
        }
    }
    catch {
        Write-Log "Managed download pass did not fully complete: $($_.Exception.Message)" "ERROR"
        throw
    }
}
else {
    Write-Log "LayoutOnly was specified. Managed download pass skipped." "WARN"
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

Write-Log "Done. ForgerEMS layout created and shortcuts/docs updated." "OK"
if ($LayoutOnly) {
    Write-Log "Next: install Ventoy, add ISO/manual items, then run Update-ForgerEMS.ps1 when you want managed downloads." "OK"
}
elseif ($WaitForManagedDownloads) {
    Write-Log "Next: install Ventoy, review any remaining placeholder/manual/review-first shortcuts, and rerun Update-ForgerEMS.ps1 later for refreshes." "OK"
}
else {
    $rootLogsPath = J $root "_logs"
    $rootDownloadsPath = J $root "_downloads"
    Write-Log "Next: downloads are running in the background; watch $rootLogsPath and $rootDownloadsPath for activity." "OK"
    Write-Log "Next after downloads: install Ventoy if you have not already, review remaining placeholder/manual/review-first shortcuts, and rerun Update-ForgerEMS.ps1 later for refreshes." "OK"
}
if ($script:LogFile -and (Test-Path -LiteralPath (Split-Path -Parent $script:LogFile))) {
    Write-Log "Log saved: $script:LogFile" "OK"
}
else {
    Write-Log "Log file was not created because the run was previewed only." "INFO"
}
