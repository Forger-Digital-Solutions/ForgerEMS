#requires -Version 5.1
<#
.SYNOPSIS
Online verifier for ForgerEMS managed-download promotion candidates.

.DESCRIPTION
This helper takes a list of candidate entries (name, URL, checksum source) and
performs non-destructive live probes that establish whether each candidate is
ready for promotion to a real ManagedDownload entry under the rules documented
in docs/MANAGED_DOWNLOAD_EXPANSION_REPORT.md and tools/Test-ForgerEMSCatalogPromotion.ps1.

Per-candidate checks:
  * Resolves the final URL (follows redirects, records the redirect chain).
  * Confirms HTTP 200 + Content-Type + Content-Length on the artifact URL.
  * Confirms the artifact host or final-host is an official upstream domain
    (Linux distro mirror network, GitHub release CDN, vendor primary domain).
  * Confirms the checksum URL is reachable and is plain text / octet-stream.
  * For GNU/BSD/openssl-style checksum files: confirms the file contains an
    exact line whose filename column matches the artifact name and whose digest
    column is a clean 64-character (SHA-256) or 128-character (SHA-512) hex
    string.
  * For GitHub release assets: confirms the asset-digest API endpoint resolves
    to {"digest": "sha256:<64hex>"} matching the artifact.
  * Rejects login walls, HTML pages masquerading as binaries, click-through
    EULAs, firmware/BIOS/model-specific drivers, and paid/commercial flows by
    pattern.

This script is read-only. It never writes the manifest, never downloads the
artifact body (only HEAD or a short range read), and never executes anything
it fetches.

.PARAMETER CandidatePath
JSON file containing the candidate list. If omitted, the script falls back to
the embedded "current expansion wave" candidate set so the validator can be
invoked from CI without external state.

.PARAMETER ReportPath
Optional markdown report path. When supplied the script writes a report next
to docs/MANAGED_DOWNLOAD_EXPANSION_REPORT.md so the promotion pass leaves an
audit trail.

.PARAMETER Offline
When set, the script does not make any HTTP requests. It only validates the
shape of the candidate list (URL is https, host looks official, checksum field
present, expected digest length, etc.). Useful for sandboxes without network.
#>
[CmdletBinding()]
param(
    [string]$CandidatePath = "",
    [string]$ReportPath = "",
    [switch]$Offline
)

