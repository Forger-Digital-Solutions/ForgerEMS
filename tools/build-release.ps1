<#
.SYNOPSIS
Builds a complete ForgerEMS release staging folder.

.DESCRIPTION
Publishes the .NET 8 WPF app, builds and stages the PowerShell backend bundle,
copies public manifests, optionally compiles the Inno Setup installer, produces a
versioned portable app ZIP for beta distribution, and writes SHA256 checksums under
release\current\.

.PARAMETER Version
Release version. Defaults to the repository VERSION file. A different explicit
value is refused: VERSION is authoritative.

.PARAMETER DryRun
Runs all CI-safe build and validation work, but skips Inno Setup compilation.

.PARAMETER SkipInstaller
Skips installer compilation even outside dry-run mode.

.PARAMETER Configuration
.NET build configuration. Defaults to Release.

.PARAMETER Runtime
.NET runtime identifier. Defaults to win-x64.

.PARAMETER OutputRoot
Isolated build output root (defaults to <repo>\dist). Existing publish/intermediate
folders under it are never touched when they are non-empty — choose a fresh root.

.PARAMETER ReleaseOutputRoot
Isolated release staging root (defaults to <repo>\release\current). Must not already
exist and contain files — the script refuses to erase historical artifacts.

.PARAMETER NuGetPackagesPath
Isolated NuGet packages cache forwarded to dotnet restore --packages.

.PARAMETER RequireSigning
Production packaging gate: the frontend exe and installer must carry a valid
Authenticode signature with the expected publisher. Fails closed when no certificate
is available — there is no silent unsigned production path.

.PARAMETER UnsignedCandidate
Explicitly permits an unsigned local candidate build. release.json is marked
unsigned + non-production with the source HEAD and dirty flag. Never publish these
artifacts as releases.

.PARAMETER CertificateThumbprint / TimestampUrl / ExpectedPublisher
Signing inputs: certificate thumbprint in CurrentUser\My or LocalMachine\My,
RFC3161 timestamp server, and the exact publisher SimpleName required on signed outputs.
#>

#requires -Version 5.1

[CmdletBinding()]
param(
    [string]$Version = "",
    [switch]$DryRun,
    [switch]$SkipInstaller,
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$InnoCompilerPath = "",
    [string]$OutputRoot = "",
    [string]$ReleaseOutputRoot = "",
    [string]$NuGetPackagesPath = "",
    [switch]$RequireSigning,
    [switch]$UnsignedCandidate,
    [string]$CertificateThumbprint = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$ExpectedPublisher = "Forger Digital Solutions"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "ForgerEMS.sln"
$projectPath = Join-Path $repoRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
$backendBuildScript = Join-Path $repoRoot "tools\build-backend-release.ps1"
$stageBackendScript = Join-Path $repoRoot "tools\stage-bundled-backend.ps1"
$installerScript = Join-Path $repoRoot "installer\ForgerEMS.iss"
$manifestRoot = Join-Path $repoRoot "manifests"
$distRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $repoRoot "dist"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}
$runtimeHelperPath = Join-Path $repoRoot "backend\ForgerEMS.Runtime.ps1"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)
    Write-Host "[ForgerEMS] $Message" -ForegroundColor Cyan
}

# ---- signing gate: production packaging defaults to signed; unsigned requires the
# explicit -UnsignedCandidate flag and is marked non-production in release.json ----
if ($RequireSigning -and $UnsignedCandidate) {
    throw "-RequireSigning and -UnsignedCandidate are mutually exclusive."
}
$signed = -not $UnsignedCandidate
$script:SigningApplied = $false
$script:ForgerEMSSigntool = $null
$script:ForgerEMSCertificateStore = $null
$signHelperPath = Join-Path $repoRoot "tools\sign-release-artifact.ps1"

function Assert-ForgerEMSSigningPrerequisites {
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw "Signing required but no -CertificateThumbprint was provided (fail closed)."
    }
    if ($CertificateThumbprint -notmatch '^[0-9A-Fa-f]{40}$') {
        throw "Signing required but -CertificateThumbprint is not a strict 40-hex value (fail closed)."
    }
    if (-not (Test-Path -LiteralPath $signHelperPath)) {
        throw "Signing helper not found: $signHelperPath"
    }

    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'x64\\signtool\.exe$' } |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    if (-not $signtool) {
        throw "Signing required but signtool.exe was not found under Windows Kits (fail closed)."
    }

    # Resolve the certificate store once, preferring CurrentUser then LocalMachine.
    # The signing helper re-validates private key, validity window, and code-signing
    # EKU on every invocation.
    $cert = Get-ChildItem "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
    if ($cert) {
        $script:ForgerEMSCertificateStore = 'CurrentUser'
    }
    else {
        $cert = Get-ChildItem "Cert:\LocalMachine\My\$CertificateThumbprint" -ErrorAction SilentlyContinue
        if ($cert) {
            $script:ForgerEMSCertificateStore = 'LocalMachine'
        }
    }
    if (-not $cert) {
        throw "Signing certificate $CertificateThumbprint was not found in CurrentUser\My or LocalMachine\My (fail closed)."
    }

    return $signtool
}

