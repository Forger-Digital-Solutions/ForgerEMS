param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Get-UserEnvValue {
    param([Parameter(Mandatory=$true)][string]$Name)
    [Environment]::GetEnvironmentVariable($Name, "User")
}

function Test-PlaceholderValue {
    param([AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    $v = $Value.Trim()
    $legacyOpenAiPlaceholder = 'sk-' + 'REPLACE_ME'
    if ($v -match '^(?i:REPLACE_ME|REPLACE_WITH_BETA_ACCESS_TOKEN|REPLACE_MODEL_NAME|local-model-name|model-name|changeme|TODO|OPENAI_API_KEY_PLACEHOLDER)$') { return $true }
    if ([string]::Equals($v, $legacyOpenAiPlaceholder, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    if ($v -like "REPLACE_*") { return $true }
    if ($v -like "YOUR_*") { return $true }
    if ($v -like "PASTE_*") { return $true }
    if ($v -match '(?i)REPLACE_ME') { return $true }
    if ($v -match '(?i)PLACEHOLDER') { return $true }
    if ($v -match '(?i)example\.local') { return $true }
    return $false
}

function Get-ValueState {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [switch]$Secret
    )
    $v = Get-UserEnvValue $Name
    if ([string]::IsNullOrWhiteSpace($v)) { return "MISSING" }
    if (Test-PlaceholderValue $v) { return "PLACEHOLDER" }
    if ($Secret) { return "SET" }
    return $v
}

function Get-KeyReadyState {
    param([Parameter(Mandatory=$true)][string[]]$Names)
    $sawPlaceholder = $false
    foreach ($name in $Names) {
        $v = Get-UserEnvValue $name
        if ([string]::IsNullOrWhiteSpace($v)) { continue }
        if (Test-PlaceholderValue $v) {
            $sawPlaceholder = $true
            continue
        }
        return "Ready"
    }
    if ($sawPlaceholder) { return "Placeholder" }
    return "Missing key"
}

function Test-ValidBaseUrl {
    param([AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "Missing URL" }
    if (Test-PlaceholderValue $Value) { return "Placeholder URL" }
    try {
        $uri = [Uri]$Value
        if ($uri.Scheme -notin @("http", "https")) { return "Invalid URL" }
        if (-not [string]::IsNullOrEmpty($uri.UserInfo)) { return "Invalid URL" }
        return "Ready"
    } catch {
        return "Invalid URL"
    }
}

function Write-Section {
    param([Parameter(Mandatory=$true)][string]$Title)
    Write-Host ""
    Write-Host "=== $Title ==="
}

Write-Section "FORGEREMS USER ENV VARS"
$forgerNames = @(
    "FORGEREMS_ENV",
    "FORGEREMS_RELEASE_CHANNEL",
    "FORGEREMS_LOG_LEVEL",
    "FORGEREMS_VERBOSE_LIVE_LOGS",
    "FORGEREMS_SUPPORT_EMAIL",
    "FORGEREMS_DEEP_SENSOR_MODE",
    "FORGEREMS_GITHUB_OWNER",
    "FORGEREMS_GITHUB_REPO",
    "FORGEREMS_UPDATE_INCLUDE_PRERELEASE",
    "FORGEREMS_UPDATE_TIMEOUT_SECONDS",
    "FORGEREMS_KYRA_MODE",
    "FORGEREMS_KYRA_ONLINE_ENABLED",
    "FORGEREMS_KYRA_API_FIRST",
    "FORGEREMS_KYRA_PROVIDER",
    "FORGEREMS_KYRA_PROVIDER_PRIORITY",
    "FORGEREMS_KYRA_GATEWAY_URL",
    "FORGEREMS_KYRA_GATEWAY_BETA_TOKEN",
    "FORGEREMS_KYRA_GATEWAY_TIMEOUT_SECONDS",
    "FORGEREMS_KYRA_GATEWAY_DAILY_REQUEST_LIMIT",
    "FORGEREMS_KYRA_GATEWAY_SHARE_SYSTEM_CONTEXT",
    "FORGEREMS_KYRA_CONSENSUS_MODE",
    "FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT",
    "FORGEREMS_KYRA_MEMORY_MODE",
    "FORGEREMS_KYRA_MAX_CONTEXT_TURNS",
    "FORGEREMS_KYRA_CONTEXT_MAX_CHARS",
    "FORGEREMS_OPENAI_BASE_URL",
    "FORGEREMS_OPENAI_MODEL",
    "FORGEREMS_OPENAI_API_KEY",
    "FORGEREMS_CUSTOM_PROVIDER_BASE_URL",
    "FORGEREMS_CUSTOM_PROVIDER_MODEL",
    "FORGEREMS_CUSTOM_PROVIDER_API_KEY",
    "FORGEREMS_WEATHER_PROVIDER",
    "FORGEREMS_WEATHER_DEFAULT_LOCATION",
    "FORGEREMS_WEATHER_API_KEY",
    "FORGEREMS_NEWS_PROVIDER",
    "FORGEREMS_NEWS_API_KEY",
    "FORGEREMS_FINANCE_PROVIDER",
    "FORGEREMS_FINANCE_API_KEY",
    "FORGEREMS_CRYPTO_PROVIDER",
    "FORGEREMS_CRYPTO_API_KEY",
    "FORGEREMS_STATS_PROVIDER",
    "FORGEREMS_STATS_API_KEY",
    "FORGEREMS_MARKETPLACE_ENABLED",
    "FORGEREMS_EBAY_ENABLED",
    "FORGEREMS_EBAY_APP_ID",
    "FORGEREMS_EBAY_CERT_ID",
    "FORGEREMS_EBAY_DEV_ID",
    "FORGEREMS_VALUATION_MODE",
    "FORGEREMS_DIAGNOSTICS_EXPORT_DIR",
    "FORGEREMS_DIAGNOSTICS_REDACTION_STRICT",
    "FORGEREMS_ENABLE_DIAGNOSTIC_BUNDLE",
    "FORGEREMS_TELEMETRY_ENABLED",
    "FORGEREMS_CRASH_REPORTING_ENABLED",
    "FORGEREMS_LICENSE_TIER"
)

foreach ($name in $forgerNames | Sort-Object) {
    $secret = $name -match 'KEY|TOKEN|SECRET|CERT|PASSWORD'
    "{0}={1}" -f $name, (Get-ValueState $name -Secret:$secret)
}

Write-Section "GENERIC PROVIDER USER KEYS"
$generic = @(
    "OPENAI_API_KEY",
    "ANTHROPIC_API_KEY",
    "GEMINI_API_KEY",
    "GROQ_API_KEY",
    "OPENROUTER_API_KEY",
    "CEREBRAS_API_KEY",
    "MISTRAL_API_KEY",
    "GITHUB_MODELS_TOKEN",
    "CLOUDFLARE_API_KEY",
    "CLOUDFLARE_ACCOUNT_ID",
    "OLLAMA_BASE_URL",
    "LM_STUDIO_BASE_URL"
)
foreach ($name in $generic) {
    $secret = $name -match 'KEY|TOKEN|SECRET|CERT|PASSWORD|ACCOUNT_ID'
    "{0}={1}" -f $name, (Get-ValueState $name -Secret:$secret)
}

Write-Section "PROVIDER READINESS"
$gatewayUrl = Test-ValidBaseUrl (Get-UserEnvValue "FORGEREMS_KYRA_GATEWAY_URL")
$gatewayToken = Get-KeyReadyState @("FORGEREMS_KYRA_GATEWAY_BETA_TOKEN")
if ($gatewayUrl -ne "Ready") { "ForgerEMS Gateway: $gatewayUrl" }
elseif ($gatewayToken -eq "Ready") { "ForgerEMS Gateway: Ready" }
elseif ($gatewayToken -eq "Placeholder") { "ForgerEMS Gateway: Placeholder token" }
else { "ForgerEMS Gateway: Missing beta token" }

$openAi = Get-KeyReadyState @("FORGEREMS_OPENAI_API_KEY", "OPENAI_API_KEY")
"OpenAI-compatible: $openAi"

$customBase = Get-UserEnvValue "FORGEREMS_CUSTOM_PROVIDER_BASE_URL"
$customModel = Get-UserEnvValue "FORGEREMS_CUSTOM_PROVIDER_MODEL"
$customUrl = Test-ValidBaseUrl $customBase
$customKey = Get-KeyReadyState @("FORGEREMS_CUSTOM_PROVIDER_API_KEY")
if ($customKey -eq "Missing key" -and $customBase -match '(?i)openrouter\.ai') { $customKey = Get-KeyReadyState @("OPENROUTER_API_KEY") }
if ($customKey -eq "Missing key" -and $customBase -match '(?i)groq\.com') { $customKey = Get-KeyReadyState @("GROQ_API_KEY") }
if ($customUrl -ne "Ready") { "Custom: $customUrl" }
elseif ([string]::IsNullOrWhiteSpace($customModel)) { "Custom: Missing model" }
elseif (Test-PlaceholderValue $customModel) { "Custom: Placeholder model" }
elseif ($customKey -ne "Ready") { "Custom: $customKey" }
else { "Custom: Ready" }

"OpenRouter: $(Get-KeyReadyState @("OPENROUTER_API_KEY"))"
"Groq: $(Get-KeyReadyState @("GROQ_API_KEY"))"
"Gemini: $(Get-KeyReadyState @("FORGEREMS_GEMINI_API_KEY", "GEMINI_API_KEY"))"
"Anthropic: $(Get-KeyReadyState @("FORGEREMS_ANTHROPIC_API_KEY", "ANTHROPIC_API_KEY"))"
"Mistral: $(Get-KeyReadyState @("MISTRAL_API_KEY"))"
"Cerebras: $(Get-KeyReadyState @("CEREBRAS_API_KEY"))"
"GitHub Models: $(Get-KeyReadyState @("GITHUB_MODELS_TOKEN"))"
$cfKey = Get-KeyReadyState @("CLOUDFLARE_API_KEY")
$cfAcct = Get-KeyReadyState @("CLOUDFLARE_ACCOUNT_ID")
"Cloudflare: $(if ($cfKey -eq "Ready" -and $cfAcct -eq "Ready") { "Ready" } elseif ($cfKey -ne "Ready") { $cfKey } else { "Missing account id" })"
"LM Studio: Configured/Local server not checked"
"Ollama: Configured/Local server not checked"

Write-Section "LIVE TOOLS READINESS"
$weatherProvider = Get-UserEnvValue "FORGEREMS_WEATHER_PROVIDER"
if ([string]::IsNullOrWhiteSpace($weatherProvider) -or $weatherProvider -eq "openmeteo") {
    "Weather: Ready/No key needed (Open-Meteo)"
} else {
    "Weather: $(Get-KeyReadyState @("FORGEREMS_WEATHER_API_KEY"))"
}

"News: $(Get-KeyReadyState @("FORGEREMS_NEWS_API_KEY"))"
$financeProvider = Get-UserEnvValue "FORGEREMS_FINANCE_PROVIDER"
if ([string]::IsNullOrWhiteSpace($financeProvider)) { $financeProvider = "finnhub" }
if ($financeProvider -notin @("finnhub", "alphavantage", "fmp")) {
    "Finance: Mismatch/unsupported provider"
} else {
    "Finance: $(Get-KeyReadyState @("FORGEREMS_FINANCE_API_KEY")) ($financeProvider)"
}

$cryptoProvider = Get-UserEnvValue "FORGEREMS_CRYPTO_PROVIDER"
if ([string]::IsNullOrWhiteSpace($cryptoProvider) -or $cryptoProvider -eq "coingecko") {
    "Crypto: Ready/No key needed (CoinGecko)"
} else {
    "Crypto: Shell/unsupported provider"
}

"Stats: $(Get-KeyReadyState @("FORGEREMS_STATS_API_KEY")) (FRED shell)"

$ebayEnabled = Get-UserEnvValue "FORGEREMS_EBAY_ENABLED"
if ($ebayEnabled -ne "true") {
    "eBay: Disabled/Future"
} else {
    $app = Get-KeyReadyState @("FORGEREMS_EBAY_APP_ID")
    $cert = Get-KeyReadyState @("FORGEREMS_EBAY_CERT_ID")
    $dev = Get-KeyReadyState @("FORGEREMS_EBAY_DEV_ID")
    if ($app -eq "Ready" -and $cert -eq "Ready" -and $dev -eq "Ready") { "eBay: Ready/Future provider shell" } else { "eBay: Missing key/Future" }
}