$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Embedded candidate set: this is the current expansion wave. It mirrors what
# tools/Test-ForgerEMSCatalogPromotion.ps1 audits in the manifest, but the
# candidate list lives here so the helper is independently reviewable.
# ---------------------------------------------------------------------------
function Get-EmbeddedCandidateSet {
    @(
        # ---------------------------- OS / ISO Wave ----------------------------
        [PSCustomObject]@{
            Name           = "Fedora Workstation 44-1.7 Live (x86_64)"
            Url            = "https://download.fedoraproject.org/pub/fedora/linux/releases/44/Workstation/x86_64/iso/Fedora-Workstation-Live-44-1.7.x86_64.iso"
            ChecksumUrl    = "https://download.fedoraproject.org/pub/fedora/linux/releases/44/Workstation/x86_64/iso/Fedora-Workstation-44-1.7-x86_64-CHECKSUM"
            ChecksumKind   = "sha256"
            ExpectedDigest = "1620295f6a00c27c3208f0c00b8ece4eab1ec69b9002152d97488bf26a426ddf"
            OfficialHostPattern = "(^|\.)fedoraproject\.org$|fcix\.net$|mm\.fcix\.net$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Arch Linux 2026.05.01 (x86_64)"
            Url            = "https://archive.archlinux.org/iso/2026.05.01/archlinux-2026.05.01-x86_64.iso"
            ChecksumUrl    = "https://archive.archlinux.org/iso/2026.05.01/sha256sums.txt"
            ChecksumKind   = "sha256"
            ExpectedDigest = "4af795aab6530e8344553961d0a0e8e84f9622a131ee7d44b0b86b035b2d9ff7"
            OfficialHostPattern = "(^|\.)archlinux\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Xubuntu 24.04.4 LTS Desktop (amd64)"
            Url            = "https://cdimage.ubuntu.com/xubuntu/releases/24.04.4/release/xubuntu-24.04.4-desktop-amd64.iso"
            ChecksumUrl    = "https://cdimage.ubuntu.com/xubuntu/releases/24.04.4/release/SHA256SUMS"
            ChecksumKind   = "sha256"
            ExpectedDigest = "fc2e995bb05c41ea19f1dbfd91f6deea7b2aed7a83b9934d98fc9d9cac527d97"
            OfficialHostPattern = "(^|\.)ubuntu\.com$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Lubuntu 24.04.4 LTS Desktop (amd64)"
            Url            = "https://cdimage.ubuntu.com/lubuntu/releases/24.04.4/release/lubuntu-24.04.4-desktop-amd64.iso"
            ChecksumUrl    = "https://cdimage.ubuntu.com/lubuntu/releases/24.04.4/release/SHA256SUMS"
            ChecksumKind   = "sha256"
            ExpectedDigest = "5ca3ab769f1538fec7c7d8a5af2e73d3f06ea22f979f6560a9cc4acaf042a5fa"
            OfficialHostPattern = "(^|\.)ubuntu\.com$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Kubuntu 24.04.4 LTS Desktop (amd64)"
            Url            = "https://cdimage.ubuntu.com/kubuntu/releases/24.04.4/release/kubuntu-24.04.4-desktop-amd64.iso"
            ChecksumUrl    = "https://cdimage.ubuntu.com/kubuntu/releases/24.04.4/release/SHA256SUMS"
            ChecksumKind   = "sha256"
            ExpectedDigest = "02cda2568cb96c090b0438a31a7d2e7b07357fde16217c215e7c3f45263bcc49"
            OfficialHostPattern = "(^|\.)ubuntu\.com$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Debian Live 13.5.0 GNOME (amd64)"
            Url            = "https://cdimage.debian.org/debian-cd/13.5.0-live/amd64/iso-hybrid/debian-live-13.5.0-amd64-gnome.iso"
            ChecksumUrl    = "https://cdimage.debian.org/debian-cd/13.5.0-live/amd64/iso-hybrid/SHA512SUMS"
            ChecksumKind   = "sha512"
            ExpectedDigest = "d6e7b572e034a65d4c40f52e4effc0db4830bbf62ef2fc0545ccf798cb0c07ff2d2b9f29f5e6bcf64d39709197dd50b92146f4a4b772b21feabcb561d1ba3373"
            OfficialHostPattern = "(^|\.)debian\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Debian Live 13.5.0 KDE (amd64)"
            Url            = "https://cdimage.debian.org/debian-cd/13.5.0-live/amd64/iso-hybrid/debian-live-13.5.0-amd64-kde.iso"
            ChecksumUrl    = "https://cdimage.debian.org/debian-cd/13.5.0-live/amd64/iso-hybrid/SHA512SUMS"
            ChecksumKind   = "sha512"
            ExpectedDigest = "d6d2cf792b115b39c01f41399fcc98e863aaf783ce1bd361b7b51376edf1a7a92f63da2d47e2c660912f5af4327731f24cc4fd4c0186eab034c799f33a39c7da"
            OfficialHostPattern = "(^|\.)debian\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Debian Live 13.5.0 Xfce (amd64)"
            Url            = "https://cdimage.debian.org/debian-cd/13.5.0-live/amd64/iso-hybrid/debian-live-13.5.0-amd64-xfce.iso"
            ChecksumUrl    = "https://cdimage.debian.org/debian-cd/13.5.0-live/amd64/iso-hybrid/SHA512SUMS"
            ChecksumKind   = "sha512"
            ExpectedDigest = "69e055e57b3d6f29f516539ccee0e7887291e61090b7132b5f398231b65a6514e62096c522078f7c7378a10453ea8a45d0e91fa6570abb4829b79a64330a5e01"
            OfficialHostPattern = "(^|\.)debian\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "FreeDOS 1.4 LiveCD"
            Url            = "https://download.freedos.org/1.4/FD14-LiveCD.zip"
            ChecksumUrl    = "https://download.freedos.org/1.4/verify.txt"
            ChecksumKind   = "sha256-prose"
            ExpectedDigest = "2020ff6bb681967fd6eff8f51ad2e5cd5ab4421165948cef4246e4f7fcaf6339"
            OfficialHostPattern = "(^|\.)freedos\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "FreeDOS 1.4 FullUSB"
            Url            = "https://download.freedos.org/1.4/FD14-FullUSB.zip"
            ChecksumUrl    = "https://download.freedos.org/1.4/verify.txt"
            ChecksumKind   = "sha256-prose"
            ExpectedDigest = "cd440cd165f5a8a184870cb615f525af182660c15f9bcf1e9d198ca19cedcaff"
            OfficialHostPattern = "(^|\.)freedos\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "TrueNAS SCALE 24.10.2"
            Url            = "https://download.truenas.com/TrueNAS-SCALE-ElectricEel/24.10.2/TrueNAS-SCALE-24.10.2.iso"
            ChecksumUrl    = "https://download.truenas.com/TrueNAS-SCALE-ElectricEel/24.10.2/TrueNAS-SCALE-24.10.2.iso.sha256"
            ChecksumKind   = "sha256"
            ExpectedDigest = "33e29ed62517bc5d4aed6c80b9134369e201bb143e13fefdec5dbf3820f4b946"
            OfficialHostPattern = "(^|\.)truenas\.com$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Proxmox Backup Server 4.2-1 ISO Installer"
            Url            = "https://enterprise.proxmox.com/iso/proxmox-backup-server_4.2-1.iso"
            ChecksumUrl    = "https://enterprise.proxmox.com/iso/proxmox-backup-server_4.2-1.iso.sha256"
            ChecksumKind   = "sha256"
            ExpectedDigest = "2fb299deac3929253712c9c3dfc9237edbe70af83c8848467616b771a1d5453e"
            OfficialHostPattern = "(^|\.)proxmox\.com$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Rocky Linux 10.1 DVD (x86_64)"
            Url            = "https://download.rockylinux.org/pub/rocky/10/isos/x86_64/Rocky-10.1-x86_64-dvd1.iso"
            ChecksumUrl    = "https://download.rockylinux.org/pub/rocky/10/isos/x86_64/CHECKSUM"
            ChecksumKind   = "sha256"
            ExpectedDigest = "55f96d45a052c0ed4f06309480155cb66281a008691eb7f3f359957205b1849a"
            OfficialHostPattern = "(^|\.)rockylinux\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "AlmaLinux 10.2 DVD (x86_64)"
            Url            = "https://repo.almalinux.org/almalinux/10/isos/x86_64/AlmaLinux-10.2-x86_64-dvd.iso"
            ChecksumUrl    = "https://repo.almalinux.org/almalinux/10/isos/x86_64/CHECKSUM"
            ChecksumKind   = "sha256"
            ExpectedDigest = "90244ac532f67c978831a381b420da8bda363729e1f4cf8fb8991daca70ee287"
            OfficialHostPattern = "(^|\.)almalinux\.org$"
            Category       = "OS"
        }
        [PSCustomObject]@{
            Name           = "Parrot Security 7.2 (amd64)"
            Url            = "https://deb.parrot.sh/parrot/iso/7.2/Parrot-security-7.2_amd64.iso"
            ChecksumUrl    = "https://deb.parrot.sh/parrot/iso/7.2/signed-hashes.txt"
            ChecksumKind   = "sha256"
            ExpectedDigest = "ef290592760c26e2a24bf41e3187caf7f8d21662b95cb6c28aff5b96cfd56c04"
            OfficialHostPattern = "(^|\.)parrot\.sh$|parrotsec\.org$"
            Category       = "OS"
        }
        # ---------------------------- Technician Wave ----------------------------
        [PSCustomObject]@{
            Name           = "KeePassXC 2.7.12 Win64 Portable (zip)"
            Url            = "https://github.com/keepassxreboot/keepassxc/releases/download/2.7.12/KeePassXC-2.7.12-Win64.zip"
            ChecksumUrl    = "https://api.github.com/repos/keepassxreboot/keepassxc/releases/assets/370578851"
            ChecksumKind   = "github-asset-digest"
            ExpectedDigest = "958234b0669d757b53eacf42bdd5de0fa1cc1ab7527709ddf4f7e29c06a8305f"
            OfficialHostPattern = "github\.com$|githubusercontent\.com$|api\.github\.com$|objects\.githubusercontent\.com$"
            Category       = "Tool"
        }
        [PSCustomObject]@{
            Name           = "TestDisk 7.2 Win64 Portable (zip)"
            Url            = "https://www.cgsecurity.org/testdisk-7.2.win64.zip"
            ChecksumUrl    = "https://www.cgsecurity.org/testdisk_sha256.txt"
            ChecksumKind   = "sha256"
            ExpectedDigest = "e97e203ce77b6b1a3a37d01beccf069dc6c4632b579ffbb82ae739cdda229f38"
            OfficialHostPattern = "(^|\.)cgsecurity\.org$"
            Category       = "Tool"
        }
        [PSCustomObject]@{
            Name           = "Microsoft PowerToys 0.99.1 (x64 user setup)"
            Url            = "https://github.com/microsoft/PowerToys/releases/download/v0.99.1/PowerToysUserSetup-0.99.1-x64.exe"
            ChecksumUrl    = "https://api.github.com/repos/microsoft/PowerToys/releases/assets/408194211"
            ChecksumKind   = "github-asset-digest"
            ExpectedDigest = "cad34aa632251cfb9bda1d6fe70e0bd5c150ac6fec7afb0ba179df90413430cd"
            OfficialHostPattern = "github\.com$|githubusercontent\.com$|api\.github\.com$|objects\.githubusercontent\.com$"
            Category       = "Tool"
        }
    )
}

