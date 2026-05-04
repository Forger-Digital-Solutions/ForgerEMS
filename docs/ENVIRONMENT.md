# ForgerEMS environment variables

Precedence in the app: safe defaults → optional `appsettings.json` (if introduced for a subsystem) → user settings under `%LOCALAPPDATA%\ForgerEMS\` → **process** environment variables → command-line flags (where supported).

This document lists **supported or reserved** variables for v1.2.0 Public Preview. Values are read directly by the WPF host or reserved for Kyra / future marketplace integrations.

## Core

| Variable | Example | Purpose |
|----------|---------|---------|
| `FORGEREMS_ENV` | `Production` | Deployment label (`Production`, `Beta`, `Development`). |
| `FORGEREMS_RELEASE_CHANNEL` | `preview` | Marketing/update channel hint (`stable`, `beta`, `rc`, `preview`). |
| `FORGEREMS_PORTABLE_MODE` | `true` | Portable layout hint (reserved; documentation). |
| `FORGEREMS_LOG_LEVEL` | `Info` | Log verbosity hint (`Info`, `Debug`, `Trace`). |
| `FORGEREMS_VERBOSE_LIVE_LOGS` | `false` | Verbose sidebar live logs (Settings can override per user). |
| `FORGEREMS_SUPPORT_EMAIL` | `ForgerDigitalSolutions@outlook.com` | Support contact override. |

## Updates / GitHub

| Variable | Default | Purpose |
|----------|---------|---------|
| `FORGEREMS_GITHUB_OWNER` | `Forger-Digital-Solutions` | GitHub API releases owner segment. |
| `FORGEREMS_GITHUB_REPO` | `ForgerEMS` | GitHub API releases repo segment. |
| `FORGEREMS_UPDATE_CHANNEL` | mirrors `FORGEREMS_RELEASE_CHANNEL` | Reserved for UI copy / future narrowing. |
| `FORGEREMS_UPDATE_INCLUDE_PRERELEASE` | `true` | Reserved; in-app toggle remains primary. |
| `FORGEREMS_UPDATE_USER_AGENT` | `ForgerEMS` | GitHub API User-Agent header. |
| `FORGEREMS_UPDATE_TIMEOUT_SECONDS` | `20` | HTTP client timeout for release list (5–120 clamped). |

## Kyra

| Variable | Purpose |
|----------|---------|
| `FORGEREMS_KYRA_MODE` | `offline`, `local`, `online`, `hybrid` (hint). |
| `FORGEREMS_KYRA_PROVIDER` | Provider hint (`offline`, `openai-compatible`, `lmstudio`, `ollama`, …). |
| `FORGEREMS_KYRA_ONLINE_ENABLED` | Gate online enhancement. |
| `FORGEREMS_KYRA_SHARE_SYSTEM_CONTEXT` | System context sharing hint. |
| `FORGEREMS_KYRA_REQUIRE_LOCAL_FACTS` | Prefer grounded scan facts. |
| `FORGEREMS_KYRA_MAX_CONTEXT_TURNS` | Conversation depth hint. |

### OpenAI-compatible

- `FORGEREMS_OPENAI_BASE_URL`
- `FORGEREMS_OPENAI_MODEL`
- `FORGEREMS_OPENAI_API_KEY` — **never log or display raw**.

### LM Studio

- `FORGEREMS_LMSTUDIO_BASE_URL` (default `http://localhost:1234/v1`)
- `FORGEREMS_LMSTUDIO_MODEL`

### Ollama

- `FORGEREMS_OLLAMA_BASE_URL` (default `http://localhost:11434`)
- `FORGEREMS_OLLAMA_MODEL`

### Anthropic / Gemini / custom (stubs or BYOK paths)

- `FORGEREMS_ANTHROPIC_API_KEY`, `FORGEREMS_ANTHROPIC_MODEL`
- `FORGEREMS_GEMINI_API_KEY`, `FORGEREMS_GEMINI_MODEL`
- `FORGEREMS_CUSTOM_PROVIDER_BASE_URL`, `FORGEREMS_CUSTOM_PROVIDER_MODEL`, `FORGEREMS_CUSTOM_PROVIDER_API_KEY`

Existing provider-specific names (e.g. `OPENAI_API_KEY`) remain documented in `docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md`.

## Diagnostics

| Variable | Purpose |
|----------|---------|
| `FORGEREMS_DIAGNOSTICS_EXPORT_DIR` | Optional default folder for support bundles. |
| `FORGEREMS_DIAGNOSTICS_REDACTION_STRICT` | Strict redaction mode (reserved). |
| `FORGEREMS_ENABLE_DIAGNOSTIC_BUNDLE` | `true`/`false` — disables in-app **Export Support Bundle** when `false`. |

## Marketplace / valuation (future)

- `FORGEREMS_MARKETPLACE_ENABLED` — default `false`.
- `FORGEREMS_EBAY_ENABLED`, `FORGEREMS_EBAY_APP_ID`, `FORGEREMS_EBAY_CERT_ID`, `FORGEREMS_EBAY_DEV_ID`
- `FORGEREMS_MARKETPLACE_REGION`
- `FORGEREMS_VALUATION_MODE` — `offline`, `hybrid`, `online`.

## Telemetry

- `FORGEREMS_TELEMETRY_ENABLED` — default **false**.
- `FORGEREMS_CRASH_REPORTING_ENABLED` — default **false**.

## Licensing (local preview)

- `FORGEREMS_LICENSE_TIER` — `PublicPreview` (implicit default), `BetaTesterPro`, `Pro`, `Developer`, `Free`.

No cloud activation server is contacted for these values.
