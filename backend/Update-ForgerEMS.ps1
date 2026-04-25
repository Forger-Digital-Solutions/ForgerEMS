<#
.SYNOPSIS
Applies the ForgerEMS manifest to a toolkit root.

.DESCRIPTION
Manifest-driven updater for the Ventoy core. This script reads a JSON manifest,
validates it early, resolves all target paths under the selected root, and then
updates managed files and URL shortcuts. If the selected root does not already
contain the manifest, the updater falls back to the bundled manifest shipped
beside this script.

.PARAMETER DriveLetter
Drive letter for the target USB or toolkit root, such as D.

.PARAMETER UsbRoot
Full path to the target toolkit location. If you point at the release bundle
folder itself, the script uses the USB drive root so updates land at the top
of the device.

.PARAMETER ManifestName
Manifest file name or path. Relative paths are first resolved under the target
root and then beside this script.

.PARAMETER Force
Replace managed files even when an existing destination is already present.

.PARAMETER VerifyOnly
Verify existing managed files and shortcuts without downloading replacements.

.PARAMETER NoArchive
Skip archive creation before replacing managed files.

.PARAMETER ShowVersion
Display the Ventoy core version/build metadata from the bundled manifest and
exit without making changes.

.EXAMPLE
.\Update-ForgerEMS.ps1 -DriveLetter D -WhatIf

.EXAMPLE
.\Update-ForgerEMS.ps1 -UsbRoot "D:\" -VerifyOnly

.EXAMPLE
.\Update-ForgerEMS.ps1 -UsbRoot "H:\" -ManifestName "ForgerEMS.updates.json"

.EXAMPLE
.\Update-ForgerEMS.ps1 -ShowVersion

.NOTES
Public PowerShell entrypoint. Supports -WhatIf and manifest fallback.
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

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$runtimeHelperCandidates = @(
    (Join-Path $PSScriptRoot "ForgerEMS.Runtime.ps1"),
    (Join-Path $PSScriptRoot "backend\ForgerEMS.Runtime.ps1")
) | Select-Object -Unique

$runtimeHelperImported = $false
foreach ($runtimeHelperCandidate in $runtimeHelperCandidates) {
    if (Test-Path -LiteralPath $runtimeHelperCandidate) {
        . $runtimeHelperCandidate
        $runtimeHelperImported = $true
        break
    }
}

if (-not $runtimeHelperImported) {
    throw "ForgerEMS runtime helper was not found. Checked: $($runtimeHelperCandidates -join '; ')"
}

try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

$script:LogFile = $null
$script:Summary = [ordered]@{
    Total                    = 0
    ManagedFileItems         = 0
    PlaceholderItems         = 0
    Downloaded               = 0
    Skipped                  = 0
    Verified                 = 0
    Updated                  = 0
    Shortcut                 = 0
    PlaceholderOnly          = 0
    Failed                   = 0
    FailedWithFallback       = 0
    Archived                 = 0
    Disabled                 = 0
    FallbackShortcutsCreated = 0
    FallbackShortcutsReused  = 0
}

function Write-Log {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO","OK","WARN","ERROR")][string]$Level = "INFO"
    )

    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    $line = "[$ts][$Level] $Message"

    switch ($Level) {
        "INFO"  { Write-Host $line -ForegroundColor Cyan }
        "OK"    { Write-Host $line -ForegroundColor Green }
        "WARN"  { Write-Host $line -ForegroundColor Yellow }
        "ERROR" { Write-Host $line -ForegroundColor Red }
    }

    if ($script:LogFile -and -not $WhatIfPreference) {
        $logParent = Split-Path -Parent $script:LogFile
        if ($logParent -and (Test-Path -LiteralPath $logParent)) {
            Add-Content -LiteralPath $script:LogFile -Value $line -Encoding UTF8
        }
    }
}

function Ensure-Dir {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        if ($PSCmdlet.ShouldProcess($Path, "Create directory")) {
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
            Write-Log "Created directory: $Path" "OK"
        }
        else {
            Write-Log "Would create directory: $Path" "INFO"
        }
    }
    else {
        Write-Log "Exists: $Path" "INFO"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) { return $null }

    $getFileHashCommand = Get-Command -Name Get-FileHash -ErrorAction SilentlyContinue
    if ($getFileHashCommand -and -not $WhatIfPreference) {
        try {
            $fileHash = Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop
            if ($fileHash -and $fileHash.Hash) {
                return $fileHash.Hash.ToLowerInvariant()
            }
        }
        catch {
            # Fall back to the .NET hasher below so host/cmdlet quirks do not leave the hash null.
        }
    }

    $stream = [IO.File]::OpenRead($Path)
    try {
        $sha256 = [Security.Cryptography.SHA256]::Create()
        try {
            $hashBytes = $sha256.ComputeHash($stream)
        }
        finally {
            $sha256.Dispose()
        }

        return (([BitConverter]::ToString($hashBytes)) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
    }
}

function Safe-FileName {
    param([Parameter(Mandatory)][string]$Text)
    (($Text -replace '[\\/:*?"<>|]+', '_').Trim())
}

function Write-UrlShortcut {
    param(
        [Parameter(Mandatory)][string]$ShortcutPath,
        [Parameter(Mandatory)][string]$Url
    )

    $content = @"
[InternetShortcut]
URL=$Url
"@

    Set-Content -LiteralPath $ShortcutPath -Value $content -Encoding ASCII
}