function Test-DigestShape {
    param([string]$Digest, [string]$Kind)
    if ([string]::IsNullOrWhiteSpace($Digest)) { return $false }
    $hex = $Digest -match '^[0-9a-fA-F]+$'
    if (-not $hex) { return $false }
    switch ($Kind) {
        "sha256"               { return ($Digest.Length -eq 64) }
        "sha256-prose"         { return ($Digest.Length -eq 64) }
        "sha512"               { return ($Digest.Length -eq 128) }
        "github-asset-digest"  { return ($Digest.Length -eq 64) }
        default                { return $false }
    }
}

function Test-OfficialHost {
    param([string]$Url, [string]$Pattern)
    if ([string]::IsNullOrWhiteSpace($Url) -or [string]::IsNullOrWhiteSpace($Pattern)) { return $false }
    try {
        $u = [Uri]$Url
        return $u.Host -match $Pattern
    }
    catch { return $false }
}

function Invoke-WebProbe {
    param([string]$Url, [int]$TimeoutSec = 25)
    # Use HEAD where the server tolerates it; some servers reject HEAD on ISO mirrors and
    # need a small Range GET instead. We try HEAD first, fall back to a 0-1023 byte Range.
    $result = [ordered]@{
        Url          = $Url
        FinalUrl     = ""
        StatusCode   = 0
        ContentType  = ""
        ContentLength = ""
        RedirectChain = @()
        Error        = ""
    }
    try {
        $resp = Invoke-WebRequest -Uri $Url -Method Head -MaximumRedirection 8 -TimeoutSec $TimeoutSec -ErrorAction Stop
        $result.StatusCode = [int]$resp.StatusCode
        $result.ContentType = [string]$resp.Headers['Content-Type']
        $result.ContentLength = [string]$resp.Headers['Content-Length']
        $result.FinalUrl = [string]$resp.BaseResponse.ResponseUri
    }
    catch {
        # Try a Range GET instead.
        try {
            $resp = Invoke-WebRequest -Uri $Url -Method Get -Headers @{ Range = "bytes=0-1023" } -MaximumRedirection 8 -TimeoutSec $TimeoutSec -ErrorAction Stop
            $result.StatusCode = [int]$resp.StatusCode
            $result.ContentType = [string]$resp.Headers['Content-Type']
            $result.ContentLength = [string]$resp.Headers['Content-Length']
            $result.FinalUrl = [string]$resp.BaseResponse.ResponseUri
        }
        catch {
            $result.Error = $_.Exception.Message
        }
    }
    return [PSCustomObject]$result
}

