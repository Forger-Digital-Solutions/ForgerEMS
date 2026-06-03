#requires -Version 5.1

<#
.SYNOPSIS
Verify a candidate managed-download promotion and emit a ready-to-paste
manifest fragment.

.DESCRIPTION
Operator helper that bridges the audit doc (`docs/CATALOG-PROMOTION-AUDIT.md`)
and the live manifest. It:

  1. Reachability-checks the proposed binary URL (HEAD request).
  2. Fetches the proposed checksum URL and runs it through the same
     filename-aware resolver the toolkit-health pipeline uses.
  3. If a pinned `sha256` is required (PROMOTE-B / -C path), downloads the
     binary to a temp file, computes its SHA-256, and confirms it matches the
     resolved hash from the vendor checksum file.
  4. Prints a JSON manifest fragment containing the verified URL, checksum
     coverage, and the technician metadata fields. The operator pastes the
     fragment into `manifests/ForgerEMS.updates.json` (and adjusts
     `maintenanceRank` to the next free integer).

NOTHING IS WRITTEN TO THE MANIFEST AUTOMATICALLY. The operator is the final
human-in-the-loop step.

.PARAMETER Name
Manifest entry name (e.g., "Ubuntu Server 24.04.4 LTS (amd64)").

.PARAMETER Destination
USB-relative destination path (e.g., "ISO\Linux\ubuntu-24.04.4-live-server-amd64.iso").

.PARAMETER DownloadUrl
Direct vendor URL to the binary artifact.

.PARAMETER ChecksumUrl
Vendor URL of the checksum file. May be a single-hash `.sha256` companion or a
multi-line SHA256SUMS / BSD-format file.

.PARAMETER SourceType
One of: sourceforge | github-release | official-mirror | official-version-path.

.PARAMETER FragilityLevel
One of: low | medium | high. Be honest.

.PARAMETER FallbackRule
Plain-English fallback instructions for the next operator if this URL breaks.

.PARAMETER PinSha256
When set, the script computes the binary's actual SHA-256 by downloading it,
verifies it matches the vendor's published hash, and emits both `sha256` and
`sha256Url` in the fragment. Use this for PROMOTE-B candidates where the
vendor publishes a combined SHA256SUMS file that contains multiple hashes.

.PARAMETER Kind / Family / OsCategory / RecommendedUse / LicenseNote / SourceTrust / Architecture / BootMode
Catalog metadata, all optional. Forwarded into the fragment.

