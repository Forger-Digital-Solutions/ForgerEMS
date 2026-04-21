<#
.SYNOPSIS
Runs regression checks for the Ventoy core shipping baseline.

.DESCRIPTION
Creates disposable scratch roots under .verify, invokes the public setup/update
entrypoints in separate PowerShell processes, and verifies the core safety and
compatibility guarantees: wrapper entrypoints, bundled-manifest fallback, path
escape rejection, and true dry-run behavior. With -Online, it also performs
HEAD-only upstream checks for managed URLs and vendor inventory sources.

.PARAMETER VerifyRoot
Base scratch directory for verification artifacts. A timestamped run folder is
created underneath this path. Defaults to <repo>\.verify in repo mode and to a
local .verify folder in release-bundle mode.

.PARAMETER Online
Enable HEAD-only upstream checks for manifest URLs, checksum URLs, and known
vendor inventory source URLs.

.PARAMETER RevalidateManagedDownloads
Run the lightweight managed-download revalidation workflow. This mode checks
enabled manifest-managed file items for URL reachability and checksum-source
reachability, writes a drift report, and does not modify the manifest.

.PARAMETER ShowVersion
Display the Ventoy core version/build metadata from the bundled manifest and
exit without running verification.

.PARAMETER EnforceManagedChecksums
Require checksum coverage for managed file items even during `dev`
verification. Candidate and stable releases already enforce this by default.

.EXAMPLE
.\Verify-VentoyCore.ps1

.EXAMPLE
.\Verify-VentoyCore.ps1 -Online

.EXAMPLE
.\Verify-VentoyCore.ps1 -RevalidateManagedDownloads

.EXAMPLE
.\Verify-VentoyCore.ps1 -ShowVersion

.EXAMPLE
.\Verify-VentoyCore.ps1 -EnforceManagedChecksums

.NOTES
Public PowerShell entrypoint. Keeps artifacts by default for inspection.
#>

#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$VerifyRoot = "",
    [switch]$Online,
    [switch]$RevalidateManagedDownloads,
    [switch]$ShowVersion,
    [switch]$EnforceManagedChecksums
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$powerShellExe = Join-Path $PSHOME "powershell.exe"
if (-not (Test-Path -LiteralPath $powerShellExe)) {
    $powerShellExe = "powershell.exe"
}

$results = New-Object System.Collections.Generic.List[object]
$script:WarningCount = 0
$script:ManagedRevalidationCsvPath = $null
$script:ManagedRevalidationTextPath = $null
$script:ManagedRevalidationSummaryPath = $null
$script:ManagedRevalidationArchiveRoot = $null
$script:ManagedRevalidationLatestRoot = $null
$script:ManagedRevalidationSummary = $null

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

function Add-Warning {
    param([Parameter(Mandatory)][string]$Message)

    $script:WarningCount++
    Write-Status $Message "WARN"
}

