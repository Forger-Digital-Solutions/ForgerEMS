#requires -Version 5.1
<#
.SYNOPSIS
  Public Preview release readiness validation for ForgerEMS (PASS / WARN / FAIL).

.DESCRIPTION
  Verifies repo layout, version consistency, required docs/tools, optional dist/release artifacts,
  and lightweight secret-pattern heuristics on tracked text files. Exit code 1 only on FAIL rows.

.PARAMETER RepoRoot
  Repository root (folder containing ForgerEMS.sln).

.PARAMETER Version
  Expected semantic package version (e.g. 1.2.0-preview.1).

.PARAMETER ReleaseRoot
  Optional. If set (e.g. ...\release\current), validates release.json + CHECKSUMS + installer/ZIP when present.
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Version = "1.2.0-preview.1",
    [string]$ReleaseRoot = ""
)

$ErrorActionPreference = "Stop"

$rows = [System.Collections.Generic.List[object]]::new()
$failCount = 0
$warnCount = 0

function Add-Row {
    param(
        [Parameter(Mandatory)][ValidateSet("PASS","WARN","FAIL")][string]$Level,
        [Parameter(Mandatory)][string]$Id,
        [Parameter(Mandatory)][string]$Message
    )
    $script:rows.Add([pscustomobject]@{ Level = $Level; Id = $Id; Message = $Message })
    switch ($Level) {
        "FAIL" { $script:failCount++; Write-Host "[FAIL] $Id - $Message" -ForegroundColor Red }
        "WARN" { $script:warnCount++; Write-Host "[WARN] $Id - $Message" -ForegroundColor Yellow }
        default { Write-Host "[PASS] $Id - $Message" -ForegroundColor Green }
    }
}

function Test-FileExists([string]$Rel, [string]$Id) {
    $p = Join-Path $RepoRoot $Rel
    if (Test-Path -LiteralPath $p) { Add-Row -Level "PASS" -Id $Id -Message "Found $Rel" }
    else { Add-Row -Level "FAIL" -Id $Id -Message "Missing $Rel" }
}

function Test-FileContains {
    param(
        [Parameter(Mandatory)][string]$Rel,
        [Parameter(Mandatory)][string]$Pattern,
        [Parameter(Mandatory)][string]$Id,
        [ValidateSet("PASS","WARN","FAIL")][string]$OnMiss = "FAIL"
    )
    $p = Join-Path $RepoRoot $Rel
    if (-not (Test-Path -LiteralPath $p)) {
        Add-Row -Level "FAIL" -Id $Id -Message "File missing: $Rel"
        return
    }
    $t = Get-Content -LiteralPath $p -Raw
    if ($t -match $Pattern) { Add-Row -Level "PASS" -Id $Id -Message "Pattern OK in $Rel" }
    else { Add-Row -Level $OnMiss -Id $Id -Message "Expected pattern not found in $Rel" }
}

# --- Core paths ---
Test-FileExists "ForgerEMS.sln" "sln"
Test-FileExists "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj" "wpf-csproj"
Test-FileExists "tools\build-release.ps1" "build-release"
Test-FileExists "installer\ForgerEMS.iss" "installer-iss"
Test-FileExists "docs\ENVIRONMENT.md" "doc-environment"
Test-FileExists ".env.example" "env-example"
Test-FileExists "docs\ARCHITECTURE-INTEGRATION-v1.2.0.md" "doc-architecture"
Test-FileExists "docs\UPDATE-SYSTEM-v1.2.0.md" "doc-update-v12"
Test-FileExists "docs\PUBLIC_PREVIEW_CHECKLIST_v1.2.0.md" "doc-preview-checklist"
Test-FileExists "docs\PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md" "doc-manual-qa"
Test-FileExists "docs\marketing\KICKSTARTER-DRAFT.md" "marketing-kickstarter"
Test-FileExists "docs\marketing\SOCIAL-POSTS.md" "marketing-social"
Test-FileExists "docs\marketing\PUBLIC-FAQ.md" "marketing-faq"
Test-FileExists "docs\marketing\SCREENSHOT-SHOTLIST.md" "marketing-screenshots"
Test-FileExists "tools\Test-ForgerEMSBackend.ps1" "tool-backend-test"
Test-FileExists "tools\Export-ForgerEMSDiagnostics.ps1" "tool-export-diagnostics"
Test-FileExists "tools\New-ForgerEMSPreviewRelease.ps1" "tool-new-preview"

