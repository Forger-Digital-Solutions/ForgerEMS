[CmdletBinding()]
param(
    [ValidateSet("openrouter", "groq", "none")]
    [string]$CustomProvider = "none"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Is-Placeholder {
    param([AllowNull()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $true }
    $v = $Value.Trim()
    return $v -match '^(?i:REPLACE_ME|REPLACE_WITH_BETA_ACCESS_TOKEN|REPLACE_MODEL_NAME|local-model-name|model-name|changeme|TODO)$' `
        -or $v -like "REPLACE_*" `
        -or $v -like "YOUR_*" `
        -or $v -like "PASTE_*"
}

function Set-UserEnvIfReal {
    param(
        [Parameter(Mandatory=$true)][string]$Name,
        [AllowNull()][string]$Value
    )

    if (Is-Placeholder $Value) {
        [Environment]::SetEnvironmentVariable($Name, $null, "User")
        return
    }

    [Environment]::SetEnvironmentVariable($Name, $Value.Trim(), "User")
}

# Example-only placeholders: this file is owner/dev guidance and is not the beta default setup.
$gatewayUrl = "https://REPLACE_ME.workers.dev"
$gatewayToken = "REPLACE_WITH_BETA_ACCESS_TOKEN"
$openRouterKey = "REPLACE_ME"
$groqKey = "REPLACE_ME"
$customOpenRouterBase = "https://openrouter.ai/api/v1"
$customGroqBase = "https://api.groq.com/openai/v1"
$customModel = "REPLACE_MODEL_NAME"

Set-UserEnvIfReal "FORGEREMS_KYRA_GATEWAY_URL" $gatewayUrl
Set-UserEnvIfReal "FORGEREMS_KYRA_GATEWAY_BETA_TOKEN" $gatewayToken

switch ($CustomProvider) {
    "openrouter" {
        Set-UserEnvIfReal "FORGEREMS_CUSTOM_PROVIDER_BASE_URL" $customOpenRouterBase
        Set-UserEnvIfReal "FORGEREMS_CUSTOM_PROVIDER_MODEL" $customModel
        Set-UserEnvIfReal "OPENROUTER_API_KEY" $openRouterKey
        [Environment]::SetEnvironmentVariable("GROQ_API_KEY", $null, "User")
    }
    "groq" {
        Set-UserEnvIfReal "FORGEREMS_CUSTOM_PROVIDER_BASE_URL" $customGroqBase
        Set-UserEnvIfReal "FORGEREMS_CUSTOM_PROVIDER_MODEL" $customModel
        Set-UserEnvIfReal "GROQ_API_KEY" $groqKey
        [Environment]::SetEnvironmentVariable("OPENROUTER_API_KEY", $null, "User")
    }
    default {
        [Environment]::SetEnvironmentVariable("FORGEREMS_CUSTOM_PROVIDER_BASE_URL", $null, "User")
        [Environment]::SetEnvironmentVariable("FORGEREMS_CUSTOM_PROVIDER_MODEL", $null, "User")
    }
}

Write-Host "Owner/dev example env script ran. Placeholder values were skipped or cleared."