function Get-ManifestDestinationKey {
    param([AllowNull()][string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return ""
    }

    return ([string]$RelativePath).Trim().ToLowerInvariant()
}

function Normalize-ManifestMatchText {
    param([AllowNull()][string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return ""
    }

    return ([regex]::Replace($Text.ToLowerInvariant(), '[^a-z0-9]+', ' ')).Trim()
}

function Get-PlaceholderDisplayLabelFromDestination {
    param([AllowNull()][string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        return ""
    }

    $leafName = [IO.Path]::GetFileNameWithoutExtension($RelativePath)
    if ([string]::IsNullOrWhiteSpace($leafName)) {
        return ""
    }

    return (($leafName -replace '^(download|info)\s*-\s*', '').Trim())
}

function Test-ManagedPlaceholderShadowMatch {
    param(
        [Parameter(Mandatory)]$PageItem,
        [Parameter(Mandatory)]$ManagedItem
    )

    $pageDest = ([string]$(if ($PageItem.dest) { $PageItem.dest } else { "" })).Trim()
    $managedDest = ([string]$(if ($ManagedItem.dest) { $ManagedItem.dest } else { "" })).Trim()

    if ([string]::IsNullOrWhiteSpace($pageDest) -or [string]::IsNullOrWhiteSpace($managedDest)) {
        return $false
    }

    $pageDir = Split-Path -Parent $pageDest
    $managedDir = Split-Path -Parent $managedDest

    if (-not [string]::Equals($pageDir, $managedDir, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $pageLabels = @(
        (Normalize-ManifestMatchText -Text (Get-PlaceholderDisplayLabelFromDestination -RelativePath $pageDest))
        (Normalize-ManifestMatchText -Text ([string]$(if ($PageItem.name) { $PageItem.name } else { "" })))
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    $managedTargets = @(
        (Normalize-ManifestMatchText -Text ([string]$(if ($ManagedItem.name) { $ManagedItem.name } else { "" })))
        (Normalize-ManifestMatchText -Text ([IO.Path]::GetFileNameWithoutExtension($managedDest)))
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($pageLabel in $pageLabels) {
        foreach ($managedTarget in $managedTargets) {
            if ($managedTarget.Contains($pageLabel) -or $pageLabel.Contains($managedTarget)) {
                return $true
            }
        }
    }

    return $false
}

function Get-ActiveManagedPlaceholderPlan {
    param([Parameter(Mandatory)][object[]]$Items)

    $enabledManagedFileItems = @(
        $Items | Where-Object {
            $itemEnabled = $true
            if ($null -ne $_.enabled) {
                $itemEnabled = [bool]$_.enabled
            }

            $itemType = ([string]$(if ($_.type) { $_.type } else { "file" })).Trim().ToLowerInvariant()
            $itemEnabled -and $itemType -eq "file"
        }
    )

    $byPlaceholderDest = @{}
    $byManagedDest = @{}

    foreach ($item in $Items) {
        if ($null -eq $item) { continue }

        $itemEnabled = $true
        if ($null -ne $item.enabled) {
            $itemEnabled = [bool]$item.enabled
        }

        $itemType = ([string]$(if ($item.type) { $item.type } else { "file" })).Trim().ToLowerInvariant()
        if (-not $itemEnabled -or $itemType -ne "page") {
            continue
        }

        $matchedManagedItem = @(
            $enabledManagedFileItems | Where-Object {
                Test-ManagedPlaceholderShadowMatch -PageItem $item -ManagedItem $_
            }
        ) | Select-Object -First 1

        if ($null -eq $matchedManagedItem) {
            continue
        }

        $placeholderDest = ([string]$item.dest).Trim()
        $managedDest = ([string]$matchedManagedItem.dest).Trim()
        $placeholderKey = Get-ManifestDestinationKey -RelativePath $placeholderDest
        $managedKey = Get-ManifestDestinationKey -RelativePath $managedDest
        $entry = [PSCustomObject]@{
            PlaceholderDest = $placeholderDest
            ManagedDest     = $managedDest
            PlaceholderItem = $item
            ManagedItem     = $matchedManagedItem
        }

        if (-not $byPlaceholderDest.ContainsKey($placeholderKey)) {
            $byPlaceholderDest[$placeholderKey] = $entry
        }

        if (-not $byManagedDest.ContainsKey($managedKey)) {
            $byManagedDest[$managedKey] = New-Object System.Collections.Generic.List[object]
        }

        [void]$byManagedDest[$managedKey].Add($entry)
    }

    return [PSCustomObject]@{
        ByPlaceholderDest = $byPlaceholderDest
        ByManagedDest     = $byManagedDest
    }
}

function Get-PreferredFallbackShortcutPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManagedDestination,
        [Parameter(Mandatory)]$ManagedPlaceholderPlan
    )

    $managedKey = Get-ManifestDestinationKey -RelativePath $ManagedDestination
    if (-not $ManagedPlaceholderPlan.ByManagedDest.ContainsKey($managedKey)) {
        return $null
    }

    $entry = @($ManagedPlaceholderPlan.ByManagedDest[$managedKey] | Select-Object -First 1)
    if ($entry.Count -eq 0) {
        return $null
    }

    return (Resolve-RootChildPath -Root $Root -RelativePath ([string]$entry[0].PlaceholderDest))
}

function Remove-ManagedSuccessPlaceholders {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManagedDestination,
        [Parameter(Mandatory)]$ManagedPlaceholderPlan
    )

    $managedKey = Get-ManifestDestinationKey -RelativePath $ManagedDestination
    if (-not $ManagedPlaceholderPlan.ByManagedDest.ContainsKey($managedKey)) {
        return 0
    }

    $removedCount = 0

    foreach ($entry in @($ManagedPlaceholderPlan.ByManagedDest[$managedKey] | ForEach-Object { $_ })) {
        $placeholderDestRel = ([string]$entry.PlaceholderDest).Trim()
        if ([string]::IsNullOrWhiteSpace($placeholderDestRel)) {
            continue
        }

        $placeholderPath = Resolve-RootChildPath -Root $Root -RelativePath $placeholderDestRel
        if (-not (Test-Path -LiteralPath $placeholderPath)) {
            continue
        }

        try {
            if ($PSCmdlet.ShouldProcess($placeholderDestRel, "Remove placeholder shortcut because managed payload staged successfully")) {
                Remove-Item -LiteralPath $placeholderPath -Force
                Write-Log "Removed placeholder shortcut because managed payload staged successfully: $placeholderPath" "OK"
            }
            else {
                Write-Log "Would remove placeholder shortcut because managed payload staged successfully: $placeholderPath" "INFO"
            }

            $removedCount++
        }
        catch {
            Write-Log "Failed to remove placeholder shortcut after managed staging for '$ManagedDestination': $($_.Exception.Message)" "WARN"
        }
    }

    return $removedCount
}

function Write-DownloadFallbackShortcut {
    param(
        [Parameter(Mandatory)][string]$DestinationPath,
        [Parameter(Mandatory)][string]$ItemName,
        [Parameter(Mandatory)][string]$Url,
        [AllowNull()][string]$PreferredShortcutPath
    )

    try {
        $destinationDir = Split-Path -Parent $DestinationPath
        if ([string]::IsNullOrWhiteSpace($destinationDir)) {
            return [PSCustomObject]@{
                Outcome      = "none"
                ShortcutPath = ""
            }
        }

        Ensure-Dir -Path $destinationDir

        $shortcutPath = $null

        if (-not [string]::IsNullOrWhiteSpace($PreferredShortcutPath)) {
            $shortcutPath = $PreferredShortcutPath
            $preferredShortcutDir = Split-Path -Parent $shortcutPath
            if (-not [string]::IsNullOrWhiteSpace($preferredShortcutDir)) {
                Ensure-Dir -Path $preferredShortcutDir
            }

            if (Test-Path -LiteralPath $shortcutPath) {
                Write-Log "Using existing fallback shortcut because managed download failed: $shortcutPath" "WARN"
                return [PSCustomObject]@{
                    Outcome      = "existing"
                    ShortcutPath = $shortcutPath
                }
            }
        }
        else {
            $itemNameToken = (($ItemName -split '\s+')[0]).Trim()
            if (-not [string]::IsNullOrWhiteSpace($itemNameToken)) {
                $existingRelatedShortcut = Get-ChildItem -LiteralPath $destinationDir -Filter "*.url" -ErrorAction SilentlyContinue |
                    Where-Object { $_.BaseName -like ("*" + $itemNameToken + "*") } |
                    Select-Object -First 1

                if ($existingRelatedShortcut) {
                    Write-Log "Using existing seeded placeholder shortcut as fallback because managed download failed: $($existingRelatedShortcut.FullName)" "WARN"
                    return [PSCustomObject]@{
                        Outcome      = "existing"
                        ShortcutPath = $existingRelatedShortcut.FullName
                    }
                }
            }

            $shortcutName = "DOWNLOAD - " + (Safe-FileName -Text $ItemName) + ".url"
            $shortcutPath = Join-Path $destinationDir $shortcutName

            if (Test-Path -LiteralPath $shortcutPath) {
                Write-Log "Using existing fallback shortcut because managed download failed: $shortcutPath" "WARN"
                return [PSCustomObject]@{
                    Outcome      = "existing"
                    ShortcutPath = $shortcutPath
                }
            }
        }

        Write-UrlShortcut -ShortcutPath $shortcutPath -Url $Url
        Write-Log "Fallback shortcut written because managed download failed: $shortcutPath" "WARN"
        return [PSCustomObject]@{
            Outcome      = "created"
            ShortcutPath = $shortcutPath
        }
    }
    catch {
        Write-Log "Failed to write fallback shortcut for '$ItemName': $($_.Exception.Message)" "WARN"
        return [PSCustomObject]@{
            Outcome      = "error"
            ShortcutPath = ""
        }
    }
}

function Get-ExceptionDiagnostic {
    param(
        [Management.Automation.ErrorRecord]$ErrorRecord,
        [System.Exception]$Exception
    )

    $current = if ($ErrorRecord) {
        $ErrorRecord.Exception
    }
    else {
        $Exception
    }

    if ($null -eq $current) {
        return "Unknown error."
    }

    $parts = New-Object System.Collections.Generic.List[string]
    $depth = 0

    while ($current -and $depth -lt 4) {
        $entry = "{0}: {1}" -f $current.GetType().FullName, $current.Message

        try {
            $statusCodeProperty = $current.PSObject.Properties["StatusCode"]
            if ($statusCodeProperty -and $null -ne $current.StatusCode) {
                $entry += " [HTTP $([int]$current.StatusCode)]"
            }
        }
        catch {
        }

        try {
            $responseProperty = $current.PSObject.Properties["Response"]
            if ($responseProperty -and $null -ne $current.Response) {
                $statusCode = $current.Response.StatusCode
                if ($null -ne $statusCode) {
                    $entry += " [Response $([int]$statusCode)]"
                }
            }
        }
        catch {
        }

        $parts.Add($entry)
        $current = $current.InnerException
        $depth++
    }

    return ($parts -join " <= ")
}

function Get-FileStateDescription {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return "missing"
    }

    $item = Get-Item -LiteralPath $Path
    return "exists ($($item.Length) bytes) at $Path"
}

function Get-NormalizedDisplayText {
    param([AllowNull()]$Value)

    if ($null -eq $Value) {
        return ""
    }

    $parts = @($Value) | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
    if (-not $parts -or $parts.Count -eq 0) {
        return ""
    }

    return (($parts -join " ").Trim())
}

function Get-HttpStatusCodeDisplayText {
    param([AllowNull()]$Value)

    $text = Get-NormalizedDisplayText -Value $Value
    if ([string]::IsNullOrWhiteSpace($text)) {
        return ""
    }

    $match = [regex]::Match($text, '\b\d{3}\b')
    if ($match.Success) {
        return $match.Value
    }

    return $text
}

function Invoke-HttpClientDownload {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutFile,
        [int]$TimeoutSec = 180,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    Add-Type -AssemblyName System.Net.Http | Out-Null

    $handler = New-Object System.Net.Http.HttpClientHandler
    $handler.AllowAutoRedirect = $true
    try {
        $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    }
    catch {
    }

    $client = New-Object System.Net.Http.HttpClient($handler)
    $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
    $client.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

    $response = $null
    $responseStream = $null
    $fileStream = $null

    try {
        $response = $client.GetAsync($Url, [System.Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        $null = $response.EnsureSuccessStatusCode()

        $responseStream = $response.Content.ReadAsStreamAsync().GetAwaiter().GetResult()
        $fileStream = [IO.File]::Open($OutFile, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $responseStream.CopyTo($fileStream)

        return [PSCustomObject]@{
            Method      = "HttpClient"
            StatusCode  = [int]$response.StatusCode
            ReasonPhrase = [string]$response.ReasonPhrase
            FinalUri    = [string]$response.RequestMessage.RequestUri.AbsoluteUri
        }
    }
    finally {
        if ($fileStream) {
            $fileStream.Dispose()
        }

        if ($responseStream) {
            $responseStream.Dispose()
        }

        if ($response) {
            $response.Dispose()
        }

        $client.Dispose()
        $handler.Dispose()
    }
}

function Invoke-CurlDownload {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutFile,
        [int]$TimeoutSec = 180,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    $curlPath = Join-Path $env:SystemRoot "System32\curl.exe"
    if (-not (Test-Path -LiteralPath $curlPath)) {
        throw "curl.exe is not available on this system."
    }

    $arguments = @(
        "-L",
        "-sS",
        "--fail",
        "--retry", "2",
        "--connect-timeout", [string]$TimeoutSec,
        "--user-agent", $UserAgent,
        "--output", $OutFile,
        $Url
    )

    & $curlPath @arguments 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "curl.exe exited with code $LASTEXITCODE."
    }

    return [PSCustomObject]@{
        Method   = "curl.exe"
        ExitCode = 0
        FinalUri = $Url
    }
}

function Get-UrlText {
    param(
        [Parameter(Mandatory)][string]$Url,
        [int]$TimeoutSec = 60,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    $headers = @{ "User-Agent" = $UserAgent }
    $methods = @(
        @{
            Name = "HttpClient"
            Action = {
                Add-Type -AssemblyName System.Net.Http | Out-Null

                $handler = New-Object System.Net.Http.HttpClientHandler
                $handler.AllowAutoRedirect = $true
                try {
                    $handler.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
                }
                catch {
                }

                $client = New-Object System.Net.Http.HttpClient($handler)
                $client.Timeout = [TimeSpan]::FromSeconds($TimeoutSec)
                $client.DefaultRequestHeaders.UserAgent.ParseAdd($UserAgent)

                if ($Url -like "https://api.github.com/*") {
                    $client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json")
                    $client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28")
                }

                $response = $null
                try {
                    $response = $client.GetAsync($Url).GetAwaiter().GetResult()
                    $null = $response.EnsureSuccessStatusCode()
                    [PSCustomObject]@{
                        Text         = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        Method       = "HttpClient"
                        StatusCode   = [int]$response.StatusCode
                        ReasonPhrase = [string]$response.ReasonPhrase
                        FinalUri     = [string]$response.RequestMessage.RequestUri.AbsoluteUri
                    }
                }
                finally {
                    if ($response) {
                        $response.Dispose()
                    }

                    $client.Dispose()
                    $handler.Dispose()
                }
            }
        },
        @{
            Name = "Invoke-WebRequest"
            Action = {
                $response = Invoke-WebRequest -Uri $Url -Headers $headers -TimeoutSec $TimeoutSec -MaximumRedirection 10 -UseBasicParsing
                [PSCustomObject]@{
                    Text         = [string]$response.Content
                    Method       = "Invoke-WebRequest"
                    StatusCode   = if ($response.StatusCode) { [int]$response.StatusCode } else { 0 }
                    ReasonPhrase = if ($response.StatusDescription) { [string]$response.StatusDescription } else { "" }
                    FinalUri     = if ($response.BaseResponse -and $response.BaseResponse.ResponseUri) { [string]$response.BaseResponse.ResponseUri.AbsoluteUri } else { $Url }
                }
            }
        }
    )

    foreach ($method in $methods) {
        try {
            return & $method.Action
        }
        catch {
            Write-Log "Checksum source fetch via $($method.Name) failed: $(Get-ExceptionDiagnostic -ErrorRecord $_)" "WARN"
        }
    }

    throw "All checksum source fetch strategies failed for $Url"
}

function Download-File {
    param(
        [Parameter(Mandatory)][string]$Url,
        [Parameter(Mandatory)][string]$OutFile,
        [int]$TimeoutSec = 180,
        [string]$UserAgent = "ForgerEMS-Updater/3.1",
        [int]$Retries = 3
    )

    $headers = @{ "User-Agent" = $UserAgent }

    for ($attempt = 1; $attempt -le $Retries; $attempt++) {
        try {
            Write-Log "Download attempt $attempt/$Retries starting: $Url" "INFO"

            if (Test-Path -LiteralPath $OutFile) {
                Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
            }

            $downloadSucceeded = $false
            $downloadTrace = @()
            $downloadMethods = @(
                @{
                    Name   = "HttpClient"
                    Action = {
                        Invoke-HttpClientDownload -Url $Url -OutFile $OutFile -TimeoutSec $TimeoutSec -UserAgent $UserAgent
                    }
                },
                @{
                    Name   = "Invoke-WebRequest"
                    Action = {
                        $response = Invoke-WebRequest -Uri $Url -OutFile $OutFile -Headers $headers -TimeoutSec $TimeoutSec -MaximumRedirection 10 -UseBasicParsing
                        [PSCustomObject]@{
                            Method       = "Invoke-WebRequest"
                            StatusCode   = if ($response.StatusCode) { [int]$response.StatusCode } else { $null }
                            ReasonPhrase = if ($response.StatusDescription) { [string]$response.StatusDescription } else { "" }
                            FinalUri     = if ($response.BaseResponse -and $response.BaseResponse.ResponseUri) { [string]$response.BaseResponse.ResponseUri.AbsoluteUri } else { $Url }
                        }
                    }
                },
                @{
                    Name   = "curl.exe"
                    Action = {
                        Invoke-CurlDownload -Url $Url -OutFile $OutFile -TimeoutSec $TimeoutSec -UserAgent $UserAgent
                    }
                },
                @{
                    Name   = "BITS"
                    Action = {
                        Start-BitsTransfer -Source $Url -Destination $OutFile -ErrorAction Stop
                        [PSCustomObject]@{
                            Method       = "BITS"
                            StatusCode   = $null
                            ReasonPhrase = ""
                            FinalUri     = $Url
                        }
                    }
                }
            )

            foreach ($downloadMethod in $downloadMethods) {
                try {
                    Write-Log "Using $($downloadMethod.Name) for foreground download." "INFO"
                    $downloadMetadata = & $downloadMethod.Action
                    $downloadMetadataRecord = @($downloadMetadata | Select-Object -First 1)[0]

                    if (-not (Test-Path -LiteralPath $OutFile)) {
                        throw "The download method returned without creating '$OutFile'."
                    }

                    $sizeBytes = (Get-Item -LiteralPath $OutFile).Length
                    if ($sizeBytes -le 0) {
                        throw "The downloaded file is empty."
                    }

                    $downloadStatusCode = if ($downloadMetadataRecord -and $downloadMetadataRecord.PSObject.Properties["StatusCode"]) { Get-HttpStatusCodeDisplayText -Value $downloadMetadataRecord.StatusCode } else { "" }
                    $downloadReasonPhrase = if ($downloadMetadataRecord -and $downloadMetadataRecord.PSObject.Properties["ReasonPhrase"]) { Get-NormalizedDisplayText -Value $downloadMetadataRecord.ReasonPhrase } else { "" }
                    $downloadFinalUri = if ($downloadMetadataRecord -and $downloadMetadataRecord.PSObject.Properties["FinalUri"]) { Get-NormalizedDisplayText -Value $downloadMetadataRecord.FinalUri } else { "" }

                    Write-Log "Download completed via $($downloadMethod.Name): $OutFile ($sizeBytes bytes)" "OK"
                    if (-not [string]::IsNullOrWhiteSpace($downloadStatusCode)) {
                        Write-Log (("Download HTTP status via $($downloadMethod.Name): $downloadStatusCode $downloadReasonPhrase").TrimEnd()) "INFO"
                    }
                    if (-not [string]::IsNullOrWhiteSpace($downloadFinalUri)) {
                        Write-Log "Download final URL via $($downloadMethod.Name): $downloadFinalUri" "INFO"
                    }
                    Write-Log "Download destination state after transfer: $(Get-FileStateDescription -Path $OutFile)" "INFO"
                    $traceEntry = "$($downloadMethod.Name)=success"
                    if (-not [string]::IsNullOrWhiteSpace($downloadStatusCode)) {
                        $traceEntry += " [HTTP $downloadStatusCode"
                        if (-not [string]::IsNullOrWhiteSpace($downloadReasonPhrase)) {
                            $traceEntry += " $downloadReasonPhrase"
                        }
                        $traceEntry += "]"
                    }
                    $downloadTrace += $traceEntry
                    $downloadSucceeded = $true

                    $attemptSummary = if ($downloadTrace.Count -gt 0) { $downloadTrace -join "; " } else { "none" }

                    return [PSCustomObject]@{
                        Method       = $downloadMethod.Name
                        SizeBytes    = $sizeBytes
                        OutFile      = $OutFile
                        StatusCode   = $downloadStatusCode
                        ReasonPhrase = $downloadReasonPhrase
                        FinalUri     = $downloadFinalUri
                        AttemptSummary = $attemptSummary
                    }
                }
                catch {
                    $failureDiagnostic = Get-ExceptionDiagnostic -ErrorRecord $_
                    Write-Log "$($downloadMethod.Name) download strategy failed: $failureDiagnostic" "WARN"
                    Write-Log "Destination state after $($downloadMethod.Name) failure: $(Get-FileStateDescription -Path $OutFile)" "INFO"
                    $failureStatusCode = Get-HttpStatusCodeDisplayText -Value $failureDiagnostic
                    $traceEntry = "$($downloadMethod.Name)=failed"
                    if ($failureStatusCode -match '^\d{3}$') {
                        $traceEntry += " [HTTP $failureStatusCode]"
                    }
                    if (-not [string]::IsNullOrWhiteSpace($failureDiagnostic)) {
                        $traceEntry += " {$failureDiagnostic}"
                    }
                    $downloadTrace += $traceEntry
                    if (Test-Path -LiteralPath $OutFile) {
                        Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
                    }
                }
            }

            if (-not $downloadSucceeded) {
                $attemptSummary = if ($downloadTrace.Count -gt 0) { $downloadTrace -join "; " } else { "none" }
                Write-Log "Downloader methods attempted: $attemptSummary" "WARN"

                $failure = New-Object System.Exception("All download strategies failed for $Url")
                $failure.Data["AttemptedMethodSummary"] = $attemptSummary
                throw $failure
            }

            return
        }
        catch {
            Write-Log "Download attempt $attempt failed for $Url :: $(Get-ExceptionDiagnostic -ErrorRecord $_)" "WARN"
            if ($attempt -eq $Retries) { throw }
            Start-Sleep -Seconds ([Math]::Min(5 * $attempt, 15))
        }
    }
}

function Get-ShaFromUrl {
    param(
        [Parameter(Mandatory)][string]$ShaUrl,
        [int]$TimeoutSec = 60,
        [string]$UserAgent = "ForgerEMS-Updater/3.1"
    )

    try {
        $response = Get-UrlText -Url $ShaUrl -TimeoutSec $TimeoutSec -UserAgent $UserAgent
        $txt = ([string]$response.Text).Trim()
        $m = [regex]::Match($txt, '([a-fA-F0-9]{64})')
        if ($m.Success) {
            return [PSCustomObject]@{
                Sha256       = $m.Groups[1].Value.ToLowerInvariant()
                Method       = [string]$response.Method
                StatusCode   = Get-HttpStatusCodeDisplayText -Value $response.StatusCode
                ReasonPhrase = Get-NormalizedDisplayText -Value $response.ReasonPhrase
                FinalUri     = Get-NormalizedDisplayText -Value $response.FinalUri
            }
        }
        return [PSCustomObject]@{
            Sha256       = $null
            Method       = [string]$response.Method
            StatusCode   = Get-HttpStatusCodeDisplayText -Value $response.StatusCode
            ReasonPhrase = Get-NormalizedDisplayText -Value $response.ReasonPhrase
            FinalUri     = Get-NormalizedDisplayText -Value $response.FinalUri
        }
    }
    finally {
    }
}

function Archive-OldFile {
    param(
        [Parameter(Mandatory)][string]$ItemName,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$ArchiveDir,
        [int]$MaxKeep = 3
    )

    if (-not (Test-Path -LiteralPath $FilePath)) { return $false }

    Ensure-Dir -Path $ArchiveDir

    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $base  = Safe-FileName -Text $ItemName
    $ext   = [IO.Path]::GetExtension($FilePath)
    if ([string]::IsNullOrWhiteSpace($ext)) { $ext = ".bin" }

    $archived = Join-Path $ArchiveDir "$base`_$stamp$ext"

    Copy-Item -LiteralPath $FilePath -Destination $archived -Force

    $pattern = "$base`_*" + $ext
    $existing = Get-ChildItem -LiteralPath $ArchiveDir -Filter $pattern -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTime -Descending

    if ($existing.Count -gt $MaxKeep) {
        $toRemove = $existing | Select-Object -Skip $MaxKeep
        foreach ($r in $toRemove) {
            Remove-Item -LiteralPath $r.FullName -Force -ErrorAction SilentlyContinue
        }
    }

    return $true
}

function Resolve-UsbRoot {
    param(
        [string]$Drive,
        [string]$Root
    )

    if ($Drive -and $Root) {
        throw "Use either -DriveLetter or -UsbRoot, not both."
    }

    if ($Root) {
        $candidate = $Root.Trim()
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Path not found: $candidate"
        }
        return Resolve-SelectedUsbRoot -Path $candidate -Source "-UsbRoot"
    }

    if ($Drive) {
        $letter = $Drive.Trim().TrimEnd(":").ToUpper()
        if (-not $letter) { throw "Invalid drive letter." }

        $candidate = "$letter`:\"
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Drive not found: $candidate"
        }
        return $candidate.TrimEnd('\')
    }

    $currentBundleRoot = Find-ReleaseBundleRoot -Path $PSScriptRoot
    if ($currentBundleRoot) {
        $scriptDriveRoot = Get-PathDriveRoot -Path $currentBundleRoot
        if ($scriptDriveRoot) {
            Write-Host ("Detected the release bundle at '{0}'. Using USB root '{1}'." -f $currentBundleRoot, $scriptDriveRoot) -ForegroundColor Cyan
            return $scriptDriveRoot
        }
    }

    Write-Host "Enter either a drive letter or a full path on the target USB." -ForegroundColor Cyan
    Write-Host "If you choose this release bundle folder, the script will use the USB root." -ForegroundColor Cyan
    $entered = Read-Host "Enter your ForgerEMS USB drive letter or target path"
    if (-not $entered) {
        throw "No drive/root provided."
    }

    $entered = $entered.Trim()

    if ($entered -match '^[A-Za-z]:?$') {
        $letter = $entered.TrimEnd(':').ToUpper()
        $candidate = "$letter`:\"
        if (-not (Test-Path -LiteralPath $candidate)) {
            throw "Drive not found: $candidate"
        }
        return $candidate.TrimEnd('\')
    }

    if (-not (Test-Path -LiteralPath $entered)) {
        throw "Path not found: $entered"
    }

    return Resolve-SelectedUsbRoot -Path $entered -Source "Requested path"
}

function Get-NormalizedPath {
    param([Parameter(Mandatory)][string]$Path)

    [IO.Path]::GetFullPath($Path).TrimEnd('\')
}

function Get-PathDriveRoot {
    param([Parameter(Mandatory)][string]$Path)

    try {
        $fullPath = [IO.Path]::GetFullPath($Path)
        $driveRoot = [IO.Path]::GetPathRoot($fullPath)
        if ([string]::IsNullOrWhiteSpace($driveRoot)) {
            return $null
        }

        return $driveRoot.TrimEnd('\')
    }
    catch {
        return $null
    }
}

function Test-IsReleaseBundleRoot {
    param([Parameter(Mandatory)][string]$Path)

    foreach ($marker in @("RELEASE-BUNDLE.txt", "VERSION.txt", "ForgerEMS.updates.json")) {
        if (-not (Test-Path -LiteralPath (Join-Path $Path $marker))) {
            return $false
        }
    }

    return $true
}

function Find-ReleaseBundleRoot {
    param([Parameter(Mandatory)][string]$Path)

    $current = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')

    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-IsReleaseBundleRoot -Path $current) {
            return $current
        }

        $parentInfo = [IO.Directory]::GetParent($current + '\')
        if ($null -eq $parentInfo) {
            break
        }

        $parent = $parentInfo.FullName.TrimEnd('\')
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return $null
}

function Test-PathWithinRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Root
    )

    $normalizedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $normalizedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\')

    return ($normalizedPath -eq $normalizedRoot) -or $normalizedPath.StartsWith($normalizedRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsReleaseBundleScratchPath {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$BundleRoot
    )

    $scratchRoot = [IO.Path]::GetFullPath((Join-Path $BundleRoot ".verify")).TrimEnd('\')
    $normalizedPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')

    return Test-PathWithinRoot -Path $normalizedPath -Root $scratchRoot
}

function Resolve-SelectedUsbRoot {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Source
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path.TrimEnd('\')
    $bundleRoot = Find-ReleaseBundleRoot -Path $resolvedPath
    if (-not $bundleRoot) {
        $bundleRoot = Find-ReleaseBundleRoot -Path $PSScriptRoot
    }

    if ($bundleRoot -and (Test-PathWithinRoot -Path $resolvedPath -Root $bundleRoot)) {
        if (Test-IsReleaseBundleScratchPath -Path $resolvedPath -BundleRoot $bundleRoot) {
            return $resolvedPath
        }

        $driveRoot = Get-PathDriveRoot -Path $resolvedPath
        if ($driveRoot -and $resolvedPath -ne $driveRoot) {
            Write-Host ("{0} '{1}' is inside the release bundle. Using USB root '{2}' instead." -f $Source, $resolvedPath, $driveRoot) -ForegroundColor Yellow
            return $driveRoot
        }
    }

    return $resolvedPath
}

function Resolve-RootChildPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([string]::IsNullOrWhiteSpace($RelativePath)) {
        throw "Relative path cannot be empty."
    }

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Relative path must stay relative to the selected root. Path='$RelativePath'"
    }

    $normalizedRoot = Get-NormalizedPath -Path $Root
    $fullPath = [IO.Path]::GetFullPath((Join-Path $normalizedRoot $RelativePath))
    $isUnderRoot = $fullPath.StartsWith($normalizedRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)

    if (($fullPath.TrimEnd('\') -ne $normalizedRoot) -and -not $isUnderRoot) {
        throw "Resolved path escapes the selected root. Root='$normalizedRoot' Relative='$RelativePath' Resolved='$fullPath'"
    }

    return $fullPath
}

function Resolve-ManifestPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$ManifestSpecifier
    )

    $candidates = @()

    if ([IO.Path]::IsPathRooted($ManifestSpecifier)) {
        $candidates += [IO.Path]::GetFullPath($ManifestSpecifier)
    }
    else {
        $candidates += Resolve-RootChildPath -Root $Root -RelativePath $ManifestSpecifier
        $candidates += [IO.Path]::GetFullPath((Join-Path $PSScriptRoot $ManifestSpecifier))
        $candidates += [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ("manifests\" + $ManifestSpecifier)))
        $candidates += [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $PSScriptRoot) ("manifests\" + $ManifestSpecifier)))
    }

    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "Manifest not found. Checked: $($candidates -join '; ')"
}

function Get-BundledManifestPath {
    Resolve-ManifestPath -Root $PSScriptRoot -ManifestSpecifier "ForgerEMS.updates.json"
}

function Assert-ManifestStringField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value) { return }

    if ([string]::IsNullOrWhiteSpace([string]$Value)) {
        throw "$FieldName must be a non-empty JSON string."
    }
}

function Assert-ManifestTimestampField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    try {
        [DateTimeOffset]::Parse([string]$Value) | Out-Null
    }
    catch {
        throw "$FieldName must be an ISO-like date/time string."
    }
}

function Assert-ManifestReleaseTypeField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("dev", "candidate", "stable")) {
        throw "$FieldName must be 'dev', 'candidate', or 'stable'."
    }
}

function Assert-ManifestChecksumPolicyField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("warn", "require-for-release")) {
        throw "$FieldName must be 'warn' or 'require-for-release'."
    }
}

function Format-BuildTimestamp {
    param([AllowNull()]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return "untracked"
    }

    if ($Value -is [DateTime] -or $Value -is [DateTimeOffset]) {
        return ([DateTimeOffset]$Value).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    }

    return [string]$Value
}

function Assert-ManifestBooleanField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value) { return }

    if ($Value -isnot [bool]) {
        throw "$FieldName must be a JSON boolean."
    }
}

function Assert-ManifestPositiveIntegerField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName,
        [int]$Minimum = 1
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    if (-not ([string]$Value -match '^\d+$')) {
        throw "$FieldName must be a whole-number JSON value."
    }

    if ([int64]$Value -lt $Minimum) {
        throw "$FieldName must be greater than or equal to $Minimum."
    }
}

function Assert-ManifestSha256Field {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    if (-not ([string]$Value -match '^[a-fA-F0-9]{64}$')) {
        throw "$FieldName must be a 64-character SHA-256 hex string."
    }
}

function Assert-ManifestSourceTypeField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("sourceforge", "github-release", "official-mirror", "official-version-path")) {
        throw "$FieldName must be 'sourceforge', 'github-release', 'official-mirror', or 'official-version-path'."
    }
}

