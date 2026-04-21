<#
.SYNOPSIS
Compatibility shim for the canonical ventoy-core setup entrypoint.

.DESCRIPTION
Preserves the historical VentoyToolkitSetup path while delegating to the
canonical script in ..\ventoy-core.
#>

#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DriveLetter,
    [string]$UsbRoot = "",
    [string]$OwnerName = "",
    [string]$ManifestName = "ForgerEMS.updates.json",
    [switch]$OpenCorePages,
    [switch]$OpenManualPages,
    [switch]$SeedManifest,
    [switch]$ForceManifestOverwrite,
    [switch]$ShowVersion
)

$forwarded = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $forwarded[$entry.Key] = $entry.Value
}

& (Join-Path (Split-Path -Parent $PSScriptRoot) "ventoy-core\Setup-ForgerEMS.ps1") @forwarded
