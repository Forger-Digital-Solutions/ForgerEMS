#requires -Version 5.1
<#
.SYNOPSIS
Audits ForgerEMS catalog downloadMode promotion policy.

.DESCRIPTION
Reads the managed-download manifest, validates first-class downloadMode policy,
prints mode/promotion counts, and optionally writes a markdown promotion report.
This helper is read-only: it never downloads artifacts and never rewrites the
manifest.
#>
[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "manifests\ForgerEMS.updates.json"),
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

function Get-ValidDownloadModes {
    @(
        "ManagedDownload",
        "OfficialDownloadPage",
        "ManualMediaRequired",
        "ReviewFirst",
        "VendorPortal",
        "LicenseRestricted",
        "DynamicMirrorOnly",
        "OEMSpecific",
        "FirmwareBlocked",
        "CommunityToolkit",
        "Unsupported",
        "InfoOnly"
    )
}

function Get-ModeLabel {
    param([Parameter(Mandatory)][string]$Mode)

    switch ($Mode) {
        "ManagedDownload" { "Managed Download" }
        "OfficialDownloadPage" { "Official Download Page" }
        "ManualMediaRequired" { "Manual Media Required" }
        "ReviewFirst" { "Review First" }
        "VendorPortal" { "Vendor Portal" }
        "OEMSpecific" { "Vendor Portal" }
        "LicenseRestricted" { "License / EULA Required" }
        "DynamicMirrorOnly" { "Official Mirror Page" }
        "FirmwareBlocked" { "Firmware / BIOS Portal" }
        "CommunityToolkit" { "Community Toolkit Page" }
        "Unsupported" { "Unsupported / Reference Only" }
        default { "Reference Info" }
    }
}

function Test-ChecksumProof {
    param([Parameter(Mandatory)]$Item)

    (-not [string]::IsNullOrWhiteSpace([string]$Item.sha256)) -or
    (-not [string]::IsNullOrWhiteSpace([string]$Item.sha256Url)) -or
    (-not [string]::IsNullOrWhiteSpace([string]$Item.sha512)) -or
    (-not [string]::IsNullOrWhiteSpace([string]$Item.sha512Url))
}

function Get-ChecksumSource {
    param([Parameter(Mandatory)]$Item)

    $parts = [System.Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace([string]$Item.sha256)) { [void]$parts.Add("pinned SHA-256") }
    if (-not [string]::IsNullOrWhiteSpace([string]$Item.sha256Url)) { [void]$parts.Add("SHA-256 URL") }
    if (-not [string]::IsNullOrWhiteSpace([string]$Item.sha512)) { [void]$parts.Add("pinned SHA-512") }
    if (-not [string]::IsNullOrWhiteSpace([string]$Item.sha512Url)) { [void]$parts.Add("SHA-512 URL") }
    if ($parts.Count -eq 0) { return "" }
    $parts -join ", "
}

function Get-PromotionBucket {
    param([Parameter(Mandatory)][string]$Mode)

    switch ($Mode) {
        "ManagedDownload" { "promoted/managed" }
        "OfficialDownloadPage" { "kept official page" }
        "ManualMediaRequired" { "kept manual media" }
        "ReviewFirst" { "kept review first" }
        "VendorPortal" { "kept vendor portal" }
        "OEMSpecific" { "kept OEM-specific" }
        "LicenseRestricted" { "kept license restricted" }
        "DynamicMirrorOnly" { "kept dynamic mirror" }
        "FirmwareBlocked" { "kept firmware blocked" }
        "CommunityToolkit" { "kept community review" }
        default { "needs human review" }
    }
}