if ($signed) {
    # Fail closed before the expensive restore/build/publish when signing is
    # required but credentials are unavailable (CI/local unsigned builds must
    # pass -UnsignedCandidate explicitly instead).
    $script:ForgerEMSSigntool = Assert-ForgerEMSSigningPrerequisites
    Write-Step "Signing prerequisites validated: signtool and certificate $CertificateThumbprint ($script:ForgerEMSCertificateStore) are available."
}

function Invoke-ForgerEMSSign {
    param(
        [Parameter(Mandatory)][string]$Path,
        [switch]$VerifyOnly
    )

    $signtool = $script:ForgerEMSSigntool
    if (-not $signtool) {
        $signtool = Assert-ForgerEMSSigningPrerequisites
    }
    if (-not $script:ForgerEMSCertificateStore) {
        throw "Signing certificate store was not resolved (fail closed)."
    }

    $helperArgs = @{
        Path                  = $Path
        CertificateThumbprint = $CertificateThumbprint
        CertificateStore      = $script:ForgerEMSCertificateStore
        SignToolPath          = $signtool
        TimestampUrl          = $TimestampUrl
        ExpectedPublisher     = $ExpectedPublisher
    }
    if ($VerifyOnly) { $helperArgs['VerifyOnly'] = $true }

    & $signHelperPath @helperArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "sign-release-artifact failed for '$Path' with exit code $LASTEXITCODE." }
    $script:SigningApplied = $true
}

function Assert-SigningCallbackValue {
    # Values embedded into the ISCC /S sign-tool callback run through a shell —
    # reject shell metacharacters, unescaped quotes, newlines, and Inno's own
    # $ and % substitution characters.
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Value
    )
    foreach ($ch in @('"', "'", '`', '&', '|', ';', '<', '>', '$', '%', "`r", "`n")) {
        if ($Value.Contains($ch)) {
            throw "Signing callback value '$Name' contains a forbidden character; refusing to build the /S command."
        }
    }
}

function Get-ForgerEMSSignToolCallback {
    # ISCC /S callback: absolute Windows PowerShell invoking the helper with
    # Inno's $f (quoted file name) and $q (quote) sequences per the official
    # SignTool docs. $f already expands to a quoted path — do not wrap it in $q.
    param(
        [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$VerifiedUninstallerPath
    )
    $powershell = Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe"
    foreach ($pair in @(
        @('powershell', $powershell),
        @('helper', $signHelperPath),
        @('signtool', $script:ForgerEMSSigntool),
        @('thumbprint', $CertificateThumbprint),
        @('store', $script:ForgerEMSCertificateStore),
        @('timestampUrl', $TimestampUrl),
        @('publisher', $ExpectedPublisher),
        @('verifiedUninstallerPath', $VerifiedUninstallerPath)
    )) {
        Assert-SigningCallbackValue -Name $pair[0] -Value $pair[1]
    }

    # $f expands to the already-quoted file Inno is asking us to sign; $q emits
    # a double quote for the surrounding arguments.
    return (
        '$q' + $powershell + '$q -NoProfile -ExecutionPolicy Bypass -File ' +
        '$q' + $signHelperPath + '$q ' +
        '-Path $f ' +
        '-CertificateThumbprint ' + $CertificateThumbprint + ' ' +
        '-CertificateStore ' + $script:ForgerEMSCertificateStore + ' ' +
        '-SignToolPath $q' + $script:ForgerEMSSigntool + '$q ' +
        '-TimestampUrl $q' + $TimestampUrl + '$q ' +
        '-ExpectedPublisher $q' + $ExpectedPublisher + '$q ' +
        '-VerifiedUninstallerPath $q' + $VerifiedUninstallerPath + '$q'
    )
}

function Ensure-Dir {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

function Get-RepoVersion {
    param([Parameter(Mandatory)][string]$RepoRoot)

    $versionFile = Join-Path $RepoRoot "VERSION"
    if (-not (Test-Path -LiteralPath $versionFile)) {
        throw "Authoritative version file not found: $versionFile"
    }

    $value = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "VERSION file is empty: $versionFile"
    }

    return $value
}

function Resolve-IsccPath {
    param([string]$ExplicitPath)

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (Test-Path -LiteralPath $ExplicitPath) { return $ExplicitPath }
        throw "Inno Setup compiler was not found at: $ExplicitPath"
    }

    $candidates = @(
        (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }

    throw "ISCC.exe was not found. Install Inno Setup 6 or rerun with -DryRun."
}

function ConvertTo-WindowsVersion {
    param([Parameter(Mandatory)][string]$Value)

    $trim = $Value.Trim()
    # Strip semver prerelease / metadata for Windows four-part version (e.g. 1.1.12-rc.1 -> 1.1.12.0).
    $numericCore = $trim -replace '-[^+]*(?:\+.*)?$', ''
    $match = [regex]::Match($numericCore, '^(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)(?:\.(?<build>\d+))?$')
    if (-not $match.Success) {
        throw "Version '$Value' could not be mapped to a Windows four-part version (numeric core: '$numericCore')."
    }

    $build = if ($match.Groups["build"].Success) { $match.Groups["build"].Value } else { "0" }
    return "{0}.{1}.{2}.{3}" -f $match.Groups["major"].Value, $match.Groups["minor"].Value, $match.Groups["patch"].Value, $build
}

function Copy-CleanDirectory {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    if (-not (Test-Path -LiteralPath $Source)) {
        throw "Required source directory was not found: $Source"
    }

    Ensure-Dir -Path $Destination
    Get-ChildItem -LiteralPath $Destination -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Copy-Item -Path (Join-Path $Source "*") -Destination $Destination -Recurse -Force
}

function Assert-NoDriverArtifacts {
    # ForgerEMS ships no kernel driver. Dr. Forge driver support is contract-first /
    # dev-foundation only, so no *.sys / *.inf / *.cat file may reach a normal package.
    param(
        [Parameter(Mandatory)][string[]]$Roots,
        [Parameter(Mandatory)][string]$Context
    )

    $driverExtensions = @(".sys", ".inf", ".cat")
    $hits = @()
    foreach ($root in $Roots) {
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path -LiteralPath $root)) { continue }
        $hits += @(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $driverExtensions -contains $_.Extension.ToLowerInvariant() })
    }

    if ($hits.Count -gt 0) {
        $list = ($hits | Select-Object -First 10 | ForEach-Object { $_.FullName }) -join "; "
        throw "Driver artifacts (*.sys / *.inf / *.cat) are not allowed in ForgerEMS release packages ($Context): $list"
    }

    Write-Step "Driver-artifact scan clean ($Context): no *.sys / *.inf / *.cat"
}

