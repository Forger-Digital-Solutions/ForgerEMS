#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetRoot,
    [string]$ManifestPath = ""
)

$ErrorActionPreference = "Stop"

$runtimeHelperPath = Join-Path $PSScriptRoot "..\ForgerEMS.Runtime.ps1"
if (Test-Path -LiteralPath $runtimeHelperPath) {
    . $runtimeHelperPath
}
else {
    throw "ForgerEMS runtime helper was not found. Checked: $runtimeHelperPath"
}

$checksumResolverPath = Join-Path $PSScriptRoot "ChecksumResolver.ps1"
if (Test-Path -LiteralPath $checksumResolverPath) {
    . $checksumResolverPath
}
else {
    throw "Checksum resolver helper was not found. Checked: $checksumResolverPath"
}

function Write-ToolkitLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO", "OK", "WARN", "ERROR")][string]$Level = "INFO"
    )

    Write-Host ("[{0}] {1}" -f $Level, $Message)
}

function Resolve-ManifestPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [IO.Path]::GetFullPath($ExplicitPath)
    }

    $candidates = @(
        (Join-Path $PSScriptRoot "..\ForgerEMS.updates.json"),
        (Join-Path $PSScriptRoot "..\manifests\ForgerEMS.updates.json"),
        (Join-Path $PSScriptRoot "..\..\manifests\ForgerEMS.updates.json"),
        (Join-Path (Get-Location).Path "manifests\ForgerEMS.updates.json"),
        (Join-Path (Get-Location).Path "ForgerEMS.updates.json")
    )

    foreach ($candidate in $candidates) {
        $resolved = [IO.Path]::GetFullPath($candidate)
        if (Test-Path -LiteralPath $resolved) {
            return $resolved
        }
    }

    throw "Could not resolve manifests\ForgerEMS.updates.json."
}

function Get-LocalReportRoot {
    $override = [Environment]::GetEnvironmentVariable("FORGEREMS_TOOLKIT_HEALTH_REPORT_ROOT", "Process")
    if (-not [string]::IsNullOrWhiteSpace($override)) {
        return $override
    }

    $localAppData = [Environment]::GetFolderPath("LocalApplicationData")
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = [IO.Path]::GetTempPath()
    }

    return (Join-Path $localAppData "ForgerEMS\Runtime\reports")
}

function Test-IsCRoot {
    param([Parameter(Mandatory)][string]$Path)

    $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Path))
    return [string]::Equals($root, "C:\", [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-CatalogMetadataString {
    param($Value)

    # Manifest schema accepts either a string or a JSON array of strings for fields like architecture / bootMode.
    # Normalize both shapes to a single comma-separated string so downstream JSON consumers see a stable scalar.
    if ($null -eq $Value) {
        return ""
    }

    if ($Value -is [string]) {
        return $Value.Trim()
    }

    if ($Value -is [System.Collections.IEnumerable]) {
        $parts = @()
        foreach ($entry in $Value) {
            if ($null -eq $entry) { continue }
            $text = [string]$entry
            if (-not [string]::IsNullOrWhiteSpace($text)) {
                $parts += $text.Trim()
            }
        }

        return ($parts -join ", ")
    }

    return [string]$Value
}

function Get-Category {
    param([string]$Destination)

    if ([string]::IsNullOrWhiteSpace($Destination)) {
        return "General"
    }

    $parts = $Destination -split '[\\/]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($parts.Count -ge 3 -and $parts[0] -eq "Tools" -and $parts[1] -eq "Portable") {
        return [string]$parts[2]
    }

    if ($parts.Count -ge 2) {
        return [string]$parts[1]
    }

    return [string]$parts[0]
}

function Get-ToolVersion {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return "Unknown"
    }

    $match = [regex]::Match($Name, '\b(v?\d+(?:\.\d+){1,3}(?:-\d+)?)\b')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return "Manual"
}

function Normalize-RelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    return $Path.Trim().TrimStart('\', '/').Replace('/', '\')
}

function Get-ItemClassification {
    param($Item)

    $type = [string]$Item.type
    $notes = [string]$Item.notes
    $sourceType = [string]$Item.sourceType

    if ($type -eq "page") {
        return [PSCustomObject]@{
            Name = "manualDownload"
            Reason = "Manifest item type is page; this is a vendor/manual shortcut, not an auto-download payload."
        }
    }

    if ($notes -match '(?i)manual only|review first|TODO-safe|shortcut only|placeholder') {
        return [PSCustomObject]@{
            Name = "manualDownload"
            Reason = "Manifest notes mark this item as manual, review-first, shortcut-only, or placeholder."
        }
    }

    if ($sourceType -match '(?i)manual|page') {
        return [PSCustomObject]@{
            Name = "manualDownload"
            Reason = "Manifest source type indicates a manual or page-based item."
        }
    }

    if ($null -ne $Item.optional -and [bool]$Item.optional) {
        return [PSCustomObject]@{
            Name = "optional"
            Reason = "Manifest marks this item optional."
        }
    }

    return [PSCustomObject]@{
        Name = "managedAutoDownload"
        Reason = "Manifest item is an enabled file payload managed by Update-ForgerEMS."
    }
}

