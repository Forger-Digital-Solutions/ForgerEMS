#requires -Version 5.1
<#
.SYNOPSIS
Creates a detached CMS (.p7s) signature over the exact bytes of a release gate
receipt, using an existing certificate-store code-signing identity.

.DESCRIPTION
Signs receipt bytes with SHA256 CMS so Test-ForgerEMSReleaseCertification.ps1 can
verify them against a pinned receipt-author thumbprint. The private key is never
exported. Fails closed when the receipt is malformed, when the certificate is
missing/expired/has no private key or lacks the Code Signing EKU, or when the
target .p7s already exists (never overwrites signed evidence). There is no
self-signed or test-certificate creation and no fake credential fallback.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReceiptPath,
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$CertificateThumbprint,
    [Parameter()][string]$SignaturePath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

try { Add-Type -AssemblyName System.Security.Cryptography.Pkcs -ErrorAction Stop }
catch { Add-Type -AssemblyName System.Security }

if ([string]::IsNullOrWhiteSpace($SignaturePath)) { $SignaturePath = "$ReceiptPath.p7s" }
if (Test-Path -LiteralPath $SignaturePath) {
    throw "Signature file already exists: $SignaturePath. Refusing to overwrite signed evidence."
}
if (-not (Test-Path -LiteralPath $ReceiptPath -PathType Leaf)) {
    throw "Receipt not found: $ReceiptPath"
}

# Minimal receipt shape guard — refuse to sign bytes that are not a gate receipt.
$receipt = Get-Content -LiteralPath $ReceiptPath -Raw | ConvertFrom-Json
$gate = [string]$receipt.gate
if ([int64]$receipt.schemaVersion -ne 1 -or
    @('Build','Tests','Integrity','Signing','Portable','Security','Policy',
      'CleanInstall','Upgrade','Uninstall','ResidueAudit','Reinstall',
      'DriverServiceTaskAudit','GuiValidation') -notcontains $gate -or
    @('PASS','FAIL','BLOCKED') -notcontains [string]$receipt.result) {
    throw "Receipt at $ReceiptPath is not a schemaVersion=1 gate receipt; refusing to sign."
}

$thumbprint = $CertificateThumbprint.ToUpperInvariant()
$cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction Stop |
    Where-Object { $_.Thumbprint -eq $thumbprint } | Select-Object -First 1
if ($null -eq $cert) {
    throw "Approved receipt-signing certificate $thumbprint was not found in CurrentUser/My or LocalMachine/My."
}
if (-not $cert.HasPrivateKey) {
    throw "Certificate $thumbprint has no private key; receipts cannot be signed with a public key only."
}
$now = Get-Date
if ($now -lt $cert.NotBefore -or $now -gt $cert.NotAfter) {
    throw "Certificate $thumbprint is not currently valid (NotBefore=$($cert.NotBefore), NotAfter=$($cert.NotAfter))."
}
$eku = $cert.Extensions | Where-Object { $_ -is [System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] }
if ($null -eq $eku -or @($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }).Count -eq 0) {
    throw "Certificate $thumbprint lacks the Code Signing EKU (1.3.6.1.5.5.7.3.3)."
}
if ($cert.GetNameInfo('SimpleName', $false) -ne 'Forger Digital Solutions') {
    throw "Certificate $thumbprint publisher is not exactly 'Forger Digital Solutions' (subject: $($cert.Subject))."
}
# The production helper may only sign with a chain-trusted identity — never a
# self-signed or untrusted certificate that could mint valid-looking .p7s.
# Revocation must be checked online across the entire chain and the certificate
# must chain for the Code Signing application policy; unavailable or revoked
# status fails closed rather than being suppressed.
$chain = New-Object System.Security.Cryptography.X509Certificates.X509Chain
try {
    $chain.ChainPolicy.RevocationMode = 'Online'
    $chain.ChainPolicy.RevocationFlag = 'EntireChain'
    [void]$chain.ChainPolicy.ApplicationPolicy.Add((New-Object System.Security.Cryptography.Oid('1.3.6.1.5.5.7.3.3')))
    if (-not $chain.Build($cert)) {
        $status = ($chain.ChainStatus | ForEach-Object { $_.Status }) -join ','
        throw "Certificate $thumbprint does not build a trusted code-signing chain with online revocation ($status); refusing to sign production receipts."
    }
} finally { $chain.Dispose() }

$receiptBytes = [IO.File]::ReadAllBytes($ReceiptPath)
$content = New-Object System.Security.Cryptography.Pkcs.ContentInfo -ArgumentList (, $receiptBytes)
$cms = New-Object System.Security.Cryptography.Pkcs.SignedCms -ArgumentList @($content, $true)   # detached
$signer = New-Object System.Security.Cryptography.Pkcs.CmsSigner($cert)
$signer.DigestAlgorithm = New-Object System.Security.Cryptography.Oid('2.16.840.1.101.3.4.2.1')  # SHA256
$cms.ComputeSignature($signer, $false)
# Exclusive create — the earlier Test-Path gate plus CreateNew mode closes the
# TOCTOU gap; an existing .p7s can never be overwritten.
$fs = [IO.FileStream]::new($SignaturePath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
try {
    $encoded = $cms.Encode()
    $fs.Write($encoded, 0, $encoded.Length)
} finally { $fs.Dispose() }
Write-Host "Signed receipt: $SignaturePath (signer thumbprint $thumbprint)"
exit 0