function Copy-ReleaseDocs {
    param(
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$Version
    )

    $docsDest = Join-Path $Destination "docs"
    Ensure-Dir -Path $docsDest
    $docs = @(
        "ABOUT_FORGEREMS.md",
        "BETA_ISSUE_REPORT_TEMPLATE.md",
        "BETA_TESTER_QUICKSTART.md",
        "DOWNLOAD_TROUBLESHOOTING.md",
        "ENVIRONMENT.md",
        "FAQ.md",
        "FIRST_TESTER_DOWNLOAD_FLOW.md",
        "FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md",
        "FORGER-SENSOR-STACK.md",
        "LEGAL.md",
        "LEGAL_NOTICES.md",
        "LINUX-WINE-COMPATIBILITY.md",
        "PRIVACY_AND_DATA_HANDLING.md",
        "SENSOR-LIMITATIONS.md",
        "TERMS_OF_USE.md",
        "THIRD-PARTY-SENSOR-NOTICES.md",
        "THIRD_PARTY_NOTICES.md",
        "UPDATE_SYSTEM.md",
        "USER_CONSENT_FLOW.md",
        ("RELEASE_NOTES_v{0}.md" -f $Version)
    )

    foreach ($doc in $docs) {
        $source = Join-Path $repoRoot ("docs\{0}" -f $doc)
        if (-not (Test-Path -LiteralPath $source)) {
            throw "Required release doc was not found: $source"
        }

        Copy-Item -LiteralPath $source -Destination (Join-Path $docsDest $doc) -Force
    }

    # docs/marketing is a packaged subfolder — preserve its relative path.
    $marketingDest = Join-Path $docsDest "marketing"
    Ensure-Dir -Path $marketingDest
    $marketingDoc = Join-Path $repoRoot "docs\marketing\PUBLIC-FAQ.md"
    if (-not (Test-Path -LiteralPath $marketingDoc)) {
        throw "Required release doc was not found: $marketingDoc"
    }
    Copy-Item -LiteralPath $marketingDoc -Destination (Join-Path $marketingDest "PUBLIC-FAQ.md") -Force

    # docs/LEGAL.md links ../SECURITY.md — the target must exist at package root.
    foreach ($rootDoc in @("SECURITY.md")) {
        $rootDocSource = Join-Path $repoRoot $rootDoc
        if (-not (Test-Path -LiteralPath $rootDocSource)) {
            throw "Required release doc was not found: $rootDocSource"
        }
        Copy-Item -LiteralPath $rootDocSource -Destination (Join-Path $Destination $rootDoc) -Force
    }
}

function Write-Checksums {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$OutputPath
    )

    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\')
    $outFull = [IO.Path]::GetFullPath($OutputPath)
    $lines = Get-ChildItem -LiteralPath $Root -File -Recurse |
        Where-Object { $_.FullName -ne $outFull } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($rootFull.Length + 1).Replace('\', '/')
            $hash = Get-ForgerSha256 -LiteralPath $_.FullName
            "{0} *{1}" -f $hash, $relative
        }

    Set-Content -LiteralPath $OutputPath -Value $lines -Encoding ASCII
}

