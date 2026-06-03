#requires -Version 5.1
<#
.SYNOPSIS
Optional link-health checker for Driver Hub catalog URLs.

.DESCRIPTION
Reads the Driver Hub catalog (parsed from DriverHubCatalog.cs) and probes each
official URL with a reasonable timeout and a browser-like user agent. Reports a
per-URL status:

  OK                - 2xx response
  Redirect          - 3xx response (still reachable, but worth noting)
  NotFound          - 404
  ForbiddenLikelyOk - 401/403/406/429/451 (vendor bot protection; likely valid
                     in a real browser)
  Timeout           - request timed out
  Error             - other network or transport error

This script is for manual release validation. It is intentionally not wired into
the unit test run because vendor sites routinely block HEAD/GET from automated
clients even when the page is healthy.

.PARAMETER CatalogPath
Optional path to DriverHubCatalog.cs. Defaults to the in-tree copy.

.PARAMETER TimeoutSec
Per-URL timeout in seconds. Default 15.

.PARAMETER ReportPath
Optional markdown output path. When supplied, a summary table is written.

.PARAMETER FailOnNotFound
When set, the script exits non-zero only if a hard 404 (NotFound) is observed.
ForbiddenLikelyOk, Redirect, Timeout, and Error never fail the script — those
are reported but treated as inconclusive, because legitimate vendor pages often
block automated requests.

.EXAMPLE
pwsh tools/Test-DriverHubLinks.ps1

.EXAMPLE
pwsh tools/Test-DriverHubLinks.ps1 -ReportPath artifacts/driver-hub-links.md -FailOnNotFound
#>
[CmdletBinding()]
param(
    [string]$CatalogPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "src\ForgerEMS.Wpf\Services\DriverHubCatalog.cs"),
    [int]$TimeoutSec = 15,
    [string]$ReportPath = "",
    [switch]$FailOnNotFound
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $CatalogPath)) {
    throw "Could not find Driver Hub catalog at: $CatalogPath"
}

# Parse every "https://..." literal from the catalog source. Driver Hub forbids
# http:// and identifier-bearing URLs, so https-only is the right surface.
$catalogText = Get-Content -LiteralPath $CatalogPath -Raw
$urlMatches = [regex]::Matches($catalogText, '"https://[^"\s]+"')
$urls = $urlMatches |
    ForEach-Object { $_.Value.Trim('"') } |
    Sort-Object -Unique

if (-not $urls -or $urls.Count -eq 0) {
    Write-Host "No URLs found in catalog at $CatalogPath" -ForegroundColor Yellow
    return
}

$userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) ForgerEMS-DriverHub-LinkCheck/1.0"

