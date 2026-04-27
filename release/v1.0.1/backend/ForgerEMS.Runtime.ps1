<#
.SYNOPSIS
Shared per-user runtime path helpers for the ForgerEMS backend.

.DESCRIPTION
Resolves a writable runtime workspace under %LOCALAPPDATA%\ForgerEMS and keeps
verification artifacts, logs, temporary files, and lightweight state out of
read-only install locations such as Program Files.
#>

function Get-ForgerEMSRuntimeRoot {
    param([string]$Root = "")

    if (-not [string]::IsNullOrWhiteSpace($Root)) {
        return [IO.Path]::GetFullPath($Root).TrimEnd('\')
    }

    $localAppData = $env:LOCALAPPDATA
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    }

    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw "Could not resolve LOCALAPPDATA for the current user."
    }

    return [IO.Path]::GetFullPath((Join-Path $localAppData "ForgerEMS")).TrimEnd('\')
}

function Get-ForgerEMSRuntimeLayout {
    param([string]$Root = "")

    $runtimeRoot = Get-ForgerEMSRuntimeRoot -Root $Root

    return [PSCustomObject]@{
        Root       = $runtimeRoot
        VerifyRoot = Join-Path $runtimeRoot ".verify"
        LogsRoot   = Join-Path $runtimeRoot "logs"
        TmpRoot    = Join-Path $runtimeRoot "tmp"
        StateRoot  = Join-Path $runtimeRoot "state"
    }
}

function Ensure-ForgerEMSRuntimeLayout {
    param([string]$Root = "")

    $layout = Get-ForgerEMSRuntimeLayout -Root $Root
    foreach ($path in @(
        $layout.Root,
        $layout.VerifyRoot,
        $layout.LogsRoot,
        $layout.TmpRoot,
        $layout.StateRoot
    ) | Select-Object -Unique) {
        if (-not (Test-Path -LiteralPath $path)) {
            New-Item -ItemType Directory -Path $path -Force | Out-Null
        }
    }

    return $layout
}

function New-ForgerEMSRuntimeTempFile {
    param(
        [string]$Prefix = "forgerems",
        [string]$Extension = ".tmp",
        [string]$Root = ""
    )

    $layout = Ensure-ForgerEMSRuntimeLayout -Root $Root
    $safePrefix = (($Prefix -replace '[^A-Za-z0-9._-]+', '_').Trim('_'))
    if ([string]::IsNullOrWhiteSpace($safePrefix)) {
        $safePrefix = "forgerems"
    }

    $safeExtension = if ([string]::IsNullOrWhiteSpace($Extension)) {
        ".tmp"
    }
    elseif ($Extension.StartsWith(".")) {
        $Extension
    }
    else {
        "." + $Extension
    }

    return Join-Path $layout.TmpRoot ($safePrefix + "_" + [Guid]::NewGuid().ToString("N") + $safeExtension)
}
