#requires -Version 5.1

# ChecksumResolver
# ----------------
# Filename-aware extraction of SHA-256/SHA-512 hashes from vendor-published checksum
# files. Replaces the previous "first 64-hex match wins" behaviour so multi-line
# SHA256SUMS / SHA512SUMS / BSD-format files can be safely consumed by
# managed-download promotions.
#
# Designed for safety:
#  - Never invents a hash.
#  - On ambiguity (two lines match the same basename) returns an empty hash and
#    a structured diagnostic reason instead of guessing.
#  - When no target filename is supplied, falls back to the legacy "first
#    SHA-256 in the file" behaviour so existing single-hash `.sha256` files
#    keep working without callers being updated.
#  - Pure helpers (`Resolve-ChecksumFromChecksumText`,
#    `Get-CanonicalChecksumFilename`) are dot-source testable.
#
# Supported formats:
#  - GNU coreutils sha256sum:   "<64-hex>  filename"   or  "<64-hex> *filename"
#  - BSD checksum:              "SHA256 (filename) = <64-hex>"
#  - GitHub asset digest JSON:  '"digest": "sha256:<64-hex>"'
#  - Single-hash file:          "<64-hex>" possibly followed by file metadata
#  - Tabs / multi-space / mixed line endings tolerated
#  - UTF-8 BOM tolerated
#  - Leading "./" stripped from filenames in the file
#  - Filenames compared by basename, case-insensitive
#  - Comment lines starting with "#" skipped
#  - SHA-512 is opt-in via -Algorithm SHA512; SHA-256 behavior is unchanged.
#  - MD5 / SHA-1 lines deliberately ignored.
#
# Return shape:
#   `Resolve-ChecksumFromChecksumText` returns a [PSCustomObject] with:
#     Hash                — lowercase 64/128-hex string, or empty on failure
#     Reason              — short string for diagnostics: "Matched", "NoTarget",
#                           "TargetNotFound", "Ambiguous", "EmptyContent",
#                           "NoSha256Lines"
#     MatchedLine         — the raw line that produced the hash, or empty
#     Candidates          — count of lines that matched the target basename
#     SourceFormat        — "GNU", "BSD", "GitHubDigest", "BareHash", or "Mixed"
#     Algorithm           — "SHA256" or "SHA512"
#
#   `Get-Sha256FromSourceUrl` continues to return a plain string for backwards
#   compatibility — empty on any failure; lowercase hex on success.

