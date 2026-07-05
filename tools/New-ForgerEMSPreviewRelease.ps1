#requires -Version 5.1
param(
  [string]$Version = "1.2.4-preview.3",
  [switch]$DryRun
)
$ErrorActionPreference = "Stop"
$script = Join-Path $PSScriptRoot "build-release.ps1"
$args = @("-Version", $Version)
if ($DryRun) { $args += "-DryRun" }
& $script @args
