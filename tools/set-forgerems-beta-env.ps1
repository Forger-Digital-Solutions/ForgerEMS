[CmdletBinding()]
param(
    [string]$GatewayUrl = "https://forgerems-kyra-gateway.forgerdigitalsolutions.workers.dev",
    [string]$BetaToken = "",
    [switch]$EnableSystemContextSharing,
    [ValidateSet("Off", "ReadOnly")]
    [string]$DeepSensorMode = "ReadOnly",
    [switch]$LocalOnly,
    [bool]$ClearOwnerProviderKeys = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Test-PlaceholderValue {
    param([AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    $v = $Value.Trim()
    $legacyOpenAiPlaceholder = 'sk-' + 'REPLACE_ME'
    return $v -match '^(?i:REPLACE_ME|REPLACE_WITH_BETA_ACCESS_TOKEN|REPLACE_MODEL_NAME|local-model-name|model-name|changeme|TODO|OPENAI_API_KEY_PLACEHOLDER)$' `
        -or [string]::Equals($v, $legacyOpenAiPlaceholder, [StringComparison]::OrdinalIgnoreCase) `
        -or $v -like "REPLACE_*" `
        -or $v -like "YOUR_*" `
        -or $v -like "PASTE_*" `
        -or $v -match '(?i)REPLACE_ME|PLACEHOLDER|example\.local'
}

function Set-UserEnv {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [Parameter()][AllowNull()][string]$Value
    )
    [Environment]::SetEnvironmentVariable($Name, $Value, "User")
}

function Get-SecretState {
    param([AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "MISSING" }
    if (Test-PlaceholderValue $Value) { return "PLACEHOLDER" }
    return "SET"
}

$ownerProviderKeys = @(
    "FORGEREMS_OPENAI_API_KEY",
    "OPENAI_API_KEY",
    "FORGEREMS_ANTHROPIC_API_KEY",
    "ANTHROPIC_API_KEY",
    "FORGEREMS_GEMINI_API_KEY",
    "GEMINI_API_KEY",
    "GROQ_API_KEY",
    "OPENROUTER_API_KEY",
    "CEREBRAS_API_KEY",
    "MISTRAL_API_KEY",
    "GITHUB_MODELS_TOKEN",
    "CLOUDFLARE_API_KEY",
    "CLOUDFLARE_ACCOUNT_ID",
    "FORGEREMS_CUSTOM_PROVIDER_API_KEY",
    "FORGEREMS_NEWS_API_KEY",
    "FORGEREMS_FINANCE_API_KEY",
    "FORGEREMS_CRYPTO_API_KEY",
    "FORGEREMS_STATS_API_KEY",
    "FORGEREMS_EBAY_APP_ID",
    "FORGEREMS_EBAY_CERT_ID",
    "FORGEREMS_EBAY_DEV_ID"
)

# Core beta-safe defaults.
Set-UserEnv "FORGEREMS_RELEASE_CHANNEL" "preview"
Set-UserEnv "FORGEREMS_LICENSE_TIER" "PublicPreview"
Set-UserEnv "FORGEREMS_DEEP_SENSOR_MODE" $DeepSensorMode
Set-UserEnv "FORGEREMS_KYRA_MODE" "hybrid"
Set-UserEnv "FORGEREMS_KYRA_ONLINE_ENABLED" "true"
Set-UserEnv "FORGEREMS_KYRA_API_FIRST" "true"
Set-UserEnv "FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT" ($(if ($EnableSystemContextSharing) { "true" } else { "false" }))
Set-UserEnv "FORGEREMS_KYRA_PERSONALITY" "bubbly-tech"
Set-UserEnv "FORGEREMS_KYRA_MAX_CONTEXT_TURNS" "100"
Set-UserEnv "FORGEREMS_KYRA_CONTEXT_MAX_CHARS" "12000"
Set-UserEnv "FORGEREMS_KYRA_GATEWAY_TIMEOUT_SECONDS" "60"
Set-UserEnv "FORGEREMS_DIAGNOSTICS_REDACTION_STRICT" "true"
Set-UserEnv "FORGEREMS_ENABLE_DIAGNOSTIC_BUNDLE" "true"
Set-UserEnv "FORGEREMS_TELEMETRY_ENABLED" "false"
Set-UserEnv "FORGEREMS_CRASH_REPORTING_ENABLED" "false"

$gatewayReady = -not $LocalOnly -and
    -not [string]::IsNullOrWhiteSpace($GatewayUrl) -and
    -not [string]::IsNullOrWhiteSpace($BetaToken) -and
    -not (Test-PlaceholderValue $GatewayUrl) -and
    -not (Test-PlaceholderValue $BetaToken)

if ($gatewayReady) {
    Set-UserEnv "FORGEREMS_KYRA_PROVIDER" "forgerems-gateway"
    Set-UserEnv "FORGEREMS_KYRA_PROVIDER_PRIORITY" "forgerems-gateway,lmstudio,ollama,offline"
    Set-UserEnv "FORGEREMS_KYRA_GATEWAY_URL" $GatewayUrl.Trim()
    Set-UserEnv "FORGEREMS_KYRA_GATEWAY_BETA_TOKEN" $BetaToken.Trim()
}
else {
    # Local/offline fallback is intentional when gateway token/url is unavailable.
    Set-UserEnv "FORGEREMS_KYRA_PROVIDER" "offline"
    Set-UserEnv "FORGEREMS_KYRA_PROVIDER_PRIORITY" "lmstudio,ollama,offline"
    Set-UserEnv "FORGEREMS_KYRA_GATEWAY_URL" $null
    Set-UserEnv "FORGEREMS_KYRA_GATEWAY_BETA_TOKEN" $null
    Write-Warning "Gateway token/url not ready. ForgerEMS was set to local/offline fallback mode."
}

if ($ClearOwnerProviderKeys) {
    foreach ($name in $ownerProviderKeys) {
        Set-UserEnv $name $null
    }
}

$tokenState = Get-SecretState([Environment]::GetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN", "User"))
Write-Host ("FORGEREMS beta env applied. Gateway mode: {0}; token state: {1}; context sharing: {2}; owner keys cleared: {3}" -f `
    ($(if ($gatewayReady) { "forgerems-gateway" } else { "offline/local" })), `
    $tokenState, `
    ($(if ($EnableSystemContextSharing) { "on" } else { "off" })), `
    ($(if ($ClearOwnerProviderKeys) { "yes" } else { "no" })))