function Get-CanonicalChecksumFilename {
    param([string]$RawName)

    if ([string]::IsNullOrWhiteSpace($RawName)) {
        return ""
    }

    $name = $RawName.Trim()

    # GNU sha256sum binary-mode prefix.
    if ($name.StartsWith("*")) {
        $name = $name.Substring(1)
    }

    # Some publishers wrap filenames in quotes — strip them.
    if (($name.Length -ge 2) -and (
            ($name.StartsWith('"') -and $name.EndsWith('"')) -or
            ($name.StartsWith("'") -and $name.EndsWith("'")))) {
        $name = $name.Substring(1, $name.Length - 2)
    }

    # Strip a leading "./" component.
    while ($name.StartsWith("./") -or $name.StartsWith(".\")) {
        $name = $name.Substring(2)
    }

    # Reduce to basename — checksum files sometimes carry relative paths.
    $name = ($name -replace '\\', '/').Trim()
    $lastSlash = $name.LastIndexOf('/')
    if ($lastSlash -ge 0) {
        $name = $name.Substring($lastSlash + 1)
    }

    return $name.Trim()
}

function Resolve-ChecksumFromChecksumText {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content,
        [AllowNull()][AllowEmptyString()][string]$TargetFileName,
        [ValidateSet("SHA256", "SHA512")][string]$Algorithm = "SHA256"
    )

    $normalizedAlgorithm = $Algorithm.ToUpperInvariant()
    $hashLength = if ($normalizedAlgorithm -eq "SHA512") { 128 } else { 64 }
    $bsdAlgorithmPattern = if ($normalizedAlgorithm -eq "SHA512") { "SHA512|SHA-512" } else { "SHA256|SHA-256" }
    $digestPrefix = if ($normalizedAlgorithm -eq "SHA512") { "sha512" } else { "sha256" }

    $result = [PSCustomObject]@{
        Hash         = ""
        Reason       = "EmptyContent"
        MatchedLine  = ""
        Candidates   = 0
        SourceFormat = ""
        Algorithm    = $normalizedAlgorithm
    }

    if ([string]::IsNullOrWhiteSpace($Content)) {
        return $result
    }

    # Strip UTF-8 BOM if present.
    if ($Content.Length -gt 0 -and [int][char]$Content[0] -eq 0xFEFF) {
        $Content = $Content.Substring(1)
    }

    $target = Get-CanonicalChecksumFilename -RawName $TargetFileName
    $hasTarget = -not [string]::IsNullOrWhiteSpace($target)
    $targetLower = if ($hasTarget) { $target.ToLowerInvariant() } else { "" }

    $gnuRegex = [regex]("^(?<hash>[0-9a-fA-F]{" + $hashLength + "})[\t ]+\*?(?<file>.+?)\s*$")
    $bsdRegex = [regex]("^(?<algo>" + $bsdAlgorithmPattern + ")\s*\((?<file>.+?)\)\s*=\s*(?<hash>[0-9a-fA-F]{" + $hashLength + "})\s*$")
    $githubDigestRegex = [regex]('"digest"\s*:\s*"' + $digestPrefix + ':(?<hash>[0-9a-fA-F]{' + $hashLength + '})"')
    $bareRegex = [regex]("^(?<hash>[0-9a-fA-F]{" + $hashLength + "})\s*$")

    $candidates = New-Object System.Collections.Generic.List[object]
    $digestCandidates = New-Object System.Collections.Generic.List[object]
    $firstSha256 = $null
    $firstFormat = ""
    $observedFormats = New-Object System.Collections.Generic.HashSet[string]
    $multiLineSeen = $false

    foreach ($rawLine in ($Content -split "(?:\r\n|\n|\r)")) {
        $line = $rawLine.Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line.StartsWith("#")) { continue }
        $githubDigestMatch = $githubDigestRegex.Match($line)
        if ($githubDigestMatch.Success) {
            [void]$observedFormats.Add("GitHubDigest")
            $candidateHash = $githubDigestMatch.Groups['hash'].Value.ToLowerInvariant()
            $digestCandidate = [PSCustomObject]@{ Hash = $candidateHash; Line = $line; Format = "GitHubDigest" }
            $digestCandidates.Add($digestCandidate) | Out-Null

            if ($null -eq $firstSha256) {
                $firstSha256 = $digestCandidate
                $firstFormat = "GitHubDigest"
            }
            continue
        }

        $bsdMatch = $bsdRegex.Match($line)
        if ($bsdMatch.Success) {
            [void]$observedFormats.Add("BSD")
            $multiLineSeen = $true
            $candidateName = Get-CanonicalChecksumFilename -RawName $bsdMatch.Groups['file'].Value
            $candidateHash = $bsdMatch.Groups['hash'].Value.ToLowerInvariant()

            if ($null -eq $firstSha256) {
                $firstSha256 = [PSCustomObject]@{ Hash = $candidateHash; Line = $line; Format = "BSD" }
                $firstFormat = "BSD"
            }

            if ($hasTarget -and $candidateName.ToLowerInvariant() -eq $targetLower) {
                $candidates.Add([PSCustomObject]@{ Hash = $candidateHash; Line = $line; Format = "BSD" }) | Out-Null
            }
            continue
        }

        $gnuMatch = $gnuRegex.Match($line)
        if ($gnuMatch.Success) {
            [void]$observedFormats.Add("GNU")
            $multiLineSeen = $true
            $candidateName = Get-CanonicalChecksumFilename -RawName $gnuMatch.Groups['file'].Value
            $candidateHash = $gnuMatch.Groups['hash'].Value.ToLowerInvariant()

            if ($null -eq $firstSha256) {
                $firstSha256 = [PSCustomObject]@{ Hash = $candidateHash; Line = $line; Format = "GNU" }
                $firstFormat = "GNU"
            }

            if ($hasTarget -and $candidateName.ToLowerInvariant() -eq $targetLower) {
                $candidates.Add([PSCustomObject]@{ Hash = $candidateHash; Line = $line; Format = "GNU" }) | Out-Null
            }
            continue
        }

        $bareMatch = $bareRegex.Match($line)
        if ($bareMatch.Success) {
            [void]$observedFormats.Add("BareHash")
            $candidateHash = $bareMatch.Groups['hash'].Value.ToLowerInvariant()

            if ($null -eq $firstSha256) {
                $firstSha256 = [PSCustomObject]@{ Hash = $candidateHash; Line = $line; Format = "BareHash" }
                $firstFormat = "BareHash"
            }
            # Bare-hash lines never produce a target-name match.
            continue
        }
    }

    $result.SourceFormat = if ($observedFormats.Count -eq 1) {
        @($observedFormats)[0]
    }
    elseif ($observedFormats.Count -gt 1) {
        "Mixed"
    }
    else {
        ""
    }

    if ($null -eq $firstSha256) {
        $result.Reason = if ($normalizedAlgorithm -eq "SHA512") { "NoSha512Lines" } else { "NoSha256Lines" }
        return $result
    }

    if (-not $hasTarget) {
        # Legacy behaviour: caller did not supply a filename context. Only allow
        # the first-hash-wins shortcut for single-hash files (no second SHA-256
        # line, no BSD entries that would be ambiguous).
        if ($firstFormat -eq "BareHash" -and -not $multiLineSeen) {
            $result.Hash = $firstSha256.Hash
            $result.Reason = "NoTarget"
            $result.MatchedLine = $firstSha256.Line
            return $result
        }

        # If the file contains exactly one SHA-256 entry (e.g. a `foo.iso.sha256`
        # that has the filename appended after the hash), it is safe to return
        # the first hash without a target.
        if ($firstFormat -ne "Mixed") {
            # Count matching checksum lines explicitly.
            $checksumLineCount = 0
            foreach ($rawLine in ($Content -split "(?:\r\n|\n|\r)")) {
                $line = $rawLine.Trim()
                if ([string]::IsNullOrWhiteSpace($line)) { continue }
                if ($line.StartsWith("#")) { continue }
                if ($bsdRegex.IsMatch($line) -or $gnuRegex.IsMatch($line) -or $githubDigestRegex.IsMatch($line) -or $bareRegex.IsMatch($line)) {
                    $checksumLineCount++
                }
            }

            if ($checksumLineCount -eq 1) {
                $result.Hash = $firstSha256.Hash
                $result.Reason = "NoTarget"
                $result.MatchedLine = $firstSha256.Line
                return $result
            }
        }

        $result.Reason = "AmbiguousNoTarget"
        return $result
    }

    if ($candidates.Count -eq 0) {
        if ($digestCandidates.Count -eq 1) {
            $result.Hash = $digestCandidates[0].Hash
            $result.Reason = "Matched"
            $result.MatchedLine = $digestCandidates[0].Line
            $result.Candidates = 1
            return $result
        }

        if ($digestCandidates.Count -gt 1) {
            $result.Reason = "Ambiguous"
            $result.Candidates = $digestCandidates.Count
            return $result
        }

        $result.Reason = "TargetNotFound"
        return $result
    }

    if ($candidates.Count -gt 1) {
        # If every candidate agrees on the same hash, treat as a non-ambiguous match.
        $distinct = ($candidates | Select-Object -ExpandProperty Hash -Unique)
        if (@($distinct).Count -eq 1) {
            $result.Hash = $candidates[0].Hash
            $result.Reason = "Matched"
            $result.MatchedLine = $candidates[0].Line
            $result.Candidates = $candidates.Count
            return $result
        }

        $result.Reason = "Ambiguous"
        $result.Candidates = $candidates.Count
        return $result
    }

    $result.Hash = $candidates[0].Hash
    $result.Reason = "Matched"
    $result.MatchedLine = $candidates[0].Line
    $result.Candidates = 1
    return $result
}

