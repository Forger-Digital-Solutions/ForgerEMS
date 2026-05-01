<#
.SYNOPSIS
  Heuristic scan for obvious secret-like strings in text sources (not a full SAST).

.DESCRIPTION
  Scans common text extensions under the repo for patterns that often indicate leaked
  credentials. Does not print full matches — only path, line, pattern id, and redacted prefix.

.PARAMETER RepoRoot
  Repository root. Default: folder that contains tools\ (parent of this script's directory).

.PARAMETER Strict
  If set, exit 1 when any hit is found outside known test paths (path contains '.Tests').

.EXAMPLE
  .\tools\check-secrets.ps1
  .\tools\check-secrets.ps1 -Strict
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [switch] $Strict
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $here = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($here)) {
        $here = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    $RepoRoot = Split-Path -Parent $here
}

if (-not (Test-Path -LiteralPath $RepoRoot)) {
    throw "RepoRoot not found: $RepoRoot"
}

$excludeDirNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($n in @('bin', 'obj', '.git', '.vs', 'node_modules', 'packages')) {
    [void]$excludeDirNames.Add($n)
}

$includeExtensions = @(
    '.cs', '.xaml', '.ps1', '.md', '.txt', '.json', '.iss', '.yml', '.yaml', '.csproj', '.props', '.targets'
)

# Pattern id -> regex (case-insensitive where useful)
$patterns = [ordered]@{
    'sk-openai-like'       = 'sk-[A-Za-z0-9_-]{10,}'
    'gsk-groq-like'        = 'gsk_[A-Za-z0-9_-]{10,}'
    'github_pat'           = 'github_pat_[A-Za-z0-9_]{10,}'
    'google_api_AIza'      = 'AIza[0-9A-Za-z_-]{10,}'
    'csk-like'             = 'csk-[A-Za-z0-9_-]{10,}'
    'cfut-like'            = 'cfut_[A-Za-z0-9_-]{10,}'
    'OPENAI_API_KEY='      = 'OPENAI_API_KEY\s*=\s*\S+'
    'GEMINI_API_KEY='      = 'GEMINI_API_KEY\s*=\s*\S+'
    'GROQ_API_KEY='        = 'GROQ_API_KEY\s*=\s*\S+'
    'ANTHROPIC_API_KEY='   = 'ANTHROPIC_API_KEY\s*=\s*\S+'
    'CLOUDFLARE_API_KEY='  = 'CLOUDFLARE_API_KEY\s*=\s*\S+'
    'GITHUB_MODELS_TOKEN=' = 'GITHUB_MODELS_TOKEN\s*=\s*\S+'
}

function Get-RedactedPreview([string] $value, [int] $maxLen = 24) {
    if ([string]::IsNullOrEmpty($value)) { return '(empty)' }
    $t = $value.Trim()
    if ($t.Length -le 8) { return '****' }
    $head = $t.Substring(0, [Math]::Min($maxLen, $t.Length))
    return ($head + '...')
}

$hits = [System.Collections.Generic.List[object]]::new()

Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Force | ForEach-Object {
    $full = $_.FullName
    $rel = $full.Substring($RepoRoot.Length).TrimStart('\', '/')

    if ($rel -replace '\\', '/' -match '(?i)^tools/check-secrets\.ps1$') {
        return
    }

    $parts = $rel -split '[\\/]'
    foreach ($p in $parts) {
        if ($excludeDirNames.Contains($p)) {
            return
        }
    }

    # Skip build outputs if present untracked
    if ($rel -match '(?i)^dist\\|^release\\') {
        return
    }

    if ($includeExtensions -notcontains $_.Extension.ToLowerInvariant()) {
        return
    }

    try {
        $lines = [System.IO.File]::ReadAllLines($full)
    }
    catch {
        return
    }

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]
        foreach ($entry in $patterns.GetEnumerator()) {
            $patternId = [string]$entry.Key
            $regex = [string]$entry.Value
            $m = [regex]::Match($line, $regex, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if (-not $m.Success) { continue }

            # Skip obvious documentation: lines that are mostly explaining env vars without values
            if ($patternId -match 'API_KEY=' -and $line -match '^\s*[#\-*\s]*(GEMINI|OPENAI|example|set\s)') { continue }

            $preview = Get-RedactedPreview $m.Value
            $hits.Add([pscustomobject]@{
                    Path    = $rel
                    Line    = $i + 1
                    Pattern = $patternId
                    Preview = $preview
                    InTests = ($rel -match '(?i)[\\/]tests[\\/]|\.Tests[\\/]')
                })
        }
    }
}

Write-Host "ForgerEMS check-secrets - repo: $RepoRoot" -ForegroundColor Cyan
Write-Host ("Scanned patterns: {0}" -f $patterns.Count)

if ($hits.Count -eq 0) {
    Write-Host "No pattern hits in scanned text files." -ForegroundColor Green
    exit 0
}

$hits | Sort-Object Path, Line | Format-Table -AutoSize Path, Line, Pattern, Preview, InTests

$nonTest = $hits | Where-Object { -not $_.InTests }
if ($Strict -and $nonTest.Count -gt 0) {
    Write-Host "Strict mode: hits outside test paths - review required." -ForegroundColor Red
    exit 1
}

Write-Host "Review hits above. Test projects may contain fake example tokens by design." -ForegroundColor Yellow
exit 0