function Assert-ManifestFragilityLevelField {
    param(
        [AllowNull()]$Value,
        [Parameter(Mandatory)][string]$FieldName
    )

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) { return }

    $normalized = ([string]$Value).Trim().ToLowerInvariant()
    if ($normalized -notin @("low", "medium", "high")) {
        throw "$FieldName must be 'low', 'medium', or 'high'."
    }
}

function Assert-ManifestContract {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$SourceName
    )

    if ($null -eq $Manifest) {
        throw "Manifest '$SourceName' is empty or invalid."
    }

    if ($null -eq $Manifest.settings) {
        Add-Member -InputObject $Manifest -MemberType NoteProperty -Name settings -Value ([PSCustomObject]@{}) -Force
    }

    if ($null -eq $Manifest.items) {
        throw "Manifest '$SourceName' must contain an items array."
    }

    $settings = $Manifest.settings

    foreach ($folderField in @("downloadFolder", "archiveFolder", "logFolder")) {
        $folderValue = $settings.$folderField
        if ($null -ne $folderValue -and -not [string]::IsNullOrWhiteSpace([string]$folderValue)) {
            Resolve-RootChildPath -Root $Root -RelativePath ([string]$folderValue) | Out-Null
        }
    }

    Assert-ManifestPositiveIntegerField -Value $Manifest.manifestVersion -FieldName "manifestVersion"
    Assert-ManifestStringField -Value $Manifest.coreName -FieldName "coreName"
    Assert-ManifestStringField -Value $Manifest.coreVersion -FieldName "coreVersion"
    Assert-ManifestTimestampField -Value $Manifest.buildTimestampUtc -FieldName "buildTimestampUtc"
    Assert-ManifestReleaseTypeField -Value $Manifest.releaseType -FieldName "releaseType"
    Assert-ManifestChecksumPolicyField -Value $Manifest.managedChecksumPolicy -FieldName "managedChecksumPolicy"
    Assert-ManifestPositiveIntegerField -Value $settings.timeoutSec -FieldName "settings.timeoutSec"
    Assert-ManifestPositiveIntegerField -Value $settings.retryCount -FieldName "settings.retryCount"
    Assert-ManifestPositiveIntegerField -Value $settings.maxArchivePerItem -FieldName "settings.maxArchivePerItem"

    $items = @($Manifest.items)
    if ($items.Count -eq 0) {
        throw "Manifest '$SourceName' must contain at least one item."
    }

    for ($i = 0; $i -lt $items.Count; $i++) {
        $item = $items[$i]
        $prefix = "items[$i]"

        if ($null -eq $item) {
            throw "$prefix cannot be null."
        }

        $name = [string]$item.name
        $url = [string]$item.url
        $dest = [string]$item.dest
        $type = if ($item.type) { ([string]$item.type).Trim().ToLowerInvariant() } else { "file" }

        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "$prefix.name is required."
        }

        if ([string]::IsNullOrWhiteSpace($url)) {
            throw "$prefix.url is required."
        }

        if ([string]::IsNullOrWhiteSpace($dest)) {
            throw "$prefix.dest is required."
        }

        if ($type -notin @("file", "page")) {
            throw "$prefix.type must be 'file' or 'page'."
        }

        Resolve-RootChildPath -Root $Root -RelativePath $dest | Out-Null
        Assert-ManifestBooleanField -Value $item.enabled -FieldName "$prefix.enabled"
        Assert-ManifestBooleanField -Value $item.archive -FieldName "$prefix.archive"
        Assert-ManifestPositiveIntegerField -Value $item.timeoutSec -FieldName "$prefix.timeoutSec"
        Assert-ManifestSha256Field -Value $item.sha256 -FieldName "$prefix.sha256"
        Assert-ManifestSourceTypeField -Value $item.sourceType -FieldName "$prefix.sourceType"
        Assert-ManifestFragilityLevelField -Value $item.fragilityLevel -FieldName "$prefix.fragilityLevel"
        Assert-ManifestStringField -Value $item.fallbackRule -FieldName "$prefix.fallbackRule"
        Assert-ManifestPositiveIntegerField -Value $item.maintenanceRank -FieldName "$prefix.maintenanceRank"
        Assert-ManifestBooleanField -Value $item.borderline -FieldName "$prefix.borderline"

        if ($null -ne $item.sha256Url -and -not [string]::IsNullOrWhiteSpace([string]$item.sha256Url)) {
            if ($type -ne "file") {
                throw "$prefix.sha256Url is only valid for file items."
            }
        }

        $hasResilienceMetadata = (
            ($null -ne $item.sourceType -and -not [string]::IsNullOrWhiteSpace([string]$item.sourceType)) -or
            ($null -ne $item.fragilityLevel -and -not [string]::IsNullOrWhiteSpace([string]$item.fragilityLevel)) -or
            ($null -ne $item.fallbackRule -and -not [string]::IsNullOrWhiteSpace([string]$item.fallbackRule)) -or
            ($null -ne $item.maintenanceRank -and -not [string]::IsNullOrWhiteSpace([string]$item.maintenanceRank)) -or
            ($null -ne $item.borderline)
        )

        if ($hasResilienceMetadata -and $type -ne "file") {
            throw "$prefix.sourceType, $prefix.fragilityLevel, $prefix.fallbackRule, $prefix.maintenanceRank, and $prefix.borderline are only valid for file items."
        }
    }
}

