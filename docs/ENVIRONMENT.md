# ForgerEMS Environment and Local Configuration

This file documents supported and reserved ForgerEMS environment variables for v1.2.1 Public Preview. Do not put real secrets in source files, screenshots, support emails, or issue reports. Use `.env.example` as a placeholder-only reference.

General app configuration is local-first. Environment variables are optional operator/developer overrides unless stated otherwise.

Placeholder values such as `REPLACE_ME`, `REPLACE_WITH_BETA_ACCESS_TOKEN`, `YOUR_*`, `PASTE_*`, `REPLACE_MODEL_NAME`, `local-model-name`, `model-name`, `example.local`, `sk-REPLACE_ME`, `changeme`, and `TODO` are treated as **not configured**. They are examples only and must not make Kyra mark a provider ready.

For installed-app testing, persistent variables are Windows **User** environment variables. Use `tools/show-forgerems-env-status.ps1` to inspect User env readiness without printing raw secrets.

Deep Sensor Mode has explicit consent precedence:

1. `FORGEREMS_DEEP_SENSOR_MODE` environment variable
2. user setting under `%LOCALAPPDATA%\ForgerEMS\settings\deep-sensor-mode.txt`
3. installer default `HKLM\Software\ForgerEMS\DeepSensorMode`
4. built-in default `Off`

## Required Dev/Build Tools

| Tool | Required? | Purpose | Where used | Validation |
|------|-----------|---------|------------|------------|
| .NET SDK 8 | Required | Restore, build, test, publish WPF app | `ForgerEMS.sln`, `tools/build-release.ps1`, CI | `dotnet --info` |
| Windows PowerShell 5.1 | Required | Backend scripts and compatibility path | `backend/*.ps1`, `tools/*.ps1` | `powershell -NoProfile -Command "$PSVersionTable.PSVersion"` |
| PowerShell 7 (`pwsh`) | Optional | Developer convenience and CI shell parity | GitHub Actions uses `pwsh` | `pwsh -NoProfile -Command "$PSVersionTable.PSVersion"` |
| Inno Setup 6 / `iscc` | Required for installer builds | Compile `ForgerEMS-Setup-*.exe` | `installer/ForgerEMS.iss`, `tools/build-release.ps1` | `iscc /?` |
| Git | Required for release/CI workflows | Tags, release scripts, audit trail | `.github/workflows/release.yml`, local release flow | `git --version` |
| GitHub CLI (`gh`) | Optional | Operator release/debug helper only | Developer workflows, not app runtime | `gh --version` |
| NuGet | Required through .NET SDK | Package restore | `LibreHardwareMonitorLib`, `System.Management`, test packages | `dotnet restore ForgerEMS.sln` |

## Runtime Dependencies and Paths

| Item | Required? | Purpose | Notes |
|------|-----------|---------|-------|
| Windows 10/11 x64 | Required | WPF desktop runtime and Windows diagnostics | Public preview targets Windows technician machines. |
| Self-contained .NET publish | Included in release | App runs without user-installed .NET Desktop Runtime for packaged builds | `src/ForgerEMS.Wpf/ForgerEMS.Wpf.csproj` sets `SelfContained=true`. |
| `System.Management` NuGet package | Required | WMI/CIM and USB/System Intelligence collectors | PackageReference in WPF project. |
| `LibreHardwareMonitorLib.dll` | Optional bundled provider | Local read-only deep sensor provider | Packaged under `providers/sensors/` when available. |
| `providers/sensors/THIRD-PARTY-NOTICES.txt` | Required when provider packaged | Legal notice | Included in installer/portable output. |
| `providers/sensors/LICENSES/` | Required when provider packaged | MPL and third-party license files | Do not remove from release bundles. |
| `%LOCALAPPDATA%\ForgerEMS\Runtime\reports` | Runtime local data | System Intelligence JSON/Markdown | Review before sharing. |
| `%LOCALAPPDATA%\ForgerEMS\Runtime\logs` and `%LOCALAPPDATA%\ForgerEMS\logs` | Runtime local data | App/session diagnostics | Support bundles redact where supported. |
| `%LOCALAPPDATA%\ForgerEMS\settings` | Runtime local settings | User settings such as Deep Sensor Mode | Do not commit. |

