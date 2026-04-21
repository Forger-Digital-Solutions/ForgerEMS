<#
.SYNOPSIS
Preferred setup entrypoint for the ForgerEMS Ventoy core.

.DESCRIPTION
Compatibility-preserving public wrapper. This is the preferred human-facing
setup command for current docs and usage examples. It delegates to
Setup_Toolkit.ps1, which remains the canonical implementation.

.PARAMETER DriveLetter
Drive letter for the target USB or toolkit root, such as D.

.PARAMETER UsbRoot
Full path to the target toolkit location. If you point at the release bundle
folder itself, the canonical setup script uses the USB drive root so the
toolkit is created at the top of the device.

.PARAMETER OwnerName
Optional owner name written into generated README content.

.PARAMETER ManifestName
Relative manifest path to seed under the selected root when -SeedManifest is
used. Defaults to ForgerEMS.updates.json.

.PARAMETER OpenCorePages
Open core official download pages after setup completes.

.PARAMETER OpenManualPages
Open manual/community download pages after setup completes.

.PARAMETER SeedManifest
Copy the bundled manifest into the selected root if it does not already exist.

.PARAMETER ForceManifestOverwrite
Overwrite an existing target manifest when used together with -SeedManifest.

.PARAMETER ShowVersion
Display the Ventoy core version/build metadata from the bundled manifest and
exit without making changes.

.EXAMPLE
.\Setup-ForgerEMS.ps1 -DriveLetter D -SeedManifest

.EXAMPLE
.\Setup-ForgerEMS.ps1 -UsbRoot "D:\" -OwnerName "Edward"

.EXAMPLE
.\Setup-ForgerEMS.ps1 -UsbRoot "H:\" -WhatIf

.EXAMPLE
.\Setup-ForgerEMS.ps1 -ShowVersion

.NOTES
Public PowerShell entrypoint. Preferred setup name. Supports -WhatIf.
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

& (Join-Path $PSScriptRoot "Setup_Toolkit.ps1") @forwarded