function Get-VentoyCoreVersionInfo {
    $manifestPath = Get-BundledManifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

    return [PSCustomObject]@{
        Name              = if ($manifest.coreName) { [string]$manifest.coreName } else { "ForgerEMS Ventoy Core" }
        Version           = if ($manifest.coreVersion) { [string]$manifest.coreVersion } else { "0.0.0-dev" }
        BuildTimestampUtc = Format-BuildTimestamp -Value $manifest.buildTimestampUtc
        ReleaseType       = if ($manifest.releaseType) { ([string]$manifest.releaseType).Trim().ToLowerInvariant() } else { "dev" }
        ManifestPath      = $manifestPath
    }
}

function Get-ManifestItemExecutionOrder {
    param([Parameter(Mandatory)]$Item)

    $type = ([string]$(if ($Item.type) { $Item.type } else { "file" })).Trim().ToLowerInvariant()
    if ($type -eq "page") {
        return 900
    }

    $dest = ([string]$Item.dest).Trim()

    switch -Wildcard ($dest) {
        "Tools\Portable\USB\*"      { return 10 }
        "Tools\Portable\Security\*" { return 20 }
        "Tools\Portable\Disk\*"     { return 30 }
        "Tools\Portable\Hardware\*" { return 40 }
        "Tools\Portable\System\*"   { return 50 }
        "Tools\Portable\Remote\*"   { return 60 }
        "Tools\Portable\GPU\*"      { return 70 }
        "Tools\Portable\Network\*"  { return 80 }
        "ISO\Tools\*"               { return 100 }
        "ISO\Windows\*"             { return 110 }
        "ISO\Linux\*"               { return 120 }
        default                     { return 200 }
    }
}