function Ensure-Dir {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Assert-Condition {
    param(
        [Parameter(Mandatory)][bool]$Condition,
        [Parameter(Mandatory)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-DirectoryChildCount {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return 0
    }

    return @(
        Get-ChildItem -LiteralPath $Path -Force -ErrorAction Stop
    ).Count
}

function Get-VentoyCoreRuntimeRoot {
    $parent = Split-Path -Parent $PSScriptRoot

    if (Test-Path -LiteralPath (Join-Path $parent "manifests\ForgerEMS.updates.json")) {
        return $parent
    }

    return $PSScriptRoot
}

function Resolve-BundledManifestPath {
    $runtimeRoot = Get-VentoyCoreRuntimeRoot
    $candidates = @(
        (Join-Path $PSScriptRoot "ForgerEMS.updates.json"),
        (Join-Path $PSScriptRoot "manifests\ForgerEMS.updates.json"),
        (Join-Path $runtimeRoot "manifests\ForgerEMS.updates.json")
    )

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Bundled manifest not found. Checked: $($candidates -join '; ')"
}

function Resolve-VendorInventoryPath {
    $runtimeRoot = Get-VentoyCoreRuntimeRoot
    $candidates = @(
        (Join-Path $PSScriptRoot "manifests\vendor.inventory.json"),
        (Join-Path $runtimeRoot "manifests\vendor.inventory.json")
    )

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    return $null
}

function Resolve-ChecksumsPath {
    $candidate = Join-Path $PSScriptRoot "CHECKSUMS.sha256"
    if (Test-Path -LiteralPath $candidate) {
        return $candidate
    }

    return $null
}

function Resolve-SignaturePath {
    $candidate = Join-Path $PSScriptRoot "SIGNATURE.txt"
    if (Test-Path -LiteralPath $candidate) {
        return $candidate
    }

    return $null
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

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

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

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

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

function Assert-ManagedDownloadSourceTypeValue {
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

function Assert-ManagedDownloadFragilityLevelValue {
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

function Get-EnabledManagedFileItems {
    param([Parameter(Mandatory)]$Manifest)

    $managedItems = New-Object System.Collections.Generic.List[object]

    foreach ($item in @($Manifest.items)) {
        if ($null -eq $item) { continue }

        $itemType = if ($item.type) { ([string]$item.type).Trim().ToLowerInvariant() } else { "file" }
        if ($itemType -ne "file") { continue }

        $isEnabled = $true
        if ($null -ne $item.enabled) {
            $isEnabled = [bool]$item.enabled
        }

        if (-not $isEnabled) { continue }
        [void]$managedItems.Add($item)
    }

    return $managedItems.ToArray()
}

function Get-ChecksumCoverageModeText {
    param([Parameter(Mandatory)]$Item)

    $hasPinnedChecksum = -not [string]::IsNullOrWhiteSpace([string]$Item.sha256)
    $hasChecksumUrl = -not [string]::IsNullOrWhiteSpace([string]$Item.sha256Url)

    if ($hasPinnedChecksum -and $hasChecksumUrl) { return "pinned+remote" }
    if ($hasPinnedChecksum) { return "pinned-only" }
    if ($hasChecksumUrl) { return "remote-only" }
    return "none"
}

function Test-ManagedDownloadBorderlineFlag {
    param([Parameter(Mandatory)]$Item)

    $value = $null
    if ($null -ne $Item.PSObject.Properties["Borderline"]) {
        $value = $Item.Borderline
    }
    elseif ($null -ne $Item.PSObject.Properties["borderline"]) {
        $value = $Item.borderline
    }

    if ($null -eq $value) {
        return $false
    }

    if ($value -is [bool]) {
        return [bool]$value
    }

    return $false
}

function Test-CrossHostRedirectIsExpected {
    param([AllowNull()][string]$SourceType)

    if ([string]::IsNullOrWhiteSpace($SourceType)) {
        return $false
    }

    return ([string]$SourceType).Trim().ToLowerInvariant() -in @("sourceforge", "github-release")
}

function Get-ManagedFileResilienceMetadataIssues {
    param([Parameter(Mandatory)]$Manifest)

    $issues = New-Object System.Collections.Generic.List[string]
    $managedItems = @(Get-EnabledManagedFileItems -Manifest $Manifest)

    if ($managedItems.Count -eq 0) {
        return $issues.ToArray()
    }

    $seenRanks = @{}
    $validRanks = New-Object System.Collections.Generic.List[int]

    foreach ($item in $managedItems) {
        $name = [string]$item.name
        $sourceType = [string]$item.sourceType
        $fragilityLevel = [string]$item.fragilityLevel
        $fallbackRule = [string]$item.fallbackRule
        $maintenanceRankText = [string]$item.maintenanceRank
        $borderline = $item.borderline

        if ([string]::IsNullOrWhiteSpace($sourceType)) {
            [void]$issues.Add($name + " is missing sourceType.")
        }
        else {
            try {
                Assert-ManagedDownloadSourceTypeValue -Value $sourceType -FieldName "sourceType"
            }
            catch {
                [void]$issues.Add($name + " has an invalid sourceType: " + $sourceType)
            }
        }

        if ([string]::IsNullOrWhiteSpace($fragilityLevel)) {
            [void]$issues.Add($name + " is missing fragilityLevel.")
        }
        else {
            try {
                Assert-ManagedDownloadFragilityLevelValue -Value $fragilityLevel -FieldName "fragilityLevel"
            }
            catch {
                [void]$issues.Add($name + " has an invalid fragilityLevel: " + $fragilityLevel)
            }
        }

        if ([string]::IsNullOrWhiteSpace($fallbackRule)) {
            [void]$issues.Add($name + " is missing fallbackRule.")
        }

        if ($null -ne $borderline -and ($borderline -isnot [bool])) {
            [void]$issues.Add($name + " has a non-boolean borderline flag.")
        }

        if ([string]::IsNullOrWhiteSpace($maintenanceRankText)) {
            [void]$issues.Add($name + " is missing maintenanceRank.")
            continue
        }

        try {
            Assert-PositiveIntegerValue -Value $item.maintenanceRank -FieldName "maintenanceRank"
            $maintenanceRank = [int]$item.maintenanceRank

            if ($seenRanks.ContainsKey($maintenanceRank)) {
                [void]$issues.Add(("maintenanceRank {0} is duplicated by {1} and {2}." -f $maintenanceRank, $seenRanks[$maintenanceRank], $name))
            }
            else {
                $seenRanks[$maintenanceRank] = $name
                [void]$validRanks.Add($maintenanceRank)
            }
        }
        catch {
            [void]$issues.Add($name + " has an invalid maintenanceRank: " + $maintenanceRankText)
        }
    }

    if ($validRanks.Count -eq $managedItems.Count) {
        $sortedRanks = @($validRanks | Sort-Object -Unique)
        $expectedRanks = 1..$managedItems.Count

        if (@(Compare-Object -ReferenceObject $expectedRanks -DifferenceObject $sortedRanks).Count -gt 0) {
            [void]$issues.Add("maintenanceRank values must cover 1.." + $managedItems.Count + " without gaps.")
        }
    }

    return $issues.ToArray()
}

function Get-ManagedDownloadRevalidationRows {
    param([Parameter(Mandatory)]$Manifest)

    $rows = New-Object System.Collections.Generic.List[object]
    $managedItems = @(
        Get-EnabledManagedFileItems -Manifest $Manifest |
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

    foreach ($item in $managedItems) {
        $sourceType = if ($item.sourceType) { ([string]$item.sourceType).Trim().ToLowerInvariant() } else { "" }
        $fragilityLevel = if ($item.fragilityLevel) { ([string]$item.fragilityLevel).Trim().ToLowerInvariant() } else { "" }
        $maintenanceRank = if ($item.maintenanceRank) { [int]$item.maintenanceRank } else { [int]::MaxValue }
        $declaredUrl = [string]$item.url
        $urlResult = Test-RemoteHead -Uri $declaredUrl
        $checksumMode = Get-ChecksumCoverageModeText -Item $item

        $hasChecksumUrl = -not [string]::IsNullOrWhiteSpace([string]$item.sha256Url)
        $checksumUrl = if ($hasChecksumUrl) { [string]$item.sha256Url } else { "" }
        $checksumResult = $null
        if ($hasChecksumUrl) {
            $checksumResult = Test-RemoteHead -Uri $checksumUrl
        }

        $declaredHost = ""
        $finalHost = ""
        $declaredChecksumHost = ""
        $finalChecksumHost = ""

        try { $declaredHost = ([Uri]$declaredUrl).Host } catch {}
        try { $finalHost = ([Uri]$urlResult.FinalUri).Host } catch {}
        if ($hasChecksumUrl) {
            try { $declaredChecksumHost = ([Uri]$checksumUrl).Host } catch {}
            try { $finalChecksumHost = ([Uri]$checksumResult.FinalUri).Host } catch {}
        }

        $driftFlags = New-Object System.Collections.Generic.List[string]
        $notes = New-Object System.Collections.Generic.List[string]

        if (-not $urlResult.Reachable) {
            [void]$driftFlags.Add("url-unreachable")
        }
        elseif ($urlResult.Note) {
            [void]$notes.Add("url: " + [string]$urlResult.Note)
        }

        if (-not [string]::IsNullOrWhiteSpace($declaredHost) -and -not [string]::IsNullOrWhiteSpace($finalHost) -and $declaredHost -ne $finalHost) {
            if (Test-CrossHostRedirectIsExpected -SourceType $sourceType) {
                [void]$notes.Add("url redirected to " + $finalHost)
            }
            else {
                [void]$driftFlags.Add("url-host-changed")
            }
        }

        if ($hasChecksumUrl) {
            if (-not $checksumResult.Reachable) {
                [void]$driftFlags.Add("checksum-source-unreachable")
            }
            elseif ($checksumResult.Note) {
                [void]$notes.Add("checksum: " + [string]$checksumResult.Note)
            }

            if (-not [string]::IsNullOrWhiteSpace($declaredChecksumHost) -and -not [string]::IsNullOrWhiteSpace($finalChecksumHost) -and $declaredChecksumHost -ne $finalChecksumHost) {
                if (Test-CrossHostRedirectIsExpected -SourceType $sourceType) {
                    [void]$notes.Add("checksum redirected to " + $finalChecksumHost)
                }
                else {
                    [void]$driftFlags.Add("checksum-host-changed")
                }
            }
        }
        else {
            [void]$notes.Add("no remote checksum source configured; revalidation is limited to the pinned manifest hash")
        }

        $overallStatus = if ($driftFlags.Count -gt 0) {
            "drift"
        }
        elseif ($hasChecksumUrl) {
            "ok"
        }
        else {
            "ok-limited"
        }

        [void]$rows.Add([PSCustomObject]@{
            MaintenanceRank    = $maintenanceRank
            Name               = [string]$item.name
            Destination        = [string]$item.dest
            SourceType         = $sourceType
            FragilityLevel     = $fragilityLevel
            OverallStatus      = $overallStatus
            Borderline         = Test-ManagedDownloadBorderlineFlag -Item $item
            Url                = $declaredUrl
            UrlReachable       = [bool]$urlResult.Reachable
            UrlStatusCode      = if ($null -ne $urlResult.StatusCode) { [string]$urlResult.StatusCode } else { "" }
            UrlFinalUri        = [string]$urlResult.FinalUri
            ChecksumMode       = $checksumMode
            ChecksumUrl        = $checksumUrl
            ChecksumReachable  = if ($hasChecksumUrl) { [bool]$checksumResult.Reachable } else { "" }
            ChecksumStatusCode = if ($hasChecksumUrl -and $null -ne $checksumResult.StatusCode) { [string]$checksumResult.StatusCode } else { "" }
            ChecksumFinalUri   = if ($hasChecksumUrl) { [string]$checksumResult.FinalUri } else { "" }
            DriftFlags         = ($driftFlags -join "; ")
            Notes              = ($notes -join "; ")
            FallbackRule       = [string]$item.fallbackRule
        })
    }

    return $rows.ToArray()
}

function Get-ManagedDownloadSummaryFromRows {
    param([Parameter(Mandatory)][object[]]$Rows)

    $rankedRows = @(
        $Rows |
        Sort-Object `
            @{ Expression = { [int]$_.MaintenanceRank } },
            @{ Expression = { [string]$_.Name } }
    )

    $borderlineRows = @($rankedRows | Where-Object { Test-ManagedDownloadBorderlineFlag -Item $_ })
    $nextCycleRows = @($rankedRows | Select-Object -First ([Math]::Min(5, $rankedRows.Count)))
    $topPriorityRows = @($rankedRows | Where-Object { [int]$_.MaintenanceRank -le 7 })
    $topPriorityIssueRows = @($topPriorityRows | Where-Object { ([string]$_.OverallStatus).Trim().ToLowerInvariant() -eq "drift" })
    $driftRows = @($rankedRows | Where-Object { ([string]$_.OverallStatus).Trim().ToLowerInvariant() -eq "drift" })

    return [PSCustomObject]@{
        TotalCount            = $rankedRows.Count
        HighCount             = @($rankedRows | Where-Object { ([string]$_.FragilityLevel).Trim().ToLowerInvariant() -eq "high" }).Count
        MediumCount           = @($rankedRows | Where-Object { ([string]$_.FragilityLevel).Trim().ToLowerInvariant() -eq "medium" }).Count
        LowCount              = @($rankedRows | Where-Object { ([string]$_.FragilityLevel).Trim().ToLowerInvariant() -eq "low" }).Count
        PinnedOnlyCount       = @($rankedRows | Where-Object { ([string]$_.ChecksumMode).Trim().ToLowerInvariant() -eq "pinned-only" }).Count
        PinnedRemoteCount     = @($rankedRows | Where-Object { ([string]$_.ChecksumMode).Trim().ToLowerInvariant() -eq "pinned+remote" }).Count
        RemoteOnlyCount       = @($rankedRows | Where-Object { ([string]$_.ChecksumMode).Trim().ToLowerInvariant() -eq "remote-only" }).Count
        OkCount               = @($rankedRows | Where-Object { ([string]$_.OverallStatus).Trim().ToLowerInvariant() -eq "ok" }).Count
        OkLimitedCount        = @($rankedRows | Where-Object { ([string]$_.OverallStatus).Trim().ToLowerInvariant() -eq "ok-limited" }).Count
        DriftCount            = $driftRows.Count
        BorderlineRows        = $borderlineRows
        NextCycleRows         = $nextCycleRows
        TopPriorityRows       = $topPriorityRows
        TopPriorityIssueRows  = $topPriorityIssueRows
        ReleaseGateStatus     = if (($driftRows.Count -eq 0) -and ($topPriorityIssueRows.Count -eq 0)) { "pass" } else { "hold" }
    }
}

function Write-ManagedDownloadRevalidationArtifacts {
    param(
        [Parameter(Mandatory)][object[]]$Rows,
        [Parameter(Mandatory)][string]$RunRoot
    )

    $csvPath = Join-Path $RunRoot "managed-download-revalidation.csv"
    $textPath = Join-Path $RunRoot "managed-download-revalidation.txt"
    $summaryPath = Join-Path $RunRoot "managed-download-summary.txt"
    $summary = Get-ManagedDownloadSummaryFromRows -Rows $Rows
    $verifyRoot = Split-Path -Parent $RunRoot
    $archiveBaseRoot = Join-Path $verifyRoot "managed-download-revalidation"
    $archiveStamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $archiveRunRoot = Join-Path $archiveBaseRoot $archiveStamp
    $latestRoot = Join-Path $archiveBaseRoot "latest"
    $archiveCsvPath = Join-Path $archiveRunRoot "managed-download-revalidation.csv"
    $archiveTextPath = Join-Path $archiveRunRoot "managed-download-revalidation.txt"
    $archiveSummaryPath = Join-Path $archiveRunRoot "managed-download-summary.txt"
    $latestCsvPath = Join-Path $latestRoot "managed-download-revalidation.csv"
    $latestTextPath = Join-Path $latestRoot "managed-download-revalidation.txt"
    $latestSummaryPath = Join-Path $latestRoot "managed-download-summary.txt"

    Ensure-Dir -Path $archiveBaseRoot
    Ensure-Dir -Path $archiveRunRoot
    Ensure-Dir -Path $latestRoot

    $Rows | Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding UTF8
    $Rows | Export-Csv -LiteralPath $archiveCsvPath -NoTypeInformation -Encoding UTF8
    $Rows | Export-Csv -LiteralPath $latestCsvPath -NoTypeInformation -Encoding UTF8

    $lines = New-Object System.Collections.Generic.List[string]
    $summaryLines = New-Object System.Collections.Generic.List[string]

    [void]$lines.Add("ForgerEMS managed-download revalidation")
    [void]$lines.Add("====================================")
    [void]$lines.Add("")
    [void]$lines.Add("This report checks the enabled manifest-managed file items only.")
    [void]$lines.Add("It does not modify the manifest or auto-promote/demote entries.")
    [void]$lines.Add(("Generated: " + (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")))
    [void]$lines.Add(("Managed file items: " + $summary.TotalCount))
    [void]$lines.Add(("Fragility: high " + $summary.HighCount + " | medium " + $summary.MediumCount + " | low " + $summary.LowCount))
    [void]$lines.Add(("Checksum posture: pinned-only " + $summary.PinnedOnlyCount + " | pinned+remote " + $summary.PinnedRemoteCount + " | remote-only " + $summary.RemoteOnlyCount))
    [void]$lines.Add(("Status: OK " + $summary.OkCount + " | OK-LIMITED " + $summary.OkLimitedCount + " | DRIFT " + $summary.DriftCount))
    [void]$lines.Add(("Borderline: " + (($summary.BorderlineRows | ForEach-Object { $_.Name }) -join "; ")))
    [void]$lines.Add(("Inspect first next cycle: " + (($summary.NextCycleRows | ForEach-Object { "[" + [string]$_.MaintenanceRank + "] " + [string]$_.Name }) -join "; ")))
    [void]$lines.Add(("Top fragility slice (ranks 1-7): " + (($summary.TopPriorityRows | ForEach-Object { "[" + [string]$_.MaintenanceRank + "] " + [string]$_.Name }) -join "; ")))
    [void]$lines.Add(("Release-prep gate: " + $summary.ReleaseGateStatus.ToUpperInvariant()))
    [void]$lines.Add(("Archive root: " + $archiveRunRoot))
    [void]$lines.Add("")

    foreach ($row in $Rows) {
        [void]$lines.Add(("[{0}] {1} | {2}/{3} | {4} | {5}" -f $row.MaintenanceRank, $row.OverallStatus.ToUpperInvariant(), $row.FragilityLevel, $row.SourceType, $row.ChecksumMode, $row.Name))
        [void]$lines.Add(("  url: " + $row.Url))
        if ([string]::IsNullOrWhiteSpace([string]$row.ChecksumUrl)) {
            [void]$lines.Add("  checksum source: pinned manifest hash only")
        }
        else {
            [void]$lines.Add(("  checksum source: " + $row.ChecksumUrl))
        }

        if (Test-ManagedDownloadBorderlineFlag -Item $row) {
            [void]$lines.Add("  borderline: yes")
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$row.DriftFlags)) {
            [void]$lines.Add(("  drift: " + $row.DriftFlags))
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$row.Notes)) {
            [void]$lines.Add(("  note: " + $row.Notes))
        }

        [void]$lines.Add(("  fallback: " + $row.FallbackRule))
        [void]$lines.Add("")
    }

    Set-Content -LiteralPath $textPath -Value $lines -Encoding UTF8
    Set-Content -LiteralPath $archiveTextPath -Value $lines -Encoding UTF8
    Set-Content -LiteralPath $latestTextPath -Value $lines -Encoding UTF8

    [void]$summaryLines.Add("ForgerEMS managed-download summary")
    [void]$summaryLines.Add("================================")
    [void]$summaryLines.Add(("Generated: " + (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")))
    [void]$summaryLines.Add(("Total safe items: " + $summary.TotalCount))
    [void]$summaryLines.Add(("Fragility: high " + $summary.HighCount + " | medium " + $summary.MediumCount + " | low " + $summary.LowCount))
    [void]$summaryLines.Add(("Checksum posture: pinned-only " + $summary.PinnedOnlyCount + " | pinned+remote " + $summary.PinnedRemoteCount + " | remote-only " + $summary.RemoteOnlyCount))
    [void]$summaryLines.Add(("Status: OK " + $summary.OkCount + " | OK-LIMITED " + $summary.OkLimitedCount + " | DRIFT " + $summary.DriftCount))
    [void]$summaryLines.Add(("Borderline: " + (($summary.BorderlineRows | ForEach-Object { $_.Name }) -join "; ")))
    [void]$summaryLines.Add(("Inspect first next cycle: " + (($summary.NextCycleRows | ForEach-Object { "[" + [string]$_.MaintenanceRank + "] " + $_.Name }) -join "; ")))
    [void]$summaryLines.Add(("Top fragility slice (ranks 1-7): " + (($summary.TopPriorityRows | ForEach-Object { "[" + [string]$_.MaintenanceRank + "] " + $_.Name }) -join "; ")))
    if ($summary.TopPriorityIssueRows.Count -eq 0) {
        [void]$summaryLines.Add("Top fragility issues: none")
    }
    else {
        [void]$summaryLines.Add(("Top fragility issues: " + (($summary.TopPriorityIssueRows | ForEach-Object { $_.Name }) -join "; ")))
    }
    [void]$summaryLines.Add(("Release-prep gate: " + $summary.ReleaseGateStatus.ToUpperInvariant()))
    [void]$summaryLines.Add(("Archive snapshot: " + $archiveRunRoot))
    [void]$summaryLines.Add(("Latest snapshot: " + $latestRoot))
    [void]$summaryLines.Add("")
    [void]$summaryLines.Add("Status meanings:")
    [void]$summaryLines.Add("- OK -> live URL plus remote checksum source resolved")
    [void]$summaryLines.Add("- OK-LIMITED -> live URL resolved, but checksum confirmation still depends on the pinned manifest hash")
    [void]$summaryLines.Add("- DRIFT -> URL/checksum source drift needs operator review before release use")

    Set-Content -LiteralPath $summaryPath -Value $summaryLines -Encoding UTF8
    Set-Content -LiteralPath $archiveSummaryPath -Value $summaryLines -Encoding UTF8
    Set-Content -LiteralPath $latestSummaryPath -Value $summaryLines -Encoding UTF8

    $script:ManagedRevalidationCsvPath = $csvPath
    $script:ManagedRevalidationTextPath = $textPath
    $script:ManagedRevalidationSummaryPath = $summaryPath
    $script:ManagedRevalidationArchiveRoot = $archiveRunRoot
    $script:ManagedRevalidationLatestRoot = $latestRoot
    $script:ManagedRevalidationSummary = $summary

    return [PSCustomObject]@{
        CsvPath         = $csvPath
        TextPath        = $textPath
        SummaryPath     = $summaryPath
        ArchiveRoot     = $archiveRunRoot
        LatestRoot      = $latestRoot
        DriftCount      = $summary.DriftCount
        LimitedCount    = $summary.OkLimitedCount
        Summary         = $summary
    }
}

function Get-NormalizedUriText {
    param([AllowNull()][string]$Uri)

    if ([string]::IsNullOrWhiteSpace($Uri)) {
        return ""
    }

    try {
        return ([Uri]$Uri).AbsoluteUri.TrimEnd('/')
    }
    catch {
        return ([string]$Uri).Trim()
    }
}

function Resolve-BundleChildPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $candidatePath = Join-Path $normalizedRoot ($RelativePath -replace '/', '\')
    $resolvedPath = [IO.Path]::GetFullPath($candidatePath).TrimEnd('\')

    if (($resolvedPath -ne $normalizedRoot) -and -not $resolvedPath.StartsWith($normalizedRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Checksum entry escapes the bundle root: $RelativePath"
    }

    return $resolvedPath
}

function Assert-ChecksumsFileValid {
    param(
        [Parameter(Mandatory)][string]$ChecksumsPath,
        [Parameter(Mandatory)][string]$BundleRoot
    )

    $entryCount = 0

    foreach ($line in Get-Content -LiteralPath $ChecksumsPath) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        if ($trimmed -notmatch '^(?<hash>[A-Fa-f0-9]{64}) \*(?<path>.+)$') {
            throw "Invalid checksum entry format: $line"
        }

        $expectedHash = $Matches["hash"].ToLowerInvariant()
        $relativePath = $Matches["path"]
        $fullPath = Resolve-BundleChildPath -Root $BundleRoot -RelativePath $relativePath

        if (-not (Test-Path -LiteralPath $fullPath)) {
            throw "Checksum file references a missing bundle file: $relativePath"
        }

        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash.ToLowerInvariant()
        if ($actualHash -ne $expectedHash) {
            throw "Checksum mismatch for bundle file: $relativePath"
        }

        $entryCount++
    }

    if ($entryCount -eq 0) {
        throw "Checksum file contains no valid entries."
    }
}

function Assert-ReleaseSignatureFileValid {
    param(
        [Parameter(Mandatory)][string]$SignaturePath,
        [Parameter(Mandatory)][string]$ChecksumsPath,
        [Parameter(Mandatory)][string]$BundleRoot,
        [Parameter(Mandatory)]$VersionInfo
    )

    $fields = @{}

    foreach ($line in Get-Content -LiteralPath $SignaturePath) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed) -or $trimmed.StartsWith("#")) {
            continue
        }

        if ($trimmed -notmatch '^(?<key>[A-Za-z0-9]+):\s*(?<value>.*)$') {
            throw "Invalid signature entry format: $line"
        }

        $fields[$Matches["key"]] = $Matches["value"]
    }

    foreach ($requiredKey in @(
        "SignatureType",
        "Algorithm",
        "CoreVersion",
        "BuildTimestampUtc",
        "ReleaseType",
        "SignedFile",
        "SignedFileSha256",
        "ManifestFile",
        "ManifestSha256"
    )) {
        if (-not $fields.ContainsKey($requiredKey) -or [string]::IsNullOrWhiteSpace([string]$fields[$requiredKey])) {
            throw "Signature file is missing required field: $requiredKey"
        }
    }

    if ([string]$fields["SignatureType"] -ne "checksum-catalog-sha256") {
        throw "Unsupported signature type: $($fields["SignatureType"])"
    }

    if ([string]$fields["Algorithm"] -ne "SHA256") {
        throw "Unsupported signature algorithm: $($fields["Algorithm"])"
    }

    Assert-Sha256Value -Value $fields["SignedFileSha256"] -FieldName "SignedFileSha256"
    Assert-Sha256Value -Value $fields["ManifestSha256"] -FieldName "ManifestSha256"

    if ([string]$fields["CoreVersion"] -ne [string]$VersionInfo.Version) {
        throw "Signature coreVersion does not match the bundled manifest."
    }

    if ((Format-BuildTimestamp -Value $fields["BuildTimestampUtc"]) -ne [string]$VersionInfo.BuildTimestampUtc) {
        throw "Signature buildTimestampUtc does not match the bundled manifest."
    }

    if (([string]$fields["ReleaseType"]).Trim().ToLowerInvariant() -ne [string]$VersionInfo.ReleaseType) {
        throw "Signature releaseType does not match the bundled manifest."
    }

    $signedFilePath = Resolve-BundleChildPath -Root $BundleRoot -RelativePath ([string]$fields["SignedFile"])
    if ([IO.Path]::GetFullPath($signedFilePath) -ne [IO.Path]::GetFullPath($ChecksumsPath)) {
        throw "Signature does not point at the active CHECKSUMS.sha256 file."
    }

    $manifestPath = Resolve-BundleChildPath -Root $BundleRoot -RelativePath ([string]$fields["ManifestFile"])

    $actualChecksumsHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $signedFilePath).Hash.ToLowerInvariant()
    if ($actualChecksumsHash -ne ([string]$fields["SignedFileSha256"]).ToLowerInvariant()) {
        throw "Signature does not match CHECKSUMS.sha256."
    }

    $actualManifestHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $manifestPath).Hash.ToLowerInvariant()
    if ($actualManifestHash -ne ([string]$fields["ManifestSha256"]).ToLowerInvariant()) {
        throw "Signature does not match ForgerEMS.updates.json."
    }
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
        throw "Vendor inventory coreVersion does not match the bundled manifest."
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
    if (-not [string]::IsNullOrWhiteSpace([string]$Inventory.releaseType) -and ([string]$Inventory.releaseType).Trim().ToLowerInvariant() -ne $ExpectedReleaseType) {
        throw "Vendor inventory releaseType does not match the bundled manifest."
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

function Get-VentoyCoreVersionInfo {
    $manifestPath = Resolve-BundledManifestPath
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

function New-RunDirectory {
    param(
        [Parameter(Mandatory)][string]$BaseRoot,
        [Parameter(Mandatory)][string]$Name
    )

    $dir = Join-Path $BaseRoot $Name
    Ensure-Dir -Path $dir
    return $dir
}

function Invoke-PublicScript {
    param(
        [Parameter(Mandatory)][string]$ScriptPath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogPath,
        [switch]$AllowFailure
    )

    $output = & $powerShellExe -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @Arguments 2>&1 | Out-String
    $exitCode = $LASTEXITCODE

    Set-Content -LiteralPath $LogPath -Value $output -Encoding UTF8

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "Script failed with exit code $exitCode. See $LogPath"
    }

    return [PSCustomObject]@{
        ExitCode = $exitCode
        Output   = $output
        LogPath  = $LogPath
    }
}

function Run-Test {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Body
    )

    try {
        & $Body
        $results.Add([PSCustomObject]@{
            Name   = $Name
            Status = "PASS"
            Detail = ""
        })
        Write-Status "[PASS] $Name" "OK"
    }
    catch {
        $results.Add([PSCustomObject]@{
            Name   = $Name
            Status = "FAIL"
            Detail = $_.Exception.Message
        })
        Write-Status "[FAIL] $Name :: $($_.Exception.Message)" "ERROR"
    }
}

function Test-RemoteHead {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [int]$TimeoutSec = 30
    )

    try {
        $response = Invoke-WebRequest -Uri $Uri -Method Head -MaximumRedirection 10 -TimeoutSec $TimeoutSec -UseBasicParsing -Headers @{ "User-Agent" = "ForgerEMS-Verify/1.0" }
        $finalUri = ""
        try {
            if ($response.BaseResponse -and $response.BaseResponse.ResponseUri) {
                $finalUri = [string]$response.BaseResponse.ResponseUri.AbsoluteUri
            }
        }
        catch {}

        return [PSCustomObject]@{
            Reachable = $true
            StatusCode = [int]$response.StatusCode
            Note = ""
            FinalUri = $finalUri
        }
    }
    catch {
        $statusCode = $null
        $finalUri = ""

        if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
            try { $statusCode = [int]$_.Exception.Response.StatusCode.value__ } catch {}
        }

        if ($_.Exception.Response -and $_.Exception.Response.ResponseUri) {
            try { $finalUri = [string]$_.Exception.Response.ResponseUri.AbsoluteUri } catch {}
        }

        if ($statusCode -in @(403, 405)) {
            return [PSCustomObject]@{
                Reachable = $true
                StatusCode = $statusCode
                Note = "HEAD returned $statusCode; treating upstream as reachable but restricted."
                FinalUri = $finalUri
            }
        }

        return [PSCustomObject]@{
            Reachable = $false
            StatusCode = $statusCode
            Note = $_.Exception.Message
            FinalUri = $finalUri
        }
    }
}

if ($ShowVersion) {
    Show-VentoyCoreVersionInfo
    exit 0
}

if ($RevalidateManagedDownloads) {
    $Online = $true
}

$runtimeRoot = Get-VentoyCoreRuntimeRoot
$manifestPath = Resolve-BundledManifestPath
$vendorInventoryPath = Resolve-VendorInventoryPath
$checksumsPath = Resolve-ChecksumsPath
$signaturePath = Resolve-SignaturePath
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$versionInfo = Get-VentoyCoreVersionInfo
$managedChecksumPolicy = Get-ManagedChecksumPolicy -Manifest $manifest

if ([string]::IsNullOrWhiteSpace($VerifyRoot)) {
    $VerifyRoot = Join-Path $runtimeRoot ".verify"
}

$runStamp = (Get-Date -Format "yyyyMMdd_HHmmss_fff") + "_" + ([Guid]::NewGuid().ToString("N").Substring(0, 8))
$runRoot = Join-Path $VerifyRoot ("ventoy-core-" + $runStamp)

$setupScript = Join-Path $PSScriptRoot "Setup-ForgerEMS.ps1"
$legacySetupScript = Join-Path $PSScriptRoot "Setup_USB_Toolkit.ps1"
$updateScript = Join-Path $PSScriptRoot "Update-ForgerEMS.ps1"

Ensure-Dir -Path $VerifyRoot
Ensure-Dir -Path $runRoot

Write-Status ("Ventoy core: {0} {1} ({2})" -f $versionInfo.Name, $versionInfo.Version, $versionInfo.BuildTimestampUtc) "INFO"
Write-Status ("Release: " + $versionInfo.ReleaseType) "INFO"
Write-Status ("Verification artifacts: " + $runRoot) "INFO"

if ($RevalidateManagedDownloads) {
    Write-Status "Managed-download revalidation mode enabled. Wrapper and vendor-inventory regression checks are skipped in this lightweight pass." "INFO"
}

foreach ($requiredPath in @($setupScript, $legacySetupScript, $updateScript, $manifestPath)) {
    Assert-Condition -Condition (Test-Path -LiteralPath $requiredPath) -Message "Required file not found: $requiredPath"
}

if (-not $RevalidateManagedDownloads) {
    Run-Test -Name "vendor-inventory-contract-is-valid" -Body {
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$vendorInventoryPath)) -Message "Vendor inventory manifest was not found."

        $inventory = Get-Content -LiteralPath $vendorInventoryPath -Raw | ConvertFrom-Json
        Assert-VendorInventoryContract `
            -Inventory $inventory `
            -SourceName $vendorInventoryPath `
            -ExpectedCoreVersion $versionInfo.Version `
            -ExpectedReleaseType $versionInfo.ReleaseType
    }
}

Run-Test -Name "managed-checksum-release-discipline" -Body {
    Assert-ManagedChecksumReleaseDiscipline `
        -ReleaseType $versionInfo.ReleaseType `
        -ManagedChecksumPolicy $managedChecksumPolicy
}

Run-Test -Name "managed-items-have-checksum-coverage" -Body {
    $missingItems = Get-ManagedItemsMissingChecksumCoverage -Manifest $manifest
    if ($missingItems.Count -eq 0) {
        return
    }

    $missingSummary = $missingItems | ForEach-Object { "{0} -> {1}" -f $_.Name, $_.Dest }
    $message = "Managed file items are missing checksum coverage (policy={0}): {1}" -f $managedChecksumPolicy, ($missingSummary -join "; ")

    $shouldRequireManagedChecksums = $EnforceManagedChecksums -or ($versionInfo.ReleaseType -in @("candidate", "stable"))
    if ($shouldRequireManagedChecksums) {
        throw $message
    }

    Add-Warning ("[offline] " + $message)
}

Run-Test -Name "managed-download-resilience-metadata-is-complete" -Body {
    $issues = @(Get-ManagedFileResilienceMetadataIssues -Manifest $manifest)
    if ($issues.Count -eq 0) {
        return
    }

    $message = "Managed file items are missing or have invalid resilience metadata: " + ($issues -join "; ")
    if ($versionInfo.ReleaseType -in @("candidate", "stable")) {
        throw $message
    }

    Add-Warning ("[offline] " + $message)
}

if ($checksumsPath -and -not $RevalidateManagedDownloads) {
    Run-Test -Name "release-checksums-are-valid" -Body {
        Assert-ChecksumsFileValid -ChecksumsPath $checksumsPath -BundleRoot $PSScriptRoot
    }
}

if ($signaturePath -and -not $RevalidateManagedDownloads) {
    Run-Test -Name "release-signature-is-valid" -Body {
        Assert-Condition -Condition (-not [string]::IsNullOrWhiteSpace([string]$checksumsPath)) -Message "SIGNATURE.txt requires CHECKSUMS.sha256 beside the release bundle."
        Assert-ReleaseSignatureFileValid `
            -SignaturePath $signaturePath `
            -ChecksumsPath $checksumsPath `
            -BundleRoot $PSScriptRoot `
            -VersionInfo $versionInfo
    }
}

if (-not $RevalidateManagedDownloads) {
    Run-Test -Name "setup-wrapper-seeds-manifest" -Body {
        $root = New-RunDirectory -BaseRoot $runRoot -Name "setup-wrapper-seed"
        $result = Invoke-PublicScript `
            -ScriptPath $setupScript `
            -Arguments @("-UsbRoot", $root, "-OwnerName", "Verify", "-SeedManifest") `
            -LogPath (Join-Path $runRoot "setup-wrapper-seeds-manifest.log")

        foreach ($expectedRelativePath in @(
            "README.txt",
            "ForgerEMS.updates.json",
            "Docs\ForgerEMS-Download-Catalog.txt",
            "Docs\ForgerEMS-Managed-Download-Maintenance.txt",
            "Docs\ForgerEMS-Link-Inventory.csv",
            "IfScriptFails(ManualSetup)\Ventoy_Official.txt"
        )) {
            $expectedPath = Join-Path $root $expectedRelativePath
            Assert-Condition -Condition (Test-Path -LiteralPath $expectedPath) -Message "Expected setup artifact missing: $expectedPath"
        }

        $legacyVentoyShortcut = Join-Path $root "DOWNLOAD - Ventoy.url"
        Assert-Condition -Condition (-not (Test-Path -LiteralPath $legacyVentoyShortcut)) -Message "Ventoy should no longer be seeded as a root shortcut."

        Assert-Condition -Condition ($result.Output -match "Done\. ForgerEMS layout created") -Message "Setup wrapper output did not include the success summary."
    }

    Run-Test -Name "legacy-wrapper-whatif-is-dry" -Body {
        $root = New-RunDirectory -BaseRoot $runRoot -Name "legacy-wrapper-preview"
        $result = Invoke-PublicScript `
            -ScriptPath $legacySetupScript `
            -Arguments @("-UsbRoot", $root, "-OwnerName", "Verify", "-WhatIf") `
            -LogPath (Join-Path $runRoot "legacy-wrapper-whatif-is-dry.log")

        Assert-Condition -Condition ($result.ExitCode -eq 0) -Message "Legacy wrapper returned a non-zero exit code."
        Assert-Condition -Condition ((Get-DirectoryChildCount -Path $root) -eq 0) -Message "Legacy wrapper preview created files or directories."
    }

    Run-Test -Name "updater-falls-back-to-bundled-manifest" -Body {
        $root = New-RunDirectory -BaseRoot $runRoot -Name "update-fallback-preview"
        $result = Invoke-PublicScript `
            -ScriptPath $updateScript `
            -Arguments @("-UsbRoot", $root, "-WhatIf") `
            -LogPath (Join-Path $runRoot "updater-falls-back-to-bundled-manifest.log")

        Assert-Condition -Condition ($result.ExitCode -eq 0) -Message "Updater preview returned a non-zero exit code."
        Assert-Condition -Condition ($result.Output -match [regex]::Escape($manifestPath)) -Message "Updater did not report using the bundled fallback manifest."
        Assert-Condition -Condition ((Get-DirectoryChildCount -Path $root) -eq 0) -Message "Updater preview created files or directories."
    }

    Run-Test -Name "manifest-path-escape-is-rejected" -Body {
        $root = New-RunDirectory -BaseRoot $runRoot -Name "path-safety"
        $manifestUnderTestPath = Join-Path $root "escape.json"

        $manifestContent = @'
{
  "manifestVersion": 1,
  "coreVersion": "verify-test",
  "buildTimestampUtc": "2026-04-20T00:00:00Z",
  "settings": {
    "downloadFolder": "_downloads",
    "archiveFolder": "_archive",
    "logFolder": "_logs"
  },
  "items": [
    {
      "name": "Escape Test",
      "type": "page",
      "dest": "..\\escape.url",
      "url": "https://example.com/",
      "enabled": true
    }
  ]
}
'@

        Set-Content -LiteralPath $manifestUnderTestPath -Value $manifestContent -Encoding UTF8

        $result = Invoke-PublicScript `
            -ScriptPath $updateScript `
            -Arguments @("-UsbRoot", $root, "-ManifestName", "escape.json", "-WhatIf") `
            -LogPath (Join-Path $runRoot "manifest-path-escape-is-rejected.log") `
            -AllowFailure

        Assert-Condition -Condition ($result.ExitCode -ne 0) -Message "Path-escape manifest unexpectedly succeeded."
        Assert-Condition -Condition ($result.Output -match "escapes the selected root") -Message "Path-escape failure did not mention root protection."
        Assert-Condition -Condition ((Get-DirectoryChildCount -Path $root) -eq 1) -Message "Path-escape test wrote unexpected artifacts."
    }
}

if ($Online) {
    if ($RevalidateManagedDownloads) {
        Run-Test -Name "managed-download-revalidation" -Body {
            $rows = @(Get-ManagedDownloadRevalidationRows -Manifest $manifest)
            $report = Write-ManagedDownloadRevalidationArtifacts -Rows $rows -RunRoot $runRoot

            $limitedItems = @($rows | Where-Object { $_.OverallStatus -eq "ok-limited" })
            if ($limitedItems.Count -gt 0) {
                Add-Warning ("[online] pinned-only checksum revalidation remains limited for: " + (($limitedItems | ForEach-Object { $_.Name }) -join "; "))
            }

            if ($report.DriftCount -gt 0) {
                $driftedItems = @($rows | Where-Object { $_.OverallStatus -eq "drift" } | ForEach-Object { $_.Name })
                throw ("Managed download drift detected for: " + ($driftedItems -join "; ") + ". See " + $report.TextPath)
            }
        }
    }
    else {
        Run-Test -Name "online-managed-upstreams" -Body {
            foreach ($item in @($manifest.items)) {
                if ($null -eq $item -or [string]::IsNullOrWhiteSpace([string]$item.url)) {
                    continue
                }

                $declaredUrl = [string]$item.url
                $sourceType = if ($item.sourceType) { ([string]$item.sourceType).Trim().ToLowerInvariant() } else { "" }
                $urlResult = Test-RemoteHead -Uri $declaredUrl

                if ($urlResult.Reachable) {
                    if ($urlResult.Note) {
                        Add-Warning ("[online] " + [string]$item.name + " -> " + $urlResult.Note)
                    }

                    $declaredHost = ""
                    $finalHost = ""
                    try { $declaredHost = ([Uri]$declaredUrl).Host } catch {}
                    try { $finalHost = ([Uri]$urlResult.FinalUri).Host } catch {}

                    if (-not [string]::IsNullOrWhiteSpace($declaredHost) -and -not [string]::IsNullOrWhiteSpace($finalHost) -and $declaredHost -ne $finalHost -and -not (Test-CrossHostRedirectIsExpected -SourceType $sourceType)) {
                        Add-Warning ("[online] " + [string]$item.name + " redirected to a different host: " + $urlResult.FinalUri)
                    }
                }
                else {
                    Add-Warning ("[online] " + [string]$item.name + " URL probe failed: " + $urlResult.Note)
                }

                $itemType = if ($item.type) { ([string]$item.type).Trim().ToLowerInvariant() } else { "file" }

                if ($itemType -eq "file") {
                    $hasPinnedChecksum = -not [string]::IsNullOrWhiteSpace([string]$item.sha256)
                    $hasChecksumUrl = -not [string]::IsNullOrWhiteSpace([string]$item.sha256Url)

                    if ($item.sha256Url) {
                        $shaResult = Test-RemoteHead -Uri ([string]$item.sha256Url)

                        if ($shaResult.Reachable) {
                            if ($shaResult.Note) {
                                Add-Warning ("[online] " + [string]$item.name + " checksum URL -> " + $shaResult.Note)
                            }

                            $declaredChecksumHost = ""
                            $finalChecksumHost = ""
                            try { $declaredChecksumHost = ([Uri]([string]$item.sha256Url)).Host } catch {}
                            try { $finalChecksumHost = ([Uri]$shaResult.FinalUri).Host } catch {}

                            if (-not [string]::IsNullOrWhiteSpace($declaredChecksumHost) -and -not [string]::IsNullOrWhiteSpace($finalChecksumHost) -and $declaredChecksumHost -ne $finalChecksumHost -and -not (Test-CrossHostRedirectIsExpected -SourceType $sourceType)) {
                                Add-Warning ("[online] " + [string]$item.name + " checksum source redirected to a different host: " + $shaResult.FinalUri)
                            }
                        }
                        else {
                            Add-Warning ("[online] " + [string]$item.name + " checksum URL probe failed: " + $shaResult.Note)

                            if (-not $hasPinnedChecksum) {
                                Add-Warning ("[online] " + [string]$item.name + " download is currently unverifiable because no pinned checksum is available.")
                            }
                        }
                    }
                    elseif (-not $hasPinnedChecksum) {
                        Add-Warning ("[online] " + [string]$item.name + " has no pinned checksum or checksum URL and is not independently verifiable.")
                    }

                    if ((-not $urlResult.Reachable) -and (-not $hasPinnedChecksum) -and (-not $hasChecksumUrl)) {
                        Add-Warning ("[online] " + [string]$item.name + " upstream could not be confirmed and the download is unverifiable.")
                    }
                }
            }
        }

        Run-Test -Name "online-vendor-upstreams" -Body {
            if (-not $vendorInventoryPath) {
                Add-Warning "[online] vendor inventory manifest not found; skipping vendor upstream checks."
                return
            }

            $inventory = Get-Content -LiteralPath $vendorInventoryPath -Raw | ConvertFrom-Json

            foreach ($entry in @($inventory.items)) {
                if ($null -eq $entry) { continue }

                $sourceUrl = [string]$entry.sourceUrl
                $hasChecksum = -not [string]::IsNullOrWhiteSpace([string]$entry.checksum)
                $isVerified = [bool]$entry.verified

                if ([string]::IsNullOrWhiteSpace($sourceUrl)) {
                    $issues = New-Object System.Collections.Generic.List[string]
                    if (-not $isVerified) { [void]$issues.Add("not marked verified") }
                    [void]$issues.Add("no source URL")
                    if (-not $hasChecksum) { [void]$issues.Add("no checksum") }
                    Add-Warning ("[online] vendor inventory item lacks provenance coverage (" + ($issues -join ", ") + "): " + [string]$entry.name)

                    continue
                }

                if (-not $isVerified) {
                    Add-Warning ("[online] vendor inventory item is not marked verified: " + [string]$entry.name)
                }

                if (-not $hasChecksum) {
                    Add-Warning ("[online] vendor inventory item has no checksum: " + [string]$entry.name)
                }

                $headResult = Test-RemoteHead -Uri $sourceUrl
                if ($headResult.Reachable) {
                    if ($headResult.Note) {
                        Add-Warning ("[online] vendor source " + [string]$entry.name + " -> " + $headResult.Note)
                    }

                    $declaredHost = ""
                    $finalHost = ""
                    try { $declaredHost = ([Uri]$sourceUrl).Host } catch {}
                    try { $finalHost = ([Uri]$headResult.FinalUri).Host } catch {}

                    if (-not [string]::IsNullOrWhiteSpace($declaredHost) -and -not [string]::IsNullOrWhiteSpace($finalHost) -and $declaredHost -ne $finalHost) {
                        Add-Warning ("[online] vendor source redirected to a different host for " + [string]$entry.name + ": " + $headResult.FinalUri)
                    }

                }
                else {
                    if ($hasChecksum) {
                        Add-Warning ("[online] vendor source " + [string]$entry.name + " probe failed: " + $headResult.Note)
                    }
                    else {
                        Add-Warning ("[online] vendor source " + [string]$entry.name + " probe failed and no checksum is recorded: " + $headResult.Note)
                    }
                }
            }
        }
    }
}

Write-Host ""
Write-Status "Verification summary" "INFO"

foreach ($result in $results) {
    if ($result.Status -eq "PASS") {
        Write-Status ("- " + $result.Name + ": PASS") "OK"
    }
    else {
        Write-Status ("- " + $result.Name + ": FAIL :: " + $result.Detail) "ERROR"
    }
}

Write-Host ""
Write-Status ("Warnings: " + $script:WarningCount) "INFO"
Write-Status ("Artifacts kept at: " + $runRoot) "INFO"
if ($script:ManagedRevalidationSummary) {
    Write-Host ""
    Write-Status "Managed download summary" "INFO"
    Write-Status ("- total safe items: " + $script:ManagedRevalidationSummary.TotalCount) "INFO"
    Write-Status ("- fragility: high " + $script:ManagedRevalidationSummary.HighCount + " | medium " + $script:ManagedRevalidationSummary.MediumCount + " | low " + $script:ManagedRevalidationSummary.LowCount) "INFO"
    Write-Status ("- checksum posture: pinned-only " + $script:ManagedRevalidationSummary.PinnedOnlyCount + " | pinned+remote " + $script:ManagedRevalidationSummary.PinnedRemoteCount + " | remote-only " + $script:ManagedRevalidationSummary.RemoteOnlyCount) "INFO"
    Write-Status ("- status: OK " + $script:ManagedRevalidationSummary.OkCount + " | OK-LIMITED " + $script:ManagedRevalidationSummary.OkLimitedCount + " | DRIFT " + $script:ManagedRevalidationSummary.DriftCount) "INFO"
    Write-Status ("- borderline: " + (($script:ManagedRevalidationSummary.BorderlineRows | ForEach-Object { $_.Name }) -join "; ")) "INFO"
    Write-Status ("- inspect first next cycle: " + (($script:ManagedRevalidationSummary.NextCycleRows | ForEach-Object { "[" + [string]$_.MaintenanceRank + "] " + $_.Name }) -join "; ")) "INFO"
    if ($script:ManagedRevalidationSummary.TopPriorityIssueRows.Count -eq 0) {
        Write-Status "- top fragility issues: none" "OK"
    }
    else {
        Write-Status ("- top fragility issues: " + (($script:ManagedRevalidationSummary.TopPriorityIssueRows | ForEach-Object { $_.Name }) -join "; ")) "ERROR"
    }
}
if ($script:ManagedRevalidationCsvPath) {
    Write-Status ("Managed download CSV report: " + $script:ManagedRevalidationCsvPath) "INFO"
}
if ($script:ManagedRevalidationTextPath) {
    Write-Status ("Managed download text report: " + $script:ManagedRevalidationTextPath) "INFO"
}
if ($script:ManagedRevalidationSummaryPath) {
    Write-Status ("Managed download summary report: " + $script:ManagedRevalidationSummaryPath) "INFO"
}
if ($script:ManagedRevalidationArchiveRoot) {
    Write-Status ("Managed download archive snapshot: " + $script:ManagedRevalidationArchiveRoot) "INFO"
}
if ($script:ManagedRevalidationLatestRoot) {
    Write-Status ("Managed download latest snapshot: " + $script:ManagedRevalidationLatestRoot) "INFO"
}

$failedCount = @($results | Where-Object { $_.Status -eq "FAIL" }).Count

if ($failedCount -gt 0) {
    Write-Status "Ventoy core verification failed." "ERROR"
    exit 1
}

Write-Status "Ventoy core verification passed." "OK"
exit 0