function Convert-ChecksumResponseContentToString {
    [CmdletBinding()]
    param(
        [AllowNull()]$Content
    )

    if ($null -eq $Content) {
        return ""
    }

    if ($Content -is [byte[]]) {
        return [Text.Encoding]::UTF8.GetString($Content)
    }

    if ($Content -is [Array] -and $Content.Length -gt 0 -and $Content[0] -is [byte]) {
        return [Text.Encoding]::UTF8.GetString([byte[]]$Content)
    }

    return [string]$Content
}

function Get-Sha256FromSourceUrl {
    <#
    .SYNOPSIS
    Fetch a vendor checksum file and return the SHA-256 hash for the target binary.

    .DESCRIPTION
    Backwards-compatible signature: callers that pass only -ShaUrl get the
    legacy "first hash in the file" behaviour ONLY when the file contains a
    single SHA-256 entry (e.g., a per-file `.sha256` companion). For multi-line
    checksum files, callers must pass -TargetFileName so the correct hash can
    be selected by filename.

    Always returns an empty string on any failure; never throws.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyString()][string]$ShaUrl,
        [string]$TargetFileName = ""
    )

    if ([string]::IsNullOrWhiteSpace($ShaUrl)) {
        return ""
    }

    $content = $null

    if (Test-Path -LiteralPath $ShaUrl -PathType Leaf) {
        try {
            $content = Get-Content -LiteralPath $ShaUrl -Raw -ErrorAction Stop
        }
        catch {
            if (Get-Command -Name Write-ToolkitLog -ErrorAction SilentlyContinue) {
                Write-ToolkitLog ("Checksum source file read failed: {0}" -f $_.Exception.Message) "WARN"
            }
            return ""
        }
    }
    else {
        try {
            $uri = [Uri]$ShaUrl
            if ($uri.Scheme -notin @("http", "https")) {
                return ""
            }

            $response = Invoke-WebRequest -Uri $ShaUrl -TimeoutSec 45 -UseBasicParsing -ErrorAction Stop
            $content = Convert-ChecksumResponseContentToString -Content $response.Content
        }
        catch {
            if (Get-Command -Name Write-ToolkitLog -ErrorAction SilentlyContinue) {
                Write-ToolkitLog ("Checksum source fetch failed: {0}" -f $_.Exception.Message) "WARN"
            }
            return ""
        }
    }

    if ($null -eq $content) {
        return ""
    }

    $resolution = Resolve-ChecksumFromChecksumText -Content $content -TargetFileName $TargetFileName -Algorithm SHA256

    if (Get-Command -Name Write-ToolkitLog -ErrorAction SilentlyContinue) {
        $diagnostic = "Checksum resolver: algorithm={0} reason={1} format='{2}' candidates={3} target='{4}'" -f `
            $resolution.Algorithm, $resolution.Reason, $resolution.SourceFormat, $resolution.Candidates, $TargetFileName
        if ([string]::IsNullOrWhiteSpace($resolution.Hash)) {
            Write-ToolkitLog $diagnostic "WARN"
        }
        else {
            Write-ToolkitLog $diagnostic "INFO"
        }
    }

    return $resolution.Hash
}

function Get-Sha512FromSourceUrl {
    <#
    .SYNOPSIS
    Fetch a vendor checksum file and return the SHA-512 hash for the target binary.

    .DESCRIPTION
    Narrow SHA-512 companion to Get-Sha256FromSourceUrl. It uses the same
    filename-aware resolver, but with -Algorithm SHA512. Multi-line checksum
    files require -TargetFileName; ambiguity returns an empty string.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][AllowNull()][AllowEmptyString()][string]$ShaUrl,
        [string]$TargetFileName = ""
    )

    if ([string]::IsNullOrWhiteSpace($ShaUrl)) {
        return ""
    }

    $content = $null

    if (Test-Path -LiteralPath $ShaUrl -PathType Leaf) {
        try {
            $content = Get-Content -LiteralPath $ShaUrl -Raw -ErrorAction Stop
        }
        catch {
            if (Get-Command -Name Write-ToolkitLog -ErrorAction SilentlyContinue) {
                Write-ToolkitLog ("Checksum source file read failed: {0}" -f $_.Exception.Message) "WARN"
            }
            return ""
        }
    }
    else {
        try {
            $uri = [Uri]$ShaUrl
            if ($uri.Scheme -notin @("http", "https")) {
                return ""
            }

            $response = Invoke-WebRequest -Uri $ShaUrl -TimeoutSec 45 -UseBasicParsing -ErrorAction Stop
            $content = Convert-ChecksumResponseContentToString -Content $response.Content
        }
        catch {
            if (Get-Command -Name Write-ToolkitLog -ErrorAction SilentlyContinue) {
                Write-ToolkitLog ("Checksum source fetch failed: {0}" -f $_.Exception.Message) "WARN"
            }
            return ""
        }
    }

    if ($null -eq $content) {
        return ""
    }

    $resolution = Resolve-ChecksumFromChecksumText -Content $content -TargetFileName $TargetFileName -Algorithm SHA512

    if (Get-Command -Name Write-ToolkitLog -ErrorAction SilentlyContinue) {
        $diagnostic = "Checksum resolver: algorithm={0} reason={1} format='{2}' candidates={3} target='{4}'" -f `
            $resolution.Algorithm, $resolution.Reason, $resolution.SourceFormat, $resolution.Candidates, $TargetFileName
        if ([string]::IsNullOrWhiteSpace($resolution.Hash)) {
            Write-ToolkitLog $diagnostic "WARN"
        }
        else {
            Write-ToolkitLog $diagnostic "INFO"
        }
    }

    return $resolution.Hash
}
