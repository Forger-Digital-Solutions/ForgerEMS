#requires -Version 5.1
<#
.SYNOPSIS
  Safely imports host-neutral Kyra.Core from the standalone Kyra repo into ForgerEMS.

.DESCRIPTION
  Copies only Kyra_Assistant/src/Kyra.Core into this repository's src/Kyra.Core.
  The script refuses ambiguous repo layouts, standalone app project imports, generated
  outputs, common artifacts, secrets, and ForgerEMS-specific strings in Kyra.Core.

.PARAMETER KyraRepoPath
  Path to the standalone Kyra_Assistant repository containing Kyra.slnx and src/Kyra.Core.

.PARAMETER Prune
  Remove destination Kyra.Core files that are absent from the source. Use with -WhatIf first.

.PARAMETER RunValidation
  Run restore, build, and Kyra.Core architecture guard tests after a real import.
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory)]
    [string]$KyraRepoPath,

    [switch]$Prune,
    [switch]$RunValidation
)

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ForbiddenTerms = @(
    "ForgerEMS",
    "ForgerDigitalSolutions",
    "Forger Digital Solutions",
    "USB Builder",
    "Toolkit Manager",
    "FlipValue",
    "FORGEREMS_",
    "HKLM\Software\ForgerEMS",
    "%LOCALAPPDATA%\ForgerEMS"
)

$SecretPatterns = @(
    "sk-[A-Za-z0-9_-]{20,}",
    "gh[pousr]_[A-Za-z0-9_]{20,}",
    "xox[baprs]-[A-Za-z0-9-]{20,}",
    "api[_-]?key\s*[:=]\s*['""][^'""]+['""]",
    "secret\s*[:=]\s*['""][^'""]+['""]",
    "token\s*[:=]\s*['""][^'""]+['""]"
)

$ExcludedDirectoryNames = @(
    "bin",
    "obj",
    ".vs",
    "TestResults",
    "release",
    "dist",
    ".claude",
    ".cursor",
    ".git",
    ".idea",
    ".vscode"
)

$ExcludedFilePatterns = @(
    "*.log",
    "*.zip",
    "*.exe",
    "*.msi",
    "*.nupkg",
    "*.snupkg",
    "*.dll",
    "*.pdb",
    "*.cache",
    "*.user",
    "*.suo",
    "*.tmp",
    "*.bak",
    "*.deps.json",
    "*.runtimeconfig.json",
    "*.g.cs",
    "*.g.i.cs",
    "*.AssemblyInfo.cs",
    "*.sourcelink.json"
)

function Resolve-ExistingDirectory {
    param([Parameter(Mandatory)][string]$Path)
    $resolved = Resolve-Path -LiteralPath $Path -ErrorAction Stop
    $item = Get-Item -LiteralPath $resolved.Path
    if (-not $item.PSIsContainer) {
        throw "Path is not a directory: $Path"
    }

    return $item.FullName
}

function Test-RepoFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    return Test-Path -LiteralPath (Join-Path $Root $RelativePath) -PathType Leaf
}

function Test-RepoDirectory {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    return Test-Path -LiteralPath (Join-Path $Root $RelativePath) -PathType Container
}

function Assert-ForgerEmsRepo {
    param([Parameter(Mandatory)][string]$Root)
    if (-not (Test-RepoFile $Root "ForgerEMS.sln") -or -not (Test-RepoDirectory $Root "src\ForgerEMS.Wpf")) {
        throw "Destination does not look like ForgerEMS. Expected ForgerEMS.sln and src\ForgerEMS.Wpf."
    }

    if (Test-RepoFile $Root "Kyra.slnx") {
        throw "Destination also contains Kyra.slnx; refusing ambiguous repo layout."
    }

    if (Test-RepoDirectory $Root "src\Kyra.App.Wpf") {
        throw "Destination already contains src\Kyra.App.Wpf; standalone Kyra app code must not exist in ForgerEMS."
    }
}

function Assert-KyraRepo {
    param([Parameter(Mandatory)][string]$Root)
    if (-not (Test-RepoFile $Root "Kyra.slnx") -or -not (Test-RepoDirectory $Root "src\Kyra.Core")) {
        throw "Source does not look like Kyra_Assistant. Expected Kyra.slnx and src\Kyra.Core."
    }

    if (Test-RepoFile $Root "ForgerEMS.sln") {
        throw "Source also contains ForgerEMS.sln; refusing ambiguous repo layout."
    }
}

function Get-RelativePath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Path
    )

    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    $uriRoot = [System.Uri]::new($rootFull + [System.IO.Path]::DirectorySeparatorChar)
    $uriPath = [System.Uri]::new($pathFull)
    return [System.Uri]::UnescapeDataString($uriRoot.MakeRelativeUri($uriPath).ToString()).Replace('/', '\')
}

function Test-IsExcludedDirectory {
    param([Parameter(Mandatory)][string]$Path)
    $parts = $Path -split '[\\/]'
    foreach ($part in $parts) {
        if ($ExcludedDirectoryNames -contains $part) {
            return $true
        }
    }

    return $false
}

function Test-IsExcludedFile {
    param([Parameter(Mandatory)][string]$Name)
    foreach ($pattern in $ExcludedFilePatterns) {
        if ($Name -like $pattern) {
            return $true
        }
    }

    return $false
}