## Environment Variables

| Variable | Required? | Default | Used by | Purpose | Safe to expose? | Notes |
|----------|-----------|---------|---------|---------|-----------------|-------|
| `FORGEREMS_ENV` | No | `Production` | WPF app, debug UI gates | Deployment label (`Production`, `Beta`, `Development`) | Yes | Development enables extra diagnostics in a few areas. |
| `FORGEREMS_RELEASE_CHANNEL` | No | `preview` | WPF app/update/support bundle | Marketing/update channel hint | Yes | Examples: `stable`, `beta`, `rc`, `preview`. |
| `FORGEREMS_PORTABLE_MODE` | No | `false` | WPF config | Portable layout hint/reserved | Yes | Not a secret. |
| `FORGEREMS_LOG_LEVEL` | No | `Info` | WPF config | Log verbosity hint | Yes | Avoid `Trace` in shared screenshots if logs include private paths. |
| `FORGEREMS_VERBOSE_LIVE_LOGS` | No | `false` | WPF UI | Verbose live logs | Yes | May reveal more local detail. |
| `FORGEREMS_SUPPORT_EMAIL` | No | `ForgerDigitalSolutions@outlook.com` | WPF/support copy | Support contact override | Yes | Not a credential. |
| `FORGEREMS_BACKEND_ROOT` | No | bundled/repo discovery | Backend discovery | Override backend script root | Treat as private path | Do not share full private path in public reports. |
| `FORGEREMS_GITHUB_OWNER` | No | `Forger-Digital-Solutions` | Update checker | GitHub owner for releases | Yes | Public repo segment. |
| `FORGEREMS_GITHUB_REPO` | No | `ForgerEMS` | Update checker | GitHub repo for releases | Yes | Public repo segment. |
| `FORGEREMS_UPDATE_CHANNEL` | No | release channel | Update UI/future narrowing | Update channel hint | Yes | Reserved; UI settings remain primary. |
| `FORGEREMS_UPDATE_INCLUDE_PRERELEASE` | No | `true` | Update config | Include prerelease hint | Yes | Reserved; in-app toggle remains primary. |
| `FORGEREMS_UPDATE_USER_AGENT` | No | `ForgerEMS` | GitHub HTTP client | User-Agent override | Yes | When unset or left at the short default, the app sends `ForgerEMS/{version} (+https://github.com/{owner}/{repo})`. Do not include secrets. |
| `FORGEREMS_GITHUB_TOKEN` | Optional secret | empty | GitHub HTTP client | Raises API rate limits for update checks | Never expose | Operator/dev only. PAT with **public_repo** read scope. Placeholders (`REPLACE_ME`, etc.) are ignored. Not required for public releases. |
| `FORGEREMS_UPDATE_TIMEOUT_SECONDS` | No | `20` | GitHub HTTP client | Release list timeout | Yes | Clamped 5-120. |
| `FORGEREMS_KYRA_MODE` | No | `hybrid` | Kyra config | Mode hint | Yes | Offline/local is available without keys. |
| `FORGEREMS_KYRA_PROVIDER` | No | `offline` | Kyra config | Provider hint | Yes | Examples: `forgerems-gateway`, `offline`, `openai-compatible`, `lmstudio`, `ollama`. |
| `FORGEREMS_KYRA_ONLINE_ENABLED` | No | `false` | Kyra config | Gate online provider use | Yes | Online providers may send prompt/context to configured provider. |
| `FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT` | No | `false` | Kyra config | Allow sanitized system context sharing | Yes | Keep off unless user/operator intentionally enables. |
| `FORGEREMS_KYRA_REQUIRE_LOCAL_FACTS` | No | `true` | Kyra config | Prefer grounded local reports | Yes | Not a secret. |
| `FORGEREMS_KYRA_API_FIRST` | No | `true` | Kyra routing | Try configured API providers before Local Kyra when mode/privacy allow | Yes | Offline fallback still remains available. |
| `FORGEREMS_KYRA_PROVIDER_PRIORITY` | No | `forgerems-gateway,openai-compatible,custom,openrouter,groq,gemini,anthropic,mistral,cerebras,github-models,cloudflare,lmstudio,ollama,offline` | Kyra routing | Provider fallback order | Yes | Does not call every provider; missing keys are skipped. |
| `FORGEREMS_KYRA_GATEWAY_URL` | No | empty | Kyra Gateway | HTTPS gateway endpoint | Yes, host only in UI | Beta gateway URL. Rejects placeholders, invalid URLs, and embedded credentials. |
| `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN` | Gateway secret | empty | Kyra Gateway | Revocable beta access token | Never expose | Not a provider API key. Redacted in UI/logs/env status. |
| `FORGEREMS_KYRA_GATEWAY_TIMEOUT_SECONDS` | No | `60` | Kyra Gateway | Gateway timeout | Yes | Clamped 3-120. |
| `FORGEREMS_KYRA_GATEWAY_DAILY_REQUEST_LIMIT` | No | empty | Kyra Gateway | Optional local client hint | Yes | Server-side limits remain authoritative. |
| `FORGEREMS_KYRA_GATEWAY_SHARE_SYSTEM_CONTEXT` | No | `false` | Kyra Gateway | Gateway-specific sanitized context gate | Yes | Requires `FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT=true` too. |
| `FORGEREMS_KYRA_PROVIDER_TIMEOUT_SECONDS` | No | `60` | Kyra routing | Provider call timeout | Yes | Clamped 3-120. |
| `FORGEREMS_KYRA_CONSENSUS_MODE` | No | `false` | Kyra routing | Future multi-provider comparison gate | Yes | Disabled to avoid surprise token/API usage. |
| `FORGEREMS_KYRA_MEMORY_MODE` | No | `session` | Kyra memory | Memory mode hint | Yes | Session memory is local. |
| `FORGEREMS_KYRA_PERSIST_MEMORY` | No | `false` | Kyra memory | Persist sanitized memory locally | Yes | Secrets are redacted before persistence. |
| `FORGEREMS_KYRA_MAX_CONTEXT_TURNS` | No | `100` | Kyra config | Conversation context depth | Yes | Clamped 1-200. |
| `FORGEREMS_KYRA_CONTEXT_MAX_CHARS` | No | `12000` | Kyra config | Prompt/context character budget | Yes | Sanitized before online use. |
| `FORGEREMS_KYRA_PERSONALITY` | No | `bubbly-tech` | Kyra tone | Personality profile hint | Yes | Future values may include professional/minimal/debug. |
| `FORGEREMS_OPENAI_BASE_URL` | No | empty | Kyra OpenAI-compatible path | Base URL override | Usually yes | Do not include embedded credentials in URLs. |
| `FORGEREMS_OPENAI_MODEL` | No | empty | Kyra OpenAI-compatible path | Model override | Yes | Not a secret. |
| `FORGEREMS_OPENAI_API_KEY` | Optional secret | empty | Kyra OpenAI-compatible path | API key presence check | Never expose | Prefer secret manager/user env. |
| `FORGEREMS_LMSTUDIO_BASE_URL` | No | `http://localhost:1234/v1` | Kyra local provider | LM Studio local server URL | Yes | Localhost by default. |
| `FORGEREMS_LMSTUDIO_MODEL` | No | empty | Kyra local provider | LM Studio model name | Yes | Not a secret. |
| `FORGEREMS_OLLAMA_BASE_URL` | No | `http://localhost:11434` | Kyra local provider | Ollama local server URL | Yes | Localhost by default. |
| `FORGEREMS_OLLAMA_MODEL` | No | empty | Kyra local provider | Ollama model name | Yes | Not a secret. |
| `FORGEREMS_ANTHROPIC_API_KEY` | Optional secret | empty | Kyra provider shell | Anthropic BYOK key | Never expose | Reserved/BYOK path. |
| `FORGEREMS_ANTHROPIC_MODEL` | No | empty | Kyra provider shell | Anthropic model | Yes | Not a secret. |
| `FORGEREMS_GEMINI_API_KEY` | Optional secret | empty | Kyra provider shell | Gemini BYOK key | Never expose | Reserved/BYOK path. |
| `FORGEREMS_GEMINI_MODEL` | No | empty | Kyra provider shell | Gemini model | Yes | Not a secret. |
| `FORGEREMS_CUSTOM_PROVIDER_BASE_URL` | No | empty | Kyra custom provider | OpenAI-compatible custom URL | Usually yes | Do not include embedded credentials. |
| `FORGEREMS_CUSTOM_PROVIDER_MODEL` | No | empty | Kyra custom provider | Custom provider model | Yes | Not a secret. |
| `FORGEREMS_CUSTOM_PROVIDER_API_KEY` | Optional secret | empty | Kyra custom provider | API key presence check | Never expose | Do not log raw. |
| `FORGEREMS_WEATHER_PROVIDER` | No | empty/openmeteo | Kyra live tools | Weather provider hint | Yes | Open-Meteo can work without a key when enabled. |
| `FORGEREMS_WEATHER_API_KEY` | Optional secret | empty | Kyra live tools | Weather provider key | Never expose | Optional for providers that require keys. |
| `FORGEREMS_WEATHER_DEFAULT_LOCATION` | No | empty | Kyra live tools | Default coarse weather location | Treat as personal location | Prefer city/ZIP, not exact address. |
| `FORGEREMS_NEWS_PROVIDER` | No | empty | Kyra live tools | News provider hint | Yes | Shell/config path; do not invent live news. |
| `FORGEREMS_NEWS_API_KEY` | Optional secret | empty | Kyra live tools | News provider key | Never expose | Optional future/current provider. |
| `FORGEREMS_FINANCE_PROVIDER` | No | empty/`finnhub` | Kyra live tools | Finance/stocks provider hint | Yes | Supported values for stock quotes: `finnhub`, `alphavantage`, `fmp`. |
| `FORGEREMS_FINANCE_API_KEY` | Optional secret | empty | Kyra live tools | Finance provider key | Never expose | Optional future/current provider. |
| `FORGEREMS_CRYPTO_PROVIDER` | No | empty/coingecko | Kyra live tools | Crypto provider hint | Yes | CoinGecko no-key path may be used when enabled. |
| `FORGEREMS_CRYPTO_API_KEY` | Optional secret | empty | Kyra live tools | Crypto provider key | Never expose | Optional future provider. |
| `FORGEREMS_STATS_PROVIDER` | No | empty/`fred` | Kyra live tools | Statistics/economic data provider hint | Yes | FRED status is shell/limited in this build; Kyra will not invent economic stats. |
| `FORGEREMS_STATS_API_KEY` | Optional secret | empty | Kyra live tools | Statistics/economic data provider key | Never expose | Optional future/current provider. |
| `FORGEREMS_DIAGNOSTICS_EXPORT_DIR` | No | empty | Support/export | Default export folder | Treat as private path | Redact in public reports. |
| `FORGEREMS_DIAGNOSTICS_REDACTION_STRICT` | No | `true` | Diagnostics | Redaction mode hint | Yes | Reserved. |
| `FORGEREMS_ENABLE_DIAGNOSTIC_BUNDLE` | No | `true` | WPF UI | Enable/disable support bundle command | Yes | Not a secret. |
| `FORGEREMS_DEEP_SENSOR_MODE` | No | `Off` | System Intelligence | Deep Sensor Mode override | Yes | Accepted: `Off`, `ReadOnly`; `AdminReadOnly` future. |
| `FORGEREMS_MARKETPLACE_ENABLED` | No | `false` | FlipValue/provider shell | Marketplace provider gate | Yes | Future/disabled. |
| `FORGEREMS_EBAY_ENABLED` | No | `false` | FlipValue/provider shell | eBay provider gate | Yes | Future/disabled. |
| `FORGEREMS_EBAY_APP_ID` | Future secret-ish | empty | Future eBay provider | eBay client/app id | Do not publish casually | Placeholder only today. |
| `FORGEREMS_EBAY_CERT_ID` | Future secret | empty | Future eBay provider | eBay certificate/client secret | Never expose | Placeholder only today. |
| `FORGEREMS_EBAY_DEV_ID` | Future secret-ish | empty | Future eBay provider | eBay developer id | Do not publish casually | Placeholder only today. |
| `FORGEREMS_MARKETPLACE_REGION` | No | empty | Future marketplace | Region hint | Yes | Placeholder/reserved. |
| `FORGEREMS_VALUATION_MODE` | No | `offline` | FlipValue | Valuation mode hint | Yes | `offline`, `hybrid`, `online`. |
| `FORGEREMS_TELEMETRY_ENABLED` | No | `false` | Config/docs | Telemetry gate | Yes | No telemetry endpoint is active by default. |
| `FORGEREMS_CRASH_REPORTING_ENABLED` | No | `false` | Config/docs | Crash reporting gate | Yes | No crash upload by default. |
| `FORGEREMS_LICENSE_TIER` | No | empty/PublicPreview | Local preview feature gating | Local entitlement hint | Yes | No cloud activation server. |
| `FORGEREMS_USB_MAPPING_DEBUG_UI` | No | unset | USB mapping wizard | Show development diagnostics | Yes | Keep unset for normal beta UI. |
| `FORGEREMS_DEV_PROVIDER_SETTINGS` | No | unset | Kyra settings UI | Expose raw session key fields in tester/dev mode | Yes | Does not store keys by itself. |
| `FORGEREMS_FORCE_DOTNET_HASH` | No | unset | Backend hash helper | Force .NET SHA256 fallback for tests | Yes | Accepted `1`/`true`. |
| `LOCALAPPDATA` | OS-provided | Windows value | Runtime paths | Local app data root | Private path | Redact full path in public logs. |
| `SystemDrive` | OS-provided | Windows value | BitLocker/security scan | OS drive selection | Usually yes | Avoid leaking private path context. |
| `PSHOME` | OS-provided | Windows value | Tests | PowerShell discovery | Usually yes | Not a secret. |

