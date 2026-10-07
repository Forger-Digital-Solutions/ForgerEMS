#Requires -Version 5.1
<#
.SYNOPSIS
Provisions an explicitly supplied production identity in a disposable GitHub-hosted Windows runner.
.DESCRIPTION
Reads protected-environment secrets only from process environment. No PFX file,
private-key export, certificate generation, or secret-bearing output is produced.
Local production builds continue to use already-provisioned certificate stores.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9A-Fa-f]{40}$')][string]$CertificateThumbprint,
    [string]$ExpectedPublisher = 'Forger Digital Solutions'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted' -or $env:RUNNER_OS -ne 'Windows') {
    throw 'Certificate provisioning is restricted to a disposable GitHub-hosted Windows release runner.'
}
if ([string]::IsNullOrWhiteSpace($ExpectedPublisher)) { throw 'An explicit expected publisher is required.' }
$thumbprint = $CertificateThumbprint.ToUpperInvariant()
if (Test-Path -LiteralPath "Cert:\CurrentUser\My\$thumbprint") {
    throw 'Refusing to replace an existing certificate. Use a fresh release runner.'
}
$encoded = [Environment]::GetEnvironmentVariable('FORGEREMS_SIGNING_PFX_BASE64')
$password = [Environment]::GetEnvironmentVariable('FORGEREMS_SIGNING_PFX_PASSWORD')
if ([string]::IsNullOrWhiteSpace($encoded) -or [string]::IsNullOrEmpty($password)) {
    throw 'Production signing requires protected-environment PFX and password secrets; no unsigned fallback exists.'
}
if ($encoded.Length -gt 24MB) { throw 'Signing PFX secret exceeds the bounded input limit.' }
$bytes = $null
$preview = $null
$persisted = $null
$store = $null
try {
    $bytes = [Convert]::FromBase64String($encoded)
    # Validate in memory before creating any persistent key/store entry.
    $preview = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $bytes, $password, [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    if ($preview.Thumbprint -ne $thumbprint -or -not $preview.HasPrivateKey) {
        throw 'Signing identity/private key does not match the configured thumbprint.'
    }
    $now = Get-Date
    if ($preview.NotBefore -gt $now -or $preview.NotAfter -lt $now) {
        throw 'The signing identity is outside its validity window.'
    }
    $ekuExtension = $preview.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1
    if ($null -eq $ekuExtension) { throw 'Code Signing EKU is required.' }
    $eku = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($ekuExtension, $ekuExtension.Critical)
    if (-not @($eku.EnhancedKeyUsages | Where-Object { $_.Value -eq '1.3.6.1.5.5.7.3.3' }).Count) {
        throw 'Code Signing EKU is required.'
    }
    $subject = $preview.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName, $false)
    if (-not [string]::Equals($subject.Trim(), $ExpectedPublisher.Trim(), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The signing identity publisher does not exactly match the configured publisher.'
    }
    $flags = [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet -bor
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet
    $persisted = [Security.Cryptography.X509Certificates.X509Certificate2]::new($bytes, $password, $flags)
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('My', 'CurrentUser')
    $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
    $store.Add($persisted)
    Write-Output "Signing identity provisioned in CurrentUser\My: $thumbprint; publisher: $subject"
}
catch {
    # Crypto/decoder exceptions must not accidentally serialize secret-bearing input.
    throw 'Production signing identity provisioning failed. Check protected secrets, thumbprint, validity, Code Signing EKU and exact publisher.'
}
finally {
    if ($store) { $store.Close(); $store.Dispose() }
    if ($preview) { $preview.Dispose() }
    if ($persisted) { $persisted.Dispose() }
    if ($bytes) { [Array]::Clear($bytes, 0, $bytes.Length) }
    $encoded = $null
    $password = $null
    [Environment]::SetEnvironmentVariable('FORGEREMS_SIGNING_PFX_BASE64', $null, 'Process')
    [Environment]::SetEnvironmentVariable('FORGEREMS_SIGNING_PFX_PASSWORD', $null, 'Process')
}