function Get-DistributionChecksumText {
    param(
        [Parameter(Mandatory)][string]$InstallerPath,
        [Parameter(Mandatory)][string]$ZipPath,
        [Parameter(Mandatory)][string]$ReleaseJsonPath,
        [Parameter(Mandatory)][string]$DownloadBetaPath,
        [Parameter(Mandatory)][string]$InstallerRelativeName,
        [Parameter(Mandatory)][string]$ZipRelativeName
    )

    $installerHash = Get-ForgerSha256 -LiteralPath $InstallerPath
    $zipHash = Get-ForgerSha256 -LiteralPath $ZipPath
    $jsonHash = Get-ForgerSha256 -LiteralPath $ReleaseJsonPath
    $downloadBetaHash = Get-ForgerSha256 -LiteralPath $DownloadBetaPath
    # Parenthesize each -f expression: comma binds tighter than -f inside @( ), which otherwise splits the array wrong.
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add(("{0} *{1}" -f $installerHash, ($InstallerRelativeName -replace '\\', '/')))
    $lines.Add(("{0} *{1}" -f $zipHash, ($ZipRelativeName -replace '\\', '/')))
    $lines.Add(("{0} *release.json" -f $jsonHash))
    $lines.Add(("{0} *DOWNLOAD_BETA.txt" -f $downloadBetaHash))
    return ($lines -join "`n") + "`n"
}

function Get-PackageLooseFilesChecksumText {
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string]$InstallerInZipName,
        [Parameter(Mandatory)][string]$StartHereName,
        [Parameter(Mandatory)][string]$VerifyName,
        [Parameter(Mandatory)][string]$ReleaseJsonName
    )

    $root = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
    $pairs = @(
        @{ Path = (Join-Path $root $InstallerInZipName); Rel = ($InstallerInZipName -replace '\\', '/') },
        @{ Path = (Join-Path $root $StartHereName); Rel = ($StartHereName -replace '\\', '/') },
        @{ Path = (Join-Path $root $VerifyName); Rel = ($VerifyName -replace '\\', '/') },
        @{ Path = (Join-Path $root $ReleaseJsonName); Rel = ($ReleaseJsonName -replace '\\', '/') }
    )
    $lines = foreach ($p in $pairs) {
        if (-not (Test-Path -LiteralPath $p.Path)) {
            throw "Package file missing for checksums: $($p.Path)"
        }
        $h = Get-ForgerSha256 -LiteralPath $p.Path
        ("{0} *{1}" -f $h, $p.Rel)
    }
    return ($lines -join "`n") + "`n"
}

function Write-DownloadBetaTxt {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version
    )
    $zipPrimary = "ForgerEMS-v{0}.zip" -f $Version
    $content = @"
================================================================================
  DOWNLOAD THE PORTABLE ZIP FIRST  (read this first)
================================================================================

On the GitHub Release -> Assets list, download:
  $zipPrimary

The ZIP is the portable ForgerEMS app package. It contains ForgerEMS.exe, bundled
runtime/backend content, docs, START_HERE.bat, VERIFY.txt, release metadata, and
checksums. The installer is still published separately for users who prefer an
installed app and are comfortable running a standalone .exe.

INCOMPLETE DOWNLOADS
- If the filename ends in .crdownload (Chrome) or looks like a partial/temp name, the download is NOT finished.
- Do NOT rename a .crdownload to .zip and do NOT run it.
- Wait until the file name ends in .zip, or cancel and retry on a stable connection.

AFTER YOU HAVE A REAL .ZIP
1. Extract the ZIP (Right-click -> Extract All). Do not run from inside the zip viewer.
2. Open the extracted folder: ForgerEMS-v$Version
3. Double-click START_HERE.bat, or run ForgerEMS.exe directly.
4. On first launch, read and accept the ForgerEMS Terms of Use before using the main tools.
5. If Windows SmartScreen appears, only choose More info -> Run anyway if this ZIP came from the official GitHub release and you verified hashes.

DEEP SENSOR MODE
- The installer or Settings may enable ForgerEMS Deep Sensor Mode.
- No separate LibreHardwareMonitor download is needed; approved local providers ship with the app where packaged.
- Deep Sensor Mode uses local read-only hardware sensors for Hardware X-Ray coverage.
- It does not control fans, voltage, clocks, BIOS, firmware, overclocking, or undervolting.
- To test manually in PowerShell: `$env:FORGEREMS_DEEP_SENSOR_MODE='ReadOnly'
- To turn off the testing override: remove FORGEREMS_DEEP_SENSOR_MODE or set it to Off.

SUPPORT PRIVACY
- Send logs/screenshots/support bundles only if comfortable.
- Review exported files before sending them.
- Do not send product keys, API keys, tokens, passwords, private documents, or sensitive files.
- For operator verification run tools/show-forgerems-env-status.ps1 and tools/audit-config-and-secrets.ps1.

VERIFY INTEGRITY
Use CHECKSUMS.sha256 from the same release page. Full steps: GitHub repo -> docs/DOWNLOAD_TROUBLESHOOTING.md
"@
    Set-Content -LiteralPath $Path -Value $content -Encoding utf8
}

function Write-StartHereBat {
    param([Parameter(Mandatory)][string]$Path)
    $content = @"
@echo off
title ForgerEMS Portable
echo.
echo Starting ForgerEMS Portable...
echo.
echo If Windows shows SmartScreen, choose More info -^> Run anyway ONLY if this
echo folder came from the official ForgerEMS GitHub release and you verified CHECKSUMS.sha256.
echo.
echo First launch requires accepting the ForgerEMS Terms of Use before the main tools unlock.
echo Review docs\TERMS_OF_USE.md, docs\PRIVACY_AND_DATA_HANDLING.md, and docs\LEGAL_NOTICES.md.
echo.
echo Deep Sensor Mode uses bundled local read-only hardware sensors when enabled.
echo No separate LibreHardwareMonitor download is needed.
echo.
start "" "%~dp0ForgerEMS.exe"
"@
    Set-Content -LiteralPath $Path -Value $content -Encoding ascii
}

function Normalize-ChecksumText {
    param([Parameter(Mandatory)][string]$Text)
    $t = $Text -replace "`r`n", "`n"
    $t = $t.TrimEnd("`n") + "`n"
    return $t
}

