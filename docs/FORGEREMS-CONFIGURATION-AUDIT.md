# ForgerEMS Configuration, Provider, and Secret Audit

Audit date: 2026-05-05  
Scope: source-controlled repo text files, scripts, manifests, workflows, installer sources, tests, docs, provider notices, and generated-template sources. Generated `bin/`, `obj/`, `dist/`, `release/current/`, and current Ventoy release output were excluded from secret pattern scans unless specifically noted.

This document is an inventory and operator checklist. It must not contain real API keys, tokens, passwords, product keys, private paths, service tags, serials, or private user data.

## 1. Required Build Tools

| Tool | Required? | Purpose | Where used | Install/setup note | Validation command |
|------|-----------|---------|------------|--------------------|--------------------|
| .NET SDK 8 | Required | Restore, build, test, publish WPF app | `ForgerEMS.sln`, `src/`, `tests/`, `tools/build-release.ps1` | Install .NET 8 SDK; repo pins `8.0.419` with roll-forward in `global.json`. | `dotnet --info` |
| Windows PowerShell 5.1 | Required | Backend scripts and installed Windows compatibility | `backend/*.ps1`, `tools/*.ps1` | Built into supported Windows versions. | `powershell -NoProfile -Command "$PSVersionTable.PSVersion"` |
| PowerShell 7 (`pwsh`) | Optional | Developer/CI shell parity | `.github/workflows/release.yml`, optional local workflows | Install from Microsoft if desired. | `pwsh -NoProfile -Command "$PSVersionTable.PSVersion"` |
| Inno Setup 6 / `iscc` | Required for installer | Compile the Windows installer | `installer/ForgerEMS.iss`, `tools/build-release.ps1` | CI installs via Chocolatey; local builders install Inno Setup 6. | `iscc /?` |
| Git | Required for release work | Tags, release workflow, source control | `.github/workflows/release.yml`, release scripts | Install Git for Windows. | `git --version` |
| GitHub CLI (`gh`) | Optional | Operator/dev release debugging | Developer-only workflows | Not required by the app. | `gh --version` |
| NuGet via .NET SDK | Required | Package restore | `LibreHardwareMonitorLib`, `System.Management`, test packages | Uses per-project PackageReference; no central package management found. | `dotnet restore ForgerEMS.sln` |

## 2. Runtime Dependencies

| Dependency/path | Required? | Purpose | Notes |
|-----------------|-----------|---------|-------|
| Windows 10/11 x64 | Required | WPF app and Windows diagnostics | Read-only diagnostics depend on Windows APIs and permissions. |
| Self-contained .NET publish | Included in release | Run packaged app without separate Desktop Runtime install | `SelfContained=true`, `RuntimeIdentifier=win-x64`. |
| `System.Management` | Required package | WMI/System.Management USB and hardware collection | PackageReference in WPF project. |
| `LibreHardwareMonitorLib.dll` | Optional bundled provider | Deep Sensor Mode local read-only sensors | Packaged under `providers/sensors/LibreHardwareMonitorLib.dll` when available. |
| `providers/sensors/THIRD-PARTY-NOTICES.txt` | Required when provider packaged | Third-party sensor notice | Ships with installer/portable. |
| `providers/sensors/LICENSES/` | Required when provider packaged | MPL and third-party license texts | Do not remove from release bundles. |
| `%LOCALAPPDATA%\ForgerEMS\Runtime\reports` | Runtime local data | System Intelligence JSON/Markdown | Redact/review before sharing. |
| `%LOCALAPPDATA%\ForgerEMS\Runtime\logs` and `%LOCALAPPDATA%\ForgerEMS\logs` | Runtime local data | App/backend diagnostics | Support bundle redacts where supported. |
| `%LOCALAPPDATA%\ForgerEMS\settings` | Runtime local settings | Deep Sensor Mode user setting and future user settings | Do not commit. |

## 3. Environment Variables

Full variable details are maintained in [ENVIRONMENT.md](ENVIRONMENT.md). Summary:

| Variable family | Required? | Default | Used by | Purpose | Safe to expose? |
|-----------------|-----------|---------|---------|---------|-----------------|
| `FORGEREMS_ENV`, release/log/support variables | No | safe production/preview defaults | App/config/support UI | Deployment labels and logging hints | Mostly yes; private paths should be redacted |
| `FORGEREMS_GITHUB_*`, `FORGEREMS_UPDATE_*` | No | public repo defaults | Update checker | GitHub Releases metadata lookup | Yes |
| `FORGEREMS_KYRA_*` | No | offline/hybrid safe defaults | Kyra | Provider/mode/context gates | Yes, except context choices can reveal policy |
| `FORGEREMS_OPENAI_*`, `FORGEREMS_ANTHROPIC_*`, `FORGEREMS_GEMINI_*`, `FORGEREMS_CUSTOM_PROVIDER_*` | Optional | empty | Kyra provider shells | Optional cloud/BYOK providers | API key variables: never expose |
| `FORGEREMS_LMSTUDIO_*`, `FORGEREMS_OLLAMA_*` | Optional | localhost defaults | Kyra local providers | Local AI server endpoints/models | Usually safe; avoid embedded credentials |
| `GEMINI_API_KEY`, `GROQ_API_KEY`, `OPENROUTER_API_KEY`, `CEREBRAS_API_KEY`, `MISTRAL_API_KEY`, `GITHUB_MODELS_TOKEN`, `CLOUDFLARE_API_KEY`, `CLOUDFLARE_ACCOUNT_ID`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY` | Optional | unset | Kyra Advanced providers | Provider-specific BYOK/session/env config | Keys/tokens: never expose |
| `FORGEREMS_DEEP_SENSOR_MODE` | No | `Off` | System Intelligence | Deep Sensor Mode override | Value is safe |
| `FORGEREMS_MARKETPLACE_*`, `FORGEREMS_EBAY_*`, `FORGEREMS_VALUATION_MODE` | Future/optional | disabled/offline | FlipValue provider shells | Marketplace/comps provider preparation | eBay secrets: never expose |
| `FORGEREMS_TELEMETRY_ENABLED`, `FORGEREMS_CRASH_REPORTING_ENABLED` | No | `false` | Config/docs | Future/disabled telemetry/crash gates | Yes |
| `FORGEREMS_LICENSE_TIER` | No | PublicPreview behavior | Local preview gating | Local entitlement hint | Yes |
| `FORGEREMS_FORCE_DOTNET_HASH` | No | unset | Backend hash tests | Force .NET SHA256 fallback | Yes |
| `FORGEREMS_USB_MAPPING_DEBUG_UI`, `FORGEREMS_DEV_PROVIDER_SETTINGS` | No | unset | Dev/debug UI | Expose diagnostics/dev key fields | Yes, but keep disabled in beta UI |
| `LOCALAPPDATA`, `SystemDrive`, `PSHOME` | OS-provided | OS value | Runtime paths/scans/tests | Windows path discovery | Redact private paths |

## 4. API and Provider Settings

| Provider/integration | Status | Required settings | Sends data off-device? | Offline fallback? | Privacy note |
|----------------------|--------|-------------------|------------------------|-------------------|--------------|
| Kyra Offline Local | Active default | None | No | N/A | Uses local rules/reports only. |
| LM Studio | Active optional local provider | `FORGEREMS_LMSTUDIO_BASE_URL`, model | No cloud by default; localhost request only | Yes | User runs local server. |
| Ollama | Active optional local provider | `FORGEREMS_OLLAMA_BASE_URL`, model | No cloud by default; localhost request only | Yes | User runs local server. |
| OpenAI-compatible | Active optional/BYOK | base URL/model plus API key | Yes when enabled | Yes | Prompts and optional sanitized context can leave device. |
| Gemini | Active optional/BYOK | `GEMINI_API_KEY` or ForgerEMS Gemini vars | Yes when enabled | Yes | Same as above. |
| Anthropic | Active optional/BYOK path | `ANTHROPIC_API_KEY` or ForgerEMS Anthropic vars | Yes when enabled | Yes | Same as above. |
| Groq/OpenRouter/Cerebras/Mistral/GitHub Models/Cloudflare Workers AI | Active optional provider slots | provider-specific env var/session key | Yes when enabled | Yes | Keys are optional and never required for beta. |
| GitHub Releases update checker | Active | none by default | Yes, public GitHub metadata request | N/A | No account/token required for public releases. |
| eBay/marketplace providers | Shell/future/disabled | future eBay credentials | Future only | Yes | Offline FlipValue heuristic remains. |
| Facebook/OfferUp marketplace | Manual/future only | none active | No active integration found | Yes | Do not assume public APIs. |
| LibreHardwareMonitor Deep Sensor Provider | Optional bundled local provider | Deep Sensor Mode `ReadOnly` | No | Windows Native provider | Local read-only hardware sensors only. |

## 5. External Network Access

| Surface | Trigger | API key required? | Data sent | Failure handling |
|---------|---------|-------------------|-----------|------------------|
| GitHub update checker | User/app update check | No | configured owner/repo and User-Agent | UI reports unavailable/error; no silent install. |
| GitHub Actions release workflow | Tag push/manual workflow | GitHub-provided `GITHUB_TOKEN` placeholder | Release assets/metadata | CI fails if publish fails. |
| Managed download catalog | User starts managed downloads/revalidation | No | HTTP request to official/pinned URLs | Logs failure, falls back/manual required, checksum discipline remains. |
| Backend revalidation | Operator `Verify-VentoyCore.ps1 -RevalidateManagedDownloads` | No | HEAD/HTTP requests to official URLs | Local report artifacts only. |
| Kyra online providers | User/operator enables and sends prompt | Provider-dependent | Prompt and optional sanitized context | Provider error/fallback; Offline Local remains. |
| LM Studio/Ollama | User enables local provider | No cloud key | localhost request | Provider unavailable if local service stopped. |
| Future marketplace/comps providers | Future/disabled | Future | None today | Offline heuristic fallback. |

No active telemetry or crash upload endpoint was found; telemetry/crash flags default to `false`.

## 6. Secret Scanning Findings

No obvious real-looking cloud API keys, private keys, product keys, or webhook credentials were found in the scanned source-controlled text files.

| Classification | Finding | Recommendation |
|----------------|---------|----------------|
| No issue | No committed private keys, `.pfx`, `.pem`, `.key`, product keys, or real-looking cloud keys were found in the source-controlled scan. | Continue running local audit before release. |
| Placeholder/sample only | `.env.example`, docs, and Kyra setup docs use `REPLACE_ME` placeholders and provider variable names. | Keep placeholders commented or obvious. |
| Placeholder/sample only | `.github/workflows/release.yml` references `${{ secrets.GITHUB_TOKEN }}`. | GitHub-managed secret placeholder; do not replace with raw token. |
| False positive / test fixture | Tests contain fake strings such as redaction fixtures and fake provider keys. | Keep in tests only; audit script marks tests separately. |
| Potential private path | Runtime docs and code mention `%LOCALAPPDATA%` and local export paths. | Redact concrete user paths in support reports. |

If a real secret is ever found: redact it in reports, rotate it immediately at the provider, remove it from the repo, and add/update ignore rules or secret storage guidance.

## 7. Files That Must Not Be Committed

`.gitignore` should exclude:

- `.env`, `.env.*` except `.env.example`
- `secrets.json`, `tokens.json`, `local.settings.json`
- secret-bearing `appsettings.Production.json` or `appsettings.*.local.json`
- `*.pfx`, `*.p12`, `*.pem`, `*.key`, keystores, private cert exports
- generated logs/reports/support bundles
- generated release/build output under `dist/`, `artifacts/`, and `release/current/`
- ISO/IMG/VHD/VHDX/WIM/ESD payloads
- EXE/MSI/ZIP payloads except intentionally tracked historical release artifacts

Do not ignore legitimate source-controlled license/notice files under `providers/sensors/`.

Note: historical `release/v1.0.1` artifacts are currently tracked even though release output is ignored now. Treat this as historical source state; avoid adding new generated release folders.

## 8. Release Packaging Requirements

Installer/portable release output must include:

- `ForgerEMS.exe`
- bundled `backend/` scripts and manifests
- `manifests/ForgerEMS.updates.json` and schemas
- `providers/sensors/LibreHardwareMonitorLib.dll` when packaged
- `providers/sensors/THIRD-PARTY-NOTICES.txt`
- `providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt`
- `providers/sensors/LICENSES/LibreHardwareMonitor-THIRD-PARTY-LICENSES.txt`
- `release.json`
- `CHECKSUMS.sha256`
- generated `DOWNLOAD_BETA.txt`
- generated ZIP files containing `START_HERE.bat`, `VERIFY.txt`, installer, `release.json`, and package checksums
- installer README/legal/privacy/FAQ references where included

## 9. Support-Report Redaction Requirements

Support reports/logs should redact or avoid:

- API keys and provider tokens
- passwords
- product keys and recovery keys
- serial numbers/service tags by default
- exact private document paths where possible
- private documents/content
- support bundle output paths tied to a user profile
- raw device IDs unless the user intentionally sends a technician/debug report

Users should review reports before sharing.

## 10. Action Items

### Must fix before beta

- Keep real `.env`/secret files ignored.
- Run `tools/audit-config-and-secrets.ps1` before publishing release assets.
- Do not add raw provider keys to docs, tests, support messages, or screenshots.
- Treat direct provider keys in `release/current` as release blockers.
- Treat gateway beta tokens as revocable access tokens and keep them redacted in logs/docs/status output.
- Verify beta setup uses gateway URL + gateway token only (no owner provider keys shipped to testers).

### Should fix before paid release

- Consider untracking historical binary release artifacts if no longer needed for source history.
- Add CI secret-audit step if release cadence grows.
- Review telemetry/crash flags and either wire an explicit disclosed provider or remove reserved flags.

### Nice to have later

- Optional integration with `gitleaks`/`trufflehog` for deeper local scans.
- Machine-readable configuration manifest generated from `ForgerEmsEnvironmentConfiguration`.
- Separate provider privacy matrix in-app for Kyra Advanced.