function Get-KyraCoreFiles {
    param([Parameter(Mandatory)][string]$CoreRoot)
    Get-ChildItem -LiteralPath $CoreRoot -File -Recurse |
        Where-Object {
            $relative = Get-RelativePath -Root $CoreRoot -Path $_.FullName
            -not (Test-IsExcludedDirectory $relative) -and
            -not (Test-IsExcludedFile $_.Name) -and
            $relative -notmatch '(^|\\)Kyra\.App\.Wpf(\\|$)'
        }
}

function Assert-SafeKyraCoreText {
    param(
        [Parameter(Mandatory)][string]$CoreRoot,
        [Parameter(Mandatory)][System.IO.FileInfo[]]$Files
    )

    foreach ($file in $Files) {
        $relative = Get-RelativePath -Root $CoreRoot -Path $file.FullName
        if ($relative -match '(^|\\)Kyra\.App\.Wpf(\\|$)') {
            throw "Refusing standalone app path in Kyra.Core import: $relative"
        }

        $text = Get-Content -LiteralPath $file.FullName -Raw
        foreach ($term in $ForbiddenTerms) {
            if ($text.IndexOf($term, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "Refusing Kyra.Core import. Forbidden host-specific term '$term' found in $relative."
            }
        }

        foreach ($pattern in $SecretPatterns) {
            if ($text -match $pattern) {
                throw "Refusing Kyra.Core import. Secret-like value detected in $relative."
            }
        }
    }
}

function Get-FileHashValue {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ""
    }

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$kyraRoot = Resolve-ExistingDirectory -Path $KyraRepoPath
Assert-ForgerEmsRepo -Root $RepoRoot
Assert-KyraRepo -Root $kyraRoot

$sourceCoreRoot = Join-Path $kyraRoot "src\Kyra.Core"
$destCoreRoot = Join-Path $RepoRoot "src\Kyra.Core"

if (-not (Test-Path -LiteralPath $destCoreRoot -PathType Container)) {
    throw "Destination Kyra.Core directory is missing: src\Kyra.Core"
}

$sourceFiles = @(Get-KyraCoreFiles -CoreRoot $sourceCoreRoot)
if ($sourceFiles.Count -eq 0) {
    throw "No importable Kyra.Core files found in source."
}

Assert-SafeKyraCoreText -CoreRoot $sourceCoreRoot -Files $sourceFiles

$sourceRelative = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$copyPlan = [System.Collections.Generic.List[object]]::new()

foreach ($sourceFile in $sourceFiles) {
    $relative = Get-RelativePath -Root $sourceCoreRoot -Path $sourceFile.FullName
    [void]$sourceRelative.Add($relative)
    $destFile = Join-Path $destCoreRoot $relative
    $action = if (-not (Test-Path -LiteralPath $destFile -PathType Leaf)) {
        "Add"
    }
    elseif ((Get-FileHashValue $sourceFile.FullName) -ne (Get-FileHashValue $destFile)) {
        "Update"
    }
    else {
        "Unchanged"
    }

    $copyPlan.Add([pscustomobject]@{
        Action = $action
        Path = "src\Kyra.Core\$relative"
        Source = $sourceFile.FullName
        Destination = $destFile
    })
}

$deletePlan = [System.Collections.Generic.List[object]]::new()
$destFiles = @(Get-KyraCoreFiles -CoreRoot $destCoreRoot)
foreach ($destFile in $destFiles) {
    $relative = Get-RelativePath -Root $destCoreRoot -Path $destFile.FullName
    if (-not $sourceRelative.Contains($relative)) {
        $deletePlan.Add([pscustomobject]@{
            Action = if ($Prune) { "Delete" } else { "WouldDeleteWithPrune" }
            Path = "src\Kyra.Core\$relative"
            Destination = $destFile.FullName
        })
    }
}

$adds = @($copyPlan | Where-Object Action -eq "Add")
$updates = @($copyPlan | Where-Object Action -eq "Update")
$unchanged = @($copyPlan | Where-Object Action -eq "Unchanged")

Write-Host "[Kyra.Core Import] Source: Kyra_Assistant/src/Kyra.Core"
Write-Host "[Kyra.Core Import] Destination: ForgerEMS/src/Kyra.Core"
Write-Host "[Kyra.Core Import] Add: $($adds.Count), Update: $($updates.Count), Unchanged: $($unchanged.Count), Delete candidates: $($deletePlan.Count)"

foreach ($entry in @($adds + $updates + $deletePlan)) {
    Write-Host ("[{0}] {1}" -f $entry.Action, $entry.Path)
}

foreach ($entry in @($adds + $updates)) {
    if ($PSCmdlet.ShouldProcess($entry.Path, $entry.Action)) {
        $parent = Split-Path -Parent $entry.Destination
        if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
            New-Item -ItemType Directory -Path $parent -Force | Out-Null
        }

        Copy-Item -LiteralPath $entry.Source -Destination $entry.Destination -Force
    }
}

if ($Prune) {
    foreach ($entry in $deletePlan) {
        if ($PSCmdlet.ShouldProcess($entry.Path, "Delete")) {
            Remove-Item -LiteralPath $entry.Destination -Force
        }
    }
}
elseif ($deletePlan.Count -gt 0) {
    Write-Host "[Kyra.Core Import] Delete candidates were not removed. Re-run with -Prune after reviewing -WhatIf output."
}

if ($RunValidation -and -not $WhatIfPreference) {
    Push-Location $RepoRoot
    try {
        dotnet restore ForgerEMS.sln
        dotnet build src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj --no-restore
        dotnet test tests\ForgerEMS.Wpf.Tests\ForgerEMS.Wpf.Tests.csproj --no-build --filter KyraCoreArchitectureGuardTests
    }
    finally {
        Pop-Location
    }
}
