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

function Get-ForgerSafePathForLog {
    param([string]$Path = "")

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return "(empty path)"
    }

    try {
        return [IO.Path]::GetFileName($Path)
    }
    catch {
        return "(unprintable path)"
    }
}

function Get-ForgerSha256 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [switch]$ForceDotNetFallback
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Cannot calculate SHA256 for missing file: $(Get-ForgerSafePathForLog -Path $LiteralPath)"
    }

    $forceFallbackFromEnvironment =
        [string]::Equals($env:FORGEREMS_FORCE_DOTNET_HASH, "1", [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($env:FORGEREMS_FORCE_DOTNET_HASH, "true", [System.StringComparison]::OrdinalIgnoreCase)

    if (-not ($ForceDotNetFallback -or $forceFallbackFromEnvironment)) {
        $getFileHashCommand = Get-Command -Name Get-FileHash -CommandType Cmdlet -ErrorAction SilentlyContinue
        if (-not $getFileHashCommand) {
            try {
                Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
            }
            catch {
                $getFileHashCommand = $null
            }

            $getFileHashCommand = Get-Command -Name Get-FileHash -CommandType Cmdlet -ErrorAction SilentlyContinue
        }

        if ($getFileHashCommand) {
            try {
                $fileHash = & $getFileHashCommand -LiteralPath $LiteralPath -Algorithm SHA256 -ErrorAction Stop
                if ($fileHash -and $fileHash.Hash) {
                    $script:ForgerLastHashProvider = "Get-FileHash"
                    return ([string]$fileHash.Hash).ToLowerInvariant()
                }
            }
            catch {
                $script:ForgerLastHashProvider = "Get-FileHashFailed"
            }
        }
    }

    $stream = $null
    $sha256 = $null
    try {
        $stream = [IO.File]::Open($LiteralPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $sha256 = [Security.Cryptography.SHA256]::Create()
        $hashBytes = $sha256.ComputeHash($stream)
        $script:ForgerLastHashProvider = "DotNetFallback"
        return (([BitConverter]::ToString($hashBytes)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        if ($sha256 -ne $null) {
            $sha256.Dispose()
        }

        if ($stream -ne $null) {
            $stream.Dispose()
        }
    }
}

function Get-ForgerSha512 {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$LiteralPath,
        [switch]$ForceDotNetFallback
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "Cannot calculate SHA512 for missing file: $(Get-ForgerSafePathForLog -Path $LiteralPath)"
    }

    $forceFallbackFromEnvironment =
        [string]::Equals($env:FORGEREMS_FORCE_DOTNET_HASH, "1", [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals($env:FORGEREMS_FORCE_DOTNET_HASH, "true", [System.StringComparison]::OrdinalIgnoreCase)

    if (-not ($ForceDotNetFallback -or $forceFallbackFromEnvironment)) {
        $getFileHashCommand = Get-Command -Name Get-FileHash -CommandType Cmdlet -ErrorAction SilentlyContinue
        if (-not $getFileHashCommand) {
            try {
                Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
            }
            catch {
                $getFileHashCommand = $null
            }

            $getFileHashCommand = Get-Command -Name Get-FileHash -CommandType Cmdlet -ErrorAction SilentlyContinue
        }

        if ($getFileHashCommand) {
            try {
                $fileHash = & $getFileHashCommand -LiteralPath $LiteralPath -Algorithm SHA512 -ErrorAction Stop
                if ($fileHash -and $fileHash.Hash) {
                    $script:ForgerLastHashProvider = "Get-FileHash"
                    return ([string]$fileHash.Hash).ToLowerInvariant()
                }
            }
            catch {
                $script:ForgerLastHashProvider = "Get-FileHashFailed"
            }
        }
    }

    $stream = $null
    $sha512 = $null
    try {
        $stream = [IO.File]::Open($LiteralPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        $sha512 = [Security.Cryptography.SHA512]::Create()
        $hashBytes = $sha512.ComputeHash($stream)
        $script:ForgerLastHashProvider = "DotNetFallback"
        return (([BitConverter]::ToString($hashBytes)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        if ($sha512 -ne $null) {
            $sha512.Dispose()
        }

        if ($stream -ne $null) {
            $stream.Dispose()
        }
    }
}

function Get-ForgerLastHashProvider {
    if ([string]::IsNullOrWhiteSpace($script:ForgerLastHashProvider)) {
        return "Unknown"
    }

    return $script:ForgerLastHashProvider
}