# --- Version consistency (csproj) ---
$csprojPath = Join-Path $RepoRoot "src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj"
[xml]$csXml = Get-Content -LiteralPath $csprojPath -Raw
$pg = @($csXml.Project.PropertyGroup)
$csVer = $null
$asmVer = $null
$fileVer = $null
$infoVer = $null
foreach ($g in $pg) {
    if ($g.Version) { if (-not $csVer) { $csVer = [string]$g.Version } }
    if ($g.AssemblyVersion) { if (-not $asmVer) { $asmVer = [string]$g.AssemblyVersion } }
    if ($g.FileVersion) { if (-not $fileVer) { $fileVer = [string]$g.FileVersion } }
    if ($g.InformationalVersion) { if (-not $infoVer) { $infoVer = [string]$g.InformationalVersion } }
}
if ($csVer -eq $Version) { Add-Row -Level "PASS" -Id "csproj-Version" -Message "<Version> is $Version" }
else { Add-Row -Level "FAIL" -Id "csproj-Version" -Message "Expected <Version>$Version</Version>, got '$csVer'" }

if ($asmVer -eq "1.2.0.0") { Add-Row -Level "PASS" -Id "csproj-AssemblyVersion" -Message "AssemblyVersion 1.2.0.0" }
else { Add-Row -Level "FAIL" -Id "csproj-AssemblyVersion" -Message "Expected 1.2.0.0, got '$asmVer'" }

if ($infoVer -eq $Version) { Add-Row -Level "PASS" -Id "csproj-InformationalVersion" -Message "InformationalVersion matches" }
else { Add-Row -Level "FAIL" -Id "csproj-InformationalVersion" -Message "Expected $Version, got '$infoVer'" }

# --- README / CHANGELOG copy ---
Test-FileContains -Rel "README.md" -Pattern "ForgerEMS v1\.2\.0 Public Preview" -Id "readme-display"
Test-FileContains -Rel "README.md" -Pattern ([regex]::Escape($Version)) -Id "readme-semver"
Test-FileContains -Rel "CHANGELOG.md" -Pattern ([regex]::Escape($Version)) -Id "changelog-version"

# --- AppReleaseInfo (source) ---
$appRel = Join-Path $RepoRoot "src\ForgerEMS.Wpf\Infrastructure\AppReleaseInfo.cs"
if (Test-Path -LiteralPath $appRel) {
    $src = Get-Content -LiteralPath $appRel -Raw
    if ($src -match 'Version\s*=\s*"' + [regex]::Escape($Version) + '"') { Add-Row -Level "PASS" -Id "AppReleaseInfo-Version" -Message "AppReleaseInfo.Version" }
    else { Add-Row -Level "FAIL" -Id "AppReleaseInfo-Version" -Message "AppReleaseInfo.Version not $Version" }
    if ($src -match 'ForgerEMS v1\.2\.0 Public Preview') { Add-Row -Level "PASS" -Id "AppReleaseInfo-Display" -Message "DisplayVersion string" }
    else { Add-Row -Level "FAIL" -Id "AppReleaseInfo-Display" -Message "DisplayVersion missing Public Preview wording" }
}
else { Add-Row -Level "FAIL" -Id "AppReleaseInfo-file" -Message "AppReleaseInfo.cs missing" }

# --- Secret heuristics (tracked *.md, *.cs, *.ps1, *.xaml, *.json under repo — exclude bin/obj) ---
$badPatterns = @(
    '(?i)FORGEREMS_OPENAI_API_KEY\s*=\s*["''][^"'']{8,}',
    '(?i)sk-[a-zA-Z0-9]{10,}',
    '(?i)Bearer\s+[a-zA-Z0-9._-]{20,}',
    '(?i)api_key\s*[:=]\s*["''][^"'']{8,}'
)
$scanRoots = @(
    (Join-Path $RepoRoot "src"),
    (Join-Path $RepoRoot "docs"),
    (Join-Path $RepoRoot "tools"),
    (Join-Path $RepoRoot "installer"),
    (Join-Path $RepoRoot "backend")
)
$script:hits = 0
foreach ($root in $scanRoots) {
    if (-not (Test-Path -LiteralPath $root)) { continue }
    Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $_.FullName -notmatch '\\(bin|obj)(\\|$)' -and
            $_.Name -ne 'check-secrets.ps1' -and
            $_.Extension -in @(".md", ".cs", ".ps1", ".xaml", ".json", ".iss", ".yml", ".yaml")
        } |
        ForEach-Object {
            $txt = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction SilentlyContinue
            if (-not $txt) { return }
            foreach ($bp in $badPatterns) {
                if ($txt -match $bp) {
                    $script:hits++
                    Write-Host "[WARN] secret-pattern: possible match in $($_.FullName)" -ForegroundColor Yellow
                }
            }
        }
}
if ($script:hits -eq 0) { Add-Row -Level "PASS" -Id "secret-scan-heuristic" -Message "No obvious API key / bearer patterns in scanned text" }
else { Add-Row -Level "WARN" -Id "secret-scan-heuristic" -Message "Possible secret-like patterns ($($script:hits)) - review warnings above" }

