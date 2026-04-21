<#
.SYNOPSIS
Compatibility shim for the canonical ventoy-core verification script.

.DESCRIPTION
Preserves the historical VentoyToolkitSetup path while delegating to the
canonical script in ..\ventoy-core.
#>

#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$VerifyRoot = "",
    [switch]$Online,
    [switch]$ShowVersion
)

$forwarded = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $forwarded[$entry.Key] = $entry.Value
}

& (Join-Path (Split-Path -Parent $PSScriptRoot) "ventoy-core\Verify-VentoyCore.ps1") @forwarded
