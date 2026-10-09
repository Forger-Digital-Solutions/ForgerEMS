#requires -Version 5.1
<#
.SYNOPSIS
  Builds a redacted operator diagnostics ZIP (logs + README + metadata).
  Complements the in-app Create Support Bundle; uses the same path redaction style as the app.
#>
param(
    [string]$OutputZip = "",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $versionFile = Join-Path (Split-Path -Parent $PSScriptRoot) "VERSION"
    if (Test-Path -LiteralPath $versionFile) {
        $Version = (Get-Content -LiteralPath $versionFile -Raw).Trim()
    }
    if ([string]::IsNullOrWhiteSpace($Version)) { $Version = "unknown" }
}
function Redact-Text([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return "" }
    $t = $s
    # Multiline secret blocks (PEM etc.) first so line-oriented rules never see inside them.
    $t = $t -replace '(?s)-----BEGIN [A-Z0-9 ]*-----.*?-----END [A-Z0-9 ]*-----', '[REDACTED_PRIVATE_BLOCK]'
    # Authorization headers: scheme + credential together.
    $t = $t -replace '(?i)\bauthorization\s*:\s*(?:basic|bearer|digest|negotiate|token|apikey)\s+[^\s''";]+', 'Authorization: [REDACTED_SECRET]'
    # Credentials embedded in URLs: scheme://user:password@host
    $t = $t -replace '(?i)\b([a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@', '$1[REDACTED_CREDENTIAL]@'
    # JSON-style pairs: "password": "value"
    $t = $t -replace '(?i)(")([A-Za-z0-9_.-]*(?:api[_-]?key|token|secret|password|passwd|pwd|credential|authorization|bearer|private[_-]?key|client[_-]?secret|access[_-]?key|account[_-]?key|connection[_-]?string|refresh[_-]?token|session[_-]?key)[A-Za-z0-9_.-]*)"\s*:\s*"[^"\r\n]*"', '$1$2": "[REDACTED_SECRET]"'
    # Environment-dump / config-style assignments: NAME=value or NAME: value with a
    # credential-bearing name — catches FORGEREMS_GITHUB_TOKEN=... and friends.
    $t = $t -replace '(?i)\b([A-Za-z0-9_]*(?:api[_-]?key|token|secret|password|passwd|pwd|credential|authorization|bearer|private[_-]?key|client[_-]?secret|access[_-]?key|account[_-]?key|connection[_-]?string|refresh[_-]?token|session[_-]?key)[A-Za-z0-9_]*)\s*[:=]\s*[''"]?[^''"\s;,}\]\[]+', '$1=[REDACTED_SECRET]'
    # Bare Bearer tokens outside an Authorization header.
    $t = $t -replace '(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{8,}\b', 'Bearer [REDACTED_SECRET]'
    # Provider-specific token formats.
    $t = $t -replace '\b(?:ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{16,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bsk-(?:ant-|live-|test-|proj-)?[A-Za-z0-9_-]{16,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bxox[baprs]-[A-Za-z0-9-]{10,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bAKIA[0-9A-Z]{16}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bglpat-[A-Za-z0-9_-]{15,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bnpm_[A-Za-z0-9]{30,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bAIza[0-9A-Za-z_-]{35}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bya29\.[0-9A-Za-z_-]{10,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\bcfut_[A-Za-z0-9_]{20,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{4,}\b', '[REDACTED_SECRET]'
    $t = $t -replace '(?i)[A-Za-z]:\\Users\\[^\\\s]+', '[REDACTED_PRIVATE_PATH]'
    $t = $t -replace '(?i)[A-Za-z]:\\[^\r\n\t ]+', '[REDACTED_PRIVATE_PATH]'
    return $t
}

$la = $env:LOCALAPPDATA
if ([string]::IsNullOrWhiteSpace($la)) { throw "LOCALAPPDATA not set." }

if ([string]::IsNullOrWhiteSpace($OutputZip)) {
    $OutputZip = Join-Path $PWD ("ForgerEMS-diagnostics-export-{0}.zip" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}

$staging = Join-Path ([IO.Path]::GetTempPath()) ("forgerems-diag-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $staging -Force | Out-Null

$meta = @"
ForgerEMS diagnostics export (operator script)
GeneratedUtc: $((Get-Date).ToUniversalTime().ToString("o"))
AppSemanticVersion: $Version
DisplayVersion: ForgerEMS v$Version
FORGEREMS_RELEASE_CHANNEL: $env:FORGEREMS_RELEASE_CHANNEL
Update owner/repo: $env:FORGEREMS_GITHUB_OWNER / $env:FORGEREMS_GITHUB_REPO

Redaction: user-profile style paths replaced with [REDACTED_PRIVATE_PATH];
secret assignments and common credential formats replaced with [REDACTED_SECRET].
Send only if comfortable. Support: ForgerDigitalSolutions@outlook.com
Do not email API keys, passwords, or private documents.
"@
Set-Content -LiteralPath (Join-Path $staging "README.txt") -Encoding utf8 -Value $meta

$roots = @(
    (Join-Path $la "ForgerEMS\logs"),
    (Join-Path $la "ForgerEMS\Runtime\logs")
)

foreach ($r in $roots) {
    if (-not (Test-Path -LiteralPath $r)) { continue }
    $destRoot = Join-Path $staging ("logs-" + (Split-Path $r -Leaf))
    New-Item -ItemType Directory -Path $destRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $r -File -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $raw = Get-Content -LiteralPath $_.FullName -Raw -ErrorAction Stop
            $safe = Redact-Text $raw
            Set-Content -LiteralPath (Join-Path $destRoot $_.Name) -Encoding utf8 -Value $safe
        }
        catch { }
    }
}

if (Test-Path -LiteralPath $OutputZip) { Remove-Item -LiteralPath $OutputZip -Force }
Compress-Archive -Path (Join-Path $staging "*") -DestinationPath $OutputZip -Force
Remove-Item -LiteralPath $staging -Recurse -Force
Write-Host "Wrote $OutputZip" -ForegroundColor Cyan