function Get-RequirementLevel {
    param($Item, [string]$Classification)

    if ($Classification -eq "managedAutoDownload") {
        return "required"
    }

    if ($Classification -eq "optional") {
        return "optional"
    }

    return "manual"
}

function Resolve-ExpectedItemPath {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [string]$Destination
    )

    $resolved = Resolve-ToolkitItemPath -UsbRoot $TargetRoot -Destination $Destination
    if ($null -eq $resolved) {
        return ""
    }

    return [string]$resolved.Path
}

function Resolve-ToolkitItemPath {
    param(
        [Parameter(Mandatory)][string]$UsbRoot,
        [Parameter(Mandatory)][string]$Destination
    )

    $relative = Normalize-RelativePath -Path $Destination
    if ([string]::IsNullOrWhiteSpace($relative)) {
        return $null
    }

    $relativeParts = @($relative -split '\\' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($relativeParts.Count -eq 0) {
        return $null
    }

    if (@($relativeParts | Where-Object { $_ -eq "." -or $_ -eq ".." }).Count -gt 0) {
        throw "Path traversal is not allowed in toolkit destination: $Destination"
    }

    $rootFull = [IO.Path]::GetFullPath($UsbRoot)
    $candidate = Join-Path $rootFull ($relativeParts -join '\')
    $candidateFull = [IO.Path]::GetFullPath($candidate)
    $rootPrefix = $rootFull.TrimEnd('\') + '\'
    if (-not $candidateFull.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Toolkit destination escaped target root: $Destination"
    }

    return [PSCustomObject]@{
        RelativePath = ($relativeParts -join '\')
        Path = $candidateFull
    }
}

function Get-ToolkitFileProbe {
    param([string]$LiteralPath)

    if ([string]::IsNullOrWhiteSpace($LiteralPath)) {
        return [PSCustomObject]@{
            Exists = $false
            SizeBytes = 0L
        }
    }

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        return [PSCustomObject]@{
            Exists = $false
            SizeBytes = 0L
        }
    }

    $item = Get-Item -LiteralPath $LiteralPath -ErrorAction Stop
    return [PSCustomObject]@{
        Exists = $true
        SizeBytes = [int64]$item.Length
    }
}

function Resolve-FallbackShortcutPath {
    param(
        [string]$DestinationPath,
        [string]$Destination
    )

    if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
        return ""
    }

    if ([IO.Path]::GetExtension($DestinationPath) -eq ".url") {
        return $DestinationPath
    }

    $directory = Split-Path -Parent $DestinationPath
    $fileName = [IO.Path]::GetFileNameWithoutExtension($DestinationPath)
    if ([string]::IsNullOrWhiteSpace($directory) -or [string]::IsNullOrWhiteSpace($fileName)) {
        return [IO.Path]::ChangeExtension($DestinationPath, ".url")
    }

    $downloadShortcut = Join-Path $directory ("DOWNLOAD - {0}.url" -f $fileName)
    if (Test-Path -LiteralPath $downloadShortcut -PathType Leaf) {
        return $downloadShortcut
    }

    return [IO.Path]::ChangeExtension($DestinationPath, ".url")
}

function Get-NormalizedNameTokens {
    param(
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $commonTokens = @(
        "amd64", "x64", "x86", "64bit", "32bit", "windows", "win", "live", "setup",
        "installer", "desktop", "portable", "package", "oracular", "stable", "plus",
        "download", "page", "official"
    )

    $clean = $Text.ToLowerInvariant() -replace '[^a-z0-9]+', ' '
    return @(
        $clean -split '\s+' |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_.Length -ge 4 -and
                $_ -notmatch '^\d+$' -and
                $_ -notin $commonTokens
            } |
            Select-Object -Unique
    )
}

function Test-NormalizedFileNameMatch {
    param(
        [Parameter(Mandatory)][IO.FileInfo]$Candidate,
        [string[]]$Tokens,
        [string]$ExpectedExtension
    )

    if ($Tokens.Count -eq 0) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedExtension) -and
        -not [string]::Equals($Candidate.Extension, $ExpectedExtension, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $candidateTokens = @(Get-NormalizedNameTokens -Text $Candidate.BaseName)
    foreach ($token in $Tokens) {
        if ($candidateTokens -contains $token) {
            return $true
        }
    }

    return $false
}

function Find-AlternateItemPath {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][string]$DestinationPath,
        [string]$Destination,
        [string]$ItemName,
        [System.Collections.Generic.List[string]]$CheckedPaths
    )

    if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
        return ""
    }

    $fileName = [IO.Path]::GetFileName($DestinationPath)
    if ([string]::IsNullOrWhiteSpace($fileName)) {
        return ""
    }

    $expectedExtension = [IO.Path]::GetExtension($DestinationPath)
    $matchTokens = @(
        Get-NormalizedNameTokens -Text ([IO.Path]::GetFileNameWithoutExtension($DestinationPath))
        Get-NormalizedNameTokens -Text $ItemName
    ) | Select-Object -Unique

    $relative = Normalize-RelativePath -Path $Destination
    $firstSegment = ($relative -split '\\' | Where-Object { $_ } | Select-Object -First 1)
    $knownRoots = @()
    if ($firstSegment) {
        $knownRoots += (Join-Path $TargetRoot $firstSegment)
    }

    foreach ($rootName in @("ISO", "Tools", "Drivers", "MediCat.USB")) {
        $knownRoots += (Join-Path $TargetRoot $rootName)
    }

    foreach ($searchRoot in ($knownRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $searchRoot -PathType Container)) {
            if ($CheckedPaths) {
                [void]$CheckedPaths.Add((Join-Path $searchRoot $fileName))
            }
            continue
        }

        if ($CheckedPaths) {
            [void]$CheckedPaths.Add((Join-Path $searchRoot $fileName))
            [void]$CheckedPaths.Add($searchRoot)
        }

        $match = Get-ChildItem -LiteralPath $searchRoot -Filter $fileName -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }

        $normalizedMatch = Get-ChildItem -LiteralPath $searchRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { Test-NormalizedFileNameMatch -Candidate $_ -Tokens $matchTokens -ExpectedExtension $expectedExtension } |
            Select-Object -First 1
        if ($null -ne $normalizedMatch) {
            return $normalizedMatch.FullName
        }
    }

    return ""
}