function Set-ChecksumFileLf {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Text
    )
    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($Path, (Normalize-ChecksumText -Text $Text), $utf8)
}

function Compress-PackageFolderZip {
    param(
        [Parameter(Mandatory)][string]$PackageRoot,
        [Parameter(Mandatory)][string]$EntryFolderName,
        [Parameter(Mandatory)][string]$DestinationZipPath
    )

    Add-Type -AssemblyName System.IO.Compression
    if (-not ("System.IO.Compression.ZipFile" -as [type])) {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
    }

    $packageFull = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\')
    $destFull = [IO.Path]::GetFullPath($DestinationZipPath)
    if (Test-Path -LiteralPath $destFull) {
        Remove-Item -LiteralPath $destFull -Force
    }
    $fixedTime = [DateTimeOffset]::Parse("2000-01-01T00:00:00Z", [System.Globalization.CultureInfo]::InvariantCulture)
    $stream = [IO.File]::Open($destFull, [IO.FileMode]::CreateNew)
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            Get-ChildItem -LiteralPath $packageFull -File -Recurse |
                Sort-Object FullName |
                ForEach-Object {
                    $relative = $_.FullName.Substring($packageFull.Length + 1).Replace('\', '/')
                    $entryName = ($EntryFolderName.TrimEnd('/') + "/" + $relative)
                    $entry = $zip.CreateEntry($entryName, [IO.Compression.CompressionLevel]::Optimal)
                    $entry.LastWriteTime = $fixedTime
                    $writer = $entry.Open()
                    try {
                        $fs = [IO.File]::OpenRead($_.FullName)
                        try {
                            $fs.CopyTo($writer)
                        } finally {
                            $fs.Dispose()
                        }
                    } finally {
                        $writer.Dispose()
                    }
                }
        } finally {
            $zip.Dispose()
        }
    } finally {
        $stream.Dispose()
    }
}

function Write-VerifyTxt {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Version
    )
    $content = @"
ForgerEMS Official Portable Preview Package
Version: $Version

Official source only:
https://github.com/Forger-Digital-Solutions/ForgerEMS/releases

You should have extracted a .zip from that release page. If you only have a single
.exe from email, Discord, or another site, STOP — it is not this verified package.

DO NOT RUN
- Files ending in .crdownload (Chrome incomplete download) — wait or re-download.
- Partial / tmp download names — do not rename them to .exe or .zip.
- Random installers from chat or unofficial mirrors.

Expected files inside this folder:
- ForgerEMS.exe
- backend\
- manifests\
- docs\
- START_HERE.bat
- VERIFY.txt
- CHECKSUMS.sha256 (hashes for the files inside this folder)
- release.json

How to run:
1. You must extract the ZIP first (do not run from the zip preview alone).
2. Double-click START_HERE.bat.
3. On first launch, accept the ForgerEMS Terms of Use before using the main tools.
4. If Windows SmartScreen appears, use More info -> Run anyway only for this official release.

Verify this folder (PowerShell, run inside the extracted folder):
  Get-FileHash ".\ForgerEMS.exe" -Algorithm SHA256
Compare the Hash line to CHECKSUMS.sha256 in this folder for ForgerEMS.exe.

The GitHub release page also publishes a root CHECKSUMS.sha256 for the standalone
installer, the portable ZIP, release.json, and DOWNLOAD_BETA.txt.

Support:
ForgerDigitalSolutions@outlook.com

Security notice:
Never send API keys, passwords, serial numbers, private documents, or sensitive personal files.

Deep Sensor Mode:
- May be enabled by the installer or Settings.
- Uses bundled local read-only hardware sensors where packaged.
- No separate LibreHardwareMonitor download is needed.
- No fan, voltage, clock, BIOS, or firmware control.
- Some readings depend on firmware, drivers, permissions, and hardware support.
- Unavailable readings are coverage limits, not failures.

Review before sharing:
Reports, support bundles, logs, and exported files may include hardware details, network adapter data, USB device details, local paths, and diagnostic notes. Do not send product keys, API keys, tokens, passwords, private documents, or sensitive files.
"@
    Set-Content -LiteralPath $Path -Value $content -Encoding utf8
}

