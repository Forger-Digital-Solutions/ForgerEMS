#Requires -Version 5.1
<#
.SYNOPSIS
  Validates ForgerEMS package-mode restore against a Kyra SDK NuGet feed (CI artifact or local feed).

.PARAMETER KyraSdkFeedPath
  Absolute or relative path to feed/ or release/sdk-current/ (contains .nupkg files).

.PARAMETER Configuration
  Build configuration for dotnet commands.

.NOTES
  Feed resolution order:
  1. -KyraSdkFeedPath parameter
  2. KYRA_SDK_FEED_PATH environment variable
  3. Dev sibling: ../../Kyra_Assistant/repo/release/sdk-current/feed (if present)

  Writes nuget.config.ci (gitignored) for restore; does not modify tracked nuget.config.
  Restores/builds ForgerEMS.Kyra.Sdk.sln (not default ForgerEMS.sln).
#>
param(
    [string] $KyraSdkFeedPath = '',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$SolutionPath = Join-Path $RepoRoot 'ForgerEMS.Kyra.Sdk.sln'
$NuGetConfigCi = Join-Path $RepoRoot 'nuget.config.ci'
$KyraSdkMsbuildProperties = 'UseKyraSdkProjectReference=false;IncludeKyraSdkDogfoodTool=true'

function Resolve-KyraSdkFeedDirectory {
    param([string] $Candidate)

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return $null
    }

    $resolved = Resolve-Path -LiteralPath $Candidate -ErrorAction Stop
    $feedSub = Join-Path $resolved.Path 'feed'
    if (Test-Path $feedSub) {
        return (Resolve-Path -LiteralPath $feedSub).Path
    }

    $hasNupkg = Get-ChildItem -LiteralPath $resolved.Path -Filter '*.nupkg' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $hasNupkg) {
        return $resolved.Path
    }

    throw "Kyra SDK feed path does not contain .nupkg files: $($resolved.Path)"
}

function Get-DefaultDevFeedPath {
    $devFeed = Join-Path $RepoRoot '..\..\Kyra_Assistant\repo\release\sdk-current\feed'
    if (Test-Path $devFeed) {
        return (Resolve-Path -LiteralPath $devFeed).Path
    }
    return $null
}

function Write-NuGetConfigCi {
    param([string] $FeedDirectory)

    $feedValue = (Resolve-Path -LiteralPath $FeedDirectory).Path.Replace('\', '/')

    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="kyra-sdk-local" value="$feedValue" />
  </packageSources>
</configuration>
"@
    Set-Content -LiteralPath $NuGetConfigCi -Value $xml -Encoding utf8
    Write-Host "Wrote $NuGetConfigCi"
    Write-Host "  kyra-sdk-local -> $feedValue"
}

$feedDirectory = $null
if (-not [string]::IsNullOrWhiteSpace($KyraSdkFeedPath)) {
    $feedDirectory = Resolve-KyraSdkFeedDirectory -Candidate $KyraSdkFeedPath
}
elseif (-not [string]::IsNullOrWhiteSpace($env:KYRA_SDK_FEED_PATH)) {
    $feedDirectory = Resolve-KyraSdkFeedDirectory -Candidate $env:KYRA_SDK_FEED_PATH
}
else {
    $feedDirectory = Get-DefaultDevFeedPath
}

if ([string]::IsNullOrWhiteSpace($feedDirectory)) {
    throw @"
Kyra SDK feed not found. Provide one of:
  -KyraSdkFeedPath <path-to-feed-or-sdk-current>
  KYRA_SDK_FEED_PATH environment variable
  Dev sibling: Kyra_Assistant/repo/release/sdk-current/feed (after tools/Kyra-Sdk-Release.ps1)
"@
}

Write-Host "Using Kyra SDK feed: $feedDirectory"

$required = @(
    'Kyra.Contracts.*.nupkg'
    'Kyra.Workers.Core.*.nupkg'
    'Kyra.Local.Core.*.nupkg'
    'Kyra.Combined.Core.*.nupkg'
    'Kyra.Sdk.*.nupkg'
)
foreach ($pattern in $required) {
    if (-not (Get-ChildItem -LiteralPath $feedDirectory -Filter $pattern | Select-Object -First 1)) {
        throw "Missing feed package matching $pattern under $feedDirectory"
    }
}

Write-NuGetConfigCi -FeedDirectory $feedDirectory

$restoreArgs = @(
    'restore', $SolutionPath,
    '--configfile', $NuGetConfigCi,
    "-p:$KyraSdkMsbuildProperties"
)
Write-Host "==> dotnet restore ForgerEMS.Kyra.Sdk.sln (package mode)"
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw 'Package-mode restore failed.' }

$buildArgs = @(
    'build', $SolutionPath,
    '-c', $Configuration,
    '--no-restore',
    '--configfile', $NuGetConfigCi,
    "-p:$KyraSdkMsbuildProperties"
)
Write-Host "==> dotnet build ForgerEMS.Kyra.Sdk.sln (package mode)"
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw 'Package-mode build failed.' }

if ($SkipTests) {
    Write-Host 'Skipped tests (-SkipTests).'
    return
}

$hostAdapterTests = Join-Path $RepoRoot 'tests\ForgerEMS.Kyra.HostAdapter.Tests\ForgerEMS.Kyra.HostAdapter.Tests.csproj'
$wpfTests = Join-Path $RepoRoot 'tests\ForgerEMS.Wpf.Tests\ForgerEMS.Wpf.Tests.csproj'

foreach ($testProject in @($hostAdapterTests, $wpfTests)) {
    Write-Host "==> dotnet test $([IO.Path]::GetFileName($testProject)) (package mode)"
    & dotnet test $testProject -c $Configuration --no-build -p:UseKyraSdkProjectReference=false
    if ($LASTEXITCODE -ne 0) { throw "Tests failed: $testProject" }
}

Write-Host ''
Write-Host 'Kyra SDK package-mode validation passed (restore, build, HostAdapter + WPF tests).'