function Get-PolicyIssues {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)]$Rows
    )

    $issues = [System.Collections.Generic.List[string]]::new()
    $valid = Get-ValidDownloadModes
    $seenNames = @{}
    $seenDests = @{}
    $requireChecksum = ([string]$Manifest.managedChecksumPolicy) -eq "require-for-release"

    foreach ($item in @($Manifest.items)) {
        $name = [string]$item.name
        $dest = [string]$item.dest
        $type = ([string]$item.type).Trim().ToLowerInvariant()
        $mode = [string]$item.downloadMode

        if ($mode -notin $valid) { [void]$issues.Add("$name has invalid downloadMode '$mode'.") }
        if ($seenNames.ContainsKey($name)) { [void]$issues.Add("duplicate name: $name") } else { $seenNames[$name] = $true }
        if ($seenDests.ContainsKey($dest)) { [void]$issues.Add("duplicate dest: $dest") } else { $seenDests[$dest] = $true }

        if ($mode -eq "ManagedDownload" -and $type -ne "file") { [void]$issues.Add("${name}: ManagedDownload must be type=file.") }
        if ($type -eq "file" -and $mode -ne "ManagedDownload") { [void]$issues.Add("${name}: type=file must be ManagedDownload.") }
        if ($mode -eq "ManagedDownload" -and $requireChecksum -and -not (Test-ChecksumProof $item)) { [void]$issues.Add("${name}: missing checksum proof.") }
        if ($type -eq "page") {
            foreach ($field in @("sha256", "sha256Url", "sha512", "sha512Url")) {
                if ($null -ne $item.PSObject.Properties[$field] -and -not [string]::IsNullOrWhiteSpace([string]$item.$field)) {
                    [void]$issues.Add("${name}: page item declares $field.")
                }
            }
        }
        if ($mode -in @("ManualMediaRequired", "FirmwareBlocked", "VendorPortal", "OEMSpecific", "LicenseRestricted") -and $type -ne "page") {
            [void]$issues.Add("${name}: $mode must stay type=page.")
        }
        if ([string]$item.actionLabel -eq "Info" -and $mode -ne "InfoOnly") {
            [void]$issues.Add("${name}: raw Info label is only allowed for InfoOnly.")
        }
    }

    $issues.ToArray()
}

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$rows = @(
    foreach ($item in @($manifest.items)) {
        $mode = [string]$item.downloadMode
        [PSCustomObject]@{
            Name           = [string]$item.name
            Type           = [string]$item.type
            DownloadMode   = $mode
            ActionLabel    = if ($item.actionLabel) { [string]$item.actionLabel } else { Get-ModeLabel $mode }
            Promoted       = if ($mode -eq "ManagedDownload") { "yes" } else { "no" }
            Reason         = [string]$item.promotionEvidence
            ChecksumSource = Get-ChecksumSource $item
            SourceTrust    = [string]$item.sourceTrust
            LegalNote      = [string]$item.legalRisk
            FollowUp       = if ([bool]$item.managedPromotionCandidate) { "review managed candidate" } else { "" }
        }
    }
)

$issues = @(Get-PolicyIssues -Manifest $manifest -Rows $rows)
$counts = $rows | Group-Object DownloadMode | Sort-Object Name

Write-Host "ForgerEMS catalog promotion audit"
Write-Host ("Manifest: " + (Resolve-Path -LiteralPath $ManifestPath))
Write-Host ("Total items: " + $rows.Count)
foreach ($count in $counts) {
    Write-Host ("- {0}: {1}" -f $count.Name, $count.Count)
}

if ($issues.Count -gt 0) {
    Write-Host ""
    Write-Host "Policy issues:" -ForegroundColor Red
    foreach ($issue in $issues) { Write-Host ("- " + $issue) -ForegroundColor Red }
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    $reportDir = Split-Path -Parent $reportFullPath
    if (-not (Test-Path -LiteralPath $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }

    $lines = [System.Collections.Generic.List[string]]::new()
    [void]$lines.Add("# Catalog Promotion Report")
    [void]$lines.Add("")
    [void]$lines.Add("Generated from `$ManifestPath`. This report is informational and does not publish or rebuild a release.")
    [void]$lines.Add("")
    [void]$lines.Add("## Summary")
    [void]$lines.Add("")
    [void]$lines.Add(("- Total manifest items: {0}" -f $rows.Count))
    foreach ($count in $counts) { [void]$lines.Add(("- {0}: {1}" -f $count.Name, $count.Count)) }
    [void]$lines.Add(("- Policy issues: {0}" -f $issues.Count))
    [void]$lines.Add("")
    [void]$lines.Add("## Items")
    [void]$lines.Add("")
    [void]$lines.Add("| name | old type | new downloadMode | promoted | reason | checksum source | source trust | legal/licensing note | follow-up needed |")
    [void]$lines.Add("|---|---:|---|---|---|---|---|---|---|")
    foreach ($row in $rows) {
        $line = "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
            ([string]$row.Name).Replace("|", "\|"),
            ([string]$row.Type).Replace("|", "\|"),
            ([string]$row.DownloadMode).Replace("|", "\|"),
            ([string]$row.Promoted).Replace("|", "\|"),
            ([string]$row.Reason).Replace("|", "\|"),
            ([string]$row.ChecksumSource).Replace("|", "\|"),
            ([string]$row.SourceTrust).Replace("|", "\|"),
            ([string]$row.LegalNote).Replace("|", "\|"),
            ([string]$row.FollowUp).Replace("|", "\|")
        [void]$lines.Add($line)
    }
    Set-Content -LiteralPath $reportFullPath -Value $lines -Encoding UTF8
    Write-Host ("Report written: " + $reportFullPath)
}

if ($issues.Count -gt 0) {
    exit 1
}

exit 0
