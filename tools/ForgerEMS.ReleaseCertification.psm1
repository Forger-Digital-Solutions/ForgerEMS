#requires -Version 5.1
<#
.SYNOPSIS
Pure ForgerEMS release-certification policy evaluator.

.DESCRIPTION
Get-ForgerEMSCertificationState validates a candidate-certification.json document and a
set of gate receipts, then derives eligibility booleans. The core is pure: every fact
that requires touching the filesystem, git, Authenticode, CMS signatures, or the network
is supplied through -VerifiedFacts by the trusted endpoint (Test-ForgerEMSReleaseCertification.ps1).
Core results alone are not certification authority — only the endpoint's independently
checked facts can produce productionEligible=true. There are no skip/trust/force switches.
#>

Set-StrictMode -Version Latest

$script:FeMandatoryGates = @(
    'Build','Tests','Integrity','Signing','Portable','Security','Policy',
    'CleanInstall','Upgrade','Uninstall','ResidueAudit','Reinstall',
    'DriverServiceTaskAudit','GuiValidation')
$script:FeLifecycleGates = @(
    'CleanInstall','Upgrade','Uninstall','ResidueAudit','Reinstall',
    'DriverServiceTaskAudit','GuiValidation')
$script:FeIsolationKinds = @('QEMU','VirtualBox','GitHubHosted')
$script:FeReceiptResults = @('PASS','FAIL','BLOCKED')
$script:FeArtifactRoles = @('Installer','Portable','Frontend','Uninstaller')
# Build/Tests/Policy gate runs legitimately pre-date the post-packaging manifest;
# all other gates must observe the final signed hashes they bind to.
$script:FePreManifestGates = @('Build','Tests','Policy')
$script:FeRequiredFactKeys = @(
    'SourceCommitExists','SourceTreeClean','SourceDeltaAllowed',
    'ArtifactFilesVerified','ArtifactSignaturesVerified','PortableFrontendVerified',
    'ReceiptSignaturesTrusted','ReceiptEvidenceVerified')

function Test-FeUtcTimestamp {
    # Requires an explicit UTC zone designator (Z or a zero +00:00/-00:00
    # offset). Zone-less or nonzero-offset stamps are rejected outright.
    param([object]$Value)
    if ($null -eq $Value) { return $null }
    $text = [string]$Value
    if ($text -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|\+00:?00|-00:?00)$') { return $null }
    $parsed = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse($text, [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed)) {
        return $null
    }
    if ($parsed.Offset -ne [TimeSpan]::Zero) { return $null }
    if ($parsed.Year -lt 2000) { return $null }
    return $parsed
}