# Get-Sha256FromSourceUrl is now provided by ChecksumResolver.ps1 (dot-sourced
# above). It accepts -TargetFileName for filename-aware multi-line checksum
# files; legacy single-hash files still resolve without a target filename for
# backwards compatibility.

function Get-ManagedCoverageToken {
    param($Report)

    $raw = [string]$Report.tool
    if ([string]::IsNullOrWhiteSpace($raw)) {
        $raw = [string]$Report.destination
    }

    $raw = $raw -replace '(?i)\bdownload page\b', ''
    $raw = $raw -replace '(?i)\bDOWNLOAD\s*-\s*', ''
    $raw = $raw -replace '\.url$', ''
    $token = ($raw.ToLowerInvariant() -replace '[^a-z0-9]+', '')
    return $token
}

function Set-ManualShortcutCoverageFromManagedDownloads {
    param([object[]]$Reports)

    $installedManaged = @($Reports | Where-Object {
        $_.requirement -eq "required" -and
        $_.status -eq "INSTALLED" -and
        $_.checksumStatus -eq "Match"
    })

    foreach ($manual in @($Reports | Where-Object { $_.status -eq "MANUAL_REQUIRED" -and $_.type -eq "manualDownload" })) {
        $manualToken = Get-ManagedCoverageToken -Report $manual
        if ([string]::IsNullOrWhiteSpace($manualToken)) {
            continue
        }

        $coveredBy = $installedManaged | Where-Object {
            $candidateToken = Get-ManagedCoverageToken -Report $_
            $_.category -eq $manual.category -and
            -not [string]::IsNullOrWhiteSpace($candidateToken) -and
            ($candidateToken.Contains($manualToken) -or $manualToken.Contains($candidateToken))
        } | Select-Object -First 1

        if ($null -eq $coveredBy) {
            continue
        }

        $manual.status = "COVERED_BY_MANAGED"
        $manual.verification = "Covered by managed download."
        $manual.recommendation = "Shortcut suppressed because managed item is installed. No action needed."
        $manual.checksumStatus = "Covered"
        $manual.finalClassification = "COVERED_BY_MANAGED"
        $manual.classificationReason = "Manual/info shortcut is covered by installed verified managed item: $($coveredBy.tool)."
    }
}

