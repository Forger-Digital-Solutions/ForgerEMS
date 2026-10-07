#Requires -Version 5.1
<#
.SYNOPSIS
    Signs a ForgerEMS release artifact with Authenticode (SHA-256 + RFC3161
    timestamp) and then verifies the signature on every invocation.
    Fails closed: any signing or verification problem aborts non-zero.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$Path,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9A-Fa-f]{40}$')]
    [string]$CertificateThumbprint,

    [Parameter(Mandatory)]
    [ValidateSet('CurrentUser', 'LocalMachine')]
    [string]$CertificateStore,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$SignToolPath,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$TimestampUrl,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$ExpectedPublisher,

    # Verify-only mode: run the same verification contract (signtool /pa,
    # Authenticode status, exact publisher, timestamp certificate) without
    # adding another signature. Used for artifacts the Inno callback has
    # already signed (e.g. cached signed uninstallers).
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Fail([string]$Message) {
    throw "sign-release-artifact: $Message"
}

function Test-PublicTimestampHost([string]$HostName) {
    $h = $HostName.Trim().TrimEnd('.')
    if ([string]::IsNullOrWhiteSpace($h)) { return $false }
    if ($h -eq 'localhost') { return $false }
    if ($h.EndsWith('.local', [System.StringComparison]::OrdinalIgnoreCase)) { return $false }

    $ip = $null
    if ([System.Net.IPAddress]::TryParse($h, [ref]$ip)) {
        # Literal IP: accept only a globally routable address.
        try {
            if ([System.Net.IPAddress]::IsLoopback($ip)) { return $false }
        } catch { return $false }
        if ($ip.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetwork) {
            $b = $ip.GetAddressBytes()
            # 0/8, 10/8, 127/8, 169.254/16, 172.16/12, 192.168/16, 224/4+, 100.64/10
            if ($b[0] -eq 0 -or $b[0] -eq 10 -or $b[0] -eq 127 -or $b[0] -ge 224) { return $false }
            if ($b[0] -eq 169 -and $b[1] -eq 254) { return $false }
            if ($b[0] -eq 172 -and $b[1] -ge 16 -and $b[1] -le 31) { return $false }
            if ($b[0] -eq 192 -and $b[1] -eq 168) { return $false }
            if ($b[0] -eq 100 -and $b[1] -ge 64 -and $b[1] -le 127) { return $false }
            return $true
        }
        if ($ip.AddressFamily -eq [System.Net.Sockets.AddressFamily]::InterNetworkV6) {
            if ($ip.IsIPv6LinkLocal -or $ip.IsIPv6SiteLocal) { return $false }
            $b = $ip.GetAddressBytes()
            if ($b[0] -eq 0xFE -and (($b[1] -band 0xC0) -eq 0x80)) { return $false } # fe80::/10
            if (($b[0] -band 0xFE) -eq 0xFC) { return $false } # fc00::/7 ULA
            if ($ip.Equals([System.Net.IPAddress]::IPv6Loopback)) { return $false }
            return $true
        }
        return $false
    }

    # DNS name: require a public-looking multi-label host.
    if ($h -notmatch '^[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9\-]*[A-Za-z0-9])?)+$') { return $false }
    return $true
}

function Test-TimestampUri([string]$Uri) {
    $parsed = $null
    if (-not [System.Uri]::TryCreate($Uri, [System.UriKind]::Absolute, [ref]$parsed)) {
        Fail "TimestampUrl is not an absolute URI: '$Uri'"
    }
    if ($parsed.Scheme -ne 'http' -and $parsed.Scheme -ne 'https') {
        Fail "TimestampUrl must use http or https (got '$($parsed.Scheme)')"
    }
    if ($parsed.UserInfo) { Fail "TimestampUrl must not contain credentials" }
    if ($parsed.Query) { Fail "TimestampUrl must not contain a query string" }
    if ($parsed.Fragment) { Fail "TimestampUrl must not contain a fragment" }
    if (-not (Test-PublicTimestampHost -HostName $parsed.Host)) {
        Fail "TimestampUrl host must be a public DNS name or globally routable IP (got '$($parsed.Host)')"
    }
}