Tests also create temporary `FORGEREMS_UT_*` variables. They are test-only and not user configuration.

## Provider-Specific Kyra Variables

These are optional operator/BYOK variables used by Kyra Advanced providers. Offline Kyra requires none of them.

| Variable | Provider | Required? | Sends data off-device? | Safe to expose? |
|----------|----------|-----------|------------------------|-----------------|
| `FORGEREMS_KYRA_GATEWAY_URL` | ForgerEMS Beta Gateway | Optional | Yes, to the gateway | Host only; do not include credentials |
| `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN` | ForgerEMS Beta Gateway | Optional gateway token | Yes, to the gateway | Never expose; revocable beta token |
| `OPENAI_API_KEY` | OpenAI/OpenAI-compatible | Optional | Yes, when provider enabled | Never expose |
| `ANTHROPIC_API_KEY` | Anthropic | Optional | Yes, when provider enabled | Never expose |
| `GEMINI_API_KEY` | Google Gemini | Optional | Yes, when provider enabled | Never expose |
| `GROQ_API_KEY` | Groq | Optional | Yes, when provider enabled | Never expose |
| `OPENROUTER_API_KEY` | OpenRouter | Optional | Yes, when provider enabled | Never expose |
| `CEREBRAS_API_KEY` | Cerebras | Optional | Yes, when provider enabled | Never expose |
| `MISTRAL_API_KEY` | Mistral | Optional | Yes, when provider enabled | Never expose |
| `GITHUB_MODELS_TOKEN` | GitHub Models | Optional | Yes, when provider enabled | Never expose |
| `FORGEREMS_GITHUB_MODELS_DEFAULT_MODEL` | GitHub Models | Optional | Yes, when provider enabled | Model ID only; example `openai/gpt-5` |
| `FORGEREMS_GITHUB_MODELS_FAST_MODEL` | GitHub Models | Optional | Yes, when provider enabled | Model ID only; example `deepseek/DeepSeek-V3-0324` |
| `FORGEREMS_GITHUB_MODELS_ALT_MODEL` | GitHub Models | Optional | Yes, when provider enabled | Model ID only; example `meta/Llama-4-Scout-17B-16E-Instruct` |
| `CLOUDFLARE_API_KEY` | Cloudflare Workers AI | Optional | Yes, when provider enabled | Never expose |
| `CLOUDFLARE_ACCOUNT_ID` | Cloudflare Workers AI | Optional | Yes, when provider enabled | Treat as sensitive account metadata |

