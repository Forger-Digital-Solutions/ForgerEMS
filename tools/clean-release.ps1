<#
.SYNOPSIS
Cleans generated ForgerEMS build and release outputs.

.DESCRIPTION
Removes transient build artifacts while preserving source code, installer
configuration, manifests, docs, and the verified backend release bundle used by
installer staging.

.PARAMETER RemoveCurrent
Also removes release\current so the next release build starts from an empty
current release folder.
#>

#requires -Version 5.1

[CmdletBinding(SupportsShouldProcess)]
param(
    [switch]$RemoveCurrent
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$repoRootFull = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\')

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)
    return [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Assert-UnderRepo {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = Get-NormalizedPath -Path $Path
    if (($fullPath -ne $repoRootFull) -and -not $fullPath.StartsWith($repoRootFull + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean path outside repo. Path='$fullPath' Repo='$repoRootFull'"
    }
}

function Remove-SafePath {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Assert-UnderRepo -Path $Path
    if ($PSCmdlet.ShouldProcess($Path, "Remove generated artifact")) {
        Remove-Item -LiteralPath $Path -Recurse -Force
        Write-Host "Removed: $Path"
    }
}

function Clear-VerifyFolder {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Assert-UnderRepo -Path $Path
    Get-ChildItem -LiteralPath $Path -Force | Where-Object { $_.Name -ne "README.md" } | ForEach-Object {
        Remove-SafePath -Path $_.FullName
    }
}

Write-Host "Cleaning generated ForgerEMS artifacts under $repoRootFull"

Remove-SafePath -Path (Join-Path $repoRoot "dist")
Remove-SafePath -Path (Join-Path $repoRoot ".tmp")
Clear-VerifyFolder -Path (Join-Path $repoRoot ".verify")

Get-ChildItem -LiteralPath (Join-Path $repoRoot "src") -Directory -Recurse -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -in @("bin", "obj") } |
    Sort-Object FullName -Descending |
    ForEach-Object { Remove-SafePath -Path $_.FullName }

$releaseRoot = Join-Path $repoRoot "release"
if (Test-Path -LiteralPath $releaseRoot) {
    Get-ChildItem -LiteralPath $releaseRoot -Directory -Force | ForEach-Object {
        $keepCurrent = $_.Name -eq "current" -and -not $RemoveCurrent
        $keepBackendBundle = $_.Name -eq "ventoy-core"

        if (-not $keepCurrent -and -not $keepBackendBundle) {
            Remove-SafePath -Path $_.FullName
        }
    }
}

Write-Host "Clean complete."
