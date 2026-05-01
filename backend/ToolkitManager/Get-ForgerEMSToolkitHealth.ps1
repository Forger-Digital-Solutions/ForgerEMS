#requires -Version 5.1

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TargetRoot,
    [string]$ManifestPath = ""
)

$ErrorActionPreference = "Stop"

function Write-ToolkitLog {
    param(
        [Parameter(Mandatory)][string]$Message,
        [ValidateSet("INFO", "OK", "WARN", "ERROR")][string]$Level = "INFO"
    )

    Write-Host ("[{0}] {1}" -f $Level, $Message)
}

function Resolve-ManifestPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return [IO.Path]::GetFullPath($ExplicitPath)
    }

    $candidates = @(
        (Join-Path $PSScriptRoot "..\ForgerEMS.updates.json"),
        (Join-Path $PSScriptRoot "..\manifests\ForgerEMS.updates.json"),
        (Join-Path $PSScriptRoot "..\..\manifests\ForgerEMS.updates.json"),
        (Join-Path (Get-Location).Path "manifests\ForgerEMS.updates.json"),
        (Join-Path (Get-Location).Path "ForgerEMS.updates.json")
    )

    foreach ($candidate in $candidates) {
        $resolved = [IO.Path]::GetFullPath($candidate)
        if (Test-Path -LiteralPath $resolved) {
            return $resolved
        }
    }

    throw "Could not resolve manifests\ForgerEMS.updates.json."
}

function Get-LocalReportRoot {
    $localAppData = [Environment]::GetFolderPath("LocalApplicationData")
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = [IO.Path]::GetTempPath()
    }

    return (Join-Path $localAppData "ForgerEMS\Runtime\reports")
}

function Test-IsCRoot {
    param([Parameter(Mandatory)][string]$Path)

    $root = [IO.Path]::GetPathRoot([IO.Path]::GetFullPath($Path))
    return [string]::Equals($root, "C:\", [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-Category {
    param([string]$Destination)

    if ([string]::IsNullOrWhiteSpace($Destination)) {
        return "General"
    }

    $parts = $Destination -split '[\\/]+' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($parts.Count -ge 3 -and $parts[0] -eq "Tools" -and $parts[1] -eq "Portable") {
        return [string]$parts[2]
    }

    if ($parts.Count -ge 2) {
        return [string]$parts[1]
    }

    return [string]$parts[0]
}

function Get-ToolVersion {
    param([string]$Name)

    if ([string]::IsNullOrWhiteSpace($Name)) {
        return "Unknown"
    }

    $match = [regex]::Match($Name, '\b(v?\d+(?:\.\d+){1,3}(?:-\d+)?)\b')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return "Manual"
}

function Normalize-RelativePath {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    return $Path.Trim().TrimStart('\', '/').Replace('/', '\')
}

function Get-ItemClassification {
    param($Item)

    $type = [string]$Item.type
    $notes = [string]$Item.notes
    $sourceType = [string]$Item.sourceType

    if ($type -eq "page") {
        return [PSCustomObject]@{
            Name = "manualDownload"
            Reason = "Manifest item type is page; this is a vendor/manual shortcut, not an auto-download payload."
        }
    }

    if ($notes -match '(?i)manual only|review first|TODO-safe|shortcut only|placeholder') {
        return [PSCustomObject]@{
            Name = "manualDownload"
            Reason = "Manifest notes mark this item as manual, review-first, shortcut-only, or placeholder."
        }
    }

    if ($sourceType -match '(?i)manual|page') {
        return [PSCustomObject]@{
            Name = "manualDownload"
            Reason = "Manifest source type indicates a manual or page-based item."
        }
    }

    if ($null -ne $Item.optional -and [bool]$Item.optional) {
        return [PSCustomObject]@{
            Name = "optional"
            Reason = "Manifest marks this item optional."
        }
    }

    return [PSCustomObject]@{
        Name = "managedAutoDownload"
        Reason = "Manifest item is an enabled file payload managed by Update-ForgerEMS."
    }
}

function Get-RequirementLevel {
    param($Item, [string]$Classification)

    if ($Classification -eq "managedAutoDownload") {
        return "required"
    }

    if ($Classification -eq "optional") {
        return "optional"
    }

    return "manual"
}

function Resolve-ExpectedItemPath {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [string]$Destination
    )

    $relative = Normalize-RelativePath -Path $Destination
    if ([string]::IsNullOrWhiteSpace($relative)) {
        return ""
    }

    return Join-Path $TargetRoot $relative
}

function Resolve-FallbackShortcutPath {
    param(
        [string]$DestinationPath,
        [string]$Destination
    )

    if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
        return ""
    }

    if ([IO.Path]::GetExtension($DestinationPath) -eq ".url") {
        return $DestinationPath
    }

    $directory = Split-Path -Parent $DestinationPath
    $fileName = [IO.Path]::GetFileNameWithoutExtension($DestinationPath)
    if ([string]::IsNullOrWhiteSpace($directory) -or [string]::IsNullOrWhiteSpace($fileName)) {
        return [IO.Path]::ChangeExtension($DestinationPath, ".url")
    }

    $downloadShortcut = Join-Path $directory ("DOWNLOAD - {0}.url" -f $fileName)
    if (Test-Path -LiteralPath $downloadShortcut -PathType Leaf) {
        return $downloadShortcut
    }

    return [IO.Path]::ChangeExtension($DestinationPath, ".url")
}

