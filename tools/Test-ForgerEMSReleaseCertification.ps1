#requires -Version 5.1
<#
.SYNOPSIS
Production certification endpoint. Independently verifies a ForgerEMS release
candidate against signed promotion receipts and writes release-certification.json.

.DESCRIPTION
This is the only authority that can mark a candidate productionEligible. It:
  * verifies the candidate manifest schema and recomputes every artifact hash/size
    (again at decision time so mid-check mutation fails);
  * verifies the source commit exists, the working tree is clean, and any
    sourceHead..HEAD delta touches only tools/, tests/, .github/workflows/, or
    FORGEREMS-V1.2.4-FINAL-REPORT.md;
  * verifies Authenticode (Valid, approved thumbprint, publisher "Forger Digital
    Solutions", timestamped) plus `signtool verify /pa /v` on Installer/Frontend/
    Uninstaller;
  * verifies the portable ZIP is fully safe (no rooted/drive/backslash traversal,
    no casefold duplicates, no symlink entries) and that the Frontend role hash
    matches the single expected ForgerEMS-v<version>/ForgerEMS.exe entry;
  * verifies a detached CMS .p7s for EVERY receipt: trusted chain
    (CheckSignature($false)), exactly one signer, SHA256 digest, signer thumbprint
    equal to -ApprovedReceiptSignerThumbprint, Code Signing EKU, publisher
    "Forger Digital Solutions", currently valid — with per-receipt diagnostics;
  * verifies every evidence leaf exists under that receipt's own directory with a
    matching hash;
  * then delegates policy to Get-ForgerEMSCertificationState.

Receipt bytes are parsed exactly once; the same parsed object and byte buffer feed
CMS verification, policy, and evidence checks. Any failure writes a fresh record
with productionEligible=false and exits nonzero. The output file is created
exclusively — an existing path, or one aliasing a manifest/receipt/artifact, is
refused. There are no skip/trust/force switches and no self-signed or
test-certificate fallback. The endpoint never modifies the candidate, receipts,
or ZIP.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RepoRoot,
    [Parameter(Mandatory)][string]$ReleaseRoot,
    [Parameter(Mandatory)][string]$CandidateManifest,
    [Parameter(Mandatory)][string]$ReceiptDirectory,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$ApprovedReceiptSignerThumbprint,
    [Parameter()][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$ApprovedArtifactSignerThumbprint,
    [Parameter()][string]$SignToolPath,
    [Parameter(Mandatory)][string]$OutputPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
# PS5.1 ships SignedCms in System.Security.dll; pwsh needs the Pkcs package.
try { Add-Type -AssemblyName System.Security.Cryptography.Pkcs -ErrorAction Stop }
catch { Add-Type -AssemblyName System.Security }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Import-Module (Join-Path $PSScriptRoot 'ForgerEMS.ReleaseCertification.psm1') -Force

$script:MaxReceiptBytes = 1MB

function Test-FeContainedPath {
    # Resolves $Relative under $Root, rejects escapes, and walks EVERY ancestor
    # directory up to $Root so a junction/symlink/reparse anywhere in the chain
    # cannot smuggle the target outside.
    param([string]$Root, [string]$Relative)
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $full = [IO.Path]::GetFullPath((Join-Path $rootFull $Relative))
    if (-not ($full.StartsWith($rootFull + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
             $full -eq $rootFull)) {
        return $false, $full
    }
    $cursor = $full
    while ($cursor -and -not $cursor.Equals($rootFull, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { return $false, $full }
        }
        $cursor = Split-Path -Parent $cursor
    }
    return $true, $full
}

function Test-FePathAliasesInput {
    param([string]$Path, [string[]]$Forbidden)
    $full = [IO.Path]::GetFullPath($Path)
    foreach ($f in $Forbidden) {
        if (-not [string]::IsNullOrWhiteSpace($f) -and
            $full.Equals([IO.Path]::GetFullPath($f), [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

function Write-FeResult {
    param([hashtable]$Record)
    $Record['schemaVersion'] = 1
    $Record['evaluatedUtc'] = (Get-Date).ToUniversalTime().ToString('o')
    $json = $Record | ConvertTo-Json -Depth 8
    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent) -and -not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    # Exclusive create: a pre-existing output (or a stale true record) is never
    # reused or overwritten.
    $fs = [IO.FileStream]::new($OutputPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($json)
        $fs.Write($bytes, 0, $bytes.Length)
    } finally { $fs.Dispose() }
}

$record = @{
    productionEligible = $false
    buildEligible = $false
    signingEligible = $false
    lifecycleEligible = $false
    securityEligible = $false
    blockingReasons = @()
    gates = @{}
    receiptDiagnostics = @()
    verifiedFacts = @{}
    inputs = [ordered]@{
        repoRoot = $RepoRoot; releaseRoot = $ReleaseRoot
        candidateManifest = $CandidateManifest; receiptDirectory = $ReceiptDirectory
        outputPath = $OutputPath
    }
}
$blockers = [System.Collections.Generic.List[string]]::new()
$receiptDiagnostics = [System.Collections.Generic.List[string]]::new()
$facts = @{}

try {
    if (Test-Path -LiteralPath $OutputPath) {
        throw "Output path already exists: $OutputPath. Refusing to overwrite; supply a fresh path."
    }

    # --- Parse candidate bytes once; hash and policy both see identical bytes ------
    # UTF8.GetString preserves the BOM (\uFEFF) which PS5.1 ConvertFrom-Json
    # rejects — strip it so the same bytes drive both hashing and parsing.
    function ConvertFrom-FeJsonBytes {
        param([byte[]]$Bytes)
        $text = [Text.Encoding]::UTF8.GetString($Bytes)
        if ($text.Length -gt 0 -and [int]$text[0] -eq 0xFEFF) { $text = $text.Substring(1) }
        return ($text | ConvertFrom-Json -ErrorAction Stop)
    }
    $candidateBytes = [IO.File]::ReadAllBytes($CandidateManifest)
    $candidate = ConvertFrom-FeJsonBytes -Bytes $candidateBytes
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $candidateManifestSha = ([BitConverter]::ToString($sha.ComputeHash($candidateBytes))).Replace('-', '')
    $sha.Dispose()
    $expectedVersion = (Get-Content -LiteralPath (Join-Path $RepoRoot 'VERSION') -Raw).Trim()

    # --- Output root must not escape via a junction/reparse parent either --------
    $outCursor = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
    while (-not [string]::IsNullOrWhiteSpace($outCursor) -and (Test-Path -LiteralPath $outCursor)) {
        $outItem = Get-Item -LiteralPath $outCursor -Force
        if ($outItem.Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Output path traverses a reparse point: $outCursor"
        }
        $outCursor = Split-Path -Parent $outCursor
    }

    # --- Parse each receipt's exact bytes once ------------------------------------
    $receiptEntries = @()
    if (Test-Path -LiteralPath $ReceiptDirectory -PathType Container) {
        $receiptFiles = @(Get-ChildItem -LiteralPath $ReceiptDirectory -Filter '*.json' -File -Recurse)
        foreach ($file in $receiptFiles) {
            # Bound BEFORE touching the bytes — a multi-GB blob is rejected by
            # size alone, not after we already parsed it.
            if ($file.Length -gt $script:MaxReceiptBytes) {
                $receiptDiagnostics.Add("$($file.Name) : receipt exceeds 1 MiB bound ($($file.Length) bytes)")
                $receiptEntries += [ordered]@{
                    File = $file.FullName
                    Directory = $file.DirectoryName
                    Bytes = [byte[]]@()
                    Parsed = [pscustomobject]@{ schemaVersion = 0; gate = $file.BaseName; malformed = $true }
                }
                continue
            }
            $bytes = [IO.File]::ReadAllBytes($file.FullName)
            $parsed = $null
            try { $parsed = ConvertFrom-FeJsonBytes -Bytes $bytes }
            catch {
                $receiptDiagnostics.Add("$($file.Name) : malformed JSON ($($_.Exception.Message))")
                $parsed = [pscustomobject]@{ schemaVersion = 0; gate = $file.BaseName; malformed = $true }
            }
            $receiptEntries += [ordered]@{
                File = $file.FullName
                Directory = $file.DirectoryName
                Bytes = $bytes
                Parsed = $parsed
            }
        }
    }
    $receipts = @($receiptEntries | ForEach-Object { $_.Parsed })

    # --- Output path may never alias a manifest, receipt, or artifact -------------
    $forbiddenOutputs = @($CandidateManifest) + @($receiptEntries | ForEach-Object { $_.File }) +
        @($receiptEntries | ForEach-Object { $_.File + '.p7s' })
    if (Test-FePathAliasesInput -Path $OutputPath -Forbidden $forbiddenOutputs) {
        throw "Output path aliases a candidate manifest or receipt file: $OutputPath"
    }

    $facts = @{
        ExpectedVersion = $expectedVersion
        CandidateManifestSha256 = $candidateManifestSha
        SourceCommitExists = $false
        SourceTreeClean = $false
        SourceDeltaAllowed = $false
        ArtifactFilesVerified = $false
        ArtifactSignaturesVerified = $false
        PortableFrontendVerified = $false
        ReceiptSignaturesTrusted = $false
        ReceiptEvidenceVerified = $false
    }

    # --- Source / integrity facts -------------------------------------------------
    # Under EAP=Stop a native command writing to stderr (e.g. git "fatal: Not a
    # valid object name" for a nonexistent commit) becomes a terminating error.
    # Run all git probes under Continue so nonzero exits degrade to false facts.
    $gitEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $headOut = @(git -C $RepoRoot rev-parse HEAD 2>$null)
        $headResolved = ($LASTEXITCODE -eq 0 -and $headOut.Count -gt 0)
        $head = ([string]($headOut | Select-Object -First 1)).Trim()
        $sourceHead = [string]$candidate.sourceHead
        git -C $RepoRoot cat-file -e "$sourceHead^{commit}" 2>$null | Out-Null
        $facts.SourceCommitExists = ($LASTEXITCODE -eq 0 -and $sourceHead -match '^[0-9A-Fa-f]{40}$')
        $dirty = @(git -C $RepoRoot status --porcelain 2>$null | Where-Object { $_ })
        # Fail closed when `git status` itself errored or HEAD could not resolve —
        # an empty porcelain list is only meaningful after a successful status.
        $facts.SourceTreeClean = ($LASTEXITCODE -eq 0 -and $headResolved -and $dirty.Count -eq 0)
        if ($facts.SourceCommitExists -and $headResolved) {
            if ($head -eq $sourceHead) {
                $facts.SourceDeltaAllowed = $true
            } else {
                $delta = @(git -C $RepoRoot diff --name-only "$sourceHead..HEAD" 2>$null | Where-Object { $_ })
                $facts.SourceDeltaAllowed = ($LASTEXITCODE -eq 0 -and
                    (@($delta | Where-Object { $_ -notmatch '^(tools/|tests/|\.github/workflows/|FORGEREMS-V1\.2\.4-FINAL-REPORT\.md$)' }).Count -eq 0))
            }
        }
    } finally {
        $ErrorActionPreference = $gitEap
    }

    # --- Artifact file hash/size verification (first pass) ------------------------
    $artifactPaths = @{}
    function Test-FeArtifactFiles {
        param([object]$CandidateObj, [hashtable]$Paths)
        $ok = $true
        foreach ($artifact in @($CandidateObj.artifacts)) {
            $relative = [string]$artifact.filename
            $contained, $full = Test-FeContainedPath -Root $ReleaseRoot -Relative $relative
            if (-not $contained -or -not (Test-Path -LiteralPath $full -PathType Leaf)) { $ok = $false; continue }
            if ($Paths.ContainsKey($full)) { $ok = $false; continue }
            $Paths[$full] = $true
            $item = Get-Item -LiteralPath $full -Force
            $hash = (Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash
            if ($hash -ne [string]$artifact.sha256 -or $item.Length -ne [int64]$artifact.sizeBytes) { $ok = $false }
        }
        return $ok
    }
    $facts.ArtifactFilesVerified = (Test-FeArtifactFiles -CandidateObj $candidate -Paths $artifactPaths) -and
        @($candidate.artifacts).Count -ge 3
    if (Test-FePathAliasesInput -Path $OutputPath -Forbidden (@($artifactPaths.Keys) + $forbiddenOutputs)) {
        throw "Output path aliases a candidate artifact: $OutputPath"
    }

    # --- Independent Authenticode + signtool verification --------------------------
    $signatureVerified = $true
    $signerRequired = @('Installer','Frontend','Uninstaller')
    if ([string]::IsNullOrWhiteSpace($ApprovedArtifactSignerThumbprint)) { $signatureVerified = $false }
    foreach ($artifact in @($candidate.artifacts)) {
        if ($signerRequired -notcontains [string]$artifact.role) { continue }
        $contained, $full = Test-FeContainedPath -Root $ReleaseRoot -Relative ([string]$artifact.filename)
        if (-not $contained -or -not (Test-Path -LiteralPath $full -PathType Leaf)) { $signatureVerified = $false; continue }
        $sig = Get-AuthenticodeSignature -FilePath $full
        if ($sig.Status -ne 'Valid' -or $null -eq $sig.SignerCertificate) { $signatureVerified = $false; continue }
        if ($sig.SignerCertificate.Thumbprint -ne $ApprovedArtifactSignerThumbprint.ToUpperInvariant()) { $signatureVerified = $false; continue }
        if ($sig.SignerCertificate.GetNameInfo('SimpleName', $false) -ne 'Forger Digital Solutions') { $signatureVerified = $false; continue }
        if ($null -eq $sig.TimeStamperCertificate) { $signatureVerified = $false; continue }
        if (-not [string]::IsNullOrWhiteSpace($SignToolPath) -and (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
            $sigEap = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            try { & $SignToolPath verify /pa /v $full 2>$null | Out-Null; $signToolExit = $LASTEXITCODE }
            finally { $ErrorActionPreference = $sigEap }
            if ($signToolExit -ne 0) { $signatureVerified = $false }
        } else { $signatureVerified = $false }
    }
    if (@($candidate.artifacts | Where-Object { $signerRequired -contains [string]$_.role }).Count -ne 3) {
        $signatureVerified = $false
    }
    $facts.ArtifactSignaturesVerified = $signatureVerified

    # --- Portable ZIP frontend identity, only after whole-archive safety ----------
    $portable = @($candidate.artifacts | Where-Object { [string]$_.role -eq 'Portable' })[0]
    $frontend = @($candidate.artifacts | Where-Object { [string]$_.role -eq 'Frontend' })[0]
    if ($null -ne $portable -and $null -ne $frontend) {
        $zipContained, $zipPath = Test-FeContainedPath -Root $ReleaseRoot -Relative ([string]$portable.filename)
        if ($zipContained -and (Test-Path -LiteralPath $zipPath -PathType Leaf)) {
            $zip = [IO.Compression.ZipFile]::OpenRead($zipPath)
            try {
                $archiveSafe = $true
                $seen = @{}
                foreach ($entry in $zip.Entries) {
                    $name = [string]$entry.FullName
                    if ($name.IndexOf([char]0) -ge 0 -or $name.Contains(':') -or
                        $name.StartsWith('/') -or $name.StartsWith('\') -or
                        ([IO.Path]::IsPathRooted($name))) { $archiveSafe = $false; break }
                    $segments = $name -split '[\\/]+'
                    if (@($segments | Where-Object { $_ -eq '..' -or $_ -eq '' }).Count -gt 0) { $archiveSafe = $false; break }
                    $folded = $name.ToLowerInvariant()
                    if ($seen.ContainsKey($folded)) { $archiveSafe = $false; break }
                    $seen[$folded] = $true
                    # Unix symlink entries (high 16 bits of ExternalAttributes)
                    $mode = ($entry.ExternalAttributes -shr 16) -band 0xF000
                    if ($mode -eq 0xA000) { $archiveSafe = $false; break }
                }
                if ($archiveSafe) {
                    $expectedName = "ForgerEMS-v$([string]$candidate.version)/ForgerEMS.exe"
                    $entries = @($zip.Entries | Where-Object { $_.FullName -eq $expectedName })
                    if ($entries.Count -eq 1) {
                        $stream = $entries[0].Open()
                        try {
                            $zipSha = [System.Security.Cryptography.SHA256]::Create()
                            try {
                                $zipHash = [BitConverter]::ToString($zipSha.ComputeHash($stream)).Replace('-', '')
                            } finally { $zipSha.Dispose() }
                        } finally { $stream.Dispose() }
                        $facts.PortableFrontendVerified = ($zipHash -eq [string]$frontend.sha256)
                    }
                }
            } finally { $zip.Dispose() }
        }
    }

    # --- Detached CMS trust for every receipt (parse-once bytes) -------------------
    $receiptTrusted = ($receiptEntries.Count -gt 0)
    foreach ($entry in $receiptEntries) {
        $name = Split-Path -Leaf $entry.File
        $diag = { param($m) $receiptDiagnostics.Add("$name : $m") }.GetNewClosure()
        if ($entry.Bytes.Length -gt $script:MaxReceiptBytes) { & $diag 'receipt exceeds 1 MiB bound'; $receiptTrusted = $false; continue }
        $signaturePath = $entry.File + '.p7s'
        if (-not (Test-Path -LiteralPath $signaturePath -PathType Leaf)) { & $diag 'missing detached .p7s'; $receiptTrusted = $false; continue }
        $signatureInfo = Get-Item -LiteralPath $signaturePath -Force
        if ($signatureInfo.Length -gt $script:MaxReceiptBytes) { & $diag 'detached .p7s exceeds 1 MiB bound'; $receiptTrusted = $false; continue }
        try {
            $content = New-Object System.Security.Cryptography.Pkcs.ContentInfo -ArgumentList (, $entry.Bytes)
            $cms = New-Object System.Security.Cryptography.Pkcs.SignedCms -ArgumentList @($content, $true)
            $cms.Decode([IO.File]::ReadAllBytes($signaturePath))
            try {
                $cms.CheckSignature($false)   # full trusted-chain verification, no suppression
            } catch {
                & $diag ("trusted-chain verification failed: " + $_.Exception.Message)
                $receiptTrusted = $false; continue
            }
            if ($cms.SignerInfos.Count -ne 1) { & $diag "signer count $($cms.SignerInfos.Count) != 1"; $receiptTrusted = $false; continue }
            $signer = $cms.SignerInfos[0]
            if ($signer.DigestAlgorithm.Value -ne '2.16.840.1.101.3.4.2.1') { & $diag "digest OID $($signer.DigestAlgorithm.Value) is not SHA256"; $receiptTrusted = $false; continue }
            $cert = $signer.Certificate
            if ($null -eq $cert) { & $diag 'signer certificate absent'; $receiptTrusted = $false; continue }
            if ($cert.Thumbprint -ne $ApprovedReceiptSignerThumbprint.ToUpperInvariant()) { & $diag "signer thumbprint $($cert.Thumbprint) does not match pinned thumbprint"; $receiptTrusted = $false; continue }
            $eku = $cert.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
            if ($null -eq $eku -or @($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }).Count -eq 0) {
                & $diag 'signer lacks Code Signing EKU'; $receiptTrusted = $false; continue
            }
            if ($cert.GetNameInfo('SimpleName', $false) -ne 'Forger Digital Solutions') { & $diag 'signer publisher is not Forger Digital Solutions'; $receiptTrusted = $false; continue }
            $now = Get-Date
            if ($now -lt $cert.NotBefore -or $now -gt $cert.NotAfter) { & $diag 'signer certificate outside validity window'; $receiptTrusted = $false; continue }
            # Explicit chain validation: SignedCms.CheckSignature does not enforce
            # revocation or application policy, so production trust requires an
            # online-revocation, CodeSigning-policy chain build that fails closed
            # when revocation status is unavailable or the chain is untrusted.
            $chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
            try {
                $chain.ChainPolicy.RevocationMode = 'Online'
                $chain.ChainPolicy.RevocationFlag = 'EntireChain'
                [void]$chain.ChainPolicy.ApplicationPolicy.Add((New-Object System.Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
                if (-not $chain.Build($cert)) {
                    & $diag ("explicit chain validation failed: " + (($chain.ChainStatus | ForEach-Object { $_.Status }) -join ','))
                    $receiptTrusted = $false; continue
                }
            } finally { $chain.Dispose() }
        } catch {
            & $diag ("CMS decode/verify exception: " + $_.Exception.Message)
            $receiptTrusted = $false
        }
    }
    $facts.ReceiptSignaturesTrusted = $receiptTrusted

    # --- Evidence leaves: relative to each receipt's own directory ------------------
    $evidenceVerified = ($receiptEntries.Count -gt 0)
    foreach ($entry in $receiptEntries) {
        $name = Split-Path -Leaf $entry.File
        $receipt = $entry.Parsed
        $evidence = @()
        if ($null -ne $receipt.PSObject.Properties['evidence'] -and $null -ne $receipt.evidence) {
            $evidence = @($receipt.evidence)
        }
        if ($evidence.Count -lt 1) {
            $evidenceVerified = $false
            $receiptDiagnostics.Add("$name : receipt declares no evidence leaves")
            continue
        }
        foreach ($leaf in $evidence) {
            $contained, $full = Test-FeContainedPath -Root $entry.Directory -Relative ([string]$leaf.path)
            if (-not $contained -or -not (Test-Path -LiteralPath $full -PathType Leaf)) {
                $evidenceVerified = $false
                $receiptDiagnostics.Add("$name : evidence leaf '$($leaf.path)' escapes the receipt directory or is missing")
                continue
            }
            if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash -ne [string]$leaf.sha256) {
                $evidenceVerified = $false
                $receiptDiagnostics.Add("$name : evidence leaf '$($leaf.path)' hash mismatch")
            }
        }
    }
    $facts.ReceiptEvidenceVerified = $evidenceVerified

    # --- Decision-time rehash: mutation during checks must fail the promotion -----
    $rehashPaths = @{}
    if ($facts.ArtifactFilesVerified) {
        $facts.ArtifactFilesVerified = Test-FeArtifactFiles -CandidateObj $candidate -Paths $rehashPaths
    }

    # --- Delegate to the pure policy core ------------------------------------------
    $state = Get-ForgerEMSCertificationState -Candidate $candidate -Receipts $receipts -VerifiedFacts $facts
    $record.gates = $state.gates
    $record.blockingReasons = @($state.blockingReasons + $receiptDiagnostics)
    $record.receiptDiagnostics = @($receiptDiagnostics)
    $record.buildId = $state.buildId
    $record.sourceHead = $state.sourceHead
    $record.version = $state.version
    $record.artifactHashes = @{}
    foreach ($artifact in @($candidate.artifacts)) {
        $record.artifactHashes[[string]$artifact.role] = [ordered]@{
            filename = [string]$artifact.filename
            sha256 = [string]$artifact.sha256
            sizeBytes = [int64]$artifact.sizeBytes
        }
    }
    $record.buildEligible = $state.buildEligible
    $record.signingEligible = $state.signingEligible
    $record.lifecycleEligible = $state.lifecycleEligible
    $record.securityEligible = $state.securityEligible
    $record.productionEligible = $state.productionEligible
}
catch {
    $blockers.Add("endpoint evaluation failed: $($_.Exception.Message)")
    $record.blockingReasons = @($blockers + $record.blockingReasons)
}

$record.verifiedFacts = $facts
$record.receiptDiagnostics = @($receiptDiagnostics)
try {
    Write-FeResult -Record $record
} catch {
    Write-Error "Could not write certification record to ${OutputPath}: $($_.Exception.Message)"
    exit 2
}
if ($record.productionEligible -eq $true) { exit 0 }
exit 1
