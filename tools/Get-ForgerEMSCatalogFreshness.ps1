#requires -Version 5.1

<#
.SYNOPSIS
Read-only catalog freshness audit for ForgerEMS managed downloads.

.DESCRIPTION
Reads the managed-download manifest and emits a freshness audit report
showing each file entry's currentPinnedVersion vs latestKnownStableVersion,
freshnessStatus classification, update channel, and whether the entry
requires manual review.

This helper is READ-ONLY. It does NOT modify the manifest, does NOT promote
new versions, does NOT auto-upgrade anything. It is a status pane the
technician consults before deciding which entries to advance in the next
managed-download promotion batch.

Live verification (-Online switch) is intentionally limited to GitHub
releases and vendor pages where the upstreamReleaseType is well-known and
the response is small. Live mode never writes back into the manifest;
results are printed only.

.PARAMETER ManifestPath
Path to ForgerEMS.updates.json. Defaults to ../manifests/ForgerEMS.updates.json
relative to the script.

.PARAMETER Online
When set, performs a single HEAD/GET against the recorded checksum URL of
each file entry to confirm the resource is still reachable. Does not
re-verify hash bytes. Use Add-ManagedDownloadCandidate.ps1 -PinSha256 for
that.

.PARAMETER Format
Output format. 'table' (default) emits a human-readable summary. 'json'
emits a structured object per file entry for downstream tooling.

.EXAMPLE
.\Get-ForgerEMSCatalogFreshness.ps1

.EXAMPLE
.\Get-ForgerEMSCatalogFreshness.ps1 -Format json | Out-File freshness.json

.EXAMPLE
.\Get-ForgerEMSCatalogFreshness.ps1 -Online
#>

[CmdletBinding()]
param(
    [string]$ManifestPath = (Join-Path $PSScriptRoot "..\manifests\ForgerEMS.updates.json"),
    [switch]$Online,
    [ValidateSet("table", "json")][string]$Format = "table"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$items = @($manifest.items | Where-Object { $_.type -eq "file" })
if ($items.Count -eq 0) {
    Write-Host "No managed file entries found."
    return
}

# Severity ranking for sorting: review-required first, then update-available,
# then up-to-date.
function Get-FreshnessSeverity {
    param([string]$Status)
    switch ($Status) {
        "UpdateUnsafe"                    { return 0 }
        "VendorWorkflowChanged"           { return 1 }
        "SourceChanged"                   { return 2 }
        "ChecksumVerificationRequired"    { return 3 }
        "MajorUpdateAvailable"            { return 4 }
        "ManualReviewRequired"            { return 5 }
        "MinorUpdateAvailable"            { return 6 }
        "PatchUpdateAvailable"            { return 7 }
        "LegacyPinned"                    { return 8 }
        "UpToDate"                        { return 9 }
        default                           { return 99 }
    }
}

function Test-FreshnessChecksumUrlReachable {
    param([string]$Url)
    if ([string]::IsNullOrWhiteSpace($Url)) { return [PSCustomObject]@{ Reachable = $false; Note = "no sha256Url" } }
    try {
        $resp = Invoke-WebRequest -Uri $Url -Method Head -TimeoutSec 20 -UseBasicParsing -ErrorAction Stop
        return [PSCustomObject]@{ Reachable = $true; Note = "HTTP $($resp.StatusCode)" }
    }
    catch {
        return [PSCustomObject]@{ Reachable = $false; Note = $_.Exception.Message }
    }
}

$rows = foreach ($it in $items) {
    $f = $it.freshness
    if ($null -eq $f) {
        # Missing freshness metadata is treated as ManualReviewRequired so old
        # entries never appear deceptively up-to-date.
        $f = [PSCustomObject]@{
            currentPinnedVersion         = ""
            latestKnownStableVersion     = ""
            lastFreshnessAuditUtc        = ""
            freshnessStatus              = "ManualReviewRequired"
            updateChannel                = "manual-only"
            requiresManualReview         = $true
            majorVersionBoundary         = $false
            checksumVerificationMode     = "unverified"
            upstreamReleaseType          = "manual"
            lastKnownChecksumSource      = ""
            updateRecommendation         = "No freshness block in manifest; treat as manual review."
        }
    }

    $online = $null
    if ($Online.IsPresent) {
        $online = Test-FreshnessChecksumUrlReachable -Url $it.sha256Url
    }

    [PSCustomObject]@{
        Rank                        = $it.maintenanceRank
        Name                        = $it.name
        Pinned                      = $f.currentPinnedVersion
        LatestStable                = $f.latestKnownStableVersion
        Status                      = $f.freshnessStatus
        Channel                     = $f.updateChannel
        ManualReview                = $f.requiresManualReview
        MajorBoundary               = $f.majorVersionBoundary
        ChecksumMode                = $f.checksumVerificationMode
        UpstreamType                = $f.upstreamReleaseType
        LastAuditUtc                = $f.lastFreshnessAuditUtc
        Recommendation              = $f.updateRecommendation
        OnlineChecksumUrlReachable  = if ($Online.IsPresent) { $online.Reachable } else { $null }
        OnlineNote                  = if ($Online.IsPresent) { $online.Note } else { "" }
        Severity                    = Get-FreshnessSeverity -Status $f.freshnessStatus
    }
}

$rows = $rows | Sort-Object Severity, Rank

if ($Format -eq "json") {
    $rows | ConvertTo-Json -Depth 5
    return
}

Write-Host ""
Write-Host "ForgerEMS Catalog Freshness Audit" -ForegroundColor Cyan
Write-Host ("Manifest: {0}" -f (Resolve-Path -LiteralPath $ManifestPath))
Write-Host ("Managed file entries: {0}" -f $rows.Count)
Write-Host ""

$rows |
    Select-Object Rank, Name, Pinned, LatestStable, Status, Channel, ManualReview, ChecksumMode |
    Format-Table -AutoSize | Out-Host

Write-Host ""
Write-Host "Per-status counts:" -ForegroundColor Cyan
$rows | Group-Object Status | ForEach-Object {
    Write-Host ("  {0,-32} {1}" -f $_.Name, $_.Count)
}

if ($Online.IsPresent) {
    Write-Host ""
    Write-Host "Online reachability (checksum URL HEAD):" -ForegroundColor Cyan
    $rows | ForEach-Object {
        $flag = if ($_.OnlineChecksumUrlReachable) { "OK  " } else { "FAIL" }
        Write-Host ("  [{0}] r{1,2} {2} -- {3}" -f $flag, $_.Rank, $_.Name, $_.OnlineNote)
    }
}

Write-Host ""
Write-Host "Reminder: this audit is read-only. ForgerEMS does NOT auto-upgrade." -ForegroundColor Yellow
Write-Host "Use tools/Add-ManagedDownloadCandidate.ps1 to verify a specific upstream candidate." -ForegroundColor Yellow
