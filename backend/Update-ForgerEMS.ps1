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
    [switch]$ShowVersion,
    # CI / release validation: keep terminating failure when managed items fail (including fallback-covered).
    [switch]$StrictManagedDownloads,
    # Retry only items recorded as retryable in the latest ForgerEMS-managed-download-result.json on the USB root.
    [switch]$RetryFailedManagedDownloads,
    # Comma-separated or repeated USB Builder profile category IDs to include for this run.
    [string[]]$IncludedCategories = @()
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$runtimeHelperCandidates = @(
    (Join-Path $PSScriptRoot "ForgerEMS.Runtime.ps1"),
    (Join-Path $PSScriptRoot "backend\ForgerEMS.Runtime.ps1")
) | Select-Object -Unique

$runtimeHelperImported = $false
foreach ($runtimeHelperCandidate in $runtimeHelperCandidates) {
    if (Test-Path -LiteralPath $runtimeHelperCandidate) {
        . $runtimeHelperCandidate
        $runtimeHelperImported = $true
        break
    }
}

if (-not $runtimeHelperImported) {
    throw "ForgerEMS runtime helper was not found. Checked: $($runtimeHelperCandidates -join '; ')"
}

$checksumResolverCandidates = @(
    (Join-Path $PSScriptRoot "ToolkitManager\ChecksumResolver.ps1"),
    (Join-Path $PSScriptRoot "backend\ToolkitManager\ChecksumResolver.ps1")
) | Select-Object -Unique

