#requires -Version 5.1

# ToolkitHealthCache.ps1
#
# Cached verification helpers for Get-ForgerEMSToolkitHealth.ps1.
#
# The toolkit health scan is dominated by SHA256/SHA512 hashing of large
# installed ISO/image files on the USB. On a populated USB this can take
# several minutes. For a normal "Refresh Health" most of those files have
# not changed since the previous verified scan, so re-hashing is wasted
# work.
#
# This module persists the verification facts for items that came back as a
# fresh Match (or a non-managed status worth caching), keyed by:
#   - sanitized target root
#   - target volume serial number (when discoverable)
#   - manifest content hash (sha256)
# and per item by:
#   - relative path
#   - size bytes
#   - last-write UTC
#   - expected checksum (string)
#   - checksum algorithm
#
# A cache hit returns the previously verified checksum without rehashing.
# A cache MISS for any of those reasons forces a fresh rehash. A -FullVerify
# pass ignores the cache entirely.
#
# The cache itself is a JSON file under the local report root. It is never
# treated as authoritative — losing the cache only costs one slow scan.

$script:ToolkitHealthCacheSchemaVersion = 1

function Get-ToolkitHealthCachePath {
    param([Parameter(Mandatory)][string]$LocalReportRoot)

    return (Join-Path $LocalReportRoot "toolkit-health-cache.json")
}

function Get-ToolkitTargetIdentityKey {
    param([Parameter(Mandatory)][string]$TargetRoot)

    $full = [IO.Path]::GetFullPath($TargetRoot).TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($full)) {
        return ""
    }
    return $full.ToUpperInvariant()
}

function Get-ToolkitTargetVolumeSerial {
    param([Parameter(Mandatory)][string]$TargetRoot)

    try {
        $rootPath = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($TargetRoot))
        if ([string]::IsNullOrWhiteSpace($rootPath)) {
            return ""
        }

        $driveLetter = $rootPath.Substring(0, 1).ToUpperInvariant()
        # Get-Volume is preferred on Windows 8+/Server 2012+ and PS 5.1; fall back to WMI when unavailable.
        try {
            $volume = Get-Volume -DriveLetter $driveLetter -ErrorAction Stop
            if ($null -ne $volume -and $null -ne $volume.UniqueId) {
                return [string]$volume.UniqueId
            }
        }
        catch {
            # Ignored; fall back to CIM.
        }

        try {
            $cim = Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DeviceID='${driveLetter}:'" -ErrorAction Stop
            if ($null -ne $cim -and $null -ne $cim.VolumeSerialNumber) {
                return [string]$cim.VolumeSerialNumber
            }
        }
        catch {
            # Ignored.
        }
    }
    catch {
        # Volume identity is best effort; cache still works without it because
        # path / size / last-write together protect against the common cases.
    }

    return ""
}

function Get-ManifestContentHash {
    param([Parameter(Mandatory)][string]$ManifestPath)

    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try {
            $bytes = [IO.File]::ReadAllBytes($ManifestPath)
            return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $sha.Dispose()
        }
    }
    catch {
        return ""
    }
}

function Get-ToolkitFileLastWriteUtcString {
    param([Parameter(Mandatory)][string]$LiteralPath)

    try {
        $info = Get-Item -LiteralPath $LiteralPath -ErrorAction Stop
        return $info.LastWriteTimeUtc.ToString("o")
    }
    catch {
        return ""
    }
}

function Load-ToolkitHealthCache {
    param([Parameter(Mandatory)][string]$CachePath)

    if (-not (Test-Path -LiteralPath $CachePath -PathType Leaf)) {
        return [PSCustomObject]@{
            Loaded = $false
            Reason = "no-cache-file"
            Targets = @{}
        }
    }

    try {
        $raw = Get-Content -LiteralPath $CachePath -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return [PSCustomObject]@{
                Loaded = $false
                Reason = "empty-cache-file"
                Targets = @{}
            }
        }

        $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $parsed) {
            return [PSCustomObject]@{
                Loaded = $false
                Reason = "null-cache-payload"
                Targets = @{}
            }
        }

        $schema = 0
        if ($null -ne $parsed.schemaVersion) {
            try {
                $schema = [int]$parsed.schemaVersion
            }
            catch {
                $schema = 0
            }
        }
        if ($schema -ne $script:ToolkitHealthCacheSchemaVersion) {
            return [PSCustomObject]@{
                Loaded = $false
                Reason = "unsupported-cache-schema"
                Targets = @{}
            }
        }

        $targets = @{}
        if ($null -ne $parsed.targets) {
            foreach ($prop in $parsed.targets.PSObject.Properties) {
                $targets[$prop.Name] = $prop.Value
            }
        }

        return [PSCustomObject]@{
            Loaded = $true
            Reason = "ok"
            Targets = $targets
        }
    }
    catch {
        return [PSCustomObject]@{
            Loaded = $false
            Reason = "cache-parse-failed"
            Targets = @{}
        }
    }
}

