#requires -Version 5.1
param(
  [string]$Version = "",
  [switch]$DryRun
)
$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "build-release.ps1"
$args = @()
if (-not [string]::IsNullOrWhiteSpace($Version)) { $args += @("-Version", $Version) }
if ($DryRun) { $args += "-DryRun" }
& $script @args