# --- Optional release output ---
if ([string]::IsNullOrWhiteSpace($ReleaseRoot)) {
    $defaultRelease = Join-Path $RepoRoot "release\current"
    if (Test-Path -LiteralPath $defaultRelease) { $ReleaseRoot = $defaultRelease }
}

if (-not [string]::IsNullOrWhiteSpace($ReleaseRoot) -and (Test-Path -LiteralPath $ReleaseRoot)) {
    Add-Row -Level "PASS" -Id "release-root" -Message "Validating $ReleaseRoot"
    $rj = Join-Path $ReleaseRoot "release.json"
    if (Test-Path -LiteralPath $rj) {
        try {
            $meta = Get-Content -LiteralPath $rj -Raw | ConvertFrom-Json
            if ($meta.version -eq $Version) { Add-Row -Level "PASS" -Id "release-json-version" -Message "release.json version" }
            else { Add-Row -Level "WARN" -Id "release-json-version" -Message "release.json version is '$($meta.version)' (expected $Version)" }
        }
        catch { Add-Row -Level "WARN" -Id "release-json-parse" -Message "Could not parse release.json" }
    }
    else { Add-Row -Level "WARN" -Id "release-json" -Message "release.json not found (run build-release)" }

    $cs256 = Join-Path $ReleaseRoot "CHECKSUMS.sha256"
    if (Test-Path -LiteralPath $cs256) {
        $h = Get-Content -LiteralPath $cs256 -Raw
        if ($h.Length -gt 20) { Add-Row -Level "PASS" -Id "checksums" -Message "CHECKSUMS.sha256 present" }
        else { Add-Row -Level "WARN" -Id "checksums" -Message "CHECKSUMS.sha256 looks empty" }
    }
    else { Add-Row -Level "WARN" -Id "checksums" -Message "CHECKSUMS.sha256 missing" }

    $inst = Join-Path $ReleaseRoot ("ForgerEMS-Setup-v{0}.exe" -f $Version)
    $zip = Join-Path $ReleaseRoot ("ForgerEMS-v{0}.zip" -f $Version)
    if (Test-Path -LiteralPath $inst) { Add-Row -Level "PASS" -Id "installer-artifact" -Message "Installer present" }
    else { Add-Row -Level "WARN" -Id "installer-artifact" -Message "Installer not found (use build-release without -SkipInstaller)" }

    if (Test-Path -LiteralPath $zip) { Add-Row -Level "PASS" -Id "zip-artifact" -Message "Primary ZIP present" }
    else { Add-Row -Level "WARN" -Id "zip-artifact" -Message "ForgerEMS-v$Version`.zip not found (expected after full installer build)" }

    $exe = Join-Path $ReleaseRoot "app\ForgerEMS.exe"
    if (Test-Path -LiteralPath $exe) { Add-Row -Level "PASS" -Id "published-exe" -Message "app\ForgerEMS.exe present" }
    else { Add-Row -Level "WARN" -Id "published-exe" -Message "Published exe missing under release\current\app" }
}
else {
    Add-Row -Level "WARN" -Id "release-output" -Message "No release\current - run tools\build-release.ps1 before artifact checks"
}

# --- Verdict ---
Write-Host ""
Write-Host "========== SUMMARY ==========" -ForegroundColor Cyan
Write-Host "FAIL: $failCount | WARN: $warnCount | PASS rows: $(($rows | Where-Object { $_.Level -eq "PASS" }).Count)" -ForegroundColor Cyan
if ($failCount -gt 0) {
    Write-Host "VERDICT: NOT READY (failures)" -ForegroundColor Red
    exit 1
}
if ($warnCount -gt 0) {
    Write-Host "VERDICT: OK WITH WARNINGS (review before ship)" -ForegroundColor Yellow
    exit 0
}
Write-Host "VERDICT: READY" -ForegroundColor Green
exit 0
