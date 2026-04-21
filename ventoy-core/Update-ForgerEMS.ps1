<#
.SYNOPSIS
Applies the ForgerEMS manifest to a toolkit root.

.DESCRIPTION
Manifest-driven updater for the Ventoy core. This script reads a JSON manifest,
validates it early, resolves all target paths under the selected root, and then
updates managed files and URL shortcuts. If the selected root does not already
contain the manifest, the updater falls back to the bundled manifest shipped
beside this script.

.PARAMETER DriveLetter
Drive letter for the target USB or toolkit root, such as D.

.PARAMETER UsbRoot
Full path to the target toolkit location. If you point at the release bundle
folder itself, the script uses the USB drive root so updates land at the top
of the device.

.PARAMETER ManifestName
Manifest file name or path. Relative paths are first resolved under the target
root and then beside this script.

.PARAMETER Force
Replace managed files even when an existing destination is already present.

.PARAMETER VerifyOnly
Verify existing managed files and shortcuts without downloading replacements.

.PARAMETER NoArchive
Skip archive creation before replacing managed files.

.PARAMETER ShowVersion
Display the Ventoy core version/build metadata from the bundled manifest and
exit without making changes.

.EXAMPLE
.\Update-ForgerEMS.ps1 -DriveLetter D -WhatIf

.EXAMPLE
.\Update-ForgerEMS.ps1 -UsbRoot "D:\" -VerifyOnly

.EXAMPLE
.\Update-ForgerEMS.ps1 -UsbRoot "H:\" -ManifestName "ForgerEMS.updates.json"

.EXAMPLE
.\Update-ForgerEMS.ps1 -ShowVersion

.NOTES
Public PowerShell entrypoint. Supports -WhatIf and manifest fallback.
#>

#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DriveLetter,
    [string]$UsbRoot = "",
    [string]$ManifestName = "ForgerEMS.updates.json",
    [switch]$Force,
    [switch]$VerifyOnly,
    [switch]$NoArchive,
    [switch]$ShowVersion
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

$script:LogFile = $null
$script:Summary = [ordered]@{
    Total      = 0
    Skipped    = 0
    Verified   = 0
    Updated    = 0
    Shortcut   = 0
    Failed     = 0
    Archived   = 0
    Disabled   = 0
}

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

function Ensure-Dir {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, "Create directory")) {
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
            Write-Log "Created directory: $Path" "OK"
        }
        else {
            Write-Log "Would create directory: $Path" "INFO"
        }
    }
    else {
        Write-Log "Exists: $Path" "INFO"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Safe-FileName {
    param([Parameter(Mandatory)][string]$Text)
    (($Text -replace '[\\/:*?"<>|]+', '_').Trim())
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

    Set-Content -LiteralPath $ShortcutPath -Value $content -Encoding ASCII
}

function Download-File {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutFile,
        [int]$TimeoutSec = 180,
        [string]$UserAgent = "ForgerEMS-Updater/3.1",
        [int]$Retries = 3
    )

    $headers = @{ "User-Agent" = $UserAgent }

    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        try {
            if (Test-Path -LiteralPath $OutFile) {
                Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
            }

            try {
                Start-BitsTransfer -Source $Url -Destination $OutFile -ErrorAction Stop
            }
            catch {
                Write-Log "BITS failed, falling back to Invoke-WebRequest: $Url" "WARN"
                Invoke-WebRequest -Uri $Url -OutFile $OutFile -Headers $headers -TimeoutSec $TimeoutSec -UseBasicParsing
            }

            return
        }
        catch {
            Write-Log "Download attempt $attempt failed for $Url :: $($_.Exception.Message)" "WARN"
            if ($attempt -eq $Retries) { throw }
            Start-Sleep -Seconds ([Math]::Min(5 * $attempt, 15))
        }
    }
}

