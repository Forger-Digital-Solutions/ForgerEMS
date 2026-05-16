<#
.SYNOPSIS
  Local ForgerEMS configuration and secret-surface audit.

.DESCRIPTION
  Scans source-controlled text files for environment-variable references, provider
  settings, and obvious secret-like strings. The script is local-only and does not
  upload data, call paid tools, or print full secret values. Findings are redacted
  and written to artifacts/config-audit/forgerems-config-audit.txt.

.PARAMETER RepoRoot
  Repository root. Defaults to the parent folder of this script's directory.

.PARAMETER Strict
  Exit 1 when a non-placeholder secret-like finding is detected outside tests/docs.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\audit-config-and-secrets.ps1
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

$outputDir = Join-Path $RepoRoot 'artifacts/config-audit'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$outputPath = Join-Path $outputDir 'forgerems-config-audit.txt'

$excludeDirNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($name in @(
        '.git', '.vs', 'bin', 'obj', 'dist', 'node_modules', 'packages'
    )) {
    [void] $excludeDirNames.Add($name)
}

$excludePathPatterns = @(
    '(^|[\\/])release[\\/]current([\\/]|$)',
    '(^|[\\/])release[\\/]ventoy-core([\\/]|$)',
    '(^|[\\/])artifacts([\\/]|$)'
)

$includeExtensions = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($extension in @(
        '.cs', '.xaml', '.ps1', '.psm1', '.md', '.txt', '.json', '.iss',
        '.yml', '.yaml', '.csproj', '.props', '.targets', '.config',
        '.xml', '.bat', '.cmd', '.sh', '.example', '.editorconfig',
        '.gitignore', '.gitattributes'
    )) {
    [void] $includeExtensions.Add($extension)
}

$secretPatterns = [ordered]@{
    'openai-key-like'        = 'sk-[A-Za-z0-9_-]{20,}'
    'anthropic-key-like'     = 'sk-ant-[A-Za-z0-9_-]{20,}'
    'groq-key-like'          = 'gsk_[A-Za-z0-9_-]{20,}'
    'github-token-like'      = '(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{20,}'
    'github-pat-like'        = 'github_pat_[A-Za-z0-9_]{20,}'
    'google-api-key-like'    = 'AIza[0-9A-Za-z_-]{20,}'
    'aws-access-key-like'    = 'AKIA[0-9A-Z]{16}'
    'slack-token-like'       = 'xox[baprs]-[A-Za-z0-9-]{12,}'
    'private-key-block'      = '-----BEGIN (RSA |EC |OPENSSH |)PRIVATE KEY-----'
    'connection-string-like' = '(?i)(password|pwd)\s*=\s*[^;\s]{8,}'
    'bearer-token-like'      = '(?i)bearer\s+[A-Za-z0-9._~+/=-]{20,}'
    'webhook-url-like'       = 'https://[^ \t''"]*(webhook|hooks)[^ \t''"]*'
}

$environmentPatterns = @(
    'Environment.GetEnvironmentVariable',
    '\$env:',
    'Env:',
    'FORGEREMS_[A-Z0-9_]+',
    'OPENAI_[A-Z0-9_]+',
    'ANTHROPIC_[A-Z0-9_]+',
    'GEMINI_[A-Z0-9_]+',
    'GOOGLE_[A-Z0-9_]+',
    'DEEPSEEK_[A-Z0-9_]+',
    'PERPLEXITY_[A-Z0-9_]+',
    'MISTRAL_[A-Z0-9_]+',
    'OPENROUTER_[A-Z0-9_]+',
    'OLLAMA[A-Z0-9_]*',
    'LM_STUDIO_[A-Z0-9_]+',
    'LMSTUDIO_[A-Z0-9_]+',
    'GITHUB_[A-Z0-9_]+',
    'EBAY_[A-Z0-9_]+',
    'FACEBOOK_[A-Z0-9_]+',
    'OFFERUP_[A-Z0-9_]+'
)

$toolPatterns = @(
    'dotnet', 'powershell', 'pwsh', '\bgit\b', '\bgh\b', '\biscc\b',
    'Invoke-WebRequest', 'Start-BitsTransfer', '\bcurl\b', 'Get-FileHash',
    'Get-ForgerSha256', 'powercfg', 'wmic', 'Get-CimInstance',
    'Get-PhysicalDisk', 'Get-StorageReliabilityCounter',
    'Confirm-SecureBootUEFI', 'manage-bde', 'LibreHardwareMonitorLib'
)