foreach ($required in @($solutionPath, $projectPath, $backendBuildScript, $stageBackendScript, $installerScript, $manifestRoot)) {
    if (-not (Test-Path -LiteralPath $required)) {
        throw "Required release input not found: $required"
    }
}

$authoritativeVersion = Get-RepoVersion -RepoRoot $repoRoot
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $authoritativeVersion
}
elseif ($Version.Trim() -ne $authoritativeVersion) {
    throw "Refusing to override authoritative VERSION ($authoritativeVersion) with '$Version'."
}

$publishDir = Join-Path $distRoot "publish\$Runtime"
$backendStageRoot = Join-Path $distRoot "backend-stage\backend"
$releaseRoot = if ([string]::IsNullOrWhiteSpace($ReleaseOutputRoot)) {
    Join-Path $repoRoot "release\current"
} else {
    [IO.Path]::GetFullPath($ReleaseOutputRoot)
}
$releaseAppRoot = Join-Path $releaseRoot "app"
$releaseBackendRoot = Join-Path $releaseAppRoot "backend"
$releaseManifestRoot = Join-Path $releaseAppRoot "manifests"
$checksumsPath = Join-Path $releaseRoot "CHECKSUMS.sha256"
$installerOutputDir = Join-Path $distRoot "installer"
$installerReleaseName = "ForgerEMS-Setup-v{0}.exe" -f $Version
$installerReleasePath = Join-Path $releaseRoot $installerReleaseName
$displayVersionLabel = "ForgerEMS v$Version"
$releaseIdentifierLabel = "ForgerEMS v$Version - package $Version (portable app ZIP plus installer)"

$buildTempRoot = Join-Path $distRoot "tmp"
Ensure-Dir -Path $buildTempRoot
$env:TEMP = $buildTempRoot
$env:TMP = $buildTempRoot

# Isolated release destination must be fresh — never erase historical artifacts.
if (Test-Path -LiteralPath $releaseRoot) {
    $existing = Get-ChildItem -LiteralPath $releaseRoot -Force -ErrorAction SilentlyContinue
    if ($existing) {
        throw "Release output root already exists and is not empty: $releaseRoot. Choose a fresh -ReleaseOutputRoot; refusing to erase existing artifacts."
    }
}

Write-Step "Release version: $Version"

if ($signed) {
    # Production packaging fails early on a dirty source tree — production
    # eligibility requires sourceDirtyFileCount = 0 in the emitted metadata.
    $earlyStatus = @(git -C $repoRoot status --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed with exit code $LASTEXITCODE; refusing to treat an unreadable source tree as clean for production packaging."
    }
    $earlyDirtyCount = @($earlyStatus | Where-Object { $_ }).Count
    if ($earlyDirtyCount -gt 0) {
        throw "Production (signed) packaging requires a clean source tree; $earlyDirtyCount dirty file(s) found. Use -UnsignedCandidate for local candidate builds."
    }
}