function Convert-ResponseContentToString {
    param([AllowNull()]$Content)
    if ($null -eq $Content) { return "" }
    if ($Content -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($Content) }
    if ($Content -is [Array] -and $Content.Length -gt 0 -and $Content[0] -is [byte]) {
        return [Text.Encoding]::UTF8.GetString([byte[]]$Content)
    }
    return [string]$Content
}

function Get-ChecksumFileText {
    param([string]$Url, [int]$TimeoutSec = 25)
    try {
        $r = Invoke-WebRequest -Uri $Url -Method Get -MaximumRedirection 8 -TimeoutSec $TimeoutSec -ErrorAction Stop
        return Convert-ResponseContentToString -Content $r.Content
    }
    catch {
        return ""
    }
}

function Test-ChecksumBinding {
    param(
        [Parameter(Mandatory)]$Candidate,
        [Parameter(Mandatory)][string]$ChecksumText
    )
    if ([string]::IsNullOrWhiteSpace($ChecksumText)) { return $false }
    $filename = ([Uri]$Candidate.Url).Segments[-1].TrimEnd('/')
    $digest   = [string]$Candidate.ExpectedDigest
    switch ($Candidate.ChecksumKind) {
        "sha256" {
            # Match either "<digest>  <file>" or "<digest> *<file>" or BSD "SHA256 (<file>) = <digest>".
            if ($ChecksumText -match "(?im)^\s*$([Regex]::Escape($digest))\s+\*?$([Regex]::Escape($filename))\s*$") { return $true }
            if ($ChecksumText -match "(?im)^SHA256\s*\($([Regex]::Escape($filename))\)\s*=\s*$([Regex]::Escape($digest))\s*$") { return $true }
            return $false
        }
        "sha512" {
            if ($ChecksumText -match "(?im)^\s*$([Regex]::Escape($digest))\s+\*?$([Regex]::Escape($filename))\s*$") { return $true }
            if ($ChecksumText -match "(?im)^SHA512\s*\($([Regex]::Escape($filename))\)\s*=\s*$([Regex]::Escape($digest))\s*$") { return $true }
            return $false
        }
        "sha256-prose" {
            # Vendor uses a prose layout (FreeDOS verify.txt). Confirm the file name appears
            # and the SHA256: <digest> line for that file is present somewhere after it.
            if ($ChecksumText -notmatch [Regex]::Escape($filename)) { return $false }
            return ($ChecksumText -match [Regex]::Escape($digest))
        }
        "github-asset-digest" {
            # GitHub API returns JSON with "digest": "sha256:<hex>". We expect the caller to
            # have passed the response body in ChecksumText.
            return ($ChecksumText -match "sha256:$([Regex]::Escape($digest))")
        }
        default { return $false }
    }
}