function Test-FeSafeRelativePath {
    param([object]$Value)
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $false }
    if ($text.IndexOf([char]0) -ge 0) { return $false }              # NUL
    if ($text.Contains(':')) { return $false }                       # drive/ADS
    if ($text.StartsWith('/') -or $text.StartsWith('\') -or $text.StartsWith('~')) { return $false }
    if ([IO.Path]::IsPathRooted($text)) { return $false }
    foreach ($segment in ($text -split '[\\/]+')) {
        if ([string]::IsNullOrWhiteSpace($segment) -or $segment -eq '.' -or $segment -eq '..') { return $false }
    }
    return $true
}

function Test-FeGuid {
    param([object]$Value)
    $parsed = [Guid]::Empty
    return (-not [string]::IsNullOrWhiteSpace([string]$Value)) -and
        [Guid]::TryParse([string]$Value, [ref]$parsed) -and $parsed -ne [Guid]::Empty
}

function Get-FeInt64 {
    param([object]$Node, [string]$Name)
    $text = Get-FeString $Node $Name
    $parsed = 0L
    if ([int64]::TryParse($text, [ref]$parsed)) { return $parsed }
    return $null
}

function Test-FeHex {
    param([object]$Value, [int]$Length)
    return ([string]$Value) -match ('^[0-9A-Fa-f]{' + $Length + '}$')
}

function Get-FeString {
    param([object]$Node, [string]$Name)
    if ($null -eq $Node) { return $null }
    $property = $Node.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return [string]$property.Value
}

function Test-FeCandidate {
    param([object]$Candidate, [string]$ExpectedVersion, [DateTimeOffset]$Now,
          [System.Collections.Generic.List[string]]$Reasons)
    $valid = $true
    if ($null -eq $Candidate) { $Reasons.Add('candidate manifest is missing'); return $false }
    if ((Get-FeInt64 $Candidate 'schemaVersion') -ne 1) { $Reasons.Add('candidate schemaVersion is not 1'); $valid = $false }
    if (-not (Test-FeGuid (Get-FeString $Candidate 'buildId'))) { $Reasons.Add('candidate buildId is not a nonempty non-zero GUID'); $valid = $false }
    $candidateGenerated = Test-FeUtcTimestamp (Get-FeString $Candidate 'generatedUtc')
    if ($null -eq $candidateGenerated) { $Reasons.Add('candidate generatedUtc is not a valid UTC timestamp'); $valid = $false }
    elseif ($candidateGenerated -gt ($Now + [TimeSpan]::FromMinutes(5))) { $Reasons.Add('candidate generatedUtc is in the future beyond 5-minute skew'); $valid = $false }
    if (-not (Test-FeHex (Get-FeString $Candidate 'sourceHead') 40)) { $Reasons.Add('candidate sourceHead is not 40-hex'); $valid = $false }
    if ((Get-FeString $Candidate 'version') -ne $ExpectedVersion) { $Reasons.Add('candidate version does not match VERSION'); $valid = $false }
    if ((Get-FeString $Candidate 'architecture') -ne 'x64') { $Reasons.Add('candidate architecture is not x64'); $valid = $false }
    if ((Get-FeString $Candidate 'runtime') -ne 'win-x64') { $Reasons.Add('candidate runtime is not win-x64'); $valid = $false }
    $dirty = Get-FeString $Candidate 'sourceDirtyFileCount'
    if ($dirty -ne '0') { $Reasons.Add('candidate sourceDirtyFileCount is not 0'); $valid = $false }

    $artifacts = @()
    if ($null -ne $Candidate -and $null -ne $Candidate.PSObject.Properties['artifacts'] -and $null -ne $Candidate.artifacts) {
        $artifacts = @($Candidate.artifacts)
    }
    $roles = @{}
    $fileNames = @{}
    foreach ($artifact in $artifacts) {
        $role = Get-FeString $artifact 'role'
        if ([string]::IsNullOrWhiteSpace($role) -or $script:FeArtifactRoles -notcontains $role) {
            $Reasons.Add("artifact has missing or unknown role '$role'"); $valid = $false; continue
        }
        if ($roles.ContainsKey($role)) { $Reasons.Add("duplicate artifact role $role"); $valid = $false; continue }
        $roles[$role] = $artifact
        $fileName = Get-FeString $artifact 'filename'
        if (-not (Test-FeSafeRelativePath $fileName)) { $Reasons.Add("artifact $role filename is not a safe contained relative path"); $valid = $false }
        elseif ($fileNames.ContainsKey($fileName.ToLowerInvariant())) { $Reasons.Add("artifact $role reuses filename $fileName"); $valid = $false }
        else { $fileNames[$fileName.ToLowerInvariant()] = $true }
        if (-not (Test-FeHex (Get-FeString $artifact 'sha256') 64)) { $Reasons.Add("artifact $role sha256 is not 64-hex"); $valid = $false }
        $size = Get-FeString $artifact 'sizeBytes'
        $sizeValue = 0L
        if (-not [int64]::TryParse($size, [ref]$sizeValue) -or $sizeValue -le 0) { $Reasons.Add("artifact $role sizeBytes is not a positive integer"); $valid = $false }
    }
    foreach ($required in @('Installer','Portable','Frontend')) {
        if (-not $roles.ContainsKey($required)) { $Reasons.Add("candidate is missing required artifact role $required"); $valid = $false }
    }
    # Uninstaller is a required role for production; unsigned engineering
    # candidates may omit it, but that omission alone blocks promotion.
    return @{ Valid = $valid; Roles = $roles }
}

function Test-FeReceipt {
    param(
        [object]$Receipt,
        [object]$Candidate,
        [hashtable]$Roles,
        [string]$ManifestSha256,
        [DateTimeOffset]$CandidateGeneratedUtc,
        [DateTimeOffset]$Now,
        [bool]$IsLifecycle,
        [System.Collections.Generic.List[string]]$Reasons)
    $valid = $true
    $gate = Get-FeString $Receipt 'gate'
    if ((Get-FeInt64 $Receipt 'schemaVersion') -ne 1) { $Reasons.Add("receipt '$gate': schemaVersion is not 1"); $valid = $false }
    $result = Get-FeString $Receipt 'result'
    if ($script:FeReceiptResults -notcontains $result) { $Reasons.Add("receipt '$gate': result must be PASS/FAIL/BLOCKED"); $valid = $false }
    if ((Get-FeString $Receipt 'sourceHead') -ne (Get-FeString $Candidate 'sourceHead')) { $Reasons.Add("receipt '$gate': sourceHead does not bind candidate"); $valid = $false }
    if ((Get-FeString $Receipt 'version') -ne (Get-FeString $Candidate 'version')) { $Reasons.Add("receipt '$gate': version does not bind candidate"); $valid = $false }
    if ((Get-FeString $Receipt 'architecture') -ne (Get-FeString $Candidate 'architecture')) { $Reasons.Add("receipt '$gate': architecture does not bind candidate"); $valid = $false }
    if ((Get-FeString $Receipt 'buildId') -ne (Get-FeString $Candidate 'buildId')) { $Reasons.Add("receipt '$gate': buildId does not bind candidate"); $valid = $false }
    if ((Get-FeString $Receipt 'manifestSha256') -ne $ManifestSha256) { $Reasons.Add("receipt '$gate': manifestSha256 does not match the candidate manifest file hash"); $valid = $false }
    $installer = $Roles['Installer']
    if ((Get-FeString $Receipt 'installerFilename') -ne (Get-FeString $installer 'filename')) { $Reasons.Add("receipt '$gate': installerFilename does not bind the Installer role"); $valid = $false }
    if ((Get-FeString $Receipt 'installerSha256') -ne (Get-FeString $installer 'sha256')) { $Reasons.Add("receipt '$gate': installerSha256 does not bind the Installer role"); $valid = $false }

    $generated = Test-FeUtcTimestamp (Get-FeString $Receipt 'generatedUtc')
    $started = Test-FeUtcTimestamp (Get-FeString $Receipt 'startedUtc')
    $completed = Test-FeUtcTimestamp (Get-FeString $Receipt 'completedUtc')
    if ($null -eq $generated -or $null -eq $started -or $null -eq $completed) {
        $Reasons.Add("receipt '$gate': generatedUtc/startedUtc/completedUtc are not valid explicit-UTC timestamps"); $valid = $false
    } else {
        # Ordering: receipt was written after its own run, and after the
        # candidate manifest it binds to.
        if ($completed -lt $started) { $Reasons.Add("receipt '$gate': completedUtc precedes startedUtc"); $valid = $false }
        if ($generated -lt $completed) { $Reasons.Add("receipt '$gate': generatedUtc precedes completedUtc"); $valid = $false }
        if ($generated -lt $CandidateGeneratedUtc) { $Reasons.Add("receipt '$gate': generatedUtc predates the candidate"); $valid = $false }
        # Lifecycle/integration gates must observe the final signed hashes —
        # their start AND completion post-date the manifest. Build/Tests/Policy
        # runs legitimately precede packaging (their signed attestation is
        # created later, so generatedUtc is still bound to the candidate).
        if ($script:FePreManifestGates -notcontains $gate -and
            ($started -lt $CandidateGeneratedUtc -or $completed -lt $CandidateGeneratedUtc)) {
            $Reasons.Add("receipt '$gate': run window predates the candidate manifest"); $valid = $false
        }
        $skewAllowance = [TimeSpan]::FromMinutes(5)
        foreach ($stamp in @($generated, $started, $completed)) {
            if ($stamp -gt ($Now + $skewAllowance)) { $Reasons.Add("receipt '$gate': timestamp is in the future beyond 5-minute skew"); $valid = $false }
        }
    }

    $evidence = @()
    if ($null -ne $Receipt.PSObject.Properties['evidence'] -and $null -ne $Receipt.evidence) {
        $evidence = @($Receipt.evidence)
    }
    if ($evidence.Count -lt 1) { $Reasons.Add("receipt '$gate': production requires at least one evidence leaf"); $valid = $false }
    foreach ($leaf in $evidence) {
        if (-not (Test-FeSafeRelativePath (Get-FeString $leaf 'path'))) { $Reasons.Add("receipt '$gate': evidence path is not a safe contained relative path"); $valid = $false }
        if (-not (Test-FeHex (Get-FeString $leaf 'sha256') 64)) { $Reasons.Add("receipt '$gate': evidence leaf sha256 is not 64-hex"); $valid = $false }
    }

    if ($IsLifecycle) {
        $guest = $null
        if ($null -ne $Receipt.PSObject.Properties['guest']) { $guest = $Receipt.guest }
        if ($null -eq $guest) { $Reasons.Add("receipt '$gate': lifecycle receipt requires a guest object"); $valid = $false }
        else {
            if (-not (Test-FeGuid (Get-FeString $guest 'vmId'))) { $Reasons.Add("receipt '$gate': guest.vmId is not a GUID"); $valid = $false }
            if ([string]::IsNullOrWhiteSpace((Get-FeString $guest 'osVersion'))) { $Reasons.Add("receipt '$gate': guest.osVersion is empty"); $valid = $false }
            if ([string]::IsNullOrWhiteSpace((Get-FeString $guest 'osBuild'))) { $Reasons.Add("receipt '$gate': guest.osBuild is empty"); $valid = $false }
            if ((Get-FeString $guest 'architecture') -ne 'x64') { $Reasons.Add("receipt '$gate': guest.architecture is not x64"); $valid = $false }
            if ($script:FeIsolationKinds -notcontains (Get-FeString $guest 'isolationKind')) { $Reasons.Add("receipt '$gate': guest.isolationKind is not QEMU/VirtualBox/GitHubHosted"); $valid = $false }
        }
    }
    return $valid
}

function Get-ForgerEMSCertificationState {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]$Candidate,
        [Parameter(Mandatory)][AllowEmptyCollection()]$Receipts,
        [Parameter(Mandatory)][hashtable]$VerifiedFacts,
        [DateTimeOffset]$Now = [DateTimeOffset]::UtcNow)

    $reasons = [System.Collections.Generic.List[string]]::new()
    $expectedVersion = [string]$VerifiedFacts['ExpectedVersion']
    $manifestSha256 = [string]$VerifiedFacts['CandidateManifestSha256']
    $fact = @{}
    foreach ($key in $script:FeRequiredFactKeys) {
        $value = $false
        if ($VerifiedFacts.ContainsKey($key) -and $VerifiedFacts[$key] -eq $true) { $value = $true }
        $fact[$key] = $value
        if (-not $value) { $reasons.Add("verified fact $key is false or missing") }
    }

    $candidateCheck = Test-FeCandidate -Candidate $Candidate -ExpectedVersion $expectedVersion -Now $Now -Reasons $reasons
    $roles = $candidateCheck.Roles
    $candidateValid = [bool]$candidateCheck.Valid

    $gateStates = [ordered]@{}
    foreach ($gate in $script:FeMandatoryGates) { $gateStates[$gate] = 'MISSING' }

    $receiptList = @($Receipts)
    $lifecycleVmIds = [System.Collections.Generic.List[string]]::new()
    $candidateGeneratedUtc = Test-FeUtcTimestamp (Get-FeString $Candidate 'generatedUtc')
    if ($null -eq $candidateGeneratedUtc) { $candidateGeneratedUtc = [DateTimeOffset]::MinValue }

    foreach ($receipt in $receiptList) {
        $gate = Get-FeString $receipt 'gate'
        if ([string]::IsNullOrWhiteSpace($gate) -or $script:FeMandatoryGates -notcontains $gate) {
            $reasons.Add("receipt has missing or unknown gate '$gate'")
            continue
        }
        if ($gateStates[$gate] -ne 'MISSING') {
            $gateStates[$gate] = 'DUPLICATE'
            $reasons.Add("duplicate receipt for gate $gate")
            continue
        }
        $isLifecycle = $script:FeLifecycleGates -contains $gate
        $receiptValid = Test-FeReceipt -Receipt $receipt -Candidate $Candidate -Roles $roles `
            -ManifestSha256 $manifestSha256 -CandidateGeneratedUtc $candidateGeneratedUtc `
            -Now $Now -IsLifecycle $isLifecycle -Reasons $reasons
        $result = Get-FeString $receipt 'result'
        $gateStates[$gate] = if (-not $receiptValid) { 'MALFORMED' } elseif ($result -eq 'PASS') { 'PASS' } else { $result }
        if ($isLifecycle -and $receiptValid -and $null -ne $receipt.PSObject.Properties['guest'] -and $null -ne $receipt.guest) {
            $lifecycleVmIds.Add((Get-FeString $receipt.guest 'vmId'))
        }
        if ($gateStates[$gate] -ne 'PASS') {
            $reasons.Add("gate $gate state is $($gateStates[$gate])")
        }
    }

    foreach ($gate in $script:FeMandatoryGates) {
        if ($gateStates[$gate] -eq 'MISSING') { $reasons.Add("mandatory gate $gate has no receipt") }
    }

    $distinctVmIds = @($lifecycleVmIds | Select-Object -Unique)
    $lifecycleSameCampaign = $distinctVmIds.Count -le 1
    if (-not $lifecycleSameCampaign) {
        $reasons.Add('lifecycle receipts span multiple VM identities; one campaign vmId is required')
    }
    $uninstallerPresent = $roles.ContainsKey('Uninstaller')
    if (-not $uninstallerPresent) {
        $reasons.Add('candidate has no Uninstaller role; unsigned engineering staging only, production rejects')
    }

    $gatesPass = @($script:FeMandatoryGates | Where-Object { $gateStates[$_] -ne 'PASS' }).Count -eq 0
    $sourceClean = $fact['SourceCommitExists'] -and $fact['SourceTreeClean'] -and $fact['SourceDeltaAllowed']

    $buildEligible = $candidateValid -and
        $gateStates['Build'] -eq 'PASS' -and $gateStates['Tests'] -eq 'PASS' -and
        $gateStates['Integrity'] -eq 'PASS' -and $sourceClean
    $signingEligible = $gateStates['Signing'] -eq 'PASS' -and $fact['ArtifactSignaturesVerified']
    $lifecycleEligible = $lifecycleSameCampaign -and
        (@($script:FeLifecycleGates | Where-Object { $gateStates[$_] -ne 'PASS' }).Count -eq 0)
    $securityEligible = $gateStates['Security'] -eq 'PASS' -and $gateStates['Portable'] -eq 'PASS' -and
        $gateStates['Policy'] -eq 'PASS'
    $productionEligible = $candidateValid -and $gatesPass -and $lifecycleSameCampaign -and $uninstallerPresent -and
        $buildEligible -and $signingEligible -and $lifecycleEligible -and $securityEligible -and
        $fact['ReceiptSignaturesTrusted'] -and $fact['ArtifactFilesVerified'] -and
        $fact['PortableFrontendVerified'] -and $fact['ReceiptEvidenceVerified'] -and
        $reasons.Count -eq 0

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        candidateValid = $candidateValid
        buildId = (Get-FeString $Candidate 'buildId')
        sourceHead = (Get-FeString $Candidate 'sourceHead')
        version = (Get-FeString $Candidate 'version')
        gates = $gateStates
        lifecycleVmIds = @($distinctVmIds)
        blockingReasons = @($reasons)
        verifiedFacts = $fact
        buildEligible = $buildEligible
        signingEligible = $signingEligible
        lifecycleEligible = $lifecycleEligible
        securityEligible = $securityEligible
        productionEligible = $productionEligible
    }
}

Export-ModuleMember -Function Get-ForgerEMSCertificationState