.EXAMPLE
.\Add-ManagedDownloadCandidate.ps1 `
    -Name "Alpine Linux 3.20.0 Standard (x86_64)" `
    -Destination "ISO\Linux\alpine-standard-3.20.0-x86_64.iso" `
    -DownloadUrl "https://dl-cdn.alpinelinux.org/alpine/v3.20/releases/x86_64/alpine-standard-3.20.0-x86_64.iso" `
    -ChecksumUrl "https://dl-cdn.alpinelinux.org/alpine/v3.20/releases/x86_64/alpine-standard-3.20.0-x86_64.iso.sha256" `
    -SourceType official-mirror -FragilityLevel medium `
    -FallbackRule "Use the official Alpine release index for the same v3.20 point release." `
    -Kind os -Family Linux -OsCategory Server `
    -RecommendedUse "Tiny musl-libc Linux for containers." `
    -LicenseNote "Free / open source." -SourceTrust official
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Name,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$DownloadUrl,
    [Parameter(Mandatory)][string]$ChecksumUrl,
    [ValidateSet("sourceforge", "github-release", "official-mirror", "official-version-path")]
    [string]$SourceType = "official-mirror",
    [ValidateSet("low", "medium", "high")][string]$FragilityLevel = "medium",
    [string]$FallbackRule = "Use the vendor's official release page to confirm the same versioned artifact and refresh checksum coverage before patching.",
    [switch]$PinSha256,

    [string]$Kind = "",
    [string]$Family = "",
    [string]$OsCategory = "",
    [string]$Architecture = "",
    [string[]]$BootMode = @(),
    [string]$RecommendedUse = "",
    [string]$TechnicianNotes = "",
    [string]$LicenseNote = "Free / open source.",
    [string]$SecureBootNote = "",
    [ValidateSet("official", "community", "manual")][string]$SourceTrust = "official",
    [string]$Notes = "Promotion candidate. Verify URL and checksum then set enabled:true."
)

$ErrorActionPreference = "Stop"

$resolverPath = Join-Path $PSScriptRoot "..\backend\ToolkitManager\ChecksumResolver.ps1"
$resolverPath = [IO.Path]::GetFullPath($resolverPath)
if (-not (Test-Path -LiteralPath $resolverPath -PathType Leaf)) {
    throw "ChecksumResolver.ps1 not found at $resolverPath. Run from a repo checkout."
}
. $resolverPath

function Write-Status {
    param([string]$Message, [ValidateSet("INFO", "OK", "WARN", "FAIL")][string]$Level = "INFO")
    Write-Host ("[{0}] {1}" -f $Level, $Message)
}

function Assert-HttpsUrl {
    param([string]$Url, [string]$Label)
    $uri = $null
    if (-not [Uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne "https") {
        throw "$Label must be an absolute https:// URL. Got: $Url"
    }
}

Assert-HttpsUrl -Url $DownloadUrl -Label "DownloadUrl"
Assert-HttpsUrl -Url $ChecksumUrl -Label "ChecksumUrl"

if ([IO.Path]::IsPathRooted($Destination) -or $Destination.Contains("..")) {
    throw "Destination must be a USB-relative path without `..` components."
}

$targetFileName = [IO.Path]::GetFileName($Destination)
if ([string]::IsNullOrWhiteSpace($targetFileName)) {
    throw "Destination must end with a filename."
}

Write-Status "Verifying candidate '$Name'"
Write-Status "  Destination basename: $targetFileName"

# --- Step 1: HEAD the binary URL ----------------------------------------------
try {
    Write-Status "HEAD $DownloadUrl"
    $head = Invoke-WebRequest -Uri $DownloadUrl -Method Head -TimeoutSec 30 -UseBasicParsing -ErrorAction Stop
    $size = $head.Headers["Content-Length"]
    Write-Status ("  Reachable. Content-Length: {0} ContentType: {1}" -f $size, $head.Headers["Content-Type"]) "OK"
}
catch {
    Write-Status "DownloadUrl HEAD failed: $($_.Exception.Message)" "FAIL"
    throw
}

# --- Step 2: fetch + parse the checksum file ----------------------------------
Write-Status "Fetching checksum file..."
try {
    $response = Invoke-WebRequest -Uri $ChecksumUrl -TimeoutSec 45 -UseBasicParsing -ErrorAction Stop
    $checksumContent = Convert-ChecksumResponseContentToString -Content $response.Content
    Write-Status ("  Got {0} bytes" -f $checksumContent.Length) "OK"
}
catch {
    Write-Status "ChecksumUrl fetch failed: $($_.Exception.Message)" "FAIL"
    throw
}

$resolution = Resolve-ChecksumFromChecksumText -Content $checksumContent -TargetFileName $targetFileName
Write-Status ("Resolver: reason={0} format={1} candidates={2}" -f $resolution.Reason, $resolution.SourceFormat, $resolution.Candidates)
if ($resolution.MatchedLine) {
    Write-Status ("  Matched line: {0}" -f $resolution.MatchedLine.Trim())
}

if ([string]::IsNullOrWhiteSpace($resolution.Hash)) {
    Write-Status "Resolver could not extract a SHA-256 for '$targetFileName'. Promotion blocked." "FAIL"
    throw "Checksum resolution failed: $($resolution.Reason)"
}

Write-Status ("Vendor-published SHA-256: {0}" -f $resolution.Hash) "OK"

# --- Step 3 (optional): pin hash by downloading and recomputing ---------------
$computedHash = ""
if ($PinSha256.IsPresent) {
    Write-Status "PinSha256: downloading binary to verify hash..."
    $tempFile = [IO.Path]::Combine([IO.Path]::GetTempPath(), "forgerems-promote-" + [Guid]::NewGuid().ToString("N") + ".bin")
    try {
        Invoke-WebRequest -Uri $DownloadUrl -OutFile $tempFile -TimeoutSec 1800 -UseBasicParsing -ErrorAction Stop
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $stream = [IO.File]::OpenRead($tempFile)
            try {
                $bytes = $sha256.ComputeHash($stream)
            }
            finally { $stream.Dispose() }
        }
        finally { $sha256.Dispose() }
        $computedHash = -join ($bytes | ForEach-Object { $_.ToString("x2") })
        Write-Status ("  Computed SHA-256: $computedHash") "OK"

        if ($computedHash -ne $resolution.Hash) {
            Write-Status "Vendor hash does not match computed hash. Promotion blocked." "FAIL"
            throw "Hash mismatch: vendor=$($resolution.Hash) computed=$computedHash"
        }
        Write-Status "  Pinned hash verified against downloaded binary." "OK"
    }
    finally {
        if (Test-Path -LiteralPath $tempFile) { Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue }
    }
}

# --- Step 4: emit a manifest fragment ----------------------------------------
$fragment = [ordered]@{
    name           = $Name
    type           = "file"
    dest           = $Destination
    url            = $DownloadUrl
}

if ($PinSha256.IsPresent) {
    $fragment.sha256 = $computedHash
}
$fragment.sha256Url      = $ChecksumUrl
$fragment.sourceType     = $SourceType
$fragment.fragilityLevel = $FragilityLevel
$fragment.fallbackRule   = $FallbackRule
$fragment.maintenanceRank = "<<NEXT_FREE_RANK>>"
$fragment.enabled        = $false
$fragment.archive        = $true

if ($Kind)             { $fragment.kind            = $Kind }
if ($Family)           { $fragment.family          = $Family }
if ($OsCategory)       { $fragment.osCategory      = $OsCategory }
if ($Architecture)     { $fragment.architecture    = $Architecture }
if ($BootMode.Count -gt 0) { $fragment.bootMode    = $BootMode }
if ($RecommendedUse)   { $fragment.recommendedUse  = $RecommendedUse }
if ($TechnicianNotes)  { $fragment.technicianNotes = $TechnicianNotes }
if ($LicenseNote)      { $fragment.licenseNote     = $LicenseNote }
if ($SecureBootNote)   { $fragment.secureBootNote  = $SecureBootNote }
$fragment.sourceTrust    = $SourceTrust
$fragment.notes          = $Notes

Write-Status "Verification complete. Manifest fragment follows (enabled:false; bump maintenanceRank to next free integer before pasting):" "OK"
Write-Host ""
$json = $fragment | ConvertTo-Json -Depth 6
# Indent each line by 4 spaces to match manifest style.
($json -split "(?:\r\n|\n|\r)") | ForEach-Object { "    $_" } | Write-Host
Write-Host ""
Write-Status "Once pasted: bump maintenanceRank, set enabled:true after a second human-in-the-loop check, then run dotnet test + Verify-VentoyCore -Online." "INFO"