function Get-ShaFromUrl {
    param(
        [Parameter(Mandatory)][string]$ShaUrl,
        [int]$TimeoutSec = 60,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    $tmp = Join-Path $env:TEMP ("forgerems_sha_" + [Guid]::NewGuid().ToString("N") + ".txt")
    try {
        Invoke-WebRequest -Uri $ShaUrl -OutFile $tmp -Headers @{ "User-Agent" = $UserAgent } -TimeoutSec $TimeoutSec -UseBasicParsing | Out-Null
        $txt = (Get-Content -LiteralPath $tmp -Raw).Trim()
        $m = [regex]::Match($txt, '([a-fA-F0-9]{64})')
        if ($m.Success) {
            return $m.Groups[1].Value.ToLowerInvariant()
        }
        return $null
    }
    finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

function Archive-OldFile {
    param(
        [Parameter(Mandatory)][string]$ItemName,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$ArchiveDir,
        [int]$MaxKeep = 3
    )

    if (-not (Test-Path -LiteralPath $FilePath)) { return $false }

    Ensure-Dir -Path $ArchiveDir

    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $base  = Safe-FileName -Text $ItemName
    $ext   = [IO.Path]::GetExtension($FilePath)
    if ([string]::IsNullOrWhiteSpace($ext)) { $ext = ".bin" }

    $archived = Join-Path $ArchiveDir "$base`_$stamp$ext"

    Copy-Item -LiteralPath $FilePath -Destination $archived -Force

    $pattern = "$base`_*" + $ext
    $existing = Get-ChildItem -LiteralPath $ArchiveDir -Filter $pattern -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending

    if ($existing.Count -gt $MaxKeep) {
        $toRemove = $existing | Select-Object -Skip $MaxKeep
        foreach ($r in $toRemove) {
            Remove-Item -LiteralPath $r.FullName -Force -ErrorAction SilentlyContinue
        }
    }

    return $true
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
        if (-not $letter) { throw "Invalid drive letter." }

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

    Write-Host "Enter either a drive letter or a full path on the target USB." -ForegroundColor Cyan
    Write-Host "If you choose this release bundle folder, the script will use the USB root." -ForegroundColor Cyan
    $entered = Read-Host "Enter your ForgerEMS USB drive letter or target path"
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

function Resolve-ManifestPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManifestSpecifier
    )

    $candidates = @()

    if ([IO.Path]::IsPathRooted($ManifestSpecifier)) {
        $candidates += [IO.Path]::GetFullPath($ManifestSpecifier)
    }
    else {
        $candidates += Resolve-RootChildPath -Root $Root -RelativePath $ManifestSpecifier
        $candidates += [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $ManifestSpecifier))
        $candidates += [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ("manifests\" + $ManifestSpecifier)))
        $candidates += [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) ("manifests\" + $ManifestSpecifier)))
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Manifest not found. Checked: $($candidates -join '; ')"
}

function Get-BundledManifestPath {
    Resolve-ManifestPath -Root $PSScriptRoot -ManifestSpecifier "ForgerEMS.updates.json"
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

function Get-VentoyCoreVersionInfo {
    $manifestPath = Get-BundledManifestPath
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

if ($ShowVersion) {
    Show-VentoyCoreVersionInfo
    return
}

$root = Resolve-UsbRoot -Drive $DriveLetter -Root $UsbRoot
$manifestPath = Resolve-ManifestPath -Root $root -ManifestSpecifier $ManifestName

$manifestRaw = Get-Content -LiteralPath $manifestPath -Raw
$manifest = $manifestRaw | ConvertFrom-Json
Assert-ManifestContract -Manifest $manifest -Root $root -SourceName $manifestPath

$dlDir     = Resolve-RootChildPath -Root $root -RelativePath ($(if ($manifest.settings.downloadFolder) { [string]$manifest.settings.downloadFolder } else { "_downloads" }))
$arcDir    = Resolve-RootChildPath -Root $root -RelativePath ($(if ($manifest.settings.archiveFolder)  { [string]$manifest.settings.archiveFolder }  else { "_archive" }))
$logDir    = Resolve-RootChildPath -Root $root -RelativePath ($(if ($manifest.settings.logFolder)      { [string]$manifest.settings.logFolder }      else { "_logs" }))
$timeout   = [int]($(if ($manifest.settings.timeoutSec) { $manifest.settings.timeoutSec } else { 180 }))
$userAgent = $(if ($manifest.settings.userAgent) { [string]$manifest.settings.userAgent } else { "ForgerEMS-Updater/3.1" })
$maxKeep   = [int]($(if ($manifest.settings.maxArchivePerItem) { $manifest.settings.maxArchivePerItem } else { 3 }))
$retries   = [int]($(if ($manifest.settings.retryCount) { $manifest.settings.retryCount } else { 3 }))

Ensure-Dir -Path $dlDir
Ensure-Dir -Path $logDir
if (-not $NoArchive) {
    Ensure-Dir -Path $arcDir
}

$script:LogFile = Join-Path $logDir ("update_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".log")

Write-Log ("Ventoy core: {0} {1} ({2})" -f $(if ($manifest.coreName) { [string]$manifest.coreName } else { "ForgerEMS Ventoy Core" }), $(if ($manifest.coreVersion) { [string]$manifest.coreVersion } else { "0.0.0-dev" }), $(Format-BuildTimestamp -Value $manifest.buildTimestampUtc)) "INFO"
Write-Log ("Release: " + $(if ($manifest.releaseType) { ([string]$manifest.releaseType).Trim().ToLowerInvariant() } else { "dev" })) "INFO"
Write-Log "Root: $root" "INFO"
Write-Log "Manifest: $manifestPath" "INFO"
Write-Log "Force=$Force VerifyOnly=$VerifyOnly NoArchive=$NoArchive" "INFO"

if (-not $manifest.items) {
    throw "Manifest has no items."
}

foreach ($item in $manifest.items) {
    $script:Summary.Total++

    $name = ([string]$item.name).Trim()
    $type = ([string]$(if ($item.type) { $item.type } else { "file" })).Trim().ToLowerInvariant()
    $url  = ([string]$item.url).Trim()
    $destRel = ([string]$item.dest).Trim()
    $enabled = $true
    if ($null -ne $item.enabled) { $enabled = [bool]$item.enabled }

    if (-not $enabled) {
        Write-Log "Skipping disabled item: $name" "WARN"
        $script:Summary.Disabled++
        continue
    }

    if ([string]::IsNullOrWhiteSpace($name) -or
        [string]::IsNullOrWhiteSpace($url) -or
        [string]::IsNullOrWhiteSpace($destRel)) {
        Write-Log "Skipping invalid manifest item (missing name/url/dest)." "WARN"
        $script:Summary.Failed++
        continue
    }

    $dest = Resolve-RootChildPath -Root $root -RelativePath $destRel
    $destDir = Split-Path -Parent $dest
    Ensure-Dir -Path $destDir

    $itemTimeout = if ($item.timeoutSec) { [int]$item.timeoutSec } else { $timeout }
    $archiveItem = $true
    if ($null -ne $item.archive) { $archiveItem = [bool]$item.archive }

    Write-Log "---- $name ----" "INFO"
    Write-Log "Type: $type" "INFO"
    Write-Log "Dest: $destRel" "INFO"
    Write-Log "URL:  $url" "INFO"

    if ($type -eq "page") {
        if ($VerifyOnly) {
            Write-Log "VerifyOnly: skipping page shortcut." "WARN"
            $script:Summary.Skipped++
            continue
        }

        if (-not $PSCmdlet.ShouldProcess($destRel, "Write URL shortcut")) {
            Write-Log "Would update shortcut: $destRel -> $url" "INFO"
            $script:Summary.Skipped++
            continue
        }

        try {
            Write-UrlShortcut -ShortcutPath $dest -Url $url
            Write-Log "Shortcut updated: $destRel" "OK"
            $script:Summary.Shortcut++
        }
        catch {
            Write-Log "Shortcut write failed: $($_.Exception.Message)" "ERROR"
            $script:Summary.Failed++
        }

        continue
    }

    if ($type -ne "file") {
        Write-Log "Unsupported item type '$type' for '$name'. Supported: file, page." "WARN"
        $script:Summary.Failed++
        continue
    }

    $sha = ([string]$item.sha256).Trim().ToLowerInvariant()
    $shaUrl = ([string]$item.sha256Url).Trim()

    if (-not $sha -and $shaUrl -and ($VerifyOnly -or -not $WhatIfPreference)) {
        try {
            $sha = Get-ShaFromUrl -ShaUrl $shaUrl -TimeoutSec $itemTimeout -UserAgent $userAgent
            if ($sha) {
                Write-Log "Fetched SHA256 from sha256Url." "OK"
            }
            else {
                Write-Log "sha256Url was provided but no valid hash was parsed." "WARN"
            }
        }
        catch {
            Write-Log "Failed fetching sha256Url: $($_.Exception.Message)" "WARN"
        }
    }
    elseif (-not $sha -and $shaUrl -and $WhatIfPreference) {
        Write-Log "WhatIf: would fetch SHA256 from sha256Url during a real run." "INFO"
    }

    if ($VerifyOnly) {
        if (-not (Test-Path -LiteralPath $dest)) {
            Write-Log "Verify failed: destination missing: $destRel" "ERROR"
            $script:Summary.Failed++
            continue
        }

        if ($sha) {
            $cur = Get-Sha256 -Path $dest
            if ($cur -eq $sha) {
                Write-Log "Verified OK (sha256 match)." "OK"
                $script:Summary.Verified++
            }
            else {
                Write-Log "Verify failed: sha256 mismatch. Expected=$sha Got=$cur" "ERROR"
                $script:Summary.Failed++
            }
        }
        else {
            Write-Log "No sha256 provided; cannot verify '$name'." "WARN"
            $script:Summary.Skipped++
        }

        continue
    }

    if (-not $Force -and $sha -and (Test-Path -LiteralPath $dest)) {
        $cur = Get-Sha256 -Path $dest
        if ($cur -eq $sha) {
            Write-Log "Up-to-date (sha256 match). Skipping." "OK"
            $script:Summary.Skipped++
            continue
        }
    }
    elseif (-not $Force -and -not $sha -and (Test-Path -LiteralPath $dest)) {
        Write-Log "Destination exists and no sha256 is provided. Skipping to avoid blind overwrite." "WARN"
        $script:Summary.Skipped++
        continue
    }

    if (-not $PSCmdlet.ShouldProcess($destRel, "Download, verify, archive, and replace destination")) {
        Write-Log "Would update file: $destRel from $url" "INFO"
        $script:Summary.Skipped++
        continue
    }

    $tmpName = Safe-FileName -Text $name
    $tmpPath = Join-Path $dlDir ($tmpName + ".download")

    try {
        Download-File -Url $url -OutFile $tmpPath -TimeoutSec $itemTimeout -UserAgent $userAgent -Retries $retries
    }
    catch {
        Write-Log "Download failed: $($_.Exception.Message)" "ERROR"
        $script:Summary.Failed++
        continue
    }

    try {
        if ($sha) {
            $got = Get-Sha256 -Path $tmpPath
            if ($got -ne $sha) {
                throw "SHA256 mismatch. Expected=$sha Got=$got"
            }
            Write-Log "SHA256 OK." "OK"
        }
        else {
            Write-Log "No sha256 set for '$name' (recommended for important ISOs/tools)." "WARN"
        }

        if (-not $NoArchive -and $archiveItem -and (Test-Path -LiteralPath $dest)) {
            $didArchive = Archive-OldFile -ItemName $name -FilePath $dest -ArchiveDir $arcDir -MaxKeep $maxKeep
            if ($didArchive) {
                Write-Log "Archived old file." "OK"
                $script:Summary.Archived++
            }
        }

        Move-Item -LiteralPath $tmpPath -Destination $dest -Force

        Write-Log "Updated: $name" "OK"
        $script:Summary.Updated++
    }
    catch {
        Write-Log "Update failed for '$name': $($_.Exception.Message)" "ERROR"
        $script:Summary.Failed++
        if (Test-Path -LiteralPath $tmpPath) {
            Remove-Item -LiteralPath $tmpPath -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Log "---------------- SUMMARY ----------------" "INFO"
Write-Log "Total:    $($script:Summary.Total)" "INFO"
Write-Log "Updated:  $($script:Summary.Updated)" "INFO"
Write-Log "Verified: $($script:Summary.Verified)" "INFO"
Write-Log "Shortcut: $($script:Summary.Shortcut)" "INFO"
Write-Log "Archived: $($script:Summary.Archived)" "INFO"
Write-Log "Skipped:  $($script:Summary.Skipped)" "INFO"
Write-Log "Disabled: $($script:Summary.Disabled)" "INFO"
Write-Log "Failed:   $($script:Summary.Failed)" "INFO"
if ($script:LogFile -and (Test-Path -LiteralPath (Split-Path -Parent $script:LogFile))) {
    Write-Log "Log saved: $script:LogFile" "OK"
}
else {
    Write-Log "Log file was not created because the run was previewed only." "INFO"
}
