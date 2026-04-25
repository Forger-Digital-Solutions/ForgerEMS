<#
.SYNOPSIS
Compatibility shim for the canonical backend updater.

.DESCRIPTION
Preserves the historical VentoyToolkitSetup path while delegating to the
canonical script in ..\..\.
#>

#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$DriveLetter,
    [string]$UsbRoot = "",
    [string]$ManifestName = "ForgerEMS.updates.json",
    [switch]$Force,
    [switch]$VerifyOnly,
    [switch]$NoArchive,
    [switch]$ShowVersion
)

$forwarded = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $forwarded[$entry.Key] = $entry.Value
}

$backendRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& (Join-Path $backendRoot "Update-ForgerEMS.ps1") @forwarded
