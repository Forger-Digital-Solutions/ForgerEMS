# ForgerEMS (Beta)

**Forger Engineering Maintenance Suite** — a Windows desktop app for technicians who work with USB toolkits, repairs, and diagnostics.

**Current release line:** **v1.1.12-rc.3** (mechanical RC; USB mapping wizard, USB benchmark scheduling, and related fixes continue to evolve).

**Kickstarter:** [Campaign link — add when live](https://www.kickstarter.com/)

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) — send **sanitized** screenshots and short log excerpts only; never passwords, keys, or private files.

---

## What is ForgerEMS?

ForgerEMS helps you **build and maintain a capable USB toolkit**, **understand what the PC is doing** (storage, health signals, diagnostics), and get **guided help from Kyra** — an assistant that works **offline by default**. It is built for repair benches, shops, resellers, and advanced home users who want fewer guess-and-check afternoons.

This is **beta software**: behavior and packaging can change between builds. See [docs/LEGAL.md](docs/LEGAL.md) and [docs/PRIVACY.md](docs/PRIVACY.md).

---

## Key features

| Feature | What it does |
|--------|----------------|
| **USB Builder** | Guided flows to verify, prepare, and update Ventoy-oriented USB maintenance media, with managed downloads and careful drive selection. |
| **USB Intelligence** | Measure read/write on a **safe removable** target, map **which physical USB port** you used, and get practical guidance from benchmarks and topology hints (best-effort; varies by PC). |
| **System Intelligence** | Local scan summaries — hardware and health-oriented cards you can use before a repair or resale prep. |
| **Diagnostics** | Unified health checklist, file/link safety helpers, and technician-oriented tools (including WSL-related helpers where applicable). |
| **Toolkit Manager** | Manifest-driven health for what is on your USB; clear paths when something must be supplied manually. |
| **Kyra** | In-app assistant: **offline local** answers first; optional online help only when your environment already enables it (operators). **Beta testers are not asked to supply API keys in the app.** |

More context: [docs/ABOUT_FORGEREMS.md](docs/ABOUT_FORGEREMS.md) · Behavior notes: [KYRA_BEHAVIOR_SPEC.md](KYRA_BEHAVIOR_SPEC.md) (repository root).

---

## Download (ZIP-first)

**Always download the ZIP from GitHub Releases — not the standalone `.exe` first.** The ZIP is the supported, beginner-friendly path; Windows and browsers are usually kinder to a folder you extract than to a raw installer download.

1. Open **[Releases — Forger-Digital-Solutions/ForgerEMS](https://github.com/Forger-Digital-Solutions/ForgerEMS/releases)**.
2. Under **Assets**, download **one** of:
   - `ForgerEMS-v<version>.zip` **or**
   - `ForgerEMS-Beta-v<version>.zip` (same bundle policy; easier to spot in a long list)
3. **Wait** until the download finishes completely (see [docs/DOWNLOAD_TROUBLESHOOTING.md](docs/DOWNLOAD_TROUBLESHOOTING.md) if you see `.crdownload` or stalls).
4. Extract to a **short path** (for example `Desktop\ForgerEMS`).
5. Open the extracted folder and double-click **`START_HERE.bat`**. It guides you through verification and install.

Optionally verify integrity using **`CHECKSUMS.sha256`** from the **same** release page before you run anything.

The standalone **`ForgerEMS-Setup-v<version>.exe`** on the release is an **advanced / direct** asset for people who already know they want it; it is **not** the recommended first download.

**Helpful links**

- [Releases](https://github.com/Forger-Digital-Solutions/ForgerEMS/releases)
- [FAQ](docs/FAQ.md)
- [Download troubleshooting](docs/DOWNLOAD_TROUBLESHOOTING.md)
- [Beta tester quickstart](docs/BETA_TESTER_QUICKSTART.md)
- [How in-app updates work](docs/UPDATE_SYSTEM.md)

---

## Beta, SmartScreen, and trust

- **SmartScreen** and browser warnings are **common** for newer or less-known Windows software. ForgerEMS does **not** ask you to disable Windows security. Prefer the **ZIP → `START_HERE.bat`** path and verify hashes when you can.
- **ZIP-first** releases include `VERIFY.txt` and checksum material so you can confirm what you downloaded.
- **Local-first:** scans and reports are stored on **your PC** (typically under `%LOCALAPPDATA%\ForgerEMS\`). There is **no silent upload** of your logs or scans to Forger Digital Solutions.
- **Automated quality:** the solution ships with a large automated test suite (`dotnet test` on `ForgerEMS.sln`); the exact count grows with each release.

**Pro / preview labels** during beta are for feedback; licensing is not final. See release notes under `docs/` for the build you are testing.

---

## In-app updates

The app can check **public GitHub Releases** for this repo (no account required for public releases). It compares your installed build to the **latest eligible release** (by **publish date**, then assets). **Nothing** is downloaded or installed unless **you** choose to. Details: [docs/UPDATE_SYSTEM.md](docs/UPDATE_SYSTEM.md).

---

## For developers

Prerequisites: Windows 10/11, .NET 8 SDK, PowerShell 5.1+, Inno Setup 6 (for installer builds).

```powershell
dotnet restore .\ForgerEMS.sln
dotnet build .\ForgerEMS.sln -c Release --no-incremental
dotnet test .\ForgerEMS.sln -c Release --no-build
```

Staging without compiling the installer:

```powershell
.\tools\build-release.ps1 -DryRun
```

Full local release (version follows `src/ForgerEMS.Wpf/ForgerEMS.Wpf.csproj`, currently **1.1.12-rc.3**):

```powershell
.\tools\build-release.ps1 -Version 1.1.12-rc.3
```

Release layout, CI, and operator checklists: [RELEASE_PROCESS.md](RELEASE_PROCESS.md), [BETA_RELEASE_CHECKLIST.md](BETA_RELEASE_CHECKLIST.md), [BETA_TESTING_GUIDE.md](BETA_TESTING_GUIDE.md).

**Operator-only Kyra online setup** (environment variables, local servers): [docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md](docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md) — not required for normal beta testing.

---

## Repository layout

```text
ForgerEMS/
├── src/                 # .NET 8 WPF app
├── backend/             # PowerShell backend and toolkit scripts
├── manifests/           # updates.json and schema files
├── tools/               # build, staging, and release scripts
├── installer/           # Inno Setup configuration
├── docs/                # product and release documentation
├── .github/workflows/   # GitHub Actions
├── README.md
├── RELEASE_PROCESS.md
└── LICENSE
```

## Screenshots (placeholders)

Add campaign-quality PNGs under `docs/screenshots/` when ready.

| Shot | Suggested filename |
|------|---------------------|
| Main dashboard | `docs/screenshots/main-dashboard.png` |
| USB Builder | `docs/screenshots/usb-toolkit-workflow.png` |
| USB Intelligence | `docs/screenshots/usb-intelligence-pro.png` |
| Kyra | `docs/screenshots/kyra-assistant.png` |
| System Intelligence | `docs/screenshots/system-intelligence.png` |

## License

Copyright © 2026 Forger Digital Solutions. See [LICENSE](LICENSE).