function Get-NormalizedNameTokens {
    param(
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $commonTokens = @(
        "amd64", "x64", "x86", "64bit", "32bit", "windows", "win", "live", "setup",
        "installer", "desktop", "portable", "package", "oracular", "stable", "plus",
        "download", "page", "official"
    )

    $clean = $Text.ToLowerInvariant() -replace '[^a-z0-9]+', ' '
    return @(
        $clean -split '\s+' |
            Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) -and
                $_.Length -ge 4 -and
                $_ -notmatch '^\d+$' -and
                $_ -notin $commonTokens
            } |
            Select-Object -Unique
    )
}

function Test-NormalizedFileNameMatch {
    param(
        [Parameter(Mandatory)][IO.FileInfo]$Candidate,
        [string[]]$Tokens,
        [string]$ExpectedExtension
    )

    if ($Tokens.Count -eq 0) {
        return $false
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedExtension) -and
        -not [string]::Equals($Candidate.Extension, $ExpectedExtension, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $candidateTokens = @(Get-NormalizedNameTokens -Text $Candidate.BaseName)
    foreach ($token in $Tokens) {
        if ($candidateTokens -contains $token) {
            return $true
        }
    }

    return $false
}

function Find-AlternateItemPath {
    param(
        [Parameter(Mandatory)][string]$TargetRoot,
        [Parameter(Mandatory)][string]$DestinationPath,
        [string]$Destination,
        [string]$ItemName,
        [System.Collections.Generic.List[string]]$CheckedPaths
    )

    if ([string]::IsNullOrWhiteSpace($DestinationPath)) {
        return ""
    }

    $fileName = [IO.Path]::GetFileName($DestinationPath)
    if ([string]::IsNullOrWhiteSpace($fileName)) {
        return ""
    }

    $expectedExtension = [IO.Path]::GetExtension($DestinationPath)
    $matchTokens = @(
        Get-NormalizedNameTokens -Text ([IO.Path]::GetFileNameWithoutExtension($DestinationPath))
        Get-NormalizedNameTokens -Text $ItemName
    ) | Select-Object -Unique

    $relative = Normalize-RelativePath -Path $Destination
    $firstSegment = ($relative -split '\\' | Where-Object { $_ } | Select-Object -First 1)
    $knownRoots = @()
    if ($firstSegment) {
        $knownRoots += (Join-Path $TargetRoot $firstSegment)
    }

    foreach ($rootName in @("ISO", "Tools", "Drivers", "MediCat.USB")) {
        $knownRoots += (Join-Path $TargetRoot $rootName)
    }

    foreach ($searchRoot in ($knownRoots | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $searchRoot -PathType Container)) {
            if ($CheckedPaths) {
                [void]$CheckedPaths.Add((Join-Path $searchRoot $fileName))
            }
            continue
        }

        if ($CheckedPaths) {
            [void]$CheckedPaths.Add((Join-Path $searchRoot $fileName))
            [void]$CheckedPaths.Add($searchRoot)
        }

        $match = Get-ChildItem -LiteralPath $searchRoot -Filter $fileName -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($null -ne $match) {
            return $match.FullName
        }

        $normalizedMatch = Get-ChildItem -LiteralPath $searchRoot -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { Test-NormalizedFileNameMatch -Candidate $_ -Tokens $matchTokens -ExpectedExtension $expectedExtension } |
            Select-Object -First 1
        if ($null -ne $normalizedMatch) {
            return $normalizedMatch.FullName
        }
    }

    return ""
}

