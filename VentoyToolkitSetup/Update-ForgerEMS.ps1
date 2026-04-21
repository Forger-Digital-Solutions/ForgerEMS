<#
.SYNOPSIS
Compatibility shim for the canonical ventoy-core updater.

.DESCRIPTION
Preserves the historical VentoyToolkitSetup path while delegating to the
canonical script in ..\ventoy-core.
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

& (Join-Path (Split-Path -Parent $PSScriptRoot) "ventoy-core\Update-ForgerEMS.ps1") @forwarded
