# ForgerEMS Environment and Local Configuration

This file documents supported and reserved ForgerEMS environment variables for v1.2.4. Do not put real secrets in source files, screenshots, support emails, or issue reports. Use `.env.example` as a placeholder-only reference.

General app configuration is local-first. Environment variables are optional operator/developer overrides unless stated otherwise.

Placeholder values such as `REPLACE_ME`, `YOUR_*`, `PASTE_*`, `example.local`, `changeme`, and `TODO` are treated as **not configured**. They are examples only and must not mark a feature or provider ready.

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
| Windows 10/11 x64 | Required | WPF desktop runtime and Windows diagnostics | Targets Windows technician machines. |
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
| `FORGEREMS_RELEASE_CHANNEL` | No | `stable` | WPF app/update/support bundle | Marketing/update channel hint | Yes | Examples: `stable`, `beta`, `rc`, `preview`. |
| `FORGEREMS_PORTABLE_MODE` | No | `false` | WPF config | Portable layout hint/reserved | Yes | Not a secret. |
| `FORGEREMS_LOG_LEVEL` | No | `Info` | WPF config | Log verbosity hint | Yes | Avoid `Trace` in shared screenshots if logs include private paths. |
| `FORGEREMS_VERBOSE_LIVE_LOGS` | No | `false` | WPF UI | Verbose live logs | Yes | May reveal more local detail. |
| `FORGEREMS_SUPPORT_EMAIL` | No | `ForgerDigitalSolutions@outlook.com` | WPF/support copy | Support contact override | Yes | Not a credential. |
| `FORGEREMS_BACKEND_ROOT` | No | bundled/repo discovery | Backend discovery | Override backend script root | Treat as private path | Do not share full private path in public reports. |
| `FORGEREMS_GITHUB_OWNER` | No | `Forger-Digital-Solutions` | Update checker | GitHub owner for releases | Yes | Public repo segment. |
| `FORGEREMS_GITHUB_REPO` | No | `ForgerEMS` | Update checker | GitHub repo for releases | Yes | Public repo segment. |
| `FORGEREMS_UPDATE_CHANNEL` | No | release channel | Update UI/future narrowing | Update channel hint | Yes | Reserved; UI settings remain primary. |
| `FORGEREMS_UPDATE_INCLUDE_PRERELEASE` | No | `false` | Update config | Include prerelease hint | Yes | Reserved; in-app toggle remains primary. |
| `FORGEREMS_UPDATE_USER_AGENT` | No | `ForgerEMS` | GitHub HTTP client | User-Agent override | Yes | When unset or left at the short default, the app sends `ForgerEMS/{version} (+https://github.com/{owner}/{repo})`. Do not include secrets. |
| `FORGEREMS_GITHUB_TOKEN` | Optional secret | empty | GitHub HTTP client | Raises API rate limits for update checks | Never expose | Operator/dev only. PAT with **public_repo** read scope. Placeholders (`REPLACE_ME`, etc.) are ignored. Not required for public releases. |
| `FORGEREMS_UPDATE_TIMEOUT_SECONDS` | No | `20` | GitHub HTTP client | Release list timeout | Yes | Clamped 5-120. |
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
| `FORGEREMS_FORCE_DOTNET_HASH` | No | unset | Backend hash helper | Force .NET SHA256 fallback for tests | Yes | Accepted `1`/`true`. |
| `LOCALAPPDATA` | OS-provided | Windows value | Runtime paths | Local app data root | Private path | Redact full path in public logs. |
| `SystemDrive` | OS-provided | Windows value | BitLocker/security scan | OS drive selection | Usually yes | Avoid leaking private path context. |
| `PSHOME` | OS-provided | Windows value | Tests | PowerShell discovery | Usually yes | Not a secret. |

Tests also create temporary `FORGEREMS_UT_*` variables. They are test-only and not user configuration.

## External Network Access

| Integration | Trigger | Key required? | Data sent | Failure behavior |
|-------------|---------|---------------|-----------|------------------|
| GitHub Releases update checker | User/app update check | No | Repo owner/repo, User-Agent | Reports unavailable/update error; no install without user action. |
| Managed download catalog | USB Builder/Toolkit managed downloads | No | URL request to official source/manifest URL | Falls back or marks item manual/failed; checksum verification remains required where configured. |
| Backend revalidation | Operator runs `Verify-VentoyCore.ps1 -RevalidateManagedDownloads` | No | HEAD/HTTP requests to official URLs | Writes local revalidation artifacts. |
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