function Get-ToolkitCachedTargetEntry {
    param(
        [Parameter(Mandatory)]$Cache,
        [Parameter(Mandatory)][string]$TargetIdentityKey,
        [Parameter(Mandatory)][string]$VolumeSerial,
        [Parameter(Mandatory)][string]$ManifestHash
    )

    if (-not $Cache.Loaded) {
        return $null
    }
    if (-not $Cache.Targets.ContainsKey($TargetIdentityKey)) {
        return $null
    }

    $entry = $Cache.Targets[$TargetIdentityKey]
    if ($null -eq $entry) {
        return $null
    }

    # Strict identity rules:
    #   stored=empty                 -> accept (historic cache made no serial
    #                                   commitment; fall back to path/size/
    #                                   last-write/expected-checksum gates).
    #   stored=non-empty, current=empty -> REJECT (we previously identified
    #                                   this volume; we will not downgrade
    #                                   identity trust just because Get-Volume
    #                                   transiently failed on this run).
    #   stored=non-empty, current=non-empty, mismatch -> REJECT.
    #   stored=non-empty, current=non-empty, match    -> accept.
    # Manifest hash mismatch is unconditionally invalidating.
    $storedSerial = if ($null -ne $entry.volumeSerial) { [string]$entry.volumeSerial } else { "" }
    if (-not [string]::IsNullOrWhiteSpace($storedSerial)) {
        if ([string]::IsNullOrWhiteSpace($VolumeSerial)) {
            return $null
        }
        if (-not [string]::Equals($storedSerial, $VolumeSerial, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $null
        }
    }

    $storedManifestHash = if ($null -ne $entry.manifestHash) { [string]$entry.manifestHash } else { "" }
    if ([string]::IsNullOrWhiteSpace($storedManifestHash) -or
        -not [string]::Equals($storedManifestHash, $ManifestHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    return $entry
}

function Get-ToolkitCachedItem {
    param(
        $TargetCacheEntry,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ($null -eq $TargetCacheEntry -or $null -eq $TargetCacheEntry.items) {
        return $null
    }

    $items = $TargetCacheEntry.items
    if ($items -is [System.Collections.IDictionary]) {
        if ($items.Contains($RelativePath)) {
            return $items[$RelativePath]
        }
        # ConvertFrom-Json yields a PSCustomObject — properties named with backslashes
        # are still accessible via PSObject.Properties lookup by name.
    }

    $prop = $items.PSObject.Properties[$RelativePath]
    if ($null -ne $prop) {
        return $prop.Value
    }

    return $null
}

function Test-ToolkitCacheHit {
    param(
        $CachedItem,
        [Parameter(Mandatory)][int64]$ActualSizeBytes,
        [Parameter(Mandatory)][string]$ActualLastWriteUtc,
        [Parameter(Mandatory)][string]$ExpectedChecksum,
        [Parameter(Mandatory)][string]$ChecksumAlgorithm
    )

    if ($null -eq $CachedItem) {
        return [PSCustomObject]@{ Hit = $false; Reason = "no-cached-entry" }
    }

    $cachedStatus = if ($null -ne $CachedItem.status) { [string]$CachedItem.status } else { "" }
    $cachedChecksumStatus = if ($null -ne $CachedItem.checksumStatus) { [string]$CachedItem.checksumStatus } else { "" }
    if (-not [string]::Equals($cachedStatus, "INSTALLED", [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($cachedChecksumStatus, "Match", [System.StringComparison]::OrdinalIgnoreCase)) {
        return [PSCustomObject]@{ Hit = $false; Reason = "prior-not-match" }
    }

    $cachedSize = if ($null -ne $CachedItem.sizeBytes) { [int64]$CachedItem.sizeBytes } else { -1 }
    if ($cachedSize -ne $ActualSizeBytes) {
        return [PSCustomObject]@{ Hit = $false; Reason = "size-changed" }
    }

    $cachedLastWrite = if ($null -ne $CachedItem.lastWriteUtc) { [string]$CachedItem.lastWriteUtc } else { "" }
    if ([string]::IsNullOrWhiteSpace($cachedLastWrite) -or
        -not [string]::Equals($cachedLastWrite, $ActualLastWriteUtc, [System.StringComparison]::Ordinal)) {
        return [PSCustomObject]@{ Hit = $false; Reason = "last-write-changed" }
    }

    $cachedExpected = if ($null -ne $CachedItem.expectedChecksum) { [string]$CachedItem.expectedChecksum } else { "" }
    if ([string]::IsNullOrWhiteSpace($cachedExpected) -or [string]::IsNullOrWhiteSpace($ExpectedChecksum) -or
        -not [string]::Equals($cachedExpected, $ExpectedChecksum, [System.StringComparison]::OrdinalIgnoreCase)) {
        return [PSCustomObject]@{ Hit = $false; Reason = "expected-checksum-changed" }
    }

    $cachedAlgorithm = if ($null -ne $CachedItem.checksumAlgorithm) { [string]$CachedItem.checksumAlgorithm } else { "" }
    if (-not [string]::Equals($cachedAlgorithm, $ChecksumAlgorithm, [System.StringComparison]::OrdinalIgnoreCase)) {
        return [PSCustomObject]@{ Hit = $false; Reason = "checksum-algorithm-changed" }
    }

    $cachedActual = if ($null -ne $CachedItem.actualChecksum) { [string]$CachedItem.actualChecksum } else { "" }
    if ([string]::IsNullOrWhiteSpace($cachedActual)) {
        return [PSCustomObject]@{ Hit = $false; Reason = "actual-checksum-missing" }
    }

    return [PSCustomObject]@{
        Hit = $true
        Reason = "ok"
        ActualChecksum = $cachedActual
        VerifiedUtc = if ($null -ne $CachedItem.verifiedUtc) { [string]$CachedItem.verifiedUtc } else { "" }
    }
}

function Save-ToolkitHealthCache {
    param(
        [Parameter(Mandatory)][string]$CachePath,
        [Parameter(Mandatory)][string]$TargetIdentityKey,
        [Parameter(Mandatory)][string]$TargetRoot,
        [string]$VolumeSerial = "",
        [Parameter(Mandatory)][string]$ManifestHash,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][hashtable]$ItemsByRelativePath
    )

    $existing = Load-ToolkitHealthCache -CachePath $CachePath
    $targets = [ordered]@{}
    foreach ($key in $existing.Targets.Keys) {
        if ($key -eq $TargetIdentityKey) {
            continue
        }
        $targets[$key] = $existing.Targets[$key]
    }

    $itemsOrdered = [ordered]@{}
    foreach ($key in ($ItemsByRelativePath.Keys | Sort-Object)) {
        $itemsOrdered[$key] = $ItemsByRelativePath[$key]
    }

    $targets[$TargetIdentityKey] = [ordered]@{
        targetRoot = $TargetRoot
        volumeSerial = $VolumeSerial
        manifestHash = $ManifestHash
        manifestPath = $ManifestPath
        savedUtc = (Get-Date).ToUniversalTime().ToString("o")
        items = $itemsOrdered
    }

    $payload = [ordered]@{
        schemaVersion = $script:ToolkitHealthCacheSchemaVersion
        product = "ForgerEMS"
        targets = $targets
    }

    try {
        $directory = Split-Path -Parent $CachePath
        if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path -LiteralPath $directory -PathType Container)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        $json = $payload | ConvertTo-Json -Depth 8

        # Atomic write: stage to a temp file, then Move-Item -Force into place.
        # A crash mid-write can otherwise leave a truncated JSON file at the
        # final path; the loader still recovers safely, but we prefer not to
        # leave a corrupt cache that wastes the next scan.
        $tempPath = $CachePath + ".tmp"
        $json | Set-Content -LiteralPath $tempPath -Encoding UTF8
        try {
            Move-Item -LiteralPath $tempPath -Destination $CachePath -Force
        }
        catch {
            # Move-Item -Force fails on some PS hosts when the destination is
            # locked; fall back to direct write so we at least try to persist.
            $json | Set-Content -LiteralPath $CachePath -Encoding UTF8
            if (Test-Path -LiteralPath $tempPath -PathType Leaf) {
                Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            }
        }
        return $true
    }
    catch {
        # Failing to persist the cache is not fatal — the next scan will rehash.
        return $false
    }
}