function Get-ToolkitItemStatus {
    param(
        [Parameter(Mandatory)]$Item,
        [Parameter(Mandatory)][string]$ResolvedTargetRoot
    )

    $destination = Normalize-RelativePath -Path ([string]$Item.dest)
    $type = [string]$Item.type
    $name = [string]$Item.name
    $expectedHash = [string]$Item.sha256
    $url = [string]$Item.url
    $classificationInfo = Get-ItemClassification -Item $Item
    $classification = [string]$classificationInfo.Name
    $classificationReason = [string]$classificationInfo.Reason
    $requirement = Get-RequirementLevel -Item $Item -Classification $classification
    $destinationPath = Resolve-ExpectedItemPath -TargetRoot $ResolvedTargetRoot -Destination $destination
    $fallbackPath = Resolve-FallbackShortcutPath -DestinationPath $destinationPath -Destination $destination
    $resolvedPath = $destinationPath
    $checkedPaths = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($destinationPath)) {
        [void]$checkedPaths.Add($destinationPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($fallbackPath)) {
        [void]$checkedPaths.Add($fallbackPath)
    }

    $status = "UNKNOWN"
    $verification = "No verification data available."
    $recommendation = "Review this manifest item manually."
    $actualHash = ""

    if ([string]::IsNullOrWhiteSpace($destination)) {
        $status = "UNKNOWN"
        $recommendation = "Manifest item has no destination path."
    }
    elseif ($classification -eq "manualDownload") {
        if (Test-Path -LiteralPath $destinationPath) {
            $status = "PLACEHOLDER"
            $verification = "Shortcut present."
            $recommendation = "Open the shortcut and complete the vendor-controlled download manually."
        }
        elseif (-not [string]::IsNullOrWhiteSpace($fallbackPath) -and (Test-Path -LiteralPath $fallbackPath -PathType Leaf)) {
            $status = "PLACEHOLDER"
            $verification = "Fallback shortcut present."
            $recommendation = "Open the fallback shortcut and complete the vendor-controlled download manually."
            $resolvedPath = $fallbackPath
        }
        else {
            $status = "MANUAL_REQUIRED"
            $verification = "Manual shortcut not found."
            $recommendation = "Run Setup USB Toolkit to restore the vendor download shortcut, then complete the manual download if needed."
        }
    }
    elseif ($classification -eq "optional") {
        if (Test-Path -LiteralPath $destinationPath -PathType Leaf) {
            $status = "INSTALLED"
            $verification = "Optional item present."
            $recommendation = "No action needed."
        }
        else {
            $status = "SKIPPED"
            $verification = "Optional item is not present."
            $recommendation = "Optional item can be added later if this workflow needs it."
        }
    }
    else {
        if (-not (Test-Path -LiteralPath $destinationPath -PathType Leaf)) {
            $alternatePath = Find-AlternateItemPath -TargetRoot $ResolvedTargetRoot -DestinationPath $destinationPath -Destination $destination -ItemName $name -CheckedPaths $checkedPaths
            if (-not [string]::IsNullOrWhiteSpace($alternatePath)) {
                $resolvedPath = $alternatePath
            }
        }

        if (Test-Path -LiteralPath $resolvedPath -PathType Leaf) {
            if (-not [string]::IsNullOrWhiteSpace($expectedHash)) {
                $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $resolvedPath).Hash.ToLowerInvariant()
                if ([string]::Equals($actualHash, $expectedHash.ToLowerInvariant(), [System.StringComparison]::OrdinalIgnoreCase)) {
                    $status = "INSTALLED"
                    $verification = "SHA256 verified."
                    $recommendation = "No action needed."
                }
                else {
                    $status = "HASH_FAILED"
                    $verification = "SHA256 mismatch."
                    $recommendation = "Run Update Toolkit to replace this managed item from the manifest source."
                }
            }
            else {
                $status = "INSTALLED"
                $verification = "File present; no pinned SHA256 in manifest."
                $recommendation = "Keep this item under manual review because no pinned hash is available."
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($fallbackPath) -and (Test-Path -LiteralPath $fallbackPath -PathType Leaf)) {
            $status = "MANUAL_REQUIRED"
            $verification = "Managed file missing; fallback shortcut present."
            $recommendation = "Run Update Toolkit, or use the fallback shortcut if the source is currently gated or unavailable."
            $resolvedPath = $fallbackPath
        }
        else {
            $status = "MISSING_REQUIRED"
            $verification = "Required managed file not found."
            $recommendation = "Run Update Toolkit to download or restore this required managed item."
        }
    }

    return [PSCustomObject][ordered]@{
        tool = $name
        category = Get-Category -Destination $destination
        status = $status
        type = $classification
        requirement = $requirement
        version = Get-ToolVersion -Name $name
        expectedPath = $destination
        verification = $verification
        recommendation = $recommendation
        destination = $destination
        path = $resolvedPath
        checkedExactPath = $destinationPath
        checkedFallbackPaths = @($checkedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        matchedPath = if ((-not [string]::IsNullOrWhiteSpace($resolvedPath)) -and (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) { $resolvedPath } else { "" }
        classificationReason = $classificationReason
        sha256Expected = $expectedHash
        sha256Actual = $actualHash
        sourceType = [string]$Item.sourceType
        url = $url
    }
}

$resolvedTargetRoot = [IO.Path]::GetFullPath($TargetRoot)
if (-not (Test-Path -LiteralPath $resolvedTargetRoot -PathType Container)) {
    throw "Target root was not found: $resolvedTargetRoot"
}

$manifestPath = Resolve-ManifestPath -ExplicitPath $ManifestPath
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Manifest was not found: $manifestPath"
}

Write-ToolkitLog ("Toolkit health scan started for {0}" -f $resolvedTargetRoot)
Write-ToolkitLog ("Manifest: {0}" -f $manifestPath)

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$items = @($manifest.items | Where-Object { $null -eq $_.enabled -or $_.enabled -eq $true })
$toolReports = @()

$index = 0
foreach ($item in $items) {
    $index++
    if (($index % 10) -eq 0) {
        Write-ToolkitLog ("Scanned {0}/{1} toolkit items..." -f $index, $items.Count)
    }

    $toolReports += Get-ToolkitItemStatus -Item $item -ResolvedTargetRoot $resolvedTargetRoot
}

$requiredManagedMissing = @($toolReports | Where-Object { $_.requirement -eq "required" -and $_.status -eq "MISSING_REQUIRED" })
$manualActionItems = @($toolReports | Where-Object { $_.status -in @("MANUAL_REQUIRED", "PLACEHOLDER") })
$hashFailureItems = @($toolReports | Where-Object { $_.status -eq "HASH_FAILED" })
$summary = [ordered]@{
    installed = @($toolReports | Where-Object { $_.status -eq "INSTALLED" }).Count
    missing = $requiredManagedMissing.Count
    missingRequired = $requiredManagedMissing.Count
    updates = @($toolReports | Where-Object { $_.status -eq "UPDATE_AVAILABLE" }).Count
    failed = $hashFailureItems.Count
    manual = @($toolReports | Where-Object { $_.status -eq "MANUAL_REQUIRED" }).Count
    placeholder = @($toolReports | Where-Object { $_.status -eq "PLACEHOLDER" }).Count
    skipped = @($toolReports | Where-Object { $_.status -eq "SKIPPED" }).Count
    unknown = @($toolReports | Where-Object { $_.status -eq "UNKNOWN" }).Count
    total = $toolReports.Count
    requiredManagedTotal = @($toolReports | Where-Object { $_.requirement -eq "required" }).Count
}

$healthVerdict = if ($summary.failed -gt 0 -or $summary.missing -gt 0) {
    "PARTIAL"
}
elseif (($summary.manual + $summary.placeholder) -gt 0) {
    "MANUAL ACTION NEEDED"
}
else {
    "READY"
}

$manualExplanation = "Manual items are download pages, licensed/gated tools, or informational shortcuts that ForgerEMS intentionally does not auto-download. They do not count as required managed-tool failures."

$localReportRoot = Get-LocalReportRoot
New-Item -ItemType Directory -Path $localReportRoot -Force | Out-Null
$localJsonPath = Join-Path $localReportRoot "toolkit-health-latest.json"
$localMarkdownPath = Join-Path $localReportRoot "toolkit-health-latest.md"

$targetReportsWritten = $false
$targetJsonPath = ""
$targetMarkdownPath = ""
if (-not (Test-IsCRoot -Path $resolvedTargetRoot)) {
    $targetReportRoot = Join-Path $resolvedTargetRoot "_reports"
    New-Item -ItemType Directory -Path $targetReportRoot -Force | Out-Null
    $targetJsonPath = Join-Path $targetReportRoot "toolkit-health.json"
    $targetMarkdownPath = Join-Path $targetReportRoot "toolkit-health.md"
    $targetReportsWritten = $true
}
else {
    Write-ToolkitLog "Target report copy skipped because ForgerEMS never writes reports to C:\." "WARN"
}

$report = [ordered]@{
    schemaVersion = 1
    product = "ForgerEMS"
    releaseIdentifier = "ForgerEMS v1.1.1 - Flip Intelligence Update"
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
    targetRoot = $resolvedTargetRoot
    manifestPath = $manifestPath
    manifestCoreVersion = [string]$manifest.coreVersion
    healthVerdict = $healthVerdict
    manualItemsExplanation = $manualExplanation
    summary = $summary
    requiredManagedMissing = @($requiredManagedMissing | Select-Object tool, category, expectedPath, checkedExactPath, checkedFallbackPaths, matchedPath, classificationReason, verification, recommendation)
    manualActionList = @($manualActionItems | Select-Object tool, category, status, expectedPath, recommendation)
    hashFailures = @($hashFailureItems | Select-Object tool, category, expectedPath, sha256Expected, sha256Actual, recommendation)
    items = $toolReports
    reportPaths = [ordered]@{
        localJson = $localJsonPath
        localMarkdown = $localMarkdownPath
        targetJson = $targetJsonPath
        targetMarkdown = $targetMarkdownPath
        targetReportsWritten = $targetReportsWritten
    }
}

$json = $report | ConvertTo-Json -Depth 10
$json | Set-Content -LiteralPath $localJsonPath -Encoding UTF8
if ($targetReportsWritten) {
    $json | Set-Content -LiteralPath $targetJsonPath -Encoding UTF8
}

$markdown = New-Object System.Collections.Generic.List[string]
[void]$markdown.Add("# ForgerEMS Toolkit Health")
[void]$markdown.Add("")
[void]$markdown.Add(("Generated UTC: {0}" -f $report.generatedUtc))
[void]$markdown.Add(("Target root: {0}" -f $resolvedTargetRoot))
[void]$markdown.Add(("Manifest: {0}" -f $manifestPath))
[void]$markdown.Add(("Health verdict: **{0}**" -f $healthVerdict))
[void]$markdown.Add("")
[void]$markdown.Add($manualExplanation)
[void]$markdown.Add("")
[void]$markdown.Add("## Summary")
[void]$markdown.Add(("- Installed: {0}" -f $summary.installed))
[void]$markdown.Add(("- Missing required managed tools: {0}" -f $summary.missing))
[void]$markdown.Add(("- Updates: {0}" -f $summary.updates))
[void]$markdown.Add(("- Failed: {0}" -f $summary.failed))
[void]$markdown.Add(("- Manual: {0}" -f $summary.manual))
[void]$markdown.Add(("- Placeholder: {0}" -f $summary.placeholder))
[void]$markdown.Add(("- Skipped: {0}" -f $summary.skipped))
[void]$markdown.Add(("- Unknown: {0}" -f $summary.unknown))
[void]$markdown.Add("")
[void]$markdown.Add("## Required Managed Missing")
if ($requiredManagedMissing.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $requiredManagedMissing) {
        [void]$markdown.Add(("- {0} ({1}) - {2}" -f $item.tool, $item.category, $item.expectedPath))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Manual Action Items")
if ($manualActionItems.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $manualActionItems) {
        [void]$markdown.Add(("- {0} ({1}) - {2}" -f $item.tool, $item.status, $item.recommendation))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Hash Failures")
if ($hashFailureItems.Count -eq 0) {
    [void]$markdown.Add("- None.")
}
else {
    foreach ($item in $hashFailureItems) {
        [void]$markdown.Add(("- {0} - expected {1}, actual {2}" -f $item.tool, $item.sha256Expected, $item.sha256Actual))
    }
}
[void]$markdown.Add("")
[void]$markdown.Add("## Items")
[void]$markdown.Add("| Tool | Category | Status | Type | Expected/Found path | Verification | Recommendation |")
[void]$markdown.Add("| --- | --- | --- | --- | --- | --- | --- |")
foreach ($item in $toolReports) {
    $tool = ([string]$item.tool).Replace("|", "/")
    $category = ([string]$item.category).Replace("|", "/")
    $type = ([string]$item.type).Replace("|", "/")
    $foundPath = if ([string]::IsNullOrWhiteSpace([string]$item.matchedPath)) { "" } else { "Found: {0}" -f $item.matchedPath }
    $expectedPath = (([string]$item.expectedPath) + $(if ($foundPath) { " / $foundPath" } else { "" })).Replace("|", "/")
    $verification = ([string]$item.verification).Replace("|", "/")
    $recommendation = ([string]$item.recommendation).Replace("|", "/")
    [void]$markdown.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} |" -f $tool, $category, $item.status, $type, $expectedPath, $verification, $recommendation))
}

$markdown | Set-Content -LiteralPath $localMarkdownPath -Encoding UTF8
if ($targetReportsWritten) {
    $markdown | Set-Content -LiteralPath $targetMarkdownPath -Encoding UTF8
}

Write-ToolkitLog ("Toolkit health scan complete. Verdict={0}; Installed={1}, MissingRequired={2}, Updates={3}, Failed={4}, Manual={5}, Placeholder={6}, Skipped={7}" -f $healthVerdict, $summary.installed, $summary.missing, $summary.updates, $summary.failed, $summary.manual, $summary.placeholder, $summary.skipped) "OK"
Write-ToolkitLog ("Local JSON report: {0}" -f $localJsonPath) "OK"
Write-ToolkitLog ("Local Markdown report: {0}" -f $localMarkdownPath) "OK"
if ($targetReportsWritten) {
    Write-ToolkitLog ("Target JSON report: {0}" -f $targetJsonPath) "OK"
    Write-ToolkitLog ("Target Markdown report: {0}" -f $targetMarkdownPath) "OK"
}