Write-Step "Restoring solution"
Push-Location $repoRoot
try {
    if ([string]::IsNullOrWhiteSpace($NuGetPackagesPath)) {
        dotnet restore ".\ForgerEMS.sln" --disable-parallel
    } else {
        dotnet restore ".\ForgerEMS.sln" --disable-parallel --packages $NuGetPackagesPath
    }
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

Write-Step "Building solution"
Push-Location $repoRoot
try {
    dotnet build ".\ForgerEMS.sln" -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE." }
}
finally {
    Pop-Location
}

Write-Step "Publishing WPF app"
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true /p:PublishSingleFile=true /p:Version=$Version /p:InformationalVersion=$Version /p:PublishDir="$publishDir\"
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

if ($signed) {
    Write-Step "Signing frontend executable"
    $appExe = Join-Path $publishDir "ForgerEMS.exe"
    if (-not (Test-Path -LiteralPath $appExe)) { throw "Expected publish output was not found: $appExe" }
    Invoke-ForgerEMSSign -Path $appExe
}
elseif ($UnsignedCandidate) {
    Write-Step "Unsigned candidate build: frontend executable will NOT be Authenticode-signed"
}

Write-Step "Building backend release bundle"
$backendReleaseFamilyRoot = Join-Path $distRoot "backend-release\ventoy-core"
& $backendBuildScript -ReleaseFamilyRoot $backendReleaseFamilyRoot

$backendManifest = Get-Content -LiteralPath (Join-Path $manifestRoot "ForgerEMS.updates.json") -Raw | ConvertFrom-Json
$backendVersion = [string]$backendManifest.coreVersion
if ([string]::IsNullOrWhiteSpace($backendVersion)) {
    throw "manifests\ForgerEMS.updates.json is missing coreVersion."
}

$backendReleaseRoot = Join-Path $backendReleaseFamilyRoot $backendVersion
if (-not (Test-Path -LiteralPath $backendReleaseRoot)) {
    throw "Expected backend release bundle was not found: $backendReleaseRoot"
}

Write-Step "Staging bundled backend"
& $stageBackendScript -FrontendVersion $Version -ReleaseBundleRoot $backendReleaseRoot -OutputRoot $backendStageRoot -StageFamilyRoot (Split-Path -Parent $backendStageRoot)

Write-Step "Preparing release folder"
Ensure-Dir -Path (Split-Path -Parent $releaseRoot)
Ensure-Dir -Path $releaseRoot
Copy-CleanDirectory -Source $publishDir -Destination $releaseAppRoot
Copy-CleanDirectory -Source $backendStageRoot -Destination $releaseBackendRoot
Copy-CleanDirectory -Source $manifestRoot -Destination $releaseManifestRoot
Copy-ReleaseDocs -Destination $releaseAppRoot -Version $Version

Write-Step "Scanning staged release for driver artifacts"
Assert-NoDriverArtifacts -Roots @($publishDir, $backendStageRoot, $releaseAppRoot) -Context "staged app/backend"

if ($DryRun -or $SkipInstaller) {
    Write-Step "Skipping installer compilation"
}
else {
    Write-Step "Compiling installer"
    Ensure-Dir -Path $installerOutputDir
    $isccPath = Resolve-IsccPath -ExplicitPath $InnoCompilerPath
    $appVersionInfo = ConvertTo-WindowsVersion -Value $Version

    $isccArgs = [System.Collections.Generic.List[string]]::new()
    $isccArgs.Add("/DAppVersion=$Version")
    $isccArgs.Add("/DAppVersionInfo=$appVersionInfo")
    $isccArgs.Add("/DDisplayVersion=$displayVersionLabel")
    $isccArgs.Add("/DReleaseIdentifier=$releaseIdentifierLabel")
    $isccArgs.Add("/DPublishDir=$publishDir")
    $isccArgs.Add("/DBackendBundleDir=$backendStageRoot")
    $isccArgs.Add("/DOutputDir=$installerOutputDir")

    $signedUninstallerDir = Join-Path $installerOutputDir "signing"
    if ($signed) {
        $isccArgs.Add("/DRequireSigning=1")
        # Fresh directory for the cached signed uninstaller Inno emits. Reuse an
        # empty directory or create a fresh one — never delete a pre-existing
        # non-empty directory.
        if (Test-Path -LiteralPath $signedUninstallerDir) {
            $existingEntries = @(Get-ChildItem -LiteralPath $signedUninstallerDir -Force -ErrorAction SilentlyContinue)
            if ($existingEntries.Count -gt 0) {
                throw "SignedUninstallerDir already exists and is not empty: $signedUninstallerDir. Refusing to remove pre-existing contents - clear it manually or use a fresh output root."
            }
        }
        else {
            Ensure-Dir -Path $signedUninstallerDir
        }
        # Inno deletes the signed uninst.eXX.tmp after embedding it; the
        # callback captures a verified copy here for release attestation.
        $verifiedUninstallerPath = Join-Path $signedUninstallerDir 'verified-uninstaller.exe'
        $isccArgs.Add("/DSignedUninstallerDir=$signedUninstallerDir")
        $isccArgs.Add("/SForgerEMSRelease=$(Get-ForgerEMSSignToolCallback -VerifiedUninstallerPath $verifiedUninstallerPath)")
    }
    else {
        $isccArgs.Add("/DUnsignedCandidate=1")
    }
    $isccArgs.Add($installerScript)

    & $isccPath @isccArgs
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

    $versionedInstallerPath = Join-Path $installerOutputDir ("ForgerEMS-Setup-v{0}.exe" -f $Version)
    if (-not (Test-Path -LiteralPath $versionedInstallerPath)) {
        throw "Expected installer output was not found: $versionedInstallerPath"
    }

    if ($signed) {
        # With SignTool=ForgerEMSRelease configured, Inno has already signed the
        # installer through the callback (which signs AND verifies each
        # invocation). Verify rather than sign again; fall back to a single
        # helper sign if the output somehow arrived unsigned.
        $installerSig = Get-AuthenticodeSignature -FilePath $versionedInstallerPath
        if ($null -ne $installerSig -and $installerSig.Status -eq [System.Management.Automation.SignatureStatus]::Valid) {
            Write-Step "Verifying installer signature applied by Inno SignTool callback"
            Invoke-ForgerEMSSign -Path $versionedInstallerPath -VerifyOnly
        }
        else {
            Write-Step "Signing installer"
            Invoke-ForgerEMSSign -Path $versionedInstallerPath
        }

        # Captured signed uninstaller: Inno embeds the signed uninst.eXX.tmp
        # bytes into the installer and then deletes the temporary file, so the
        # helper's verified copy is the durable artifact — it must exist and
        # pass the same verification contract.
        if (-not (Test-Path -LiteralPath $verifiedUninstallerPath -PathType Leaf)) {
            throw "SignedUninstaller is required but the signing callback did not capture a verified uninstaller to $verifiedUninstallerPath (fail closed)."
        }
        Invoke-ForgerEMSSign -Path $verifiedUninstallerPath -VerifyOnly
        Write-Step "Verified captured signed uninstaller."
    }

    Copy-Item -LiteralPath $versionedInstallerPath -Destination (Join-Path $releaseRoot (Split-Path -Leaf $versionedInstallerPath)) -Force
}

Write-Step "Writing release metadata"
$sourceHead = (git -C $repoRoot rev-parse HEAD 2>$null)
if ($LASTEXITCODE -ne 0 -or -not ($sourceHead -match '^[0-9a-fA-F]{40}$')) {
    throw "git rev-parse HEAD failed or did not return a 40-hex commit id ('$sourceHead'); refusing to write release metadata with unknown provenance."
}
$gitStatusLines = @(git -C $repoRoot status --porcelain 2>$null)
if ($LASTEXITCODE -ne 0) {
    throw "git status failed with exit code $LASTEXITCODE; refusing to write release metadata with unknown dirty state."
}
$dirtyCount = @($gitStatusLines | Where-Object { $_ }).Count
$metadata = [ordered]@{
    product = "ForgerEMS"
    publisher = "Forger Digital Solutions"
    version = $Version
    releaseIdentifier = $releaseIdentifierLabel
    channel = if ($UnsignedCandidate) { "preview" } else { "stable" }
    backendVersion = $backendVersion
    runtime = $Runtime
    configuration = $Configuration
    dryRun = [bool]$DryRun
    signed = [bool]$script:SigningApplied
    unsignedCandidate = [bool]$UnsignedCandidate
    productionEligible = (-not $UnsignedCandidate -and $script:SigningApplied -and -not $SkipInstaller -and -not $DryRun -and $dirtyCount -eq 0)
    sourceHead = $sourceHead
    sourceDirtyFileCount = $dirtyCount
    generatedUtc = (Get-Date).ToUniversalTime().ToString("o")
}
if ($UnsignedCandidate) {
    Write-Warning "UNSIGNED LOCAL CANDIDATE: artifacts are not Authenticode-signed and are marked non-production in release.json. Do not publish."
}
$metadata | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $releaseRoot "release.json") -Encoding UTF8