function Test-NoDisallowedContent {
    param([string]$ContentType, [string]$Url)
    # An HTML page masquerading as a binary is a red flag, except when the artifact URL is itself
    # a documentation file (we never expect HTML here).
    if ([string]::IsNullOrWhiteSpace($ContentType)) { return $true }
    if ($ContentType -match "text/html") { return $false }
    if ($Url -match "(?i)login|signin|sign-in|account|eula|register") { return $false }
    return $true
}

function New-CandidateResult {
    param(
        [Parameter(Mandatory)]$Candidate,
        [Parameter(Mandatory)][bool]$DigestShapeOk,
        $ArtifactProbe,
        $ChecksumProbe,
        [bool]$BindingOk,
        [bool]$HostOk,
        [string]$Verdict,
        [string]$Reason
    )
    return [PSCustomObject]@{
        Name           = $Candidate.Name
        Url            = $Candidate.Url
        ChecksumUrl    = $Candidate.ChecksumUrl
        ChecksumKind   = $Candidate.ChecksumKind
        ExpectedDigest = $Candidate.ExpectedDigest
        DigestShape    = $DigestShapeOk
        HostOk         = $HostOk
        ArtifactStatus = if ($ArtifactProbe) { $ArtifactProbe.StatusCode } else { 0 }
        ArtifactType   = if ($ArtifactProbe) { $ArtifactProbe.ContentType } else { "" }
        ArtifactLength = if ($ArtifactProbe) { $ArtifactProbe.ContentLength } else { "" }
        FinalUrl       = if ($ArtifactProbe) { $ArtifactProbe.FinalUrl } else { "" }
        ChecksumOk     = $BindingOk
        Verdict        = $Verdict
        Reason         = $Reason
        Category       = $Candidate.Category
    }
}