function Assert-NoCallbackMetacharacters([string]$Value, [string]$Name) {
    # The Inno /S callback line is executed through a shell — reject anything
    # that could break quoting, inject a second command, or be expanded by
    # Inno's own $/% substitution. Parentheses/commas are safe inside the
    # $q-quoted argument and are not blocked.
    foreach ($ch in @('"', "'", '`', '&', '|', ';', '<', '>', '$', '%', "`r", "`n")) {
        if ($Value.Contains($ch)) {
            Fail "$Name contains a character not permitted in the signing callback: '$($Name)' value rejected"
        }
    }
}

$artifact = (Resolve-Path -LiteralPath $Path).ProviderPath
if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
    Fail "Artifact not found: $Path"
}

if (-not (Test-Path -LiteralPath $SignToolPath -PathType Leaf)) {
    Fail "SignTool not found: $SignToolPath"
}
$signtool = (Resolve-Path -LiteralPath $SignToolPath).ProviderPath

Test-TimestampUri -Uri $TimestampUrl

Assert-NoCallbackMetacharacters -Value $ExpectedPublisher -Name 'ExpectedPublisher'
Assert-NoCallbackMetacharacters -Value $CertificateThumbprint -Name 'CertificateThumbprint'

$storePath = "Cert:\$CertificateStore\My"
$cert = Get-ChildItem -LiteralPath $storePath -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $CertificateThumbprint.ToUpperInvariant() } |
    Select-Object -First 1
if ($null -eq $cert) {
    Fail "Certificate $CertificateThumbprint not found in $storePath"
}
if (-not $cert.HasPrivateKey) {
    Fail "Certificate has no private key"
}
$now = Get-Date
if ($cert.NotBefore -gt $now -or $cert.NotAfter -lt $now) {
    Fail "Certificate is not currently valid (NotBefore=$($cert.NotBefore.ToString('u')) NotAfter=$($cert.NotAfter.ToString('u')))"
}
$eku = $cert.EnhancedKeyUsageList | Where-Object { $_.ObjectId -eq '1.3.6.1.5.5.7.3.3' }
if ($null -eq $eku) {
    Fail "Certificate lacks the Code Signing EKU (1.3.6.1.5.5.7.3.3)"
}

if (-not $VerifyOnly) {
    $signArgs = [System.Collections.Generic.List[string]]::new()
    $signArgs.Add('sign')
    $signArgs.Add('/fd');  $signArgs.Add('sha256')
    $signArgs.Add('/tr');  $signArgs.Add($TimestampUrl)
    $signArgs.Add('/td');  $signArgs.Add('sha256')
    $signArgs.Add('/sha1'); $signArgs.Add($cert.Thumbprint)
    if ($CertificateStore -eq 'LocalMachine') { $signArgs.Add('/sm') }
    $signArgs.Add('/v')
    $signArgs.Add($artifact)

    $signOutput = & $signtool @signArgs 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        Fail "signtool sign failed (exit $LASTEXITCODE): $signOutput"
    }
}

$verifyArgs = @('verify', '/pa', '/v', $artifact)
$verifyOutput = & $signtool @verifyArgs 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) {
    Fail "signtool verify /pa failed (exit $LASTEXITCODE): $verifyOutput"
}

$signature = Get-AuthenticodeSignature -LiteralPath $artifact
if ($null -eq $signature -or $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
    $status = if ($null -eq $signature) { '<none>' } else { $signature.Status.ToString() }
    Fail "Authenticode verification failed - status $status"
}

$actualSubject = if ($null -ne $signature.SignerCertificate) { $signature.SignerCertificate.GetNameInfo('SimpleName', $false) } else { '' }
# Exact normalized match: trim whitespace, case-insensitive ordinal comparison
# against the certificate SimpleName (CN value), matching the build contract.
if (-not [string]::Equals($actualSubject.Trim(), $ExpectedPublisher.Trim(), [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail "Publisher mismatch - expected '$ExpectedPublisher', actual '$actualSubject'"
}

if ($null -eq $signature.TimeStamperCertificate) {
    Fail "Signature has no timestamp certificate (RFC3161 timestamp required)"
}

return @{
    Path        = $artifact
    Status      = $signature.Status.ToString()
    Subject     = $actualSubject
    Thumbprint  = $signature.SignerCertificate.Thumbprint
    Timestamped = $true
}