function Test-Url {
    param([string]$Url)

    $result = [ordered]@{
        Url     = $Url
        Status  = "Error"
        Code    = ""
        Detail  = ""
    }

    foreach ($method in @("Head", "Get")) {
        try {
            $response = Invoke-WebRequest `
                -Uri $Url `
                -Method $method `
                -MaximumRedirection 5 `
                -TimeoutSec $TimeoutSec `
                -UserAgent $userAgent `
                -UseBasicParsing `
                -ErrorAction Stop
            $code = [int]$response.StatusCode
            $result.Code = "$code"
            if ($code -ge 200 -and $code -lt 300) {
                $result.Status = "OK"
            } elseif ($code -ge 300 -and $code -lt 400) {
                $result.Status = "Redirect"
            } else {
                $result.Status = "Error"
                $result.Detail = "HTTP $code"
            }
            return [pscustomobject]$result
        } catch [System.Net.WebException] {
            $resp = $_.Exception.Response
            if ($null -ne $resp) {
                $code = [int]$resp.StatusCode
                $result.Code = "$code"
                switch ($code) {
                    404 { $result.Status = "NotFound"; return [pscustomobject]$result }
                    401 { $result.Status = "ForbiddenLikelyOk"; $result.Detail = "Unauthorized"; return [pscustomobject]$result }
                    403 { $result.Status = "ForbiddenLikelyOk"; $result.Detail = "Forbidden"; return [pscustomobject]$result }
                    406 { $result.Status = "ForbiddenLikelyOk"; $result.Detail = "NotAcceptable"; return [pscustomobject]$result }
                    429 { $result.Status = "ForbiddenLikelyOk"; $result.Detail = "RateLimited"; return [pscustomobject]$result }
                    451 { $result.Status = "ForbiddenLikelyOk"; $result.Detail = "BlockedForLegalReasons"; return [pscustomobject]$result }
                    default {
                        if ($method -eq "Head") { continue }
                        $result.Status = "Error"
                        $result.Detail = "HTTP $code"
                        return [pscustomobject]$result
                    }
                }
            } else {
                if ($method -eq "Head") { continue }
                $result.Status = "Error"
                $result.Detail = $_.Exception.Message
                return [pscustomobject]$result
            }
        } catch [System.TimeoutException] {
            $result.Status = "Timeout"
            $result.Detail = "Timed out after ${TimeoutSec}s"
            return [pscustomobject]$result
        } catch {
            $msg = $_.Exception.Message
            if ($msg -match "timed out" -or $msg -match "Timeout") {
                $result.Status = "Timeout"
                $result.Detail = "Timed out after ${TimeoutSec}s"
                return [pscustomobject]$result
            }
            # Some hosts return non-standard status without exception body — try GET fallback.
            if ($method -eq "Head") { continue }
            $resp = $_.Exception.Response
            if ($null -ne $resp) {
                $code = [int]$resp.StatusCode
                $result.Code = "$code"
                if ($code -eq 404) {
                    $result.Status = "NotFound"
                } elseif ($code -in 401,403,406,429,451) {
                    $result.Status = "ForbiddenLikelyOk"
                    $result.Detail = "HTTP $code"
                } else {
                    $result.Status = "Error"
                    $result.Detail = "HTTP $code"
                }
                return [pscustomobject]$result
            }
            $result.Status = "Error"
            $result.Detail = $msg
            return [pscustomobject]$result
        }
    }

    return [pscustomobject]$result
}

Write-Host "Driver Hub link health (timeout=${TimeoutSec}s, user-agent set)" -ForegroundColor Cyan
Write-Host "Total URLs: $($urls.Count)"
Write-Host ""

$results = foreach ($url in $urls) {
    $r = Test-Url -Url $url
    $color = switch ($r.Status) {
        "OK"                { "Green" }
        "Redirect"          { "DarkCyan" }
        "ForbiddenLikelyOk" { "Yellow" }
        "Timeout"           { "DarkYellow" }
        "NotFound"          { "Red" }
        default             { "Magenta" }
    }
    Write-Host ("{0,-19} {1,4}  {2}" -f $r.Status, $r.Code, $r.Url) -ForegroundColor $color
    $r
}

$summary = $results | Group-Object Status | Sort-Object Name
Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
foreach ($g in $summary) {
    Write-Host ("  {0,-19} {1}" -f $g.Name, $g.Count)
}

$notFound = $results | Where-Object { $_.Status -eq "NotFound" }
if ($notFound.Count -gt 0) {
    Write-Host ""
    Write-Host "Hard 404s detected:" -ForegroundColor Red
    foreach ($n in $notFound) { Write-Host "  $($n.Url)" -ForegroundColor Red }
}

if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $dir = Split-Path -Parent $ReportPath
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $lines = @()
    $lines += "# Driver Hub link health report"
    $lines += ""
    $lines += "- Generated: $(Get-Date -Format 'u')"
    $lines += "- Catalog: ``$([System.IO.Path]::GetFileName($CatalogPath))``"
    $lines += "- Total URLs: $($urls.Count)"
    $lines += "- Timeout: ${TimeoutSec}s"
    $lines += ""
    $lines += "| Status | HTTP | URL | Note |"
    $lines += "| --- | --- | --- | --- |"
    foreach ($r in ($results | Sort-Object Status, Url)) {
        $lines += "| $($r.Status) | $($r.Code) | $($r.Url) | $($r.Detail) |"
    }
    $lines += ""
    $lines += "## Notes"
    $lines += ""
    $lines += "- ``OK`` / ``Redirect`` — page resolved cleanly."
    $lines += "- ``ForbiddenLikelyOk`` — vendor bot protection (401/403/406/429/451)."
    $lines += "  These usually open fine in a real browser; treat as ""manual verify""."
    $lines += "- ``Timeout`` — vendor took too long; usually transient."
    $lines += "- ``NotFound`` — hard 404. Replace the URL in DriverHubCatalog.cs."
    $lines += "- ``Error`` — transport problem; rerun before treating as a real failure."
    Set-Content -LiteralPath $ReportPath -Value ($lines -join [Environment]::NewLine) -Encoding UTF8
    Write-Host ""
    Write-Host "Report written: $ReportPath" -ForegroundColor Cyan
}

if ($FailOnNotFound -and $notFound.Count -gt 0) {
    exit 1
}