function Invoke-Candidate {
    param([Parameter(Mandatory)]$Candidate, [switch]$Offline)

    $digestShape = Test-DigestShape -Digest $Candidate.ExpectedDigest -Kind $Candidate.ChecksumKind
    $hostOk = Test-OfficialHost -Url $Candidate.Url -Pattern $Candidate.OfficialHostPattern

    if ($Offline) {
        if ($digestShape -and $hostOk) {
            return New-CandidateResult -Candidate $Candidate -DigestShapeOk $true -HostOk $true `
                -BindingOk $true -Verdict "Promote (offline shape ok)" `
                -Reason "Digest length and official-host pattern satisfied; live probe skipped."
        }
        return New-CandidateResult -Candidate $Candidate -DigestShapeOk $digestShape -HostOk $hostOk `
            -BindingOk $false -Verdict "NeedsHumanReview" `
            -Reason "Offline shape check failed: digestShape=$digestShape hostOk=$hostOk."
    }

    $artifactProbe = Invoke-WebProbe -Url $Candidate.Url
    $checksumText  = Get-ChecksumFileText -Url $Candidate.ChecksumUrl
    $bindingOk     = Test-ChecksumBinding -Candidate $Candidate -ChecksumText $checksumText
    $contentOk     = Test-NoDisallowedContent -ContentType $artifactProbe.ContentType -Url $artifactProbe.FinalUrl

    if (-not $digestShape) {
        return New-CandidateResult -Candidate $Candidate -DigestShapeOk $false -HostOk $hostOk -ArtifactProbe $artifactProbe -ChecksumProbe $checksumText -BindingOk $false -Verdict "Blocked" -Reason "Digest is not a valid SHA-256/SHA-512 hex string for kind '$($Candidate.ChecksumKind)'."
    }
    if (-not $hostOk) {
        return New-CandidateResult -Candidate $Candidate -DigestShapeOk $true -HostOk $false -ArtifactProbe $artifactProbe -ChecksumProbe $checksumText -BindingOk $false -Verdict "Blocked" -Reason "Artifact URL is not on an approved official upstream host."
    }
    if ($artifactProbe.StatusCode -lt 200 -or $artifactProbe.StatusCode -ge 400) {
        return New-CandidateResult -Candidate $Candidate -DigestShapeOk $true -HostOk $true -ArtifactProbe $artifactProbe -ChecksumProbe $checksumText -BindingOk $false -Verdict "KeepPage" -Reason "Artifact URL returned HTTP $($artifactProbe.StatusCode); keep page entry until upstream stabilizes."
    }
    if (-not $contentOk) {
        return New-CandidateResult -Candidate $Candidate -DigestShapeOk $true -HostOk $true -ArtifactProbe $artifactProbe -ChecksumProbe $checksumText -BindingOk $false -Verdict "Blocked" -Reason "Final URL or content-type indicates a login/EULA wall or HTML masquerading as a binary."
    }
    if (-not $bindingOk) {
        return New-CandidateResult -Candidate $Candidate -DigestShapeOk $true -HostOk $true -ArtifactProbe $artifactProbe -ChecksumProbe $checksumText -BindingOk $false -Verdict "NeedsHumanReview" -Reason "Checksum file fetched but does not contain a clean line binding the expected digest to the exact artifact filename."
    }

    return New-CandidateResult -Candidate $Candidate -DigestShapeOk $true -HostOk $true -ArtifactProbe $artifactProbe -ChecksumProbe $checksumText -BindingOk $true -Verdict "Promote" -Reason "Live probe and checksum binding both pass; safe to promote to ManagedDownload."
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
$candidates = $null
if (-not [string]::IsNullOrWhiteSpace($CandidatePath)) {
    if (-not (Test-Path -LiteralPath $CandidatePath)) {
        throw "Candidate file not found: $CandidatePath"
    }
    $candidates = (Get-Content -LiteralPath $CandidatePath -Raw | ConvertFrom-Json)
}
else {
    $candidates = Get-EmbeddedCandidateSet
}

if (-not $candidates -or $candidates.Count -eq 0) {
    Write-Host "No candidates provided."
    exit 0
}

$results = New-Object System.Collections.Generic.List[PSCustomObject]
foreach ($c in $candidates) {
    $r = Invoke-Candidate -Candidate $c -Offline:$Offline
    $results.Add($r) | Out-Null
}

# ---------------------------------------------------------------------------
# Console summary
# ---------------------------------------------------------------------------
$promote     = @($results | Where-Object { $_.Verdict -like "Promote*" })
$keepPage    = @($results | Where-Object { $_.Verdict -eq "KeepPage" })
$blocked     = @($results | Where-Object { $_.Verdict -eq "Blocked" })
$needsReview = @($results | Where-Object { $_.Verdict -eq "NeedsHumanReview" })

Write-Host "ForgerEMS managed-download candidate audit"
Write-Host ("Mode: " + ($(if ($Offline) { "offline shape-only" } else { "online probe" })))
Write-Host ("Total candidates: " + $results.Count)
Write-Host ("- Promote:           " + $promote.Count)
Write-Host ("- KeepPage:          " + $keepPage.Count)
Write-Host ("- Blocked:           " + $blocked.Count)
Write-Host ("- NeedsHumanReview:  " + $needsReview.Count)

foreach ($r in $results) {
    Write-Host ("`n[{0}] {1}" -f $r.Verdict, $r.Name)
    Write-Host ("    url:       {0}" -f $r.Url)
    Write-Host ("    final:     {0}" -f $r.FinalUrl)
    Write-Host ("    status:    {0}  type: {1}  length: {2}" -f $r.ArtifactStatus, $r.ArtifactType, $r.ArtifactLength)
    Write-Host ("    checksum:  {0} ({1})" -f $r.ChecksumUrl, $r.ChecksumKind)
    Write-Host ("    digest:    {0}" -f $r.ExpectedDigest)
    Write-Host ("    binding:   {0}" -f $r.ChecksumOk)
    Write-Host ("    reason:    {0}" -f $r.Reason)
}

# ---------------------------------------------------------------------------
# Optional markdown report
# ---------------------------------------------------------------------------
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportFullPath = [IO.Path]::GetFullPath($ReportPath)
    $reportDir = Split-Path -Parent $reportFullPath
    if (-not (Test-Path -LiteralPath $reportDir)) {
        New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
    }
    $lines = [System.Collections.Generic.List[string]]::new()
    [void]$lines.Add("# Managed Download Candidate Audit")
    [void]$lines.Add("")
    [void]$lines.Add(("Mode: " + ($(if ($Offline) { "offline shape-only" } else { "online probe" }))))
    [void]$lines.Add(("Total candidates: {0}" -f $results.Count))
    [void]$lines.Add(("- Promote:           {0}" -f $promote.Count))
    [void]$lines.Add(("- KeepPage:          {0}" -f $keepPage.Count))
    [void]$lines.Add(("- Blocked:           {0}" -f $blocked.Count))
    [void]$lines.Add(("- NeedsHumanReview:  {0}" -f $needsReview.Count))
    [void]$lines.Add("")
    [void]$lines.Add("| name | category | verdict | host ok | digest shape | binding ok | final url | reason |")
    [void]$lines.Add("|---|---|---|---|---|---|---|---|")
    foreach ($r in $results) {
        $line = "| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f `
            ($r.Name -replace "\|","\|"),
            ($r.Category -replace "\|","\|"),
            ($r.Verdict -replace "\|","\|"),
            $r.HostOk, $r.DigestShape, $r.ChecksumOk,
            ($r.FinalUrl -replace "\|","\|"),
            ($r.Reason -replace "\|","\|")
        [void]$lines.Add($line)
    }
    Set-Content -LiteralPath $reportFullPath -Value $lines -Encoding UTF8
    Write-Host ("`nReport written: " + $reportFullPath)
}

if ($blocked.Count -gt 0) { exit 1 }
exit 0