Session API keys entered in Kyra Advanced are memory-only and override environment variables for that app session. Do not paste real provider keys into docs, screenshots, tickets, or support email.

ForgerEMS Beta Gateway lets beta builds use real Kyra API time without shipping owner provider secrets. The desktop app sends prompts to `FORGEREMS_KYRA_GATEWAY_URL` with a revocable beta token. Provider API keys live only in the server-side gateway environment, and gateway usage limits may apply. Local/offline fallback remains available when the gateway is missing, rate-limited, unavailable, or disabled.

Current beta gateway has token validation and request-size limits; durable per-token/per-IP rate limiting should be enabled before broad public beta.

GitHub Models uses one `GITHUB_MODELS_TOKEN` for all configured model IDs. The `DEFAULT`, `FAST`, and `ALT` variables are model choices, not separate secrets. After changing Windows User environment variables, restart ForgerEMS or use the provider/env refresh path so Kyra sees the new values.

## External Network Access

| Integration | Trigger | Key required? | Data sent | Failure behavior |
|-------------|---------|---------------|-----------|------------------|
| GitHub Releases update checker | User/app update check | No | Repo owner/repo, User-Agent | Reports unavailable/update error; no install without user action. |
| Managed download catalog | USB Builder/Toolkit managed downloads | No | URL request to official source/manifest URL | Falls back or marks item manual/failed; checksum verification remains required where configured. |
| Backend revalidation | Operator runs `Verify-VentoyCore.ps1 -RevalidateManagedDownloads` | No | HEAD/HTTP requests to official URLs | Writes local revalidation artifacts. |
| Kyra ForgerEMS Gateway | Beta gateway mode enabled/configured | Gateway beta token, not provider API keys | Prompt plus optional sanitized context if both context-sharing gates are enabled | Falls back to BYOK/local/offline when unavailable or limited. |
| Kyra online providers | User/operator enables online provider and sends prompt | Provider-dependent | Prompt plus optional sanitized context if enabled | Falls back/error message; Offline Local remains available. |
| LM Studio/Ollama | User runs local server/provider | No cloud key | Localhost requests | Provider shown unavailable if local service is stopped. |
| FlipValue/eBay/marketplace shells | Future/disabled by default | Future | None today unless future provider enabled | Offline heuristic fallback remains. |

## Secret Handling Rules

- Secret variables are marked **Never expose** above.
- Do not commit `.env`, local settings, token JSON, certificates, private keys, product keys, or support bundles.
- Use `REPLACE_ME` placeholders only in docs/examples.
- Support reports should be redacted and reviewed before sharing.
- If a real key is committed, rotate it immediately and remove it from history according to your repo policy.

## Release Packaging Notes

Release bundles should include:

- `ForgerEMS.exe`
- bundled `backend/` scripts and manifests
- `manifests/`
- `providers/sensors/LibreHardwareMonitorLib.dll` when packaged
- `providers/sensors/THIRD-PARTY-NOTICES.txt`
- `providers/sensors/LICENSES/`
- `release.json`
- `CHECKSUMS.sha256`
- generated `DOWNLOAD_BETA.txt`
- generated ZIP contents: `START_HERE.bat`, `VERIFY.txt`, installer, `release.json`, and package checksums
- docs/legal/privacy/FAQ files where installer/portable packaging references them

## Local Secret Audit

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\audit-config-and-secrets.ps1
```

The script is local-only and redacts secret-like values. It does not upload anything.