function Convert-ToRepoRelativePath {
    param([Parameter(Mandatory)][string] $Path)
    return $Path.Substring($RepoRoot.Length).TrimStart('\', '/')
}

function Should-ScanFile {
    param([Parameter(Mandatory)][System.IO.FileInfo] $File)

    $relative = Convert-ToRepoRelativePath $File.FullName
    $parts = $relative -split '[\\/]'
    foreach ($part in $parts) {
        if ($excludeDirNames.Contains($part)) {
            return $false
        }
    }

    foreach ($pattern in $excludePathPatterns) {
        if ($relative -match $pattern) {
            return $false
        }
    }

    if ($File.Name -eq '.env.example') {
        return $true
    }

    if ($File.Name -in @('.gitignore', '.gitattributes')) {
        return $true
    }

    return $includeExtensions.Contains($File.Extension)
}

function Get-RedactedPreview {
    param([Parameter(Mandatory)][string] $Value)
    $trimmed = $Value.Trim()
    if ($trimmed.Length -le 8) {
        return '****'
    }

    $prefixLength = [Math]::Min(8, $trimmed.Length)
    $suffixLength = [Math]::Min(4, [Math]::Max(0, $trimmed.Length - $prefixLength))
    $prefix = $trimmed.Substring(0, $prefixLength)
    $suffix = if ($suffixLength -gt 0) { $trimmed.Substring($trimmed.Length - $suffixLength) } else { '' }
    return "$prefix...$suffix"
}

function Is-PlaceholderLine {
    param([Parameter(Mandatory)][string] $Line)
    return $Line -match '(?i)REPLACE_ME|REPLACE_WITH_BETA_ACCESS_TOKEN|REPLACE_MODEL_NAME|REPLACE_[A-Z0-9_]+|YOUR_[A-Z0-9_]+|PASTE_[A-Z0-9_]+|local-model-name|model-name|changeme|TODO|fake-|example|placeholder|sample|dummy|not-a-real|redaction fixture|SECRET123'
}

$secretFindings = [System.Collections.Generic.List[object]]::new()
$envRefs = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$toolRefs = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$filesScanned = 0

Get-ChildItem -LiteralPath $RepoRoot -Recurse -File -Force | ForEach-Object {
    if (-not (Should-ScanFile $_)) {
        return
    }

    $filesScanned++
    $relative = Convert-ToRepoRelativePath $_.FullName

    try {
        $lines = [System.IO.File]::ReadAllLines($_.FullName)
    }
    catch {
        return
    }

    for ($i = 0; $i -lt $lines.Length; $i++) {
        $line = $lines[$i]

        foreach ($pattern in $environmentPatterns) {
            foreach ($match in [regex]::Matches($line, $pattern)) {
                $value = $match.Value.Trim('$', ':')
                if ($value -eq 'Environment.GetEnvironmentVariable' -or $value -eq '$env:' -or $value -eq 'Env:') {
                    continue
                }

                if ($value -cnotmatch '^[A-Z][A-Z0-9_]+$') {
                    continue
                }

                [void] $envRefs.Add($value)
            }
        }

        foreach ($pattern in $toolPatterns) {
            if ($line -match $pattern) {
                [void] $toolRefs.Add(($pattern -replace '\\b', '' -replace '\\', ''))
            }
        }

        foreach ($entry in $secretPatterns.GetEnumerator()) {
            $patternId = [string] $entry.Key
            $regex = [string] $entry.Value
            foreach ($match in [regex]::Matches($line, $regex)) {
                $isPlaceholder = (Is-PlaceholderLine $line) -or ($relative -match '(?i)(^|[\\/])tests[\\/]|\.Tests[\\/]')
                $isReleasePath = $relative -match '(?i)(^|[\\/])(release|dist|installer)([\\/]|$)|appsettings|\.env$'
                $secretFindings.Add([pscustomobject]@{
                        Path          = $relative
                        Line          = $i + 1
                        Pattern       = $patternId
                        Preview       = Get-RedactedPreview $match.Value
                        Classification = if ($isPlaceholder) { 'Placeholder/sample or test fixture' } elseif ($isReleasePath) { 'Release blocker - provider secret in shipped/config path' } else { 'Potential secret - review required' }
                    })
            }
        }
    }
}

$report = [System.Collections.Generic.List[string]]::new()
$report.Add("ForgerEMS Configuration and Secret Audit")
$report.Add("GeneratedUtc: $([DateTimeOffset]::UtcNow.ToString('u'))")
$report.Add("RepoRoot: $RepoRoot")
$report.Add("FilesScanned: $filesScanned")
$report.Add("")
$report.Add("Environment Variables / References")
if ($envRefs.Count -eq 0) {
    $report.Add("- None found in scanned text files.")
}
else {
    foreach ($name in $envRefs) {
        $report.Add("- $name")
    }
}

$report.Add("")
$report.Add("External Tool / Command References")
if ($toolRefs.Count -eq 0) {
    $report.Add("- None found in scanned text files.")
}
else {
    foreach ($tool in $toolRefs) {
        $report.Add("- $tool")
    }
}

$report.Add("")
$report.Add("Secret-Like Findings")
if ($secretFindings.Count -eq 0) {
    $report.Add("- No obvious secret-like values found by this heuristic scan.")
}
else {
    foreach ($finding in ($secretFindings | Sort-Object Path, Line, Pattern)) {
        $report.Add("- $($finding.Classification): $($finding.Path):$($finding.Line) [$($finding.Pattern)] $($finding.Preview)")
    }
}

$report.Add("")
$report.Add("Notes")
$report.Add("- This is a heuristic local scan, not a formal security audit.")
$report.Add("- Values are redacted. Do not paste real API keys, tokens, passwords, product keys, serial numbers, or private documents into support messages.")
$report.Add("- Provider API keys in release assets, appsettings, installer defaults, or .env files are release blockers. Gateway beta tokens are redacted and must be revocable.")
$report.Add("- For deeper release gates, optional tools such as gitleaks or trufflehog may be run locally by maintainers; this script does not require them.")

[System.IO.File]::WriteAllLines($outputPath, $report)
$report | ForEach-Object { Write-Output $_ }

$reviewRequired = $secretFindings | Where-Object { $_.Classification -eq 'Potential secret - review required' -or $_.Classification -like 'Release blocker*' }
if ($Strict -and $reviewRequired.Count -gt 0) {
    Write-Error "Strict mode: potential secret-like findings require review. See $outputPath"
    exit 1
}

exit 0