function Show-VentoyCoreVersionInfo {
    $info = Get-VentoyCoreVersionInfo
    Write-Host ("{0} {1} ({2})" -f $info.Name, $info.Version, $info.BuildTimestampUtc) -ForegroundColor Cyan
    Write-Host ("Release: " + $info.ReleaseType) -ForegroundColor DarkCyan
    Write-Host ("Manifest: " + $info.ManifestPath) -ForegroundColor DarkCyan
}

if ($ShowVersion) {
    Show-VentoyCoreVersionInfo
    return
}

$root = Resolve-UsbRoot -Drive $DriveLetter -Root $UsbRoot
$manifestPath = Resolve-ManifestPath -Root $root -ManifestSpecifier $ManifestName

$manifestRaw = Get-Content -LiteralPath $manifestPath -Raw
$manifest = $manifestRaw | ConvertFrom-Json
Assert-ManifestContract -Manifest $manifest -Root $root -SourceName $manifestPath

$dlDir     = Resolve-RootChildPath -Root $root -RelativePath ($(if ($manifest.settings.downloadFolder) { [string]$manifest.settings.downloadFolder } else { "_downloads" }))
$arcDir    = Resolve-RootChildPath -Root $root -RelativePath ($(if ($manifest.settings.archiveFolder)  { [string]$manifest.settings.archiveFolder }  else { "_archive" }))
$logDir    = Resolve-RootChildPath -Root $root -RelativePath ($(if ($manifest.settings.logFolder)      { [string]$manifest.settings.logFolder }      else { "_logs" }))
$timeout   = [int]($(if ($manifest.settings.timeoutSec) { $manifest.settings.timeoutSec } else { 180 }))
$userAgent = $(if ($manifest.settings.userAgent) { [string]$manifest.settings.userAgent } else { "ForgerEMS-Updater/3.1" })
$maxKeep   = [int]($(if ($manifest.settings.maxArchivePerItem) { $manifest.settings.maxArchivePerItem } else { 3 }))
$retries   = [int]($(if ($manifest.settings.retryCount) { $manifest.settings.retryCount } else { 3 }))

