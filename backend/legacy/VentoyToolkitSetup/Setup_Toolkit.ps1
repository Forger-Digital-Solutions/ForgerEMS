<#
.SYNOPSIS
Compatibility shim for the canonical setup implementation.

.DESCRIPTION
Preserves the historical VentoyToolkitSetup path while delegating to the
canonical script in ..\..\.
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

$backendRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& (Join-Path $backendRoot "Setup_Toolkit.ps1") @forwarded