function Get-ToolkitItemStatus {
    param(
        [Parameter(Mandatory)]$Item,
        [Parameter(Mandatory)][string]$ResolvedTargetRoot
    )

    $destination = Normalize-RelativePath -Path ([string]$Item.dest)
    $type = [string]$Item.type
    $name = [string]$Item.name
    $sha256 = ([string]$Item.sha256).Trim().ToLowerInvariant()
    $sha256Url = ([string]$Item.sha256Url).Trim()
    $sha512 = ([string]$Item.sha512).Trim().ToLowerInvariant()
    $sha512Url = ([string]$Item.sha512Url).Trim()
    $checksumAlgorithm = if ($sha256) { "SHA256" } elseif ($sha512) { "SHA512" } elseif ($sha256Url) { "SHA256" } elseif ($sha512Url) { "SHA512" } else { "" }
    $expectedHash = if ($checksumAlgorithm -eq "SHA512") { $sha512 } else { $sha256 }
    $checksumUrl = if ($checksumAlgorithm -eq "SHA512") { $sha512Url } else { $sha256Url }
    $url = [string]$Item.url
    $classificationInfo = Get-ItemClassification -Item $Item
    $classification = [string]$classificationInfo.Name
    $classificationReason = [string]$classificationInfo.Reason
    $requirement = Get-RequirementLevel -Item $Item -Classification $classification
    $resolvedDestination = Resolve-ToolkitItemPath -UsbRoot $ResolvedTargetRoot -Destination $destination
    $destinationPath = if ($null -ne $resolvedDestination) { [string]$resolvedDestination.Path } else { "" }
    $resolvedRelativePath = if ($null -ne $resolvedDestination) { [string]$resolvedDestination.RelativePath } else { $destination }
    $fallbackPath = Resolve-FallbackShortcutPath -DestinationPath $destinationPath -Destination $destination
    $resolvedPath = $destinationPath
    $checkedPaths = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($destinationPath)) {
        [void]$checkedPaths.Add($destinationPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($fallbackPath)) {
        [void]$checkedPaths.Add($fallbackPath)
    }

    $status = "UNKNOWN"
    $verification = "No verification data available."
    $recommendation = "Review this manifest item manually."
    $actualHash = ""
    $checksumStatus = "NotChecked"
    $diagnosticMessage = ""
    $finalProbe = [PSCustomObject]@{
        Exists = $false
        SizeBytes = 0L
    }

    if ([string]::IsNullOrWhiteSpace($destination)) {
        $status = "UNKNOWN"
        $recommendation = "Manifest item has no destination path."
    }
    elseif ($classification -eq "manualDownload") {
        $manualProbe = Get-ToolkitFileProbe -LiteralPath $destinationPath
        if ($manualProbe.Exists) {
            $status = "PLACEHOLDER"
            $verification = "Shortcut present."
            $recommendation = "Open the shortcut and complete the vendor-controlled download manually."
            $finalProbe = $manualProbe
        }
        elseif (-not [string]::IsNullOrWhiteSpace($fallbackPath) -and (Test-Path -LiteralPath $fallbackPath -PathType Leaf)) {
            $status = "PLACEHOLDER"
            $verification = "Fallback shortcut present."
            $recommendation = "Open the fallback shortcut and complete the vendor-controlled download manually."
            $resolvedPath = $fallbackPath
            $finalProbe = Get-ToolkitFileProbe -LiteralPath $resolvedPath
        }
        else {
            $status = "MANUAL_REQUIRED"
            $verification = "Manual shortcut not found."
            $recommendation = "Run Setup USB Toolkit to restore the vendor download shortcut, then complete the manual download if needed."
        }
    }
    elseif ($classification -eq "optional") {
        $optionalProbe = Get-ToolkitFileProbe -LiteralPath $destinationPath
        if ($optionalProbe.Exists -and $optionalProbe.SizeBytes -gt 0) {
            $status = "INSTALLED"
            $verification = "Optional item present."
            $recommendation = "No action needed."
            $finalProbe = $optionalProbe
        }
        else {
            $status = "SKIPPED"
            $verification = "Optional item is not present."
            $recommendation = "Optional item can be added later if this workflow needs it."
        }
    }
    else {
        $exactProbe = Get-ToolkitFileProbe -LiteralPath $destinationPath
        if (-not ($exactProbe.Exists -and $exactProbe.SizeBytes -gt 0)) {
            $alternatePath = Find-AlternateItemPath -TargetRoot $ResolvedTargetRoot -DestinationPath $destinationPath -Destination $destination -ItemName $name -CheckedPaths $checkedPaths
            if (-not [string]::IsNullOrWhiteSpace($alternatePath)) {
                $resolvedPath = $alternatePath
            }
        }

        $resolvedProbe = Get-ToolkitFileProbe -LiteralPath $resolvedPath
        if ($resolvedProbe.Exists -and $resolvedProbe.SizeBytes -gt 0) {
            $finalProbe = $resolvedProbe
            if ([string]::IsNullOrWhiteSpace($expectedHash) -and -not [string]::IsNullOrWhiteSpace($checksumUrl)) {
                Write-ToolkitLog ("{0} checksum source URL available: {1}" -f $checksumAlgorithm, $checksumUrl) "INFO"
                # Derive the target filename so multi-line checksum files can be parsed safely.
                # Precedence: manifest destination basename (canonical artifact name we just hashed) ->
                # local resolved path -> URL basename. The dest basename is the authoritative key because
                # that is the exact filename the vendor's checksum file will reference.
                $targetFileName = ""
                if (-not [string]::IsNullOrWhiteSpace($destination)) {
                    try {
                        $targetFileName = [IO.Path]::GetFileName($destination)
                    }
                    catch {
                        $targetFileName = ""
                    }
                }
                if ([string]::IsNullOrWhiteSpace($targetFileName) -and -not [string]::IsNullOrWhiteSpace($resolvedPath)) {
                    $targetFileName = [IO.Path]::GetFileName($resolvedPath)
                }
                if ([string]::IsNullOrWhiteSpace($targetFileName) -and -not [string]::IsNullOrWhiteSpace($url)) {
                    try {
                        $targetFileName = [IO.Path]::GetFileName([Uri]$url)
                    }
                    catch {
                        $targetFileName = ""
                    }
                }
                $expectedHash = if ($checksumAlgorithm -eq "SHA512") {
                    Get-Sha512FromSourceUrl -ShaUrl $checksumUrl -TargetFileName $targetFileName
                }
                else {
                    Get-Sha256FromSourceUrl -ShaUrl $checksumUrl -TargetFileName $targetFileName
                }
                if (-not [string]::IsNullOrWhiteSpace($expectedHash)) {
                    Write-ToolkitLog ("Resolved {0} from checksum URL: {1}" -f $checksumAlgorithm, $expectedHash) "OK"
                }
            }

            if (-not [string]::IsNullOrWhiteSpace($expectedHash)) {
                try {
                    $actualHash = if ($checksumAlgorithm -eq "SHA512") {
                        Get-ForgerSha512 -LiteralPath $resolvedPath
                    }
                    else {
                        Get-ForgerSha256 -LiteralPath $resolvedPath
                    }
                    Write-ToolkitLog ("{0} hash provider: {1} file={2}" -f $checksumAlgorithm, (Get-ForgerLastHashProvider), (Get-ForgerSafePathForLog -Path $resolvedPath)) "INFO"
                    if ([string]::Equals($actualHash, $expectedHash.ToLowerInvariant(), [System.StringComparison]::OrdinalIgnoreCase)) {
                        $status = "INSTALLED"
                        $verification = "$checksumAlgorithm verified."
                        $checksumStatus = "Match"
                        $recommendation = "No action needed."
                    }
                    else {
                        $status = "HASH_FAILED"
                        $verification = "$checksumAlgorithm mismatch."
                        $checksumStatus = "Mismatch"
                        $recommendation = "Run Update Toolkit to replace this managed item from the manifest source."
                    }
                }
                catch {
                    $status = "VERIFICATION_PENDING"
                    $verification = "File present; checksum verification pending due to hash provider error."
                    $checksumStatus = "Pending"
                    $diagnosticMessage = $_.Exception.Message
                    $recommendation = "Re-run Refresh Toolkit health or Update Toolkit to complete verification."
                }
            }
            else {
                $status = "VERIFICATION_PENDING"
                if (-not [string]::IsNullOrWhiteSpace($checksumUrl)) {
                    $verification = "File present; offline checksum pending (checksum URL unavailable or unresolved)."
                    $recommendation = "File is present. Re-run Revalidate when network is available to verify checksum."
                }
                else {
                    $verification = "File present; checksum not verified (manifest has no pinned supported checksum)."
                    $recommendation = "File is present. Optional integrity verification may be done manually if required."
                }
                $checksumStatus = "Pending"
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($fallbackPath) -and (Test-Path -LiteralPath $fallbackPath -PathType Leaf)) {
            $status = "MANUAL_REQUIRED"
            $verification = "Managed file missing; fallback shortcut present."
            $recommendation = "Run Update Toolkit, or use the fallback shortcut if the source is currently gated or unavailable."
            $resolvedPath = $fallbackPath
        }
        else {
            $status = "MISSING_REQUIRED"
            $verification = "Required managed file not found."
            $checksumStatus = "NotAvailable"
            $recommendation = "Run Update Toolkit to download or restore this required managed item."
        }
    }

    Write-ToolkitLog (
        "Toolkit item status: tool='{0}' rel='{1}' abs='{2}' exists={3} size={4} checksum={5} status={6}" -f
        $name,
        $resolvedRelativePath,
        $destinationPath,
        $finalProbe.Exists,
        $finalProbe.SizeBytes,
        $checksumStatus,
        $status
    ) "INFO"

    $freshness = $Item.freshness

    return [PSCustomObject][ordered]@{
        tool = $name
        category = Get-Category -Destination $destination
        status = $status
        type = $classification
        requirement = $requirement
        version = Get-ToolVersion -Name $name
        expectedPath = $resolvedRelativePath
        verification = $verification
        recommendation = $recommendation
        destination = $destination
        path = $resolvedPath
        resolvedAbsolutePath = $destinationPath
        resolvedRelativePath = $resolvedRelativePath
        exists = [bool]$finalProbe.Exists
        sizeBytes = [int64]$finalProbe.SizeBytes
        checksumStatus = $checksumStatus
        finalClassification = $status
        diagnosticMessage = $diagnosticMessage
        checkedExactPath = $destinationPath
        checkedFallbackPaths = @($checkedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        matchedPath = if ((-not [string]::IsNullOrWhiteSpace($resolvedPath)) -and (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) { $resolvedPath } else { "" }
        classificationReason = $classificationReason
        sha256Expected = $expectedHash
        sha256Actual = $actualHash
        checksumAlgorithm = $checksumAlgorithm
        sourceType = [string]$Item.sourceType
        url = $url
        # Additive catalog metadata (introduced 2026-05-21). Fields are optional in the manifest; missing entries surface as empty strings/false so downstream consumers stay schema-stable.
        kind = [string]$Item.kind
        family = [string]$Item.family
        osCategory = [string]$Item.osCategory
        architecture = Get-CatalogMetadataString -Value $Item.architecture
        bootMode = Get-CatalogMetadataString -Value $Item.bootMode
        recommendedUse = [string]$Item.recommendedUse
        technicianNotes = [string]$Item.technicianNotes
        licenseNote = [string]$Item.licenseNote
        manualOnly = if ($null -ne $Item.manualOnly) { [bool]$Item.manualOnly } else { $false }
        legacyWarning = [string]$Item.legacyWarning
        ventoyNotes = [string]$Item.ventoyNotes
        secureBootNote = [string]$Item.secureBootNote
        sourceTrust = [string]$Item.sourceTrust
        currentPinnedVersion = [string]$freshness.currentPinnedVersion
        latestKnownStableVersion = [string]$freshness.latestKnownStableVersion
        lastFreshnessAuditUtc = [string]$freshness.lastFreshnessAuditUtc
        freshnessStatus = [string]$freshness.freshnessStatus
        checksumVerificationMode = [string]$freshness.checksumVerificationMode
        updateRecommendation = [string]$freshness.updateRecommendation
    }
}

$resolvedTargetRoot = [IO.Path]::GetFullPath($TargetRoot)
if (-not (Test-Path -LiteralPath $resolvedTargetRoot -PathType Container)) {
    throw "Target root was not found: $resolvedTargetRoot"
}

$manifestPath = Resolve-ManifestPath -ExplicitPath $ManifestPath
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest was not found: $manifestPath"
}

Write-ToolkitLog ("Toolkit health scan started for {0}" -f $resolvedTargetRoot)
Write-ToolkitLog ("Manifest: {0}" -f $manifestPath)

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$items = @($manifest.items | Where-Object { $null -eq $_.enabled -or $_.enabled -eq $true })
$toolReports = @()

$index = 0
foreach ($item in $items) {
    $index++
    if (($index % 10) -eq 0) {
        Write-ToolkitLog ("Scanned {0}/{1} toolkit items..." -f $index, $items.Count)
    }

    $toolReports += Get-ToolkitItemStatus -Item $item -ResolvedTargetRoot $resolvedTargetRoot
}

Set-ManualShortcutCoverageFromManagedDownloads -Reports $toolReports

$requiredManagedMissing = @($toolReports | Where-Object { $_.requirement -eq "required" -and $_.status -eq "MISSING_REQUIRED" })
$manualActionItems = @($toolReports | Where-Object { $_.status -in @("MANUAL_REQUIRED", "PLACEHOLDER") })
$coveredByManagedItems = @($toolReports | Where-Object { $_.status -eq "COVERED_BY_MANAGED" })
$hashFailureItems = @($toolReports | Where-Object { $_.status -eq "HASH_FAILED" })
$verificationPendingItems = @($toolReports | Where-Object { $_.status -eq "VERIFICATION_PENDING" })
$summary = [ordered]@{
    installed = @($toolReports | Where-Object { $_.status -eq "INSTALLED" }).Count
    missing = $requiredManagedMissing.Count
    missingRequired = $requiredManagedMissing.Count
    updates = @($toolReports | Where-Object { $_.status -eq "UPDATE_AVAILABLE" }).Count
    failed = $hashFailureItems.Count
    verificationPending = $verificationPendingItems.Count
    manual = @($toolReports | Where-Object { $_.status -eq "MANUAL_REQUIRED" }).Count
    placeholder = @($toolReports | Where-Object { $_.status -eq "PLACEHOLDER" }).Count
    coveredByManaged = $coveredByManagedItems.Count
    skipped = @($toolReports | Where-Object { $_.status -eq "SKIPPED" }).Count
    unknown = @($toolReports | Where-Object { $_.status -eq "UNKNOWN" }).Count
    total = $toolReports.Count
    requiredManagedTotal = @($toolReports | Where-Object { $_.requirement -eq "required" }).Count
}

$healthVerdict = if ($summary.failed -gt 0 -or $summary.missing -gt 0) {
    "PARTIAL"
}
elseif (($summary.manual + $summary.placeholder) -gt 0) {
    "MANUAL ACTION NEEDED"
}
else {
    "READY"
}

$manualExplanation = "Manual items are download pages, licensed/gated tools, or informational shortcuts that ForgerEMS intentionally does not auto-download. They do not count as required managed-tool failures."
$coveredExplanation = "Covered/suppressed shortcuts are manual/info shortcuts intentionally omitted because the matching managed download is installed and verified."

$localReportRoot = Get-LocalReportRoot
New-Item -ItemType Directory -Path $localReportRoot -Force | Out-Null
$localJsonPath = Join-Path $localReportRoot "toolkit-health-latest.json"
$localMarkdownPath = Join-Path $localReportRoot "toolkit-health-latest.md"

$targetReportsWritten = $false
$targetJsonPath = ""
$targetMarkdownPath = ""
if (-not (Test-IsCRoot -Path $resolvedTargetRoot)) {
    $targetReportRoot = Join-Path $resolvedTargetRoot "_reports"
    New-Item -ItemType Directory -Path $targetReportRoot -Force | Out-Null
    $targetJsonPath = Join-Path $targetReportRoot "toolkit-health.json"
    $targetMarkdownPath = Join-Path $targetReportRoot "toolkit-health.md"
    $targetReportsWritten = $true
}
else {
    Write-ToolkitLog "Target report copy skipped because ForgerEMS never writes reports to C:\." "WARN"
}

$report = [ordered]@{
    schemaVersion = 1
    product = "ForgerEMS"
    releaseIdentifier = "ForgerEMS v1.2.1 Public Preview"
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    targetRoot = $resolvedTargetRoot
    manifestPath = $manifestPath
    manifestCoreVersion = [string]$manifest.coreVersion
    healthVerdict = $healthVerdict
    manualItemsExplanation = $manualExplanation
    coveredShortcutsExplanation = $coveredExplanation
    summary = $summary
    requiredManagedMissing = @($requiredManagedMissing | Select-Object tool, category, expectedPath, checkedExactPath, checkedFallbackPaths, matchedPath, classificationReason, verification, recommendation)
    manualActionList = @($manualActionItems | Select-Object tool, category, status, expectedPath, recommendation)
    coveredByManaged = @($coveredByManagedItems | Select-Object tool, category, status, expectedPath, verification, recommendation, classificationReason)
    hashFailures = @($hashFailureItems | Select-Object tool, category, expectedPath, sha256Expected, sha256Actual, recommendation)
    verificationPending = @($verificationPendingItems | Select-Object tool, category, expectedPath, matchedPath, verification, recommendation)
    items = $toolReports
    reportPaths = [ordered]@{
        localJson = $localJsonPath
        localMarkdown = $localMarkdownPath
        targetJson = $targetJsonPath
        targetMarkdown = $targetMarkdownPath
        targetReportsWritten = $targetReportsWritten
    }
}

$json = $report | ConvertTo-Json -Depth 10
$json | Set-Content -LiteralPath $localJsonPath -Encoding UTF8
if ($targetReportsWritten) {
    $json | Set-Content -LiteralPath $targetJsonPath -Encoding UTF8
}

$markdown = New-Object System.Collections.Generic.List[string]
[void]$markdown.Add("# ForgerEMS Toolkit Health")
[void]$markdown.Add("")
[void]$markdown.Add(("Generated UTC: {0}" -f $report.generatedUtc))
[void]$markdown.Add(("Target root: {0}" -f $resolvedTargetRoot))
[void]$markdown.Add(("Manifest: {0}" -f $manifestPath))
[void]$markdown.Add(("Health verdict: **{0}**" -f $healthVerdict))
[void]$markdown.Add("")
[void]$markdown.Add($manualExplanation)
[void]$markdown.Add($coveredExplanation)
[void]$markdown.Add("")
[void]$markdown.Add("## Summary")
[void]$markdown.Add(("- Installed: {0}" -f $summary.installed))
[void]$markdown.Add(("- Missing required managed tools: {0}" -f $summary.missing))
[void]$markdown.Add(("- Updates: {0}" -f $summary.updates))
[void]$markdown.Add(("- Failed: {0}" -f $summary.failed))
[void]$markdown.Add(("- Verification pending: {0}" -f $summary.verificationPending))
[void]$markdown.Add(("- Manual: {0}" -f $summary.manual))
[void]$markdown.Add(("- Placeholder: {0}" -f $summary.placeholder))
[void]$markdown.Add(("- Covered by managed item: {0}" -f $summary.coveredByManaged))
[void]$markdown.Add(("- Skipped: {0}" -f $summary.skipped))
[void]$markdown.Add(("- Unknown: {0}" -f $summary.unknown))
[void]$markdown.Add("")
[void]$markdown.Add("## Required Managed Missing")
if ($requiredManagedMissing.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $requiredManagedMissing) {
        [void]$markdown.Add(("- {0} ({1}) - {2}" -f $item.tool, $item.category, $item.expectedPath))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Manual Action Items")
if ($manualActionItems.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $manualActionItems) {
        [void]$markdown.Add(("- {0} ({1}) - {2}" -f $item.tool, $item.status, $item.recommendation))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Covered / Suppressed Shortcuts")
if ($coveredByManagedItems.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $coveredByManagedItems) {
        [void]$markdown.Add(("- {0} ({1}) - {2}" -f $item.tool, $item.category, $item.recommendation))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Hash Failures")
if ($hashFailureItems.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $hashFailureItems) {
        [void]$markdown.Add(("- {0} - expected {1}, actual {2}" -f $item.tool, $item.sha256Expected, $item.sha256Actual))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Verification Pending")
if ($verificationPendingItems.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $verificationPendingItems) {
        [void]$markdown.Add(("- {0} ({1}) - {2}" -f $item.tool, $item.category, $item.verification))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Items")
[void]$markdown.Add("| Tool | Category | Status | Type | Expected/Found path | Verification | Recommendation |")
[void]$markdown.Add("| --- | --- | --- | --- | --- | --- | --- |")
foreach ($item in $toolReports) {
    $tool = ([string]$item.tool).Replace("|", "/")
    $category = ([string]$item.category).Replace("|", "/")
    $type = ([string]$item.type).Replace("|", "/")
    $foundPath = if ([string]::IsNullOrWhiteSpace([string]$item.matchedPath)) { "" } else { "Found: {0}" -f $item.matchedPath }
    $expectedPath = (([string]$item.expectedPath) + $(if ($foundPath) { " / $foundPath" } else { "" })).Replace("|", "/")
    $verification = ([string]$item.verification).Replace("|", "/")
    $recommendation = ([string]$item.recommendation).Replace("|", "/")
    [void]$markdown.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $tool, $category, $item.status, $type, $expectedPath, $verification, $recommendation))
}

$markdown | Set-Content -LiteralPath $localMarkdownPath -Encoding UTF8
if ($targetReportsWritten) {
    $markdown | Set-Content -LiteralPath $targetMarkdownPath -Encoding UTF8
}

Write-ToolkitLog ("Toolkit health scan complete. Verdict={0}; Installed={1}, MissingRequired={2}, Updates={3}, Failed={4}, Pending={5}, Manual={6}, Placeholder={7}, Covered={8}, Skipped={9}" -f $healthVerdict, $summary.installed, $summary.missing, $summary.updates, $summary.failed, $summary.verificationPending, $summary.manual, $summary.placeholder, $summary.coveredByManaged, $summary.skipped) "OK"
Write-ToolkitLog ("Local JSON report: {0}" -f $localJsonPath) "OK"
Write-ToolkitLog ("Local Markdown report: {0}" -f $localMarkdownPath) "OK"
if ($targetReportsWritten) {
    Write-ToolkitLog ("Target JSON report: {0}" -f $targetJsonPath) "OK"
    Write-ToolkitLog ("Target Markdown report: {0}" -f $targetMarkdownPath) "OK"
}