foreach ($checksumResolverCandidate in $checksumResolverCandidates) {
    if (Test-Path -LiteralPath $checksumResolverCandidate) {
        . $checksumResolverCandidate
        break
    }
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

$script:LogFile = $null
$script:Summary = [ordered]@{
    Total                    = 0
    ManagedFileItems         = 0
    PlaceholderItems         = 0
    Downloaded               = 0
    Skipped                  = 0
    Verified                 = 0
    Updated                  = 0
    Shortcut                 = 0
    PlaceholderOnly          = 0
    Failed                   = 0
    FailedWithFallback       = 0
    Archived                 = 0
    Disabled                 = 0
    FallbackShortcutsCreated = 0
    FallbackShortcutsReused  = 0
    UpToDateSkipped          = 0
    WarnEvents               = 0
    ExtrasDirsCreated        = 0
    ExtrasDirsSkipped        = 0
    ExtrasReadmesCreated     = 0
    ExtrasReadmesSkipped     = 0
}

$script:ManagedFailureLines = [System.Collections.Generic.List[string]]::new()
$script:ManagedDownloadFailedRecords = [System.Collections.Generic.List[hashtable]]::new()
$script:ManagedDownloadRunId = [guid]::NewGuid().ToString('N')
$script:ManagedDownloadRunStartedUtc = (Get-Date).ToUniversalTime().ToString('o')
$script:RetryManagedDestinations = $null
$script:NormalizeManifestMatchTextCallCount = 0
$script:ManagedPlaceholderShadowMatchCallCount = 0
$script:ProgressLogFile = $env:FORGEREMS_UPDATE_PROGRESS_LOG
if ([string]::IsNullOrWhiteSpace($script:ProgressLogFile)) {
    $script:ProgressLogFile = Join-Path $env:TEMP ("ForgerEMS-Update-progress-{0}.log" -f $PID)
}

function Write-ProgressLog {
    param([Parameter(Mandatory)][string]$Message)

    if ([string]::IsNullOrWhiteSpace($script:ProgressLogFile)) {
        return
    }

    try {
        $progressParent = Split-Path -Parent $script:ProgressLogFile
        if (-not [string]::IsNullOrWhiteSpace($progressParent)) {
            [IO.Directory]::CreateDirectory($progressParent) | Out-Null
        }

        $line = "[{0}] {1}" -f (Get-Date).ToString("yyyy-MM-dd HH:mm:ss.fff"), $Message
        [IO.File]::AppendAllText($script:ProgressLogFile, $line + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }
    catch {
        # Progress logging must never change updater behavior.
    }
}

Write-ProgressLog ("startup / args parsed: DriveLetter='{0}' UsbRoot='{1}' ManifestName='{2}' WhatIf={3} VerifyOnly={4} IncludedCategories='{5}'" -f `
    $DriveLetter, $UsbRoot, $ManifestName, [bool]$WhatIfPreference, [bool]$VerifyOnly, ($IncludedCategories -join ","))

function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INIT","INFO","OK","WARN","ERROR","ACTION","COMPLETE")][string]$Level = "INFO"
    )

    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$ts][$Level] $Message"

    switch ($Level) {
        "INFO"  { Write-Host $line -ForegroundColor Cyan }
        "OK"    { Write-Host $line -ForegroundColor Green }
        "WARN"  {
            $script:Summary.WarnEvents++
            Write-Host $line -ForegroundColor Yellow
        }
        "ERROR" { Write-Host $line -ForegroundColor Red }
        "ACTION" { Write-Host $line -ForegroundColor Yellow }
        "INIT" { Write-Host $line -ForegroundColor Cyan }
        "COMPLETE" { Write-Host $line -ForegroundColor Green }
    }

    if ($script:LogFile -and -not $WhatIfPreference) {
        $logParent = Split-Path -Parent $script:LogFile
        if ($logParent -and (Test-Path -LiteralPath $logParent)) {
            Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
        }
    }
}

function Invoke-TimedReleasePhase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$ScriptBlock
    )

    Write-ProgressLog ("{0} start" -f $Name)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $ScriptBlock
    }
    finally {
        $sw.Stop()
        Write-ProgressLog ("{0} end elapsedMs={1:n0}" -f $Name, $sw.Elapsed.TotalMilliseconds)
        Write-Log ("Timing: {0} completed in {1:n0} ms" -f $Name, $sw.Elapsed.TotalMilliseconds) "INFO"
    }
}

function Add-ManagedDownloadFailedRecord {
    param(
        [string]$Name,
        [string]$DestRel,
        [string]$Url,
        [string]$FailureKind,
        [string]$SafeReason,
        [string]$HttpStatus,
        [string]$FallbackRelPath
    )

    $domain = ""
    try {
        if (-not [string]::IsNullOrWhiteSpace($Url)) {
            $u = [uri]$Url
            $domain = $u.Host
        }
    }
    catch { }

    $safeUrl = $Url
    if ($safeUrl.Length -gt 512) {
        $safeUrl = $safeUrl.Substring(0, 512) + "…"
    }

    [void]$script:ManagedDownloadFailedRecords.Add(@{
            id                      = $Name
            name                    = $Name
            category                = ""
            type                    = "file"
            destinationRelativePath = $DestRel
            sourceDomain            = $domain
            sourceUrl               = $safeUrl
            failureKind             = $FailureKind
            safeReason              = $SafeReason
            downloaderAttempts      = 0
            httpStatus              = $HttpStatus
            checksumExpected        = ""
            checksumActual          = ""
            fallbackRelativePath    = $FallbackRelPath
            retryable               = $true
            required                = $true
        })
}

function Write-ForgerEmsManagedDownloadResultJson {
    param(
        [Parameter(Mandatory)][string]$RootPath,
        [Parameter(Mandatory)][string]$Readiness
    )

    $obj = [ordered]@{
        runId                  = $script:ManagedDownloadRunId
        startedAt              = $script:ManagedDownloadRunStartedUtc
        completedAt            = (Get-Date).ToUniversalTime().ToString('o')
        readiness              = $Readiness
        totalManifestItems     = $script:Summary.Total
        managedSelected        = $script:Summary.ManagedFileItems
        managedCompleted       = $script:Summary.Downloaded
        managedFailed          = $script:Summary.Failed
        manualInfoShortcuts    = $script:Summary.PlaceholderItems
        placeholderOnly        = $script:Summary.PlaceholderOnly
        fallbackCreated        = $script:Summary.FallbackShortcutsCreated
        fallbackReused         = $script:Summary.FallbackShortcutsReused
        failedItems            = @($script:ManagedDownloadFailedRecords)
        retryFailedModeActive  = ($null -ne $script:RetryManagedDestinations)
    }

    $path = Join-Path $RootPath "ForgerEMS-managed-download-result.json"
    if ($WhatIfPreference) {
        Write-Log "WhatIf: skipping managed download result JSON: $path" "INFO"
        return
    }

    try {
        $json = $obj | ConvertTo-Json -Depth 12
        Set-Content -LiteralPath $path -Value $json -Encoding UTF8
        Write-Log "Managed download result JSON: $path" "OK"
    }
    catch {
        Write-Log ("Could not write managed download result JSON: {0}" -f $_.Exception.Message) "WARN"
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

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    $hash = Get-ForgerSha256 -LiteralPath $Path
    Write-Log ("SHA256 hash provider: {0} file={1}" -f (Get-ForgerLastHashProvider), (Get-ForgerSafePathForLog -Path $Path)) "INFO"
    return $hash
}

function Get-Sha512 {
    param([Parameter(Mandatory)][string]$Path)

    $hash = Get-ForgerSha512 -LiteralPath $Path
    Write-Log ("SHA512 hash provider: {0} file={1}" -f (Get-ForgerLastHashProvider), (Get-ForgerSafePathForLog -Path $Path)) "INFO"
    return $hash
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

function Get-ManifestDestinationKey {
    param([AllowNull()][string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return ""
    }

    return ([string]$RelativePath).Trim().ToLowerInvariant()
}

function Normalize-ManifestMatchText {
    param([AllowNull()][string]$Text)

    $script:NormalizeManifestMatchTextCallCount++
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

    $script:ManagedPlaceholderShadowMatchCallCount++
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

function Get-ManifestItemEnabled {
    param([AllowNull()]$Item)

    if ($null -eq $Item) {
        return $false
    }

    if ($null -ne $Item.enabled) {
        return [bool]$Item.enabled
    }

    return $true
}

function Get-ManifestItemType {
    param([AllowNull()]$Item)

    if ($null -eq $Item) {
        return "file"
    }

    return ([string]$(if ($Item.type) { $Item.type } else { "file" })).Trim().ToLowerInvariant()
}

function Get-UniqueNonEmptyManifestMatchText {
    param([object[]]$Values)

    $seen = @{}
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($value in @($Values)) {
        $normalized = Normalize-ManifestMatchText -Text ([string]$value)
        if ([string]::IsNullOrWhiteSpace($normalized)) {
            continue
        }

        if (-not $seen.ContainsKey($normalized)) {
            $seen[$normalized] = $true
            [void]$result.Add($normalized)
        }
    }

    return $result.ToArray()
}

function New-ManagedPlaceholderMatchInfo {
    param(
        [Parameter(Mandatory)]$Item,
        [Parameter(Mandatory)][ValidateSet("page", "file")][string]$Kind
    )

    $dest = ([string]$(if ($Item.dest) { $Item.dest } else { "" })).Trim()
    $dir = if ([string]::IsNullOrWhiteSpace($dest)) { "" } else { [string](Split-Path -Parent $dest) }
    $name = [string]$(if ($Item.name) { $Item.name } else { "" })

    if ($Kind -eq "page") {
        $matchTexts = Get-UniqueNonEmptyManifestMatchText -Values @(
            (Get-PlaceholderDisplayLabelFromDestination -RelativePath $dest),
            $name
        )
    }
    else {
        $matchTexts = Get-UniqueNonEmptyManifestMatchText -Values @(
            $name,
            ([IO.Path]::GetFileNameWithoutExtension($dest))
        )
    }

    return [PSCustomObject]@{
        Item       = $Item
        Dest       = $dest
        DestKey    = Get-ManifestDestinationKey -RelativePath $dest
        Directory  = $dir
        MatchTexts = $matchTexts
    }
}

function Test-ManagedPlaceholderShadowMatchInfo {
    param(
        [Parameter(Mandatory)]$PageInfo,
        [Parameter(Mandatory)]$ManagedInfo
    )

    $script:ManagedPlaceholderShadowMatchCallCount++
    if ([string]::IsNullOrWhiteSpace($PageInfo.Dest) -or [string]::IsNullOrWhiteSpace($ManagedInfo.Dest)) {
        return $false
    }

    if (-not [string]::Equals([string]$PageInfo.Directory, [string]$ManagedInfo.Directory, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    foreach ($pageLabel in @($PageInfo.MatchTexts)) {
        foreach ($managedTarget in @($ManagedInfo.MatchTexts)) {
            if ($managedTarget.Contains($pageLabel) -or $pageLabel.Contains($managedTarget)) {
                return $true
            }
        }
    }

    return $false
}

function Get-ActiveManagedPlaceholderPlan {
    param([Parameter(Mandatory)][object[]]$Items)

    $managedByDirectory = @{}
    $pageInfos = New-Object System.Collections.Generic.List[object]
    $byPlaceholderDest = @{}
    $byManagedDest = @{}

    foreach ($item in $Items) {
        if ($null -eq $item) { continue }

        if (-not (Get-ManifestItemEnabled -Item $item)) {
            continue
        }

        $itemType = Get-ManifestItemType -Item $item
        if ($itemType -eq "file") {
            $managedInfo = New-ManagedPlaceholderMatchInfo -Item $item -Kind "file"
            if ([string]::IsNullOrWhiteSpace($managedInfo.Dest)) {
                continue
            }

            $dirKey = ([string]$managedInfo.Directory).ToLowerInvariant()
            if (-not $managedByDirectory.ContainsKey($dirKey)) {
                $managedByDirectory[$dirKey] = New-Object System.Collections.Generic.List[object]
            }

            [void]($managedByDirectory[$dirKey]).Add($managedInfo)
            continue
        }

        if ($itemType -eq "page") {
            [void]$pageInfos.Add((New-ManagedPlaceholderMatchInfo -Item $item -Kind "page"))
        }
    }

    foreach ($pageInfo in $pageInfos) {
        $dirKey = ([string]$pageInfo.Directory).ToLowerInvariant()
        if (-not $managedByDirectory.ContainsKey($dirKey)) {
            continue
        }

        $matchedManagedInfo = $null
        foreach ($managedInfo in $managedByDirectory[$dirKey]) {
            if (Test-ManagedPlaceholderShadowMatchInfo -PageInfo $pageInfo -ManagedInfo $managedInfo) {
                $matchedManagedInfo = $managedInfo
                break
            }
        }

        if ($null -eq $matchedManagedInfo) {
            continue
        }

        $entry = [PSCustomObject]@{
            PlaceholderDest = $pageInfo.Dest
            ManagedDest     = $matchedManagedInfo.Dest
            PlaceholderItem = $pageInfo.Item
            ManagedItem     = $matchedManagedInfo.Item
        }

        if (-not $byPlaceholderDest.ContainsKey($pageInfo.DestKey)) {
            $byPlaceholderDest[$pageInfo.DestKey] = $entry
        }

        if (-not $byManagedDest.ContainsKey($matchedManagedInfo.DestKey)) {
            $byManagedDest[$matchedManagedInfo.DestKey] = New-Object System.Collections.Generic.List[object]
        }

        [void]($byManagedDest[$matchedManagedInfo.DestKey]).Add($entry)
    }

    return [PSCustomObject]@{
        ByPlaceholderDest = $byPlaceholderDest
        ByManagedDest     = $byManagedDest
    }
}

function Get-PreferredFallbackShortcutPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManagedDestination,
        [Parameter(Mandatory)]$ManagedPlaceholderPlan
    )

    $managedKey = Get-ManifestDestinationKey -RelativePath $ManagedDestination
    if (-not $ManagedPlaceholderPlan.ByManagedDest.ContainsKey($managedKey)) {
        return $null
    }

    $entry = @($ManagedPlaceholderPlan.ByManagedDest[$managedKey] | Select-Object -First 1)
    if ($entry.Count -eq 0) {
        return $null
    }

    return (Resolve-RootChildPath -Root $Root -RelativePath ([string]$entry[0].PlaceholderDest))
}

function Remove-ManagedSuccessPlaceholders {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManagedDestination,
        [Parameter(Mandatory)]$ManagedPlaceholderPlan
    )

    $managedKey = Get-ManifestDestinationKey -RelativePath $ManagedDestination
    if (-not $ManagedPlaceholderPlan.ByManagedDest.ContainsKey($managedKey)) {
        return 0
    }

    $removedCount = 0

    foreach ($entry in @($ManagedPlaceholderPlan.ByManagedDest[$managedKey] | ForEach-Object { $_ })) {
        $placeholderDestRel = ([string]$entry.PlaceholderDest).Trim()
        if ([string]::IsNullOrWhiteSpace($placeholderDestRel)) {
            continue
        }

        $placeholderPath = Resolve-RootChildPath -Root $Root -RelativePath $placeholderDestRel
        if (-not (Test-Path -LiteralPath $placeholderPath)) {
            continue
        }

        try {
            if ($PSCmdlet.ShouldProcess($placeholderDestRel, "Remove placeholder shortcut because managed payload staged successfully")) {
                Remove-Item -LiteralPath $placeholderPath -Force
                Write-Log "Removed placeholder shortcut because managed payload staged successfully: $placeholderPath" "OK"
            }
            else {
                Write-Log "Would remove placeholder shortcut because managed payload staged successfully: $placeholderPath" "INFO"
            }

            $removedCount++
        }
        catch {
            Write-Log "Failed to remove placeholder shortcut after managed staging for '$ManagedDestination': $($_.Exception.Message)" "WARN"
        }
    }

    return $removedCount
}

function Write-DownloadFallbackShortcut {
    param(
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string]$ItemName,
        [Parameter(Mandatory)][string]$Url,
        [AllowNull()][string]$PreferredShortcutPath
    )

    try {
        $destinationDir = Split-Path -Parent $DestinationPath
        if ([string]::IsNullOrWhiteSpace($destinationDir)) {
            return [PSCustomObject]@{
                Outcome      = "none"
                ShortcutPath = ""
            }
        }

        Ensure-Dir -Path $destinationDir

        $shortcutPath = $null

        if (-not [string]::IsNullOrWhiteSpace($PreferredShortcutPath)) {
            $shortcutPath = $PreferredShortcutPath
            $preferredShortcutDir = Split-Path -Parent $shortcutPath
            if (-not [string]::IsNullOrWhiteSpace($preferredShortcutDir)) {
                Ensure-Dir -Path $preferredShortcutDir
            }

            if (Test-Path -LiteralPath $shortcutPath) {
                Write-Log "Using existing fallback shortcut because managed download failed: $shortcutPath" "WARN"
                return [PSCustomObject]@{
                    Outcome      = "existing"
                    ShortcutPath = $shortcutPath
                }
            }
        }
        else {
            $itemNameToken = (($ItemName -split '\s+')[0]).Trim()
            if (-not [string]::IsNullOrWhiteSpace($itemNameToken)) {
                $existingRelatedShortcut = Get-ChildItem -LiteralPath $destinationDir -Filter "*.url" -ErrorAction SilentlyContinue |
                    Where-Object { $_.BaseName -like ("*" + $itemNameToken + "*") } |
                    Select-Object -First 1

                if ($existingRelatedShortcut) {
                    Write-Log "Using existing seeded placeholder shortcut as fallback because managed download failed: $($existingRelatedShortcut.FullName)" "WARN"
                    return [PSCustomObject]@{
                        Outcome      = "existing"
                        ShortcutPath = $existingRelatedShortcut.FullName
                    }
                }
            }

            $shortcutName = "DOWNLOAD - " + (Safe-FileName -Text $ItemName) + ".url"
            $shortcutPath = Join-Path $destinationDir $shortcutName

            if (Test-Path -LiteralPath $shortcutPath) {
                Write-Log "Using existing fallback shortcut because managed download failed: $shortcutPath" "WARN"
                return [PSCustomObject]@{
                    Outcome      = "existing"
                    ShortcutPath = $shortcutPath
                }
            }
        }

        Write-UrlShortcut -ShortcutPath $shortcutPath -Url $Url
        Write-Log "Fallback shortcut written because managed download failed: $shortcutPath" "WARN"
        return [PSCustomObject]@{
            Outcome      = "created"
            ShortcutPath = $shortcutPath
        }
    }
    catch {
        Write-Log "Failed to write fallback shortcut for '$ItemName': $($_.Exception.Message)" "WARN"
        return [PSCustomObject]@{
            Outcome      = "error"
            ShortcutPath = ""
        }
    }
}

function Get-ExceptionDiagnostic {
    param(
        [Management.Automation.ErrorRecord]$ErrorRecord,
        [System.Exception]$Exception
    )

    $current = if ($ErrorRecord) {
        $ErrorRecord.Exception
    }
    else {
        $Exception
    }

    if ($null -eq $current) {
        return "Unknown error."
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $depth = 0

    while ($current -and $depth -lt 4) {
        $entry = "{0}: {1}" -f $current.GetType().FullName, $current.Message

        try {
            $statusCodeProperty = $current.PSObject.Properties["StatusCode"]
            if ($statusCodeProperty -and $null -ne $current.StatusCode) {
                $entry += " [HTTP $([int]$current.StatusCode)]"
            }
        }
        catch {
        }

        try {
            $responseProperty = $current.PSObject.Properties["Response"]
            if ($responseProperty -and $null -ne $current.Response) {
                $statusCode = $current.Response.StatusCode
                if ($null -ne $statusCode) {
                    $entry += " [Response $([int]$statusCode)]"
                }
            }
        }
        catch {
        }

        $parts.Add($entry)
        $current = $current.InnerException
        $depth++
    }

    return ($parts -join " <= ")
}

function Get-FileStateDescription {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return "missing"
    }

    $item = Get-Item -LiteralPath $Path
    return "exists ($($item.Length) bytes) at $Path"
}

function Get-NormalizedDisplayText {
    param([AllowNull()]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $parts = @($Value) | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    if (-not $parts -or $parts.Count -eq 0) {
        return ""
    }

    return (($parts -join " ").Trim())
}

function Get-HttpStatusCodeDisplayText {
    param([AllowNull()]$Value)

    $text = Get-NormalizedDisplayText -Value $Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ""
    }

    $match = [regex]::Match($text, '\b\d{3}\b')
    if ($match.Success) {
        return $match.Value
    }

    return $text
}

function Invoke-HttpClientDownload {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutFile,
        [int]$TimeoutSec = 180,
        [string]$UserAgent = "ForgerEMS-Updater/3.1",
        [string]$ItemName = "payload"
    )

    Add-Type -AssemblyName System.Net.Http | Out-Null

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $true
    try {
        $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    }
    catch {
    }

    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

    $response = $null
    $responseStream = $null
    $fileStream = $null

    try {
        $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $null = $response.EnsureSuccessStatusCode()

        $totalBytes = if ($response.Content.Headers.ContentLength.HasValue) { [int64]$response.Content.Headers.ContentLength.Value } else { [int64]0 }
        $responseStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $fileStream = New-Object System.IO.FileStream($OutFile, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None, 1048576, [IO.FileOptions]::SequentialScan)
        $buffer = New-Object byte[] 1048576
        $downloadedBytes = [int64]0
        $lastLogBytes = [int64]0
        $lastLogUtc = [DateTime]::UtcNow
        $lastProgressUtc = [DateTime]::UtcNow
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()

        while (($read = $responseStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $fileStream.Write($buffer, 0, $read)
            $downloadedBytes += [int64]$read

            $nowUtc = [DateTime]::UtcNow
            if (($nowUtc - $lastLogUtc).TotalSeconds -ge 2 -or ($totalBytes -gt 0 -and $downloadedBytes -ge $totalBytes)) {
                $elapsedSeconds = [Math]::Max($stopwatch.Elapsed.TotalSeconds, 0.001)
                $speedMbps = ($downloadedBytes / 1MB) / $elapsedSeconds
                $downloadedMb = [Math]::Round($downloadedBytes / 1MB, 0)

                if ($totalBytes -gt 0) {
                    $totalMb = [Math]::Round($totalBytes / 1MB, 0)
                    $percent = [Math]::Min(100, [Math]::Round(($downloadedBytes / [double]$totalBytes) * 100, 1))
                    $remainingBytes = [Math]::Max(0, $totalBytes - $downloadedBytes)
                    $etaSeconds = if ($speedMbps -gt 0) { [int][Math]::Round(($remainingBytes / 1MB) / $speedMbps) } else { 0 }
                    $eta = if ($etaSeconds -ge 3600) {
                        '{0}h {1}m {2}s' -f [int]($etaSeconds / 3600), [int](($etaSeconds % 3600) / 60), [int]($etaSeconds % 60)
                    }
                    elseif ($etaSeconds -ge 60) {
                        '{0}m {1}s' -f [int]($etaSeconds / 60), [int]($etaSeconds % 60)
                    }
                    else {
                        '{0}s' -f $etaSeconds
                    }

                    Write-Log ("Downloading {0}... {1}% | {2} MB / {3} MB | {4:0.0} MB/s | ETA {5}" -f $ItemName, $percent, $downloadedMb, $totalMb, $speedMbps, $eta) "INFO"
                }
                else {
                    Write-Log ("Downloading {0}... {1} MB downloaded | {2:0.0} MB/s" -f $ItemName, $downloadedMb, $speedMbps) "INFO"
                }

                if ($downloadedBytes -gt $lastLogBytes) {
                    $lastProgressUtc = $nowUtc
                    $lastLogBytes = $downloadedBytes
                }
                elseif (($nowUtc - $lastProgressUtc).TotalSeconds -ge 30) {
                    Write-Log "Download appears stalled; retrying may be required if no progress resumes." "WARN"
                    $lastProgressUtc = $nowUtc
                }

                $lastLogUtc = $nowUtc
            }
        }

        $fileStream.Flush($true)
        $stopwatch.Stop()

        return [PSCustomObject]@{
            Method      = "HttpClient"
            StatusCode  = [int]$response.StatusCode
            ReasonPhrase = [string]$response.ReasonPhrase
            FinalUri    = [string]$response.RequestMessage.RequestUri.AbsoluteUri
        }
    }
    finally {
        if ($fileStream) {
            $fileStream.Dispose()
        }

        if ($responseStream) {
            $responseStream.Dispose()
        }

        if ($response) {
            $response.Dispose()
        }

        $client.Dispose()
        $handler.Dispose()
    }
}

function Invoke-CurlDownload {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutFile,
        [int]$TimeoutSec = 180,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    $curlPath = Join-Path $env:SystemRoot "System32\curl.exe"
    if (-not (Test-Path -LiteralPath $curlPath)) {
        throw "curl.exe is not available on this system."
    }

    $arguments = @(
        "-L",
        "-sS",
        "--fail",
        "--retry", "2",
        "--connect-timeout", [string]$TimeoutSec,
        "--user-agent", $UserAgent,
        "--output", $OutFile,
        $Url
    )

    & $curlPath @arguments 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe exited with code $LASTEXITCODE."
    }

    return [PSCustomObject]@{
        Method   = "curl.exe"
        ExitCode = 0
        FinalUri = $Url
    }
}

function Get-UrlText {
    param(
        [Parameter(Mandatory)][string]$Url,
        [int]$TimeoutSec = 60,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    $headers = @{ "User-Agent" = $UserAgent }
    $methods = @(
        @{
            Name = "HttpClient"
            Action = {
                Add-Type -AssemblyName System.Net.Http | Out-Null

                $handler = New-Object System.Net.Http.HttpClientHandler
                $handler.AllowAutoRedirect = $true
                try {
                    $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
                }
                catch {
                }

                $client = New-Object System.Net.Http.HttpClient($handler)
                $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
                $client.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

                if ($Url -like "https://api.github.com/*") {
                    $client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
                    $client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28")
                }

                $response = $null
                try {
                    $response = $client.GetAsync($Url).GetAwaiter().GetResult()
                    $null = $response.EnsureSuccessStatusCode()
                    [PSCustomObject]@{
                        Text         = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        Method       = "HttpClient"
                        StatusCode   = [int]$response.StatusCode
                        ReasonPhrase = [string]$response.ReasonPhrase
                        FinalUri     = [string]$response.RequestMessage.RequestUri.AbsoluteUri
                    }
                }
                finally {
                    if ($response) {
                        $response.Dispose()
                    }

                    $client.Dispose()
                    $handler.Dispose()
                }
            }
        },
        @{
            Name = "Invoke-WebRequest"
            Action = {
                $response = Invoke-WebRequest -Uri $Url -Headers $headers -TimeoutSec $TimeoutSec -MaximumRedirection 10 -UseBasicParsing
                [PSCustomObject]@{
                    Text         = [string]$response.Content
                    Method       = "Invoke-WebRequest"
                    StatusCode   = if ($response.StatusCode) { [int]$response.StatusCode } else { 0 }
                    ReasonPhrase = if ($response.StatusDescription) { [string]$response.StatusDescription } else { "" }
                    FinalUri     = if ($response.BaseResponse -and $response.BaseResponse.ResponseUri) { [string]$response.BaseResponse.ResponseUri.AbsoluteUri } else { $Url }
                }
            }
        }
    )

    foreach ($method in $methods) {
        try {
            return & $method.Action
        }
        catch {
            Write-Log "Checksum source fetch via $($method.Name) failed: $(Get-ExceptionDiagnostic -ErrorRecord $_)" "WARN"
        }
    }

    throw "All checksum source fetch strategies failed for $Url"
}

function Download-File {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutFile,
        [int]$TimeoutSec = 180,
        [string]$UserAgent = "ForgerEMS-Updater/3.1",
        [int]$Retries = 3,
        [string]$ItemName = "payload"
    )

    $headers = @{ "User-Agent" = $UserAgent }

    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        try {
            Write-Log "Download attempt $attempt/$Retries starting: $Url" "INFO"

            if (Test-Path -LiteralPath $OutFile) {
                Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
            }

            $downloadSucceeded = $false
            $downloadTrace = @()
            $downloadMethods = @(
                @{
                    Name   = "HttpClient"
                    Action = {
                        Invoke-HttpClientDownload -Url $Url -OutFile $OutFile -TimeoutSec $TimeoutSec -UserAgent $UserAgent -ItemName $ItemName
                    }
                },
                @{
                    Name   = "Invoke-WebRequest"
                    Action = {
                        $response = Invoke-WebRequest -Uri $Url -OutFile $OutFile -Headers $headers -TimeoutSec $TimeoutSec -MaximumRedirection 10 -UseBasicParsing
                        [PSCustomObject]@{
                            Method       = "Invoke-WebRequest"
                            StatusCode   = if ($response.StatusCode) { [int]$response.StatusCode } else { $null }
                            ReasonPhrase = if ($response.StatusDescription) { [string]$response.StatusDescription } else { "" }
                            FinalUri     = if ($response.BaseResponse -and $response.BaseResponse.ResponseUri) { [string]$response.BaseResponse.ResponseUri.AbsoluteUri } else { $Url }
                        }
                    }
                },
                @{
                    Name   = "curl.exe"
                    Action = {
                        Invoke-CurlDownload -Url $Url -OutFile $OutFile -TimeoutSec $TimeoutSec -UserAgent $UserAgent
                    }
                },
                @{
                    Name   = "BITS"
                    Action = {
                        Start-BitsTransfer -Source $Url -Destination $OutFile -ErrorAction Stop
                        [PSCustomObject]@{
                            Method       = "BITS"
                            StatusCode   = $null
                            ReasonPhrase = ""
                            FinalUri     = $Url
                        }
                    }
                }
            )

            foreach ($downloadMethod in $downloadMethods) {
                try {
                    Write-Log "Using $($downloadMethod.Name) for foreground download." "INFO"
                    $downloadMetadata = & $downloadMethod.Action
                    $downloadMetadataRecord = @($downloadMetadata | Select-Object -First 1)[0]

                    if (-not (Test-Path -LiteralPath $OutFile)) {
                        throw "The download method returned without creating '$OutFile'."
                    }

                    $sizeBytes = (Get-Item -LiteralPath $OutFile).Length
                    if ($sizeBytes -le 0) {
                        throw "The downloaded file is empty."
                    }

                    $downloadStatusCode = if ($downloadMetadataRecord -and $downloadMetadataRecord.PSObject.Properties["StatusCode"]) { Get-HttpStatusCodeDisplayText -Value $downloadMetadataRecord.StatusCode } else { "" }
                    $downloadReasonPhrase = if ($downloadMetadataRecord -and $downloadMetadataRecord.PSObject.Properties["ReasonPhrase"]) { Get-NormalizedDisplayText -Value $downloadMetadataRecord.ReasonPhrase } else { "" }
                    $downloadFinalUri = if ($downloadMetadataRecord -and $downloadMetadataRecord.PSObject.Properties["FinalUri"]) { Get-NormalizedDisplayText -Value $downloadMetadataRecord.FinalUri } else { "" }

                    Write-Log "Download complete: $ItemName" "OK"
                    Write-Log "Download completed via $($downloadMethod.Name): $OutFile ($sizeBytes bytes)" "OK"
                    if (-not [string]::IsNullOrWhiteSpace($downloadStatusCode)) {
                        Write-Log (("Download HTTP status via $($downloadMethod.Name): $downloadStatusCode $downloadReasonPhrase").TrimEnd()) "INFO"
                    }
                    if (-not [string]::IsNullOrWhiteSpace($downloadFinalUri)) {
                        Write-Log "Download final URL via $($downloadMethod.Name): $downloadFinalUri" "INFO"
                    }
                    Write-Log "Download destination state after transfer: $(Get-FileStateDescription -Path $OutFile)" "INFO"
                    $traceEntry = "$($downloadMethod.Name)=success"
                    if (-not [string]::IsNullOrWhiteSpace($downloadStatusCode)) {
                        $traceEntry += " [HTTP $downloadStatusCode"
                        if (-not [string]::IsNullOrWhiteSpace($downloadReasonPhrase)) {
                            $traceEntry += " $downloadReasonPhrase"
                        }
                        $traceEntry += "]"
                    }
                    $downloadTrace += $traceEntry
                    $downloadSucceeded = $true

                    $attemptSummary = if ($downloadTrace.Count -gt 0) { $downloadTrace -join "; " } else { "none" }

                    return [PSCustomObject]@{
                        Method       = $downloadMethod.Name
                        SizeBytes    = $sizeBytes
                        OutFile      = $OutFile
                        StatusCode   = $downloadStatusCode
                        ReasonPhrase = $downloadReasonPhrase
                        FinalUri     = $downloadFinalUri
                        AttemptSummary = $attemptSummary
                    }
                }
                catch {
                    $failureDiagnostic = Get-ExceptionDiagnostic -ErrorRecord $_
                    Write-Log "$($downloadMethod.Name) download strategy failed: $failureDiagnostic" "WARN"
                    Write-Log "Destination state after $($downloadMethod.Name) failure: $(Get-FileStateDescription -Path $OutFile)" "INFO"
                    $failureStatusCode = Get-HttpStatusCodeDisplayText -Value $failureDiagnostic
                    $traceEntry = "$($downloadMethod.Name)=failed"
                    if ($failureStatusCode -match '^\d{3}$') {
                        $traceEntry += " [HTTP $failureStatusCode]"
                    }
                    if (-not [string]::IsNullOrWhiteSpace($failureDiagnostic)) {
                        $traceEntry += " {$failureDiagnostic}"
                    }
                    $downloadTrace += $traceEntry
                    if (Test-Path -LiteralPath $OutFile) {
                        Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
                    }
                }
            }

            if (-not $downloadSucceeded) {
                $attemptSummary = if ($downloadTrace.Count -gt 0) { $downloadTrace -join "; " } else { "none" }
                Write-Log "Downloader methods attempted: $attemptSummary" "WARN"

                $failure = New-Object System.Exception("All download strategies failed for $Url")
                $failure.Data["AttemptedMethodSummary"] = $attemptSummary
                throw $failure
            }

            return
        }
        catch {
            Write-Log "Download attempt $attempt failed for $Url :: $(Get-ExceptionDiagnostic -ErrorRecord $_)" "WARN"
            if ($attempt -eq $Retries) { throw }
            $nextAttempt = $attempt + 1
            Write-Log "Download appears stalled; retrying..." "WARN"
            Write-Log "Retry attempt $nextAttempt/$Retries for $ItemName" "ACTION"
            Start-Sleep -Seconds ([Math]::Min(5 * $attempt, 15))
        }
    }
}

function Get-ShaFromUrl {
    param(
        [Parameter(Mandatory)][string]$ShaUrl,
        [ValidateSet("SHA256", "SHA512")][string]$Algorithm = "SHA256",
        [string]$TargetFileName = "",
        [int]$TimeoutSec = 60,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    try {
        $response = Get-UrlText -Url $ShaUrl -TimeoutSec $TimeoutSec -UserAgent $UserAgent
        $txt = ([string]$response.Text).Trim()
        if (Get-Command -Name Resolve-ChecksumFromChecksumText -ErrorAction SilentlyContinue) {
            $resolution = Resolve-ChecksumFromChecksumText -Content $txt -TargetFileName $TargetFileName -Algorithm $Algorithm
            $hash = [string]$resolution.Hash
            return [PSCustomObject]@{
                Sha256       = if ($Algorithm -eq "SHA256") { $hash } else { $null }
                Sha512       = if ($Algorithm -eq "SHA512") { $hash } else { $null }
                Method       = [string]$response.Method
                StatusCode   = Get-HttpStatusCodeDisplayText -Value $response.StatusCode
                ReasonPhrase = Get-NormalizedDisplayText -Value $response.ReasonPhrase
                FinalUri     = Get-NormalizedDisplayText -Value $response.FinalUri
                ResolverReason = [string]$resolution.Reason
                ResolverFormat = [string]$resolution.SourceFormat
                ResolverCandidates = [int]$resolution.Candidates
            }
        }

        return [PSCustomObject]@{
            Sha256       = $null
            Sha512       = $null
            Method       = [string]$response.Method
            StatusCode   = Get-HttpStatusCodeDisplayText -Value $response.StatusCode
            ReasonPhrase = Get-NormalizedDisplayText -Value $response.ReasonPhrase
            FinalUri     = Get-NormalizedDisplayText -Value $response.FinalUri
            ResolverReason = "NotFound"
            ResolverFormat = ""
            ResolverCandidates = 0
        }
    }
    finally {
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

    Write-ProgressLog ("Resolve-UsbRoot start: Drive='{0}' Root='{1}'" -f $Drive, $Root)
    if ($Drive -and $Root) {
        throw "Use either -DriveLetter or -UsbRoot, not both."
    }

    if ($Root) {
        $candidate = $Root.Trim()
        Write-ProgressLog ("Resolve-UsbRoot root candidate trimmed: {0}" -f $candidate)
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Path not found: $candidate"
        }
        Write-ProgressLog "Resolve-UsbRoot root candidate exists"
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

    Write-ProgressLog ("Find-ReleaseBundleRoot start: {0}" -f $Path)
    $current = [IO.Path]::GetFullPath((Resolve-Path -LiteralPath $Path).Path)

    while (-not [string]::IsNullOrWhiteSpace($current)) {
        Write-ProgressLog ("Find-ReleaseBundleRoot checking: {0}" -f $current)
        if (Test-IsReleaseBundleRoot -Path $current) {
            Write-ProgressLog ("Find-ReleaseBundleRoot found: {0}" -f $current)
            return $current
        }

        $parentInfo = [IO.Directory]::GetParent($current)
        if ($null -eq $parentInfo) {
            break
        }

        $parent = [IO.Path]::GetFullPath($parentInfo.FullName)
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    Write-ProgressLog ("Find-ReleaseBundleRoot none: {0}" -f $Path)
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

    Write-ProgressLog ("Resolve-SelectedUsbRoot start: Source='{0}' Path='{1}'" -f $Source, $Path)
    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')
    Write-ProgressLog ("Resolve-SelectedUsbRoot resolved path: {0}" -f $resolvedPath)
    $bundleRoot = Find-ReleaseBundleRoot -Path $resolvedPath
    if (-not $bundleRoot) {
        Write-ProgressLog "Resolve-SelectedUsbRoot checking script root for release bundle"
        $bundleRoot = Find-ReleaseBundleRoot -Path $PSScriptRoot
    }
    Write-ProgressLog ("Resolve-SelectedUsbRoot bundle root: {0}" -f $(if ($bundleRoot) { $bundleRoot } else { "<none>" }))

    if ($bundleRoot -and (Test-PathWithinRoot -Path $resolvedPath -Root $bundleRoot)) {
        if (Test-IsReleaseBundleScratchPath -Path $resolvedPath -BundleRoot $bundleRoot) {
            return $resolvedPath
        }

        $driveRoot = Get-PathDriveRoot -Path $resolvedPath
        if ($driveRoot -and $resolvedPath -ne $driveRoot) {
            Write-Host ("{0} '{1}' is inside the release bundle. Using USB root '{2}' instead." -f $Source, $resolvedPath, $driveRoot) -ForegroundColor Yellow
            Assert-UsbRootIsSafe -Root $driveRoot
            return $driveRoot
        }
    }

    Assert-UsbRootIsSafe -Root $resolvedPath
    return $resolvedPath
}

function Assert-UsbRootIsSafe {
    param([Parameter(Mandatory)][string]$Root)

    Write-ProgressLog ("Assert-UsbRootIsSafe start: {0}" -f $Root)
    $driveRoot = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Root))
    if ([string]::IsNullOrWhiteSpace($driveRoot)) {
        throw "Could not resolve a drive root for selected USB target '$Root'."
    }

    if ($driveRoot.TrimEnd('\') -ieq "C:") {
        Write-ProgressLog ("Assert-UsbRootIsSafe rejecting protected drive: {0}" -f $driveRoot)
        throw "C:\ is the protected Windows system drive and can never be used by ForgerEMS."
    }
    Write-ProgressLog ("Assert-UsbRootIsSafe accepted: {0}" -f $Root)
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

function Get-DefaultUsbBuilderCategoryIds {
    return @(
        "core",
        "windows",
        "legacy-windows",
        "linux-rescue",
        "diagnostics",
        "oem-tools"
    )
}

function Get-NormalizedUsbBuilderCategoryIds {
    param([string[]]$CategoryIds)

    $tokens = @()
    foreach ($raw in @($CategoryIds)) {
        foreach ($part in (([string]$raw) -split ",")) {
            $trimmed = $part.Trim().ToLowerInvariant()
            if (-not [string]::IsNullOrWhiteSpace($trimmed)) {
                $tokens += $trimmed
            }
        }
    }

    if ($tokens.Count -eq 0) {
        $tokens = @(Get-DefaultUsbBuilderCategoryIds)
    }

    if ("core" -notin $tokens) {
        $tokens += "core"
    }

    return @($tokens | Select-Object -Unique)
}

function New-UsbBuilderCategorySet {
    param([string[]]$CategoryIds)

    $set = @{}
    foreach ($id in (Get-NormalizedUsbBuilderCategoryIds -CategoryIds $CategoryIds)) {
        $set[$id] = $true
    }

    return $set
}

function Get-ManifestStringProperty {
    param(
        [AllowNull()]$Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) { return "" }
    if ($Object.PSObject.Properties.Name -notcontains $Name) { return "" }
    return ([string]$Object.$Name).Trim()
}

function Get-UsbBuilderCategoryIdForPath {
    param(
        [string]$RelativePath,
        [string]$Name = "",
        [string]$Family = ""
    )

    $path = (([string]$RelativePath).Trim() -replace '/', '\').ToLowerInvariant()
    $nameText = ([string]$Name).Trim().ToLowerInvariant()
    $familyText = ([string]$Family).Trim().ToLowerInvariant()

    if ($path.Contains("ventoy") -or $nameText.Contains("ventoy")) { return "core" }
    if ($path.StartsWith("iso\macos\")) { return "macos" }
    if ($path.StartsWith("iso\android\") -or $path.StartsWith("tools\android\")) { return "android" }
    if ($path.StartsWith("iso\ios-ipados\") -or $path.StartsWith("tools\apple-mobile\")) { return "ios-ipados" }
    if ($path.StartsWith("drivers\")) { return "oem-tools" }
    if ($path.StartsWith("iso\windows-legacy\")) { return "legacy-windows" }
    if ($path.StartsWith("iso\windows\windows-manual-iso-drop\windows 8.1") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows 8") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows 7") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows vista") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows xp") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows 2000") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows me") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows 98") -or
        $path.StartsWith("iso\windows\windows-manual-iso-drop\windows 95")) {
        return "legacy-windows"
    }
    if ($path.StartsWith("iso\windows\") -or $familyText -eq "windows") { return "windows" }
    if ($path.StartsWith("iso\linux\") -or $familyText -eq "linux") { return "linux-rescue" }
    if ($path.StartsWith("iso\tools\") -or $path.StartsWith("tools\portable\") -or $path.StartsWith("medicat.usb\")) { return "diagnostics" }

    return "diagnostics"
}

function Get-UsbBuilderCategoryIdForManifestEntry {
    param(
        [AllowNull()]$Entry,
        [string]$RelativePath = ""
    )

    $explicit = Get-ManifestStringProperty -Object $Entry -Name "categoryId"
    if (-not [string]::IsNullOrWhiteSpace($explicit)) {
        return $explicit.Trim().ToLowerInvariant()
    }

    $path = if ([string]::IsNullOrWhiteSpace($RelativePath)) { Get-ManifestStringProperty -Object $Entry -Name "dest" } else { $RelativePath }
    return Get-UsbBuilderCategoryIdForPath `
        -RelativePath $path `
        -Name (Get-ManifestStringProperty -Object $Entry -Name "name") `
        -Family (Get-ManifestStringProperty -Object $Entry -Name "family")
}

function Test-UsbBuilderCategoryIncluded {
    param(
        [Parameter(Mandatory)]$CategorySet,
        [AllowNull()]$Entry,
        [string]$RelativePath = ""
    )

    $categoryId = Get-UsbBuilderCategoryIdForManifestEntry -Entry $Entry -RelativePath $RelativePath
    return $CategorySet.ContainsKey($categoryId)
}

function Resolve-ManifestPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManifestSpecifier
    )

    $candidates = @()

    if ([IO.Path]::IsPathRooted($ManifestSpecifier)) {
        # Absolute path: honor user-supplied location exactly.
        $candidates += [IO.Path]::GetFullPath($ManifestSpecifier)
    }
    else {
        # Why: a Setup-USB pass from an older release seeds ForgerEMS.updates.json
        # at the USB root. If Update-USB resolved that USB-side copy first, a freshly
        # packaged 30-managed-download catalog would be shadowed by a stale 16-item
        # one already on the USB. The packaged bundled catalog must win so Update
        # actually upgrades the USB. USB-side stays as a final fallback for
        # offline/portable scenarios where the script is run from a USB-only layout.
        $candidates += [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $ManifestSpecifier))
        $candidates += [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ("manifests\" + $ManifestSpecifier)))
        $candidates += [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) ("manifests\" + $ManifestSpecifier)))
        $candidates += Resolve-RootChildPath -Root $Root -RelativePath $ManifestSpecifier
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

function Assert-ManifestSha512Field {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    if (-not ([string]$Value -match '^[a-fA-F0-9]{128}$')) {
        throw "$FieldName must be a 128-character SHA-512 hex string."
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
        Assert-ManifestSha512Field -Value $item.sha512 -FieldName "$prefix.sha512"
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

        if ($null -ne $item.sha512Url -and -not [string]::IsNullOrWhiteSpace([string]$item.sha512Url)) {
            if ($type -ne "file") {
                throw "$prefix.sha512Url is only valid for file items."
            }
        }

        $hasResilienceMetadata = (
            ($null -ne $item.sourceType -and -not [string]::IsNullOrWhiteSpace([string]$item.sourceType)) -or
            ($null -ne $item.fragilityLevel -and -not [string]::IsNullOrWhiteSpace([string]$item.fragilityLevel)) -or
            ($null -ne $item.maintenanceRank -and -not [string]::IsNullOrWhiteSpace([string]$item.maintenanceRank)) -or
            ($null -ne $item.borderline)
        )

        if ($hasResilienceMetadata -and $type -ne "file") {
            throw "$prefix.sourceType, $prefix.fragilityLevel, $prefix.maintenanceRank, and $prefix.borderline are only valid for file items."
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

function Get-ManifestItemExecutionOrder {
    param([Parameter(Mandatory)]$Item)

    $type = ([string]$(if ($Item.type) { $Item.type } else { "file" })).Trim().ToLowerInvariant()
    if ($type -eq "page") {
        return 900
    }

    $dest = ([string]$Item.dest).Trim()

    switch -Wildcard ($dest) {
        "Tools\Portable\USB\*"      { return 10 }
        "Tools\Portable\Security\*" { return 20 }
        "Tools\Portable\Disk\*"     { return 30 }
        "Tools\Portable\Hardware\*" { return 40 }
        "Tools\Portable\System\*"   { return 50 }
        "Tools\Portable\Remote\*"   { return 60 }
        "Tools\Portable\GPU\*"      { return 70 }
        "Tools\Portable\Network\*"  { return 80 }
        "ISO\Tools\*"               { return 100 }
        "ISO\Windows\*"             { return 110 }
        "ISO\Linux\*"               { return 120 }
        default                     { return 200 }
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
Write-ProgressLog ("UsbRoot safety resolved: {0}" -f $root)
$manifestPath = Resolve-ManifestPath -Root $root -ManifestSpecifier $ManifestName
Write-ProgressLog ("manifest file found: {0}" -f $manifestPath)

$manifestRaw = Invoke-TimedReleasePhase -Name "Manifest load" -ScriptBlock {
    Get-Content -LiteralPath $manifestPath -Raw
}
$manifest = Invoke-TimedReleasePhase -Name "Manifest ConvertFrom-Json" -ScriptBlock {
    $manifestRaw | ConvertFrom-Json
}
Write-ProgressLog "ConvertFrom-Json complete"
Invoke-TimedReleasePhase -Name "Manifest contract validation" -ScriptBlock {
    Assert-ManifestContract -Manifest $manifest -Root $root -SourceName $manifestPath
}

if ($RetryFailedManagedDownloads) {
    $priorResultPath = Join-Path $root "ForgerEMS-managed-download-result.json"
    if (-not (Test-Path -LiteralPath $priorResultPath)) {
        throw "RetryFailedManagedDownloads requires prior result file: $priorResultPath"
    }
    $prior = Get-Content -LiteralPath $priorResultPath -Raw | ConvertFrom-Json
    $script:RetryManagedDestinations = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($fi in @($prior.failedItems)) {
        if ($null -eq $fi) { continue }
        $rp = [string]$(if ($fi.destinationRelativePath) { $fi.destinationRelativePath } else { "" })
        $retryable = $true
        if ($null -ne $fi.retryable) { $retryable = [bool]$fi.retryable }
        if ($retryable -and -not [string]::IsNullOrWhiteSpace($rp)) {
            [void]$script:RetryManagedDestinations.Add($rp.Trim())
        }
    }
    Write-Log ("RetryFailedManagedDownloads: {0} managed path(s) selected for retry." -f $script:RetryManagedDestinations.Count) "INFO"
}

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
try {
    $manifestHash = Get-ForgerSha256 -LiteralPath $manifestPath
    $manifestTotalItems = @($manifest.items).Count
    $manifestFileItems = @($manifest.items | Where-Object { ([string]$(if ($_.type) { $_.type } else { "file" })).Trim().ToLowerInvariant() -eq "file" }).Count
    Write-Log ("Manifest SHA256: {0}" -f $manifestHash) "INFO"
    Write-Log ("Manifest items: total={0} file-type={1}" -f $manifestTotalItems, $manifestFileItems) "INFO"
}
catch {
    Write-Log ("Could not summarize manifest: {0}" -f $_.Exception.Message) "WARN"
}
Write-Log "Force=$Force VerifyOnly=$VerifyOnly NoArchive=$NoArchive" "INFO"

if (-not $manifest.items) {
    throw "Manifest has no items."
}

$builderCategorySet = New-UsbBuilderCategorySet -CategoryIds $IncludedCategories
$builderCategoriesText = (($builderCategorySet.Keys | Sort-Object) -join ", ")
Write-Log "USB Builder profile categories included: $builderCategoriesText" "INFO"
Write-Log "Unchecked categories are skipped for this run only; existing USB files are not deleted." "INFO"

function Invoke-ManifestExtras {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)]$IncludedCategorySet,
        [switch]$Force
    )

    $extras = $Manifest.extras
    if ($null -eq $extras) {
        return
    }

    Write-Log "Manifest extras phase started." "INFO"

    $seedDirs = @()
    if ($extras.PSObject.Properties.Name -contains 'seedDirectories') {
        $seedDirs = @($extras.seedDirectories | Where-Object {
                -not [string]::IsNullOrWhiteSpace([string]$_) -and
                (Test-UsbBuilderCategoryIncluded -CategorySet $IncludedCategorySet -Entry $null -RelativePath ([string]$_))
            })
    }
    foreach ($relDir in $seedDirs) {
        $rel = ([string]$relDir).Trim()
        if ([string]::IsNullOrWhiteSpace($rel)) { continue }
        $fullDir = Resolve-RootChildPath -Root $Root -RelativePath $rel
        if (Test-Path -LiteralPath $fullDir -PathType Container) {
            Write-Log "Extras: directory exists, skipping: $rel" "INFO"
            $script:Summary.ExtrasDirsSkipped++
            continue
        }
        if ($PSCmdlet.ShouldProcess($fullDir, "Create manual-ISO drop directory")) {
            New-Item -ItemType Directory -Path $fullDir -Force | Out-Null
            Write-Log "Extras: created directory: $rel" "OK"
            $script:Summary.ExtrasDirsCreated++
        }
        else {
            Write-Log "Extras: would create directory: $rel" "INFO"
        }
    }

    $readmes = @()
    if ($extras.PSObject.Properties.Name -contains 'readmes') {
        $readmes = @($extras.readmes | Where-Object {
                $null -ne $_ -and
                -not [string]::IsNullOrWhiteSpace([string]$_.dest) -and
                (Test-UsbBuilderCategoryIncluded -CategorySet $IncludedCategorySet -Entry $_ -RelativePath ([string]$_.dest))
            })
    }
    foreach ($readme in $readmes) {
        $dest = ([string]$readme.dest).Trim()
        if ([string]::IsNullOrWhiteSpace($dest)) { continue }
        # Never let extras pretend to be ISOs or executables.
        $extension = [IO.Path]::GetExtension($dest)
        if (-not [string]::Equals($extension, ".txt", [System.StringComparison]::OrdinalIgnoreCase) -and
            -not [string]::Equals($extension, ".md", [System.StringComparison]::OrdinalIgnoreCase)) {
            Write-Log "Extras: refusing to write non-text README dest: $dest" "WARN"
            continue
        }

        $fullPath = Resolve-RootChildPath -Root $Root -RelativePath $dest
        $parent = Split-Path -Parent $fullPath
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            if ($PSCmdlet.ShouldProcess($parent, "Create parent directory for README")) {
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
            }
        }

        $bodyLines = @()
        if ($readme.PSObject.Properties.Name -contains 'body') {
            $rawBody = $readme.body
            if ($rawBody -is [System.Array] -or $rawBody -is [System.Collections.IEnumerable] -and -not ($rawBody -is [string])) {
                $bodyLines = @($rawBody | ForEach-Object { [string]$_ })
            }
            else {
                $bodyLines = @([string]$rawBody)
            }
        }
        $bodyText = ($bodyLines -join [System.Environment]::NewLine)

        if ((Test-Path -LiteralPath $fullPath -PathType Leaf) -and -not $Force) {
            Write-Log "Extras: README exists, skipping (use -Force to overwrite): $dest" "INFO"
            $script:Summary.ExtrasReadmesSkipped++
            continue
        }

        if ($PSCmdlet.ShouldProcess($fullPath, "Write README")) {
            [System.IO.File]::WriteAllText($fullPath, $bodyText, [System.Text.UTF8Encoding]::new($false))
            Write-Log "Extras: wrote README: $dest" "OK"
            $script:Summary.ExtrasReadmesCreated++
        }
        else {
            Write-Log "Extras: would write README: $dest" "INFO"
        }
    }

    Write-Log ("Extras summary: dirs created={0} skipped={1}, readmes created={2} skipped={3}" -f `
        $script:Summary.ExtrasDirsCreated, $script:Summary.ExtrasDirsSkipped, `
        $script:Summary.ExtrasReadmesCreated, $script:Summary.ExtrasReadmesSkipped) "INFO"
}

Invoke-TimedReleasePhase -Name "Manifest extras processing" -ScriptBlock {
    Invoke-ManifestExtras -Manifest $manifest -Root $root -IncludedCategorySet $builderCategorySet -Force:$Force
}

$profileItems = @(Invoke-TimedReleasePhase -Name "USB Builder profile item filtering" -ScriptBlock {
    $manifest.items | Where-Object {
        Test-UsbBuilderCategoryIncluded -CategorySet $builderCategorySet -Entry $_
    }
})
$skippedByProfileCount = @($manifest.items).Count - $profileItems.Count
Write-Log "Manifest items selected by USB Builder profile: $($profileItems.Count)" "INFO"
Write-Log "Manifest items skipped by USB Builder profile: $skippedByProfileCount" "INFO"

$orderedItems = @(Invoke-TimedReleasePhase -Name "Manifest managed item ordering" -ScriptBlock {
    $profileItems | Sort-Object `
        @{ Expression = { Get-ManifestItemExecutionOrder -Item $_ } }, `
        @{ Expression = { ([string]$(if ($_.dest) { $_.dest } else { "" })).Trim() } }, `
        @{ Expression = { ([string]$(if ($_.name) { $_.name } else { "" })).Trim() } }
})

$script:NormalizeManifestMatchTextCallCount = 0
$script:ManagedPlaceholderShadowMatchCallCount = 0
$activeManagedPlaceholderPlan = Invoke-TimedReleasePhase -Name "Get-ActiveManagedPlaceholderPlan" -ScriptBlock {
    Get-ActiveManagedPlaceholderPlan -Items $orderedItems
}
Write-Log ("Placeholder planner counters: Normalize-ManifestMatchText={0}, Test-ManagedPlaceholderShadowMatch={1}" -f `
    $script:NormalizeManifestMatchTextCallCount, $script:ManagedPlaceholderShadowMatchCallCount) "INFO"

$enabledManagedFileItems = @(
    $orderedItems | Where-Object {
        $itemEnabled = $true
        if ($null -ne $_.enabled) {
            $itemEnabled = [bool]$_.enabled
        }

        $itemType = ([string]$(if ($_.type) { $_.type } else { "file" })).Trim().ToLowerInvariant()
        $itemEnabled -and $itemType -eq "file"
    }
)
$enabledPlaceholderItems = @(
    $orderedItems | Where-Object {
        $itemEnabled = $true
        if ($null -ne $_.enabled) {
            $itemEnabled = [bool]$_.enabled
        }

        $itemType = ([string]$(if ($_.type) { $_.type } else { "file" })).Trim().ToLowerInvariant()
        $destKey = Get-ManifestDestinationKey -RelativePath ([string]$(if ($_.dest) { $_.dest } else { "" }))
        $itemEnabled -and $itemType -eq "page" -and -not $activeManagedPlaceholderPlan.ByPlaceholderDest.ContainsKey($destKey)
    }
)

Write-Log "Managed download phase started." "INFO"
Write-Log "Queued managed auto-download items: $($enabledManagedFileItems.Count)" "INFO"
Write-Log "Queued placeholder/info shortcut items: $($enabledPlaceholderItems.Count)" "INFO"
Write-Log "Suppressed placeholder/info shortcuts for active managed downloads: $($activeManagedPlaceholderPlan.ByPlaceholderDest.Count)" "INFO"
Write-Log "Execution order: portable tools first, larger ISO items later, shortcuts last." "INFO"

foreach ($queuedItem in $enabledManagedFileItems) {
    $queuedName = ([string]$(if ($queuedItem.name) { $queuedItem.name } else { "<unnamed managed item>" })).Trim()
    $queuedDest = ([string]$(if ($queuedItem.dest) { $queuedItem.dest } else { "<no-destination>" })).Trim()
    Write-Log "Queued managed item: $queuedName -> $queuedDest" "INFO"
}

foreach ($queuedPlaceholder in $enabledPlaceholderItems) {
    $queuedName = ([string]$(if ($queuedPlaceholder.name) { $queuedPlaceholder.name } else { "<unnamed placeholder item>" })).Trim()
    $queuedDest = ([string]$(if ($queuedPlaceholder.dest) { $queuedPlaceholder.dest } else { "<no-destination>" })).Trim()
    Write-Log "Queued placeholder item: $queuedName -> $queuedDest" "INFO"
}

$managedItemLoopStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
Write-ProgressLog ("Managed item loop start count={0}" -f @($orderedItems).Count)
$managedItemLoopIndex = 0
foreach ($item in $orderedItems) {
    $managedItemLoopIndex++
    $script:Summary.Total++

    $name = ([string]$item.name).Trim()
    $type = ([string]$(if ($item.type) { $item.type } else { "file" })).Trim().ToLowerInvariant()
    $url  = ([string]$item.url).Trim()
    $destRel = ([string]$item.dest).Trim()
    Write-ProgressLog ("Managed item loop item {0}/{1}: type={2} name='{3}' dest='{4}'" -f `
        $managedItemLoopIndex, @($orderedItems).Count, $type, $name, $destRel)
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

    if ($script:RetryManagedDestinations) {
        if ($type -eq "page") {
            continue
        }
        if ($type -eq "file" -and -not $script:RetryManagedDestinations.Contains($destRel)) {
            continue
        }
    }

    $dest = Resolve-RootChildPath -Root $root -RelativePath $destRel
    $destDir = Split-Path -Parent $dest
    Ensure-Dir -Path $destDir

    $itemTimeout = if ($item.timeoutSec) { [int]$item.timeoutSec } else { $timeout }
    $archiveItem = $true
    if ($null -ne $item.archive) { $archiveItem = [bool]$item.archive }

    Write-Log "---- $name ----" "INFO"
    Write-Log "Manifest item selected: $name" "INFO"
    $destKey = Get-ManifestDestinationKey -RelativePath $destRel
    if ($type -eq "page" -and $activeManagedPlaceholderPlan.ByPlaceholderDest.ContainsKey($destKey)) {
        Write-Log "Type: $type" "INFO"
        Write-Log "Dest: $destRel" "INFO"
        Write-Log "Resolved destination path: $dest" "INFO"
        Write-Log "Manifest source URL: $url" "INFO"
        Write-Log "Skipped placeholder creation because item is active managed download: $destRel" "INFO"
        $script:Summary.Skipped++
        continue
    }
    if ($type -eq "page") {
        $script:Summary.PlaceholderItems++
        Write-Log "Item role: seeded placeholder / info shortcut" "INFO"
    }
    else {
        $script:Summary.ManagedFileItems++
        Write-Log "Item role: managed auto-download item" "INFO"
    }
    Write-Log "Type: $type" "INFO"
    Write-Log "Dest: $destRel" "INFO"
    Write-Log "Resolved destination path: $dest" "INFO"
    Write-Log "Manifest source URL: $url" "INFO"

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
            $script:Summary.PlaceholderOnly++
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

    $preferredFallbackShortcutPath = Get-PreferredFallbackShortcutPath -Root $root -ManagedDestination $destRel -ManagedPlaceholderPlan $activeManagedPlaceholderPlan
    $sha = ([string]$item.sha256).Trim().ToLowerInvariant()
    $shaUrl = ([string]$item.sha256Url).Trim()
    $sha512 = ([string]$item.sha512).Trim().ToLowerInvariant()
    $sha512Url = ([string]$item.sha512Url).Trim()
    $checksumAlgorithm = if ($sha) { "SHA256" } elseif ($sha512) { "SHA512" } elseif ($shaUrl) { "SHA256" } elseif ($sha512Url) { "SHA512" } else { "" }
    $expectedChecksum = if ($checksumAlgorithm -eq "SHA512") { $sha512 } else { $sha }
    $checksumUrl = if ($checksumAlgorithm -eq "SHA512") { $sha512Url } else { $shaUrl }
    $shaResult = $null

    $targetFileName = ""
    try { $targetFileName = [IO.Path]::GetFileName($destRel) } catch { $targetFileName = "" }

    if (-not $expectedChecksum -and $checksumUrl -and ($VerifyOnly -or -not $WhatIfPreference)) {
        Write-Log "$checksumAlgorithm checksum source URL: $checksumUrl" "INFO"
        try {
            $shaResult = Get-ShaFromUrl -ShaUrl $checksumUrl -Algorithm $checksumAlgorithm -TargetFileName $targetFileName -TimeoutSec $itemTimeout -UserAgent $userAgent
            $expectedChecksum = if ($checksumAlgorithm -eq "SHA512") {
                ([string]$shaResult.Sha512).Trim().ToLowerInvariant()
            }
            else {
                ([string]$shaResult.Sha256).Trim().ToLowerInvariant()
            }
            if ($expectedChecksum) {
                Write-Log "Checksum source resolved via $($shaResult.Method)." "OK"
                Write-Log "Checksum resolver: algorithm=$checksumAlgorithm reason=$($shaResult.ResolverReason) format=$($shaResult.ResolverFormat) candidates=$($shaResult.ResolverCandidates)" "INFO"
                if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.StatusCode)) {
                    Write-Log (("Checksum source HTTP status: $($shaResult.StatusCode) $($shaResult.ReasonPhrase)").TrimEnd()) "INFO"
                }
                if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.FinalUri)) {
                    Write-Log "Checksum source final URL: $($shaResult.FinalUri)" "INFO"
                    Write-Log "Resolved checksum source URL: $($shaResult.FinalUri)" "INFO"
                }
                Write-Log "Fetched $checksumAlgorithm from checksum URL: $expectedChecksum" "OK"
            }
            else {
                Write-Log "$checksumAlgorithm checksum URL was provided but no valid hash was parsed." "WARN"
                if ($shaResult) {
                    Write-Log "Checksum source method result: $($shaResult.Method)" "INFO"
                    Write-Log "Checksum resolver: algorithm=$checksumAlgorithm reason=$($shaResult.ResolverReason) format=$($shaResult.ResolverFormat) candidates=$($shaResult.ResolverCandidates)" "INFO"
                    if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.StatusCode)) {
                        Write-Log (("Checksum source HTTP status: $($shaResult.StatusCode) $($shaResult.ReasonPhrase)").TrimEnd()) "INFO"
                    }
                    if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.FinalUri)) {
                        Write-Log "Checksum source final URL: $($shaResult.FinalUri)" "INFO"
                        Write-Log "Resolved checksum source URL: $($shaResult.FinalUri)" "INFO"
                    }
                }
            }
        }
        catch {
            Write-Log "Failed fetching checksum URL: $(Get-ExceptionDiagnostic -ErrorRecord $_)" "WARN"
        }
    }
    elseif (-not $expectedChecksum -and $checksumUrl -and $WhatIfPreference) {
        Write-Log "$checksumAlgorithm checksum source URL: $checksumUrl" "INFO"
        Write-Log "WhatIf: would fetch $checksumAlgorithm from checksum URL during a real run." "INFO"
    }
    elseif ($expectedChecksum) {
        Write-Log "Pinned $checksumAlgorithm from manifest: $expectedChecksum" "INFO"
        if ($checksumUrl) {
            Write-Log "Checksum source URL available for maintenance: $checksumUrl" "INFO"
        }
        else {
            Write-Log "Checksum source URL: not provided (using pinned manifest $checksumAlgorithm only)." "INFO"
        }
    }

    if ($VerifyOnly) {
        if (-not (Test-Path -LiteralPath $dest)) {
            Write-Log "Verify failed: destination missing: $destRel" "ERROR"
            $script:Summary.Failed++
            continue
        }

        if ($expectedChecksum) {
            $cur = if ($checksumAlgorithm -eq "SHA512") { Get-Sha512 -Path $dest } else { Get-Sha256 -Path $dest }
            Write-Log "Checksum expected vs actual: algorithm=$checksumAlgorithm expected=$expectedChecksum actual=$cur" "INFO"
            if ($cur -eq $expectedChecksum) {
                Write-Log "Verified OK ($checksumAlgorithm match)." "OK"
                Write-Log "Checksum verified: $cur" "OK"
                Write-Log "Destination state after verify: $(Get-FileStateDescription -Path $dest)" "INFO"
                $script:Summary.Verified++
                $script:Summary.UpToDateSkipped++
            }
            else {
                Write-Log "Verify failed: $checksumAlgorithm mismatch. Expected=$expectedChecksum Got=$cur" "ERROR"
                $script:Summary.Failed++
            }
        }
        else {
            Write-Log "No supported checksum provided; cannot verify '$name'." "WARN"
            $script:Summary.Skipped++
        }

        continue
    }

    if ($WhatIfPreference) {
        if (-not $Force -and (Test-Path -LiteralPath $dest)) {
            if ($expectedChecksum) {
                Write-Log "WhatIf: destination exists; would calculate $checksumAlgorithm and skip if it already matches." "INFO"
            }
            else {
                Write-Log "Destination exists and no supported checksum is provided. Would skip to avoid blind overwrite." "WARN"
                $script:Summary.Skipped++
                continue
            }
        }
    }
    elseif (-not $Force -and $expectedChecksum -and (Test-Path -LiteralPath $dest)) {
        $cur = if ($checksumAlgorithm -eq "SHA512") { Get-Sha512 -Path $dest } else { Get-Sha256 -Path $dest }
        if ($cur -eq $expectedChecksum) {
            Write-Log "Up-to-date ($checksumAlgorithm match). Skipping." "OK"
            [void](Remove-ManagedSuccessPlaceholders -Root $root -ManagedDestination $destRel -ManagedPlaceholderPlan $activeManagedPlaceholderPlan)
            $script:Summary.Verified++
            $script:Summary.Skipped++
            $script:Summary.UpToDateSkipped++
            continue
        }
    }
    elseif (-not $Force -and -not $expectedChecksum -and (Test-Path -LiteralPath $dest)) {
        Write-Log "Destination exists and no supported checksum is provided. Skipping to avoid blind overwrite." "WARN"
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
    $downloadResult = $null

    try {
        Write-Log "Download start: $name" "INFO"
        $downloadResult = Download-File -Url $url -OutFile $tmpPath -TimeoutSec $itemTimeout -UserAgent $userAgent -Retries $retries -ItemName $name
        if ($downloadResult) {
            if (-not [string]::IsNullOrWhiteSpace([string]$downloadResult.AttemptSummary)) {
                Write-Log "Downloader methods attempted: $($downloadResult.AttemptSummary)" "INFO"
            }
            Write-Log "Downloader used: $($downloadResult.Method)" "INFO"
            if (-not [string]::IsNullOrWhiteSpace([string]$downloadResult.StatusCode)) {
                Write-Log (("Download HTTP status: $($downloadResult.StatusCode) $($downloadResult.ReasonPhrase)").TrimEnd()) "INFO"
            }
            if (-not [string]::IsNullOrWhiteSpace([string]$downloadResult.FinalUri)) {
                Write-Log "Resolved source URL: $($downloadResult.FinalUri)" "INFO"
            }
            Write-Log "Staged file existence and size: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
        }
    }
    catch {
        Write-Log "Item failed: $name" "ERROR"
        Write-Log "Download failed for '$name': $(Get-ExceptionDiagnostic -ErrorRecord $_)" "ERROR"
        if ($_.Exception.Data.Contains("AttemptedMethodSummary")) {
            Write-Log "Downloader methods attempted: $($_.Exception.Data['AttemptedMethodSummary'])" "WARN"
        }
        Write-Log "Staged file existence and size: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
        $fallbackResult = Write-DownloadFallbackShortcut -DestinationPath $dest -ItemName $name -Url $url -PreferredShortcutPath $preferredFallbackShortcutPath
        $script:Summary.FailedWithFallback++
        switch ($fallbackResult.Outcome) {
            "created"  { $script:Summary.FallbackShortcutsCreated++ }
            "existing" { $script:Summary.FallbackShortcutsReused++ }
        }
        if ($fallbackResult.Outcome -eq "created" -or $fallbackResult.Outcome -eq "existing") {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome) -> $($fallbackResult.ShortcutPath)" "WARN"
        }
        else {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome)" "WARN"
        }
        Write-Log "Item staging verdict: FAILED WITH FALLBACK" "ERROR"
        $script:Summary.Failed++
        $fbPath = if ($fallbackResult.ShortcutPath) { $fallbackResult.ShortcutPath } else { "(none)" }
        [void]$script:ManagedFailureLines.Add("Item: $name | Status: failed with fallback shortcut | Destination: $destRel | Fallback: $fbPath")
        Add-ManagedDownloadFailedRecord -Name $name -DestRel $destRel -Url $url -FailureKind "DownloadException" -SafeReason "Download failed before staging completed." -HttpStatus "" -FallbackRelPath $destRel
        continue
    }

    try {
        $verifiedHash = $null
        if ($expectedChecksum) {
            $verifiedHash = if ($checksumAlgorithm -eq "SHA512") { Get-Sha512 -Path $tmpPath } else { Get-Sha256 -Path $tmpPath }
            Write-Log "Checksum expected vs actual: algorithm=$checksumAlgorithm expected=$expectedChecksum actual=$verifiedHash" "INFO"
            if ($verifiedHash -ne $expectedChecksum) {
                throw "$checksumAlgorithm mismatch. Expected=$expectedChecksum Got=$verifiedHash"
            }
            Write-Log "Checksum verification passed: $name" "OK"
            Write-Log "Checksum verified: $verifiedHash" "OK"
            $script:Summary.Verified++
        }
        else {
            Write-Log "Checksum verification skipped: no supported checksum set for '$name' (recommended for important ISOs/tools)." "WARN"
        }

        if (-not $NoArchive -and $archiveItem -and (Test-Path -LiteralPath $dest)) {
            $didArchive = Archive-OldFile -ItemName $name -FilePath $dest -ArchiveDir $arcDir -MaxKeep $maxKeep
            if ($didArchive) {
                Write-Log "Archived old file." "OK"
                $script:Summary.Archived++
            }
        }

        Move-Item -LiteralPath $tmpPath -Destination $dest -Force

        Write-Log "Final file written: $dest" "OK"
        Write-Log "Final destination write result: success -> $(Get-FileStateDescription -Path $dest)" "INFO"
        if ($expectedChecksum) {
            Write-Log "Verified payload ready at destination with expected ${checksumAlgorithm}: $expectedChecksum" "OK"
        }
        [void](Remove-ManagedSuccessPlaceholders -Root $root -ManagedDestination $destRel -ManagedPlaceholderPlan $activeManagedPlaceholderPlan)
        Write-Log "Updated: $name" "OK"
        Write-Log "Item staging verdict: STAGED" "OK"
        $script:Summary.Downloaded++
        $script:Summary.Updated++
    }
    catch {
        Write-Log "Item failed: $name" "ERROR"
        Write-Log "Update failed for '$name': $(Get-ExceptionDiagnostic -ErrorRecord $_)" "ERROR"
        Write-Log "Staged file existence and size: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
        Write-Log "Final destination write result: failed -> $dest" "ERROR"
        $fallbackResult = Write-DownloadFallbackShortcut -DestinationPath $dest -ItemName $name -Url $url -PreferredShortcutPath $preferredFallbackShortcutPath
        $script:Summary.FailedWithFallback++
        switch ($fallbackResult.Outcome) {
            "created"  { $script:Summary.FallbackShortcutsCreated++ }
            "existing" { $script:Summary.FallbackShortcutsReused++ }
        }
        if ($fallbackResult.Outcome -eq "created" -or $fallbackResult.Outcome -eq "existing") {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome) -> $($fallbackResult.ShortcutPath)" "WARN"
        }
        else {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome)" "WARN"
        }
        Write-Log "Item staging verdict: FAILED WITH FALLBACK" "ERROR"
        $script:Summary.Failed++
        $fbPath = if ($fallbackResult.ShortcutPath) { $fallbackResult.ShortcutPath } else { "(none)" }
        [void]$script:ManagedFailureLines.Add("Item: $name | Status: failed with fallback shortcut | Destination: $destRel | Fallback: $fbPath")
        Add-ManagedDownloadFailedRecord -Name $name -DestRel $destRel -Url $url -FailureKind "StagingException" -SafeReason "Downloaded file could not be verified or moved into place." -HttpStatus "" -FallbackRelPath $destRel
        if (Test-Path -LiteralPath $tmpPath) {
            Remove-Item -LiteralPath $tmpPath -Force -ErrorAction SilentlyContinue
        }
        Write-Log "Staged file existence and size after cleanup: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
    }
}
$managedItemLoopStopwatch.Stop()
Write-ProgressLog ("Managed item loop end elapsedMs={0:n0}" -f $managedItemLoopStopwatch.Elapsed.TotalMilliseconds)
Write-Log ("Timing: Managed item loop completed in {0:n0} ms" -f $managedItemLoopStopwatch.Elapsed.TotalMilliseconds) "INFO"

$skippedOrPlaceholderOnly = $script:Summary.Skipped + $script:Summary.PlaceholderOnly
$finalFailureMessage = $null

Write-Log "---------------- MANAGED-DOWNLOAD SUMMARY ----------------" "INFO"
Write-Log "Total manifest items: $($script:Summary.Total)" "INFO"
Write-Log "Managed downloads selected (auto): $($script:Summary.ManagedFileItems)" "INFO"
Write-Log "Managed downloads completed (written/updated): $($script:Summary.Downloaded)" "INFO"
Write-Log "Managed downloads failed with fallback shortcut: $($script:Summary.FailedWithFallback)" "INFO"
Write-Log "Manual/info shortcut items (expected, not failed downloads): $($script:Summary.PlaceholderItems)" "INFO"
Write-Log "Verified successfully: $($script:Summary.Verified)" "INFO"
Write-Log "Placeholder-only / skipped manifest lines: $skippedOrPlaceholderOnly" "INFO"
Write-Log "Fallback shortcuts created: $($script:Summary.FallbackShortcutsCreated)" "INFO"
Write-Log "Fallback shortcuts reused: $($script:Summary.FallbackShortcutsReused)" "INFO"
Write-Log "Archived prior files: $($script:Summary.Archived)" "INFO"
Write-Log "Disabled manifest items: $($script:Summary.Disabled)" "INFO"
Write-Log "Total failed managed items: $($script:Summary.Failed)" "INFO"

Write-Log "--- ACTION SUMMARY ---" "OK"
Write-Log ("Items downloaded: $($script:Summary.Downloaded)") "OK"
Write-Log ("Items already up to date: $($script:Summary.UpToDateSkipped)") "OK"
Write-Log ("Shortcuts updated: $($script:Summary.Shortcut)") "OK"
Write-Log ("Failures: $($script:Summary.Failed)") $(if ($script:Summary.Failed -gt 0) { "WARN" } else { "OK" })
Write-Log ("Warnings: $($script:Summary.WarnEvents)") $(if ($script:Summary.WarnEvents -gt 0) { "WARN" } else { "OK" })
$actionUsbReadiness = if ($script:Summary.Failed -eq 0) {
    "READY"
}
elseif ($StrictManagedDownloads) {
    "FAILED"
}
else {
    "PARTIALLY STAGED"
}
$actionReadinessLevel = if ($actionUsbReadiness -eq "READY") {
    "OK"
}
elseif ($actionUsbReadiness -eq "FAILED") {
    "ERROR"
}
else {
    "WARN"
}
Write-Log ("USB readiness: $actionUsbReadiness") $actionReadinessLevel

if ($script:Summary.Failed -gt 0) {
    Write-Log "USB readiness: PARTIALLY STAGED - USB layout is present; one or more managed downloads need attention. Manual/info shortcuts above are normal and are not failed downloads." "WARN"
    Write-Log "------ FAILED MANAGED ITEMS (DETAIL) ------" "WARN"
    if ($script:ManagedFailureLines.Count -gt 0) {
        foreach ($line in $script:ManagedFailureLines) {
            Write-Log $line "WARN"
        }
    }
    else {
        Write-Log "(No per-item detail captured; search this log for 'Item failed:')" "WARN"
    }
    $namesForMessage = if ($script:ManagedFailureLines.Count -gt 0) {
        ($script:ManagedFailureLines | ForEach-Object { ($_ -split '\|')[0].Replace('Item: ', '').Trim() }) -join ", "
    }
    else {
        "see log for item names"
    }
    $partialMsg = "Managed download pass completed with $($script:Summary.Failed) failed item(s): $namesForMessage. USB readiness: PARTIALLY_STAGED (toolkit usable; review fallback shortcuts or retry failed downloads)."
    if ($StrictManagedDownloads) {
        Write-Log "Strict managed-download mode: treating partial staging as failure." "ERROR"
        $finalFailureMessage = $partialMsg
    }
    else {
        Write-Log $partialMsg "WARN"
        Write-Log "ForgerEMS USB Builder finished with warnings. Beta support: ForgerDigitalSolutions@outlook.com" "WARN"
    }
}
else {
    Write-Log "USB readiness: READY. Managed auto-download items completed without failures." "OK"
    Write-Log "ForgerEMS USB Builder finished successfully. Your USB is READY." "OK"
}

if ($script:LogFile -and $WhatIfPreference) {
    Write-Log "Log file write skipped because -WhatIf is active: $script:LogFile" "INFO"
}
elseif ($script:LogFile -and (Test-Path -LiteralPath (Split-Path -Parent $script:LogFile))) {
    Write-Log "Log saved: $script:LogFile" "OK"
}
else {
    Write-Log "Log file was not created because the log directory is unavailable." "INFO"
}

$managedResultReadiness = "READY"
if ($script:Summary.Failed -gt 0) {
    $managedResultReadiness = "PARTIALLY_STAGED"
}
if ($finalFailureMessage) {
    $managedResultReadiness = "FAILED"
}
Write-ProgressLog ("summary written: readiness={0} failed={1} warnings={2}" -f `
    $managedResultReadiness, $script:Summary.Failed, $script:Summary.WarnEvents)
Write-ForgerEmsManagedDownloadResultJson -RootPath $root -Readiness $managedResultReadiness

if ($finalFailureMessage) {
    throw $finalFailureMessage
}
