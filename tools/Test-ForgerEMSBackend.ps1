#requires -Version 5.1
<#
.SYNOPSIS
  Preflight checks for ForgerEMS repo + backend layout (Public Preview).
#>
$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Test-PathOrWarn([string]$Path, [string]$Label) {
  if (Test-Path -LiteralPath $Path) { Write-Host "[OK] $Label" -ForegroundColor Green }
  else { Write-Host "[MISS] $Label -> $Path" -ForegroundColor Yellow }
}

Write-Host "=== ForgerEMS backend preflight ===" -ForegroundColor Cyan
Test-PathOrWarn (Join-Path $repoRoot "ForgerEMS.sln") "Solution file"
Test-PathOrWarn (Join-Path $repoRoot "backend") "backend folder"
Test-PathOrWarn (Join-Path $repoRoot "manifests") "manifests folder"
Test-PathOrWarn (Join-Path $repoRoot "backend\Update-ForgerEMS.ps1") "Update-ForgerEMS.ps1"
Test-PathOrWarn (Join-Path $repoRoot "backend\SystemIntelligence\Invoke-ForgerEMSSystemScan.ps1") "System Intelligence scan script"
Write-Host "PSVersion: $($PSVersionTable.PSVersion)"
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) { Write-Host "[OK] dotnet: $($dotnet.Source)" -ForegroundColor Green } else { Write-Host "[MISS] dotnet SDK on PATH" -ForegroundColor Yellow }
$iscc = @(
  (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
  (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($iscc) { Write-Host "[OK] Inno Setup: $iscc" -ForegroundColor Green } else { Write-Host "[INFO] Inno Setup 6 not found (optional unless packaging)" -ForegroundColor DarkGray }
Write-Host "Done." -ForegroundColor Cyan
