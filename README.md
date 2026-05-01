# ForgerEMS (Beta)

**Current beta:** **v1.1.4** — *ForgerEMS Beta v1.1.4 — Whole-App Intelligence Preview*.

**ForgerEMS** is the Forger Engineering Maintenance Suite from **Forger Digital Solutions**: a Windows desktop companion for offline-first repair, diagnostics, resale intelligence, and building or refreshing a controlled USB engineering toolkit.

The repository is organized as the official source for the WPF application, PowerShell backend, Ventoy integration, update manifests, installer configuration, and release automation.

## What It Does

ForgerEMS gives operators a guided desktop interface for preparing and maintaining USB toolkit media, running System Intelligence, diagnostics (link/file safety checks, WSL helpers), and optional **Kyra AI**. The WPF app wraps a PowerShell backend that validates manifests, manages downloads, stages the Ventoy-based toolkit flow, and supports installed-mode distribution through a Windows installer.

## Kyra AI (summary)

- **Offline Local Kyra** works without any API key (rules + optional local models such as Ollama / LM Studio).
- **Optional online providers** (free API pool, BYOK) extend Kyra when you enable them; response source labeling shows which path answered.
- **Session API keys** are kept in memory for the current run only and are **not** written to settings JSON.
- **Environment variables** are read in order: process → user → machine. **Session keys override environment.** After changing user/machine variables, use **Refresh Provider Status** in Kyra Advanced so credentials are picked up without restarting.
- Supported variable names include: `GEMINI_API_KEY`, `GROQ_API_KEY`, `OPENROUTER_API_KEY`, `CEREBRAS_API_KEY`, `MISTRAL_API_KEY`, `GITHUB_MODELS_TOKEN`, `CLOUDFLARE_API_KEY`, `CLOUDFLARE_ACCOUNT_ID`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`. Cloudflare requires **both** the API key and **account ID**.

## Beta support

**Beta issue? Send logs/screenshots to ForgerDigitalSolutions@outlook.com** (also available via in-app mailto, header, and logs panel). **Do not email API keys, passwords, serial numbers, or private documents.**

## Update notifications

The app can check **public GitHub Releases** for the `Forger-Digital-Solutions/ForgerEMS` repo (no token required for public releases). Checks are **non-blocking**, safe when **offline** (no crash), and **never** silently download or install an update. Use **Settings → App updates** for “check now”, last-checked time, ignored version, and optional installer download to `%LOCALAPPDATA%\ForgerEMS\Updates` (or your chosen folder) only when **you** choose **Download**.

**Upgrading an installed copy:** when a newer build is published, use the in-app update banner or **Settings → App updates** to download the new `ForgerEMS-Setup-v*.exe` and run it over your existing install (there is no silent auto-upgrade).

## v1.1.4 launch readiness

- **Manual smoke:** follow [FINAL_MANUAL_SMOKE_TEST.md](FINAL_MANUAL_SMOKE_TEST.md) before calling the build “launch-ready” for external users.
- **Secret hygiene:** run `.\tools\check-secrets.ps1` before tagging; review hits (unit tests may contain fake example tokens). Use `-Strict` to fail on hits outside test folders.
- **Known non-blockers for beta:** (1) Cloudflare-related tests may skip a branch if your PC already has `CLOUDFLARE_ACCOUNT_ID` in user/machine env. (2) Machine-level env resolution is not covered by a dedicated elevated automated test. (3) Update checks require network access to GitHub for success; otherwise the app should degrade gracefully.

Operator checklist: [BETA_RELEASE_CHECKLIST.md](BETA_RELEASE_CHECKLIST.md).

## Features

- USB toolkit builder for repeatable engineering and maintenance media.
- Managed downloads driven by versioned manifests and vendor inventory.
- Ventoy integration through the ForgerEMS backend scripts.
- Installed-mode support with bundled backend validation.
- Inno Setup installer configuration for Program Files deployment.
- Release scripting with staging, checksums, and CI-friendly dry runs.

## Repository Layout

```text
ForgerEMS/
├── src/                        # .NET 8 WPF app
├── backend/                    # PowerShell backend and Ventoy toolkit scripts
├── manifests/                  # updates.json and schema files
├── tools/                      # build, staging, and release scripts
├── installer/                  # Inno Setup installer configuration
├── docs/                       # product, backend, and release documentation
├── .github/workflows/          # GitHub Actions CI/CD
├── .gitignore
├── README.md
├── RELEASE_PROCESS.md
└── LICENSE
```

## Screenshots

Screenshots are intentionally not committed yet.

- Main dashboard: `docs/screenshots/main-dashboard.png` (placeholder)
- USB toolkit workflow: `docs/screenshots/usb-toolkit-workflow.png` (placeholder)
- Release/installer flow: `docs/screenshots/release-installer.png` (placeholder)

## Build

Prerequisites:

- Windows 10/11
- .NET 8 SDK
- PowerShell 5.1 or newer
- Inno Setup 6 for installer builds

Build and test the WPF app:

```powershell
dotnet restore .\ForgerEMS.sln
dotnet build .\ForgerEMS.sln -c Release --no-incremental
dotnet test .\ForgerEMS.sln -c Release --no-build
```

Create a release staging folder without compiling the installer:

```powershell
.\tools\build-release.ps1 -DryRun
```

Create a full local installer release (version defaults to `src/ForgerEMS.Wpf/ForgerEMS.Wpf.csproj` `<Version>`, currently **1.1.4**):

```powershell
.\tools\build-release.ps1
```

Installer output (when Inno Setup runs): `dist\installer\ForgerEMS-Setup-v<Version>.exe` (also copied beside other artifacts under `release\current\` for checksums).

## Release

Release outputs are written to:

```text
release/current/
release/ventoy-core/<coreVersion>/
```

The release script publishes the WPF app, builds a verified backend bundle, stages manifests, optionally compiles the Inno Setup installer, and writes SHA256 checksums for produced artifacts.

`release/current` and `release/ventoy-core` are generated build outputs and should not be used as long-lived versioned snapshots in git history.
Versioned installer/portable artifacts (for example `v1.1.4`) should be published through GitHub Releases from a tag.

See [RELEASE_PROCESS.md](RELEASE_PROCESS.md) for the operator release checklist.
For beta tester safety guidance, see [BETA_TESTING_GUIDE.md](BETA_TESTING_GUIDE.md) and [BETA_RELEASE_CHECKLIST.md](BETA_RELEASE_CHECKLIST.md).

## License

Copyright © 2026 Forger Digital Solutions.

This project is distributed under the license terms in [LICENSE](LICENSE).
