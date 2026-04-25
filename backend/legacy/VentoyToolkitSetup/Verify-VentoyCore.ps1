<#
.SYNOPSIS
Compatibility shim for the canonical backend verification script.

.DESCRIPTION
Preserves the historical VentoyToolkitSetup path while delegating to the
canonical script in ..\..\.
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

$backendRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
& (Join-Path $backendRoot "Verify-VentoyCore.ps1") @forwarded