Ensure-Dir -Path $dlDir
Ensure-Dir -Path $logDir
if (-not $NoArchive) {
    Ensure-Dir -Path $arcDir
}

$script:LogFile = Join-Path $logDir ("update_" + (Get-Date -Format "yyyyMMdd_HHmmss") + ".log")

Write-Log ("Ventoy core: {0} {1} ({2})" -f $(if ($manifest.coreName) { [string]$manifest.coreName } else { "ForgerEMS Ventoy Core" }), $(if ($manifest.coreVersion) { [string]$manifest.coreVersion } else { "0.0.0-dev" }), $(Format-BuildTimestamp -Value $manifest.buildTimestampUtc)) "INFO"
Write-Log ("Release: " + $(if ($manifest.releaseType) { ([string]$manifest.releaseType).Trim().ToLowerInvariant() } else { "dev" })) "INFO"
Write-Log "Root: $root" "INFO"
Write-Log "Manifest: $manifestPath" "INFO"
Write-Log "Force=$Force VerifyOnly=$VerifyOnly NoArchive=$NoArchive" "INFO"

if (-not $manifest.items) {
    throw "Manifest has no items."
}

$orderedItems = @($manifest.items) | Sort-Object `
    @{ Expression = { Get-ManifestItemExecutionOrder -Item $_ } }, `
    @{ Expression = { ([string]$(if ($_.dest) { $_.dest } else { "" })).Trim() } }, `
    @{ Expression = { ([string]$(if ($_.name) { $_.name } else { "" })).Trim() } }

$activeManagedPlaceholderPlan = Get-ActiveManagedPlaceholderPlan -Items $orderedItems

$enabledManagedFileItems = @(
    $orderedItems | Where-Object {
        $itemEnabled = $true
        if ($null -ne $_.enabled) {
            $itemEnabled = [bool]$_.enabled
        }

        $itemType = ([string]$(if ($_.type) { $_.type } else { "file" })).Trim().ToLowerInvariant()
        $itemEnabled -and $itemType -eq "file"
    }
)
$enabledPlaceholderItems = @(
    $orderedItems | Where-Object {
        $itemEnabled = $true
        if ($null -ne $_.enabled) {
            $itemEnabled = [bool]$_.enabled
        }

        $itemType = ([string]$(if ($_.type) { $_.type } else { "file" })).Trim().ToLowerInvariant()
        $destKey = Get-ManifestDestinationKey -RelativePath ([string]$(if ($_.dest) { $_.dest } else { "" }))
        $itemEnabled -and $itemType -eq "page" -and -not $activeManagedPlaceholderPlan.ByPlaceholderDest.ContainsKey($destKey)
    }
)

Write-Log "Managed download phase started." "INFO"
Write-Log "Queued managed auto-download items: $($enabledManagedFileItems.Count)" "INFO"
Write-Log "Queued placeholder/info shortcut items: $($enabledPlaceholderItems.Count)" "INFO"
Write-Log "Suppressed placeholder/info shortcuts for active managed downloads: $($activeManagedPlaceholderPlan.ByPlaceholderDest.Count)" "INFO"
Write-Log "Execution order: portable tools first, larger ISO items later, shortcuts last." "INFO"

foreach ($queuedItem in $enabledManagedFileItems) {
    $queuedName = ([string]$(if ($queuedItem.name) { $queuedItem.name } else { "<unnamed managed item>" })).Trim()
    $queuedDest = ([string]$(if ($queuedItem.dest) { $queuedItem.dest } else { "<no-destination>" })).Trim()
    Write-Log "Queued managed item: $queuedName -> $queuedDest" "INFO"
}

foreach ($queuedPlaceholder in $enabledPlaceholderItems) {
    $queuedName = ([string]$(if ($queuedPlaceholder.name) { $queuedPlaceholder.name } else { "<unnamed placeholder item>" })).Trim()
    $queuedDest = ([string]$(if ($queuedPlaceholder.dest) { $queuedPlaceholder.dest } else { "<no-destination>" })).Trim()
    Write-Log "Queued placeholder item: $queuedName -> $queuedDest" "INFO"
}

foreach ($item in $orderedItems) {
    $script:Summary.Total++

    $name = ([string]$item.name).Trim()
    $type = ([string]$(if ($item.type) { $item.type } else { "file" })).Trim().ToLowerInvariant()
    $url  = ([string]$item.url).Trim()
    $destRel = ([string]$item.dest).Trim()
    $enabled = $true
    if ($null -ne $item.enabled) { $enabled = [bool]$item.enabled }

    if (-not $enabled) {
        Write-Log "Skipping disabled item: $name" "WARN"
        $script:Summary.Disabled++
        continue
    }

    if ([string]::IsNullOrWhiteSpace($name) -or
        [string]::IsNullOrWhiteSpace($url) -or
        [string]::IsNullOrWhiteSpace($destRel)) {
        Write-Log "Skipping invalid manifest item (missing name/url/dest)." "WARN"
        $script:Summary.Failed++
        continue
    }

    $dest = Resolve-RootChildPath -Root $root -RelativePath $destRel
    $destDir = Split-Path -Parent $dest
    Ensure-Dir -Path $destDir

    $itemTimeout = if ($item.timeoutSec) { [int]$item.timeoutSec } else { $timeout }
    $archiveItem = $true
    if ($null -ne $item.archive) { $archiveItem = [bool]$item.archive }

    Write-Log "---- $name ----" "INFO"
    Write-Log "Manifest item selected: $name" "INFO"
    $destKey = Get-ManifestDestinationKey -RelativePath $destRel
    if ($type -eq "page" -and $activeManagedPlaceholderPlan.ByPlaceholderDest.ContainsKey($destKey)) {
        Write-Log "Type: $type" "INFO"
        Write-Log "Dest: $destRel" "INFO"
        Write-Log "Resolved destination path: $dest" "INFO"
        Write-Log "Manifest source URL: $url" "INFO"
        Write-Log "Skipped placeholder creation because item is active managed download: $destRel" "INFO"
        $script:Summary.Skipped++
        continue
    }
    if ($type -eq "page") {
        $script:Summary.PlaceholderItems++
        Write-Log "Item role: seeded placeholder / info shortcut" "INFO"
    }
    else {
        $script:Summary.ManagedFileItems++
        Write-Log "Item role: managed auto-download item" "INFO"
    }
    Write-Log "Type: $type" "INFO"
    Write-Log "Dest: $destRel" "INFO"
    Write-Log "Resolved destination path: $dest" "INFO"
    Write-Log "Manifest source URL: $url" "INFO"

    if ($type -eq "page") {
        if ($VerifyOnly) {
            Write-Log "VerifyOnly: skipping page shortcut." "WARN"
            $script:Summary.Skipped++
            continue
        }

        if (-not $PSCmdlet.ShouldProcess($destRel, "Write URL shortcut")) {
            Write-Log "Would update shortcut: $destRel -> $url" "INFO"
            $script:Summary.Skipped++
            continue
        }

        try {
            Write-UrlShortcut -ShortcutPath $dest -Url $url
            Write-Log "Shortcut updated: $destRel" "OK"
            $script:Summary.Shortcut++
            $script:Summary.PlaceholderOnly++
        }
        catch {
            Write-Log "Shortcut write failed: $($_.Exception.Message)" "ERROR"
            $script:Summary.Failed++
        }

        continue
    }

    if ($type -ne "file") {
        Write-Log "Unsupported item type '$type' for '$name'. Supported: file, page." "WARN"
        $script:Summary.Failed++
        continue
    }

    $preferredFallbackShortcutPath = Get-PreferredFallbackShortcutPath -Root $root -ManagedDestination $destRel -ManagedPlaceholderPlan $activeManagedPlaceholderPlan
    $sha = ([string]$item.sha256).Trim().ToLowerInvariant()
    $shaUrl = ([string]$item.sha256Url).Trim()
    $shaResult = $null

    if (-not $sha -and $shaUrl -and ($VerifyOnly -or -not $WhatIfPreference)) {
        Write-Log "Checksum source URL: $shaUrl" "INFO"
        try {
            $shaResult = Get-ShaFromUrl -ShaUrl $shaUrl -TimeoutSec $itemTimeout -UserAgent $userAgent
            $sha = ([string]$shaResult.Sha256).Trim().ToLowerInvariant()
            if ($sha) {
                Write-Log "Checksum source resolved via $($shaResult.Method)." "OK"
                if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.StatusCode)) {
                    Write-Log (("Checksum source HTTP status: $($shaResult.StatusCode) $($shaResult.ReasonPhrase)").TrimEnd()) "INFO"
                }
                if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.FinalUri)) {
                    Write-Log "Checksum source final URL: $($shaResult.FinalUri)" "INFO"
                    Write-Log "Resolved checksum source URL: $($shaResult.FinalUri)" "INFO"
                }
                Write-Log "Fetched SHA256 from sha256Url: $sha" "OK"
            }
            else {
                Write-Log "sha256Url was provided but no valid hash was parsed." "WARN"
                if ($shaResult) {
                    Write-Log "Checksum source method result: $($shaResult.Method)" "INFO"
                    if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.StatusCode)) {
                        Write-Log (("Checksum source HTTP status: $($shaResult.StatusCode) $($shaResult.ReasonPhrase)").TrimEnd()) "INFO"
                    }
                    if (-not [string]::IsNullOrWhiteSpace([string]$shaResult.FinalUri)) {
                        Write-Log "Checksum source final URL: $($shaResult.FinalUri)" "INFO"
                        Write-Log "Resolved checksum source URL: $($shaResult.FinalUri)" "INFO"
                    }
                }
            }
        }
        catch {
            Write-Log "Failed fetching sha256Url: $(Get-ExceptionDiagnostic -ErrorRecord $_)" "WARN"
        }
    }
    elseif (-not $sha -and $shaUrl -and $WhatIfPreference) {
        Write-Log "Checksum source URL: $shaUrl" "INFO"
        Write-Log "WhatIf: would fetch SHA256 from sha256Url during a real run." "INFO"
    }
    elseif ($sha) {
        Write-Log "Pinned SHA256 from manifest: $sha" "INFO"
        if ($shaUrl) {
            Write-Log "Checksum source URL available for maintenance: $shaUrl" "INFO"
        }
        else {
            Write-Log "Checksum source URL: not provided (using pinned manifest SHA256 only)." "INFO"
        }
    }

    if ($VerifyOnly) {
        if (-not (Test-Path -LiteralPath $dest)) {
            Write-Log "Verify failed: destination missing: $destRel" "ERROR"
            $script:Summary.Failed++
            continue
        }

        if ($sha) {
            $cur = Get-Sha256 -Path $dest
            Write-Log "Checksum expected vs actual: expected=$sha actual=$cur" "INFO"
            if ($cur -eq $sha) {
                Write-Log "Verified OK (sha256 match)." "OK"
                Write-Log "Checksum verified: $cur" "OK"
                Write-Log "Destination state after verify: $(Get-FileStateDescription -Path $dest)" "INFO"
                $script:Summary.Verified++
            }
            else {
                Write-Log "Verify failed: sha256 mismatch. Expected=$sha Got=$cur" "ERROR"
                $script:Summary.Failed++
            }
        }
        else {
            Write-Log "No sha256 provided; cannot verify '$name'." "WARN"
            $script:Summary.Skipped++
        }

        continue
    }

    if ($WhatIfPreference) {
        if (-not $Force -and (Test-Path -LiteralPath $dest)) {
            if ($sha) {
                Write-Log "WhatIf: destination exists; would calculate SHA256 and skip if it already matches." "INFO"
            }
            else {
                Write-Log "Destination exists and no sha256 is provided. Would skip to avoid blind overwrite." "WARN"
                $script:Summary.Skipped++
                continue
            }
        }
    }
    elseif (-not $Force -and $sha -and (Test-Path -LiteralPath $dest)) {
        $cur = Get-Sha256 -Path $dest
        if ($cur -eq $sha) {
            Write-Log "Up-to-date (sha256 match). Skipping." "OK"
            [void](Remove-ManagedSuccessPlaceholders -Root $root -ManagedDestination $destRel -ManagedPlaceholderPlan $activeManagedPlaceholderPlan)
            $script:Summary.Verified++
            $script:Summary.Skipped++
            continue
        }
    }
    elseif (-not $Force -and -not $sha -and (Test-Path -LiteralPath $dest)) {
        Write-Log "Destination exists and no sha256 is provided. Skipping to avoid blind overwrite." "WARN"
        $script:Summary.Skipped++
        continue
    }

    if (-not $PSCmdlet.ShouldProcess($destRel, "Download, verify, archive, and replace destination")) {
        Write-Log "Would update file: $destRel from $url" "INFO"
        $script:Summary.Skipped++
        continue
    }

    $tmpName = Safe-FileName -Text $name
    $tmpPath = Join-Path $dlDir ($tmpName + ".download")
    $downloadResult = $null

    try {
        Write-Log "Download start: $name" "INFO"
        $downloadResult = Download-File -Url $url -OutFile $tmpPath -TimeoutSec $itemTimeout -UserAgent $userAgent -Retries $retries
        if ($downloadResult) {
            if (-not [string]::IsNullOrWhiteSpace([string]$downloadResult.AttemptSummary)) {
                Write-Log "Downloader methods attempted: $($downloadResult.AttemptSummary)" "INFO"
            }
            Write-Log "Downloader used: $($downloadResult.Method)" "INFO"
            if (-not [string]::IsNullOrWhiteSpace([string]$downloadResult.StatusCode)) {
                Write-Log (("Download HTTP status: $($downloadResult.StatusCode) $($downloadResult.ReasonPhrase)").TrimEnd()) "INFO"
            }
            if (-not [string]::IsNullOrWhiteSpace([string]$downloadResult.FinalUri)) {
                Write-Log "Resolved source URL: $($downloadResult.FinalUri)" "INFO"
            }
            Write-Log "Staged file existence and size: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
        }
    }
    catch {
        Write-Log "Item failed: $name" "ERROR"
        Write-Log "Download failed for '$name': $(Get-ExceptionDiagnostic -ErrorRecord $_)" "ERROR"
        if ($_.Exception.Data.Contains("AttemptedMethodSummary")) {
            Write-Log "Downloader methods attempted: $($_.Exception.Data['AttemptedMethodSummary'])" "WARN"
        }
        Write-Log "Staged file existence and size: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
        $fallbackResult = Write-DownloadFallbackShortcut -DestinationPath $dest -ItemName $name -Url $url -PreferredShortcutPath $preferredFallbackShortcutPath
        $script:Summary.FailedWithFallback++
        switch ($fallbackResult.Outcome) {
            "created"  { $script:Summary.FallbackShortcutsCreated++ }
            "existing" { $script:Summary.FallbackShortcutsReused++ }
        }
        if ($fallbackResult.Outcome -eq "created" -or $fallbackResult.Outcome -eq "existing") {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome) -> $($fallbackResult.ShortcutPath)" "WARN"
        }
        else {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome)" "WARN"
        }
        Write-Log "Item staging verdict: FAILED WITH FALLBACK" "ERROR"
        $script:Summary.Failed++
        continue
    }

    try {
        $verifiedHash = $null
        if ($sha) {
            $verifiedHash = Get-Sha256 -Path $tmpPath
            Write-Log "Checksum expected vs actual: expected=$sha actual=$verifiedHash" "INFO"
            if ($verifiedHash -ne $sha) {
                throw "SHA256 mismatch. Expected=$sha Got=$verifiedHash"
            }
            Write-Log "Checksum verification passed: $name" "OK"
            Write-Log "Checksum verified: $verifiedHash" "OK"
            $script:Summary.Verified++
        }
        else {
            Write-Log "Checksum verification skipped: no sha256 set for '$name' (recommended for important ISOs/tools)." "WARN"
        }

        if (-not $NoArchive -and $archiveItem -and (Test-Path -LiteralPath $dest)) {
            $didArchive = Archive-OldFile -ItemName $name -FilePath $dest -ArchiveDir $arcDir -MaxKeep $maxKeep
            if ($didArchive) {
                Write-Log "Archived old file." "OK"
                $script:Summary.Archived++
            }
        }

        Move-Item -LiteralPath $tmpPath -Destination $dest -Force

        Write-Log "Final file written: $dest" "OK"
        Write-Log "Final destination write result: success -> $(Get-FileStateDescription -Path $dest)" "INFO"
        if ($sha) {
            Write-Log "Verified payload ready at destination with expected SHA256: $sha" "OK"
        }
        [void](Remove-ManagedSuccessPlaceholders -Root $root -ManagedDestination $destRel -ManagedPlaceholderPlan $activeManagedPlaceholderPlan)
        Write-Log "Updated: $name" "OK"
        Write-Log "Item staging verdict: STAGED" "OK"
        $script:Summary.Downloaded++
        $script:Summary.Updated++
    }
    catch {
        Write-Log "Item failed: $name" "ERROR"
        Write-Log "Update failed for '$name': $(Get-ExceptionDiagnostic -ErrorRecord $_)" "ERROR"
        Write-Log "Staged file existence and size: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
        Write-Log "Final destination write result: failed -> $dest" "ERROR"
        $fallbackResult = Write-DownloadFallbackShortcut -DestinationPath $dest -ItemName $name -Url $url -PreferredShortcutPath $preferredFallbackShortcutPath
        $script:Summary.FailedWithFallback++
        switch ($fallbackResult.Outcome) {
            "created"  { $script:Summary.FallbackShortcutsCreated++ }
            "existing" { $script:Summary.FallbackShortcutsReused++ }
        }
        if ($fallbackResult.Outcome -eq "created" -or $fallbackResult.Outcome -eq "existing") {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome) -> $($fallbackResult.ShortcutPath)" "WARN"
        }
        else {
            Write-Log "Fallback shortcut outcome for '$name': $($fallbackResult.Outcome)" "WARN"
        }
        Write-Log "Item staging verdict: FAILED WITH FALLBACK" "ERROR"
        $script:Summary.Failed++
        if (Test-Path -LiteralPath $tmpPath) {
            Remove-Item -LiteralPath $tmpPath -Force -ErrorAction SilentlyContinue
        }
        Write-Log "Staged file existence and size after cleanup: $(Get-FileStateDescription -Path $tmpPath)" "INFO"
    }
}

$skippedOrPlaceholderOnly = $script:Summary.Skipped + $script:Summary.PlaceholderOnly
$finalFailureMessage = $null

Write-Log "---------------- MANAGED-DOWNLOAD SUMMARY ----------------" "INFO"
Write-Log "Total manifest items: $($script:Summary.Total)" "INFO"
Write-Log "Managed auto-download items: $($script:Summary.ManagedFileItems)" "INFO"
Write-Log "Seeded placeholder/info shortcut items: $($script:Summary.PlaceholderItems)" "INFO"
Write-Log "Downloaded successfully: $($script:Summary.Downloaded)" "INFO"
Write-Log "Verified successfully: $($script:Summary.Verified)" "INFO"
Write-Log "Failed and covered by fallback shortcut: $($script:Summary.FailedWithFallback)" "INFO"
Write-Log "Skipped / placeholder only: $skippedOrPlaceholderOnly" "INFO"
Write-Log "Fallback shortcuts created: $($script:Summary.FallbackShortcutsCreated)" "INFO"
Write-Log "Fallback shortcuts reused: $($script:Summary.FallbackShortcutsReused)" "INFO"
Write-Log "Archived prior files: $($script:Summary.Archived)" "INFO"
Write-Log "Disabled manifest items: $($script:Summary.Disabled)" "INFO"
Write-Log "Total failed items: $($script:Summary.Failed)" "INFO"

if ($script:Summary.Failed -gt 0) {
    Write-Log "USB readiness: PARTIALLY STAGED. Review failed items and the fallback shortcuts before treating this USB as ready." "ERROR"
    $finalFailureMessage = "Managed download pass completed with $($script:Summary.Failed) failed item(s). The USB is only partially staged."
}
else {
    Write-Log "USB readiness: READY. Managed auto-download items completed without failures." "OK"
}

if ($script:LogFile -and $WhatIfPreference) {
    Write-Log "Log file write skipped because -WhatIf is active: $script:LogFile" "INFO"
}
elseif ($script:LogFile -and (Test-Path -LiteralPath (Split-Path -Parent $script:LogFile))) {
    Write-Log "Log saved: $script:LogFile" "OK"
}
else {
    Write-Log "Log file was not created because the log directory is unavailable." "INFO"
}

if ($finalFailureMessage) {
    throw $finalFailureMessage
}
