#requires -Version 5.1
<#
.SYNOPSIS
  Builds a redacted operator diagnostics ZIP (logs + README + metadata).
  Complements the in-app Export Support Bundle; uses the same path redaction style as the app.
#>
param(
    [string]$OutputZip = "",
    [string]$Version = "1.2.0-preview.1"
)

$ErrorActionPreference = "Stop"
function Redact-Text([string]$s) {
    if ([string]::IsNullOrEmpty($s)) { return "" }
    $t = $s -replace '(?i)[A-Za-z]:\\Users\\[^\\\s]+', '[REDACTED_PRIVATE_PATH]'
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
DisplayVersion: ForgerEMS v1.2.0 Public Preview
FORGEREMS_RELEASE_CHANNEL: $env:FORGEREMS_RELEASE_CHANNEL
Update owner/repo: $env:FORGEREMS_GITHUB_OWNER / $env:FORGEREMS_GITHUB_REPO

Redaction: user-profile style paths replaced with [REDACTED_PRIVATE_PATH].
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