Write-Step "Copying beta readme if present"
$betaReadmePath = Join-Path $distRoot ("beta\README-BETA-v{0}.txt" -f $Version)
if (Test-Path -LiteralPath $betaReadmePath) {
    Copy-Item -LiteralPath $betaReadmePath -Destination (Join-Path $releaseRoot (Split-Path -Leaf $betaReadmePath)) -Force
}

$releaseJsonPath = Join-Path $releaseRoot "release.json"

if (Test-Path -LiteralPath $runtimeHelperPath) {
    . $runtimeHelperPath
}
else {
    throw "ForgerEMS runtime helper was not found. Checked: $runtimeHelperPath"
}

if ($DryRun -or $SkipInstaller) {
    Write-Step "Generating SHA256 checksums"
    Write-Checksums -Root $releaseRoot -OutputPath $checksumsPath
}
else {
    Write-Step "Creating portable ZIP distribution bundle"
    $zipBundleName = "ForgerEMS-v{0}.zip" -f $Version
    $zipBundlePath = Join-Path $releaseRoot $zipBundleName
    $packageParent = Join-Path $releaseRoot "package"
    $packageDirName = "ForgerEMS-v{0}" -f $Version
    $packageRoot = Join-Path $packageParent $packageDirName

    if (Test-Path -LiteralPath $packageParent) {
        Remove-Item -LiteralPath $packageParent -Recurse -Force
    }
    Ensure-Dir -Path $packageRoot

    Copy-CleanDirectory -Source $releaseAppRoot -Destination $packageRoot
    Copy-Item -LiteralPath $releaseJsonPath -Destination (Join-Path $packageRoot "release.json") -Force
    Write-StartHereBat -Path (Join-Path $packageRoot "START_HERE.bat")
    Write-VerifyTxt -Path (Join-Path $packageRoot "VERIFY.txt") -Version $Version

    Write-Checksums -Root $packageRoot -OutputPath (Join-Path $packageRoot "CHECKSUMS.sha256")

    Assert-NoDriverArtifacts -Roots @($packageRoot) -Context "portable ZIP package"

    Compress-PackageFolderZip -PackageRoot $packageRoot -EntryFolderName $packageDirName -DestinationZipPath $zipBundlePath

    $downloadBetaPath = Join-Path $releaseRoot "DOWNLOAD_BETA.txt"
    Write-DownloadBetaTxt -Path $downloadBetaPath -Version $Version

    $distributionChecksumText = Get-DistributionChecksumText `
        -InstallerPath $installerReleasePath `
        -ZipPath $zipBundlePath `
        -ReleaseJsonPath $releaseJsonPath `
        -DownloadBetaPath $downloadBetaPath `
        -InstallerRelativeName $installerReleaseName `
        -ZipRelativeName $zipBundleName
    Set-ChecksumFileLf -Path $checksumsPath -Text $distributionChecksumText
    if (Test-Path -LiteralPath $packageParent) {
        Remove-Item -LiteralPath $packageParent -Recurse -Force
    }
}

Write-Host "ForgerEMS current release folder ready: $releaseRoot" -ForegroundColor Green
