# ForgerEMS (Public Preview)

**Forger Engineering Maintenance Suite** — a Windows desktop app for technicians who work with USB toolkits, repairs, and diagnostics.

**Current release line:** **v1.2.3-preview.1** — **ForgerEMS v1.2.3 Public Preview** (toolkit catalog metadata, Driver Hub, workflow presets, Toolkit Readiness Score, local machine profiles, Verify Links, Kyra awareness for the technician suite, config/env layer, support bundle export, and documentation pass; core WPF/.NET 8 architecture unchanged).

**Kickstarter:** Coming soon.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) — send **sanitized** screenshots and short log excerpts only; never passwords, keys, or private files.

---

## What is ForgerEMS?

ForgerEMS helps you **build and maintain a capable USB toolkit**, **understand what the PC is doing** (storage, health signals, diagnostics), and get **guided help from Kyra** — an assistant that works **offline by default**. It is built for repair benches, shops, resellers, and advanced home users who want fewer guess-and-check afternoons.

This is **Public Preview / prerelease** software: behavior and packaging can change between builds. See [docs/LEGAL.md](docs/LEGAL.md) and [docs/PRIVACY.md](docs/PRIVACY.md). Operator environment variables: [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md).

---

## Key features

| Feature | What it does |
|--------|----------------|
| **USB Builder** | Guided flows to verify, prepare, and update Ventoy-oriented USB maintenance media, with managed downloads and careful drive selection. The **USB Builder Profile** lets technicians enable or skip packs per run (Windows, Legacy Windows, Linux Rescue, Diagnostics, OEM Tools, macOS, Android, iOS / iPadOS). Core USB structure is required and cannot be turned off. macOS, Android, and iOS / iPadOS are off by default and treat all media as manual. Unchecking a pack only skips seeding/updating it — files already on the USB are never deleted. |
| **Drive Validator** | Wizard-style non-destructive checks against a removable USB target's free space (Quick Safe Check, Sampled Capacity Check, Full Free-Space Validation) with a **live media-integrity tile map** to flag suspicious capacity, aliasing, short reads/writes, I/O errors, or failing regions before building a toolkit. The USB Builder tab keeps a compact summary card; **Open Drive Validator** launches the Drive Validator Wizard (Select target → Choose mode → Safety review → Running → Results). Safe modes write only into `.forgerems-drive-validator` on the chosen USB; never format, never delete user files, and never run against the Windows OS drive, system / boot / EFI / VTOYEFI partitions, or internal fixed disks by default. Results are advisory evidence for a technician, **not** a guarantee that the drive is genuine and **not** a direct inspection of NAND. Destructive full-media mode is **not available** in this build. |
| **USB Intelligence** | Measure write/read on a **safe removable** target, flag likely cached read samples honestly, map **which physical USB port** you used, and get practical guidance from benchmarks and topology hints (best-effort; varies by PC). Cache-suspected reads are treated as unverified and do not upgrade recommendation quality on their own. |
| **System Intelligence** | Local scan summaries with Hardware X-Ray sensor coverage, health scoring, FlipValue, Best Use / Device Fit, and honest Unknown/NotExposed handling before repair or resale prep. |
| **Diagnostics** | Unified health checklist, file/link safety helpers, and technician-oriented tools (including WSL-related helpers where applicable). |
| **Toolkit Manager** | Manifest-driven health for what is on your USB, now with technician-focused categories and catalog metadata (purpose, official URL, license/redistribution note, download/checksum status, distribution model, beta safety rating). Health checks distinguish verified managed tools, present-but-not-verified tools, manual/info shortcuts, shortcuts covered/suppressed by installed managed tools, and missing required items. **Verify Links** runs optional **HTTP metadata-only** checks (HEAD / tiny ranged GET): reachability, redirects, and trust hints — **no full downloads and no execution** of third-party payloads. |
| **Driver Hub** | Curated app-store-style hub for official GPU utilities, OEM support portals, chipset/network/audio driver pages, BIOS/firmware support links, and Linux driver guidance. Recommended cards use System Intelligence hints when available and show brand monograms, official-page/open-download actions, copy-link actions, and safe `.url` USB shortcuts. It does **not** auto-install drivers, auto-download OEM packages, upload service tags, or automate BIOS/firmware flashing. |
| **Kyra** | In-app assistant: offline local answers first, with optional **Kyra Beta Gateway**, **Bring Your Own Key**, local AI, and live-tool paths shown in **Kyra AI Settings**. BYOK keys are optional, hidden, and never stored as plaintext appsettings. After System Intelligence, Kyra can answer many **hardware / upgrade / parts** questions from local scan data, and can explain dry-run **Technician Workflow Presets** (checklist guidance only; no destructive automation). |
| **Kyra Intelligence Network** | Local-first repair memory plus optional anonymous community learning foundations. Default is **Local Only**; community upload is off/disabled in this phase. |

More context: [docs/ABOUT_FORGEREMS.md](docs/ABOUT_FORGEREMS.md) · Behavior notes: [KYRA_BEHAVIOR_SPEC.md](KYRA_BEHAVIOR_SPEC.md) (repository root).

**Visual Effects:** The app defaults to **Static / Low Power** backgrounds for public preview responsiveness. Animated CyberViking/circuit effects remain optional in Settings, and **Off / Plain dark** is available for slower machines or remote sessions.

**Hardware X-Ray / Deep Sensor Mode:** Deep Sensor Mode is optional and uses bundled local read-only hardware sensors when enabled, including LibreHardwareMonitorLib where packaged. No separate LibreHardwareMonitor download is required. ForgerEMS does not control fans, voltages, clocks, overclocking, undervolting, BIOS, or firmware. Unavailable readings are coverage limits, not failures. **Elevated Scan** is an optional deeper scan that asks Windows for administrator approval; Standard Scan is always available without it.

## Kyra Beta Gateway

- Beta cloud access can use `FORGEREMS_KYRA_GATEWAY_URL` + `FORGEREMS_KYRA_GATEWAY_BETA_TOKEN`.
- Desktop app never needs owner provider keys for this beta path.
- Provider keys stay server-side only as Cloudflare Worker secrets.
- **Realtime research** uses `POST /v1/kyra/research`; **status** uses `GET /v1/kyra/status` (see [gateway/GATEWAY_RESEARCH_CONTRACT.md](gateway/GATEWAY_RESEARCH_CONTRACT.md)).
- System context sharing is off by default and only sends sanitized summary when enabled.
- Local/offline fallback remains available if gateway is missing, rate-limited, or unavailable.
- Do not paste provider keys or beta tokens in docs, screenshots, logs, support email, or Kyra chat.

## Kyra AI Settings and BYOK

- **Kyra AI Settings** has clean tabs for Overview, Providers, Bring Your Own Key, Live Tools, Privacy & Context, Local AI, and Diagnostics.
- BYOK is optional. Session keys are kept in memory until the app closes; saved keys use Windows protected local storage when available and fall back to session-only if protection fails.
- Environment variable setup remains supported for advanced operators under the settings panel's advanced environment setup and [docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md](docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md).
- Provider precedence is session key, protected saved key, environment variable, then Gateway/local/offline fallback.
- Diagnostics and support email must not include API keys, tokens, private documents, serial numbers, service tags, private paths, or raw exception chains.

## Kyra Intelligence Network

Kyra Intelligence Network is the safe foundation for **local-first repair memory + optional anonymous community learning**.

- **Local Kyra Memory** can store sanitized, machine-scoped repair notes on this PC: machine class, hardware category summary, health score band, issue/warning category, suggested or user-confirmed fixes, USB target safety result, best-use category, resale prep category, scan timestamp, confidence, and a ForgerEMS-generated local machine profile ID.
- **Optional Anonymous Community Learning** is off by default. The app must not share community intelligence unless the user explicitly opts in. In this phase the community client is disabled/no-op and only sanitized preview/export foundations exist.
- **Research Mode** routes current/live prompts such as crypto, stocks, weather, news, latest versions, drivers, CVEs, and market pricing to configured live tools/providers first. If no live tool is available, Kyra must say so honestly instead of inventing current data.
- **Hardware part research** uses the local System Intelligence scan for device facts, then uses configured live research/gateway tools for external truth such as official compatibility, current availability, and pricing. For batteries, Kyra should prefer OEM support/service manuals/parts pages first, treat seller listings as secondary candidates, and tell you to match voltage, watt-hour rating, connector, shape, service manual, and physical label before buying.
- Normal Kyra chat shows compact privacy/source footers. Provider routing and debug detail stays in logs, diagnostics, support bundles, or explicit technical detail flows.
- Settings include **Kyra Intelligence** controls to keep local-only, use System Intelligence context, allow gateway research when configured, view what would be shared, export Kyra memory, and delete Kyra memory.

ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

## Cross-platform toolkit packs (Windows-first)

ForgerEMS is a Windows-first technician workbench. The macOS, Android, and iOS / iPadOS USB Builder packs are off by default. When a technician enables them, the catalog opens **official vendor pages** only:

- **macOS**: Apple support / `createinstallmedia` / recovery workflow shortcuts. Installers, DMGs, and PKGs remain **user-supplied** — drop them into `ISO\macOS\macOS-Manual-Installer-Drop\<version>\`. A compatible Mac may be required. ForgerEMS does **not** redistribute Apple installers.
- **Android**: official Android SDK Platform-Tools (adb / fastboot), Google Pixel factory / OTA images, AOSP documentation, and Samsung / Motorola / OnePlus support pages. OEM firmware is device, model, bootloader, region, and carrier specific and remains **user-supplied** — drop into `ISO\Android\Android-Manual-Firmware-Drop\<vendor>\`. Flashing the wrong firmware can wipe data or brick devices. ForgerEMS does **not** redistribute Android firmware and never uses random firmware mirrors.
- **iOS / iPadOS**: Apple Devices for Windows, Finder / iTunes, recovery mode, and Apple Configurator restore workflows. IPSW files are **user-supplied** — drop into `ISO\iOS-iPadOS\iOS-Manual-IPSW-Drop\<device>\`. Restores can erase devices. Activation Lock and Apple ID ownership are outside ForgerEMS. ForgerEMS does **not** use third-party IPSW indexes.

ForgerEMS never bypasses licenses, activation, DRM, account locks, or vendor authorization flows. Toolkit `.url` filenames use a fixed taxonomy: **AUTO DOWNLOAD / DOWNLOAD** (official, redistributable, machine-resolvable), **MANUAL DOWNLOAD** (official vendor page; user must choose / sign in / accept), **MANUAL MEDIA REQUIRED** (user supplies the ISO / installer / IPSW / firmware), **GUIDE** (official how-to), **INFO** (true reference material).

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
- **Deep Sensor Mode:** sensor access is local to the device and runs only while ForgerEMS is open or System Intelligence / Hardware X-Ray scans execute. Reports are shared only if you copy/export/send them.
- **Automated quality:** the solution ships with a large automated test suite (`dotnet test` on `ForgerEMS.sln`); the exact count grows with each release.

**Pro / preview labels** during beta are for feedback; licensing is not final. See release notes under `docs/` for the build you are testing.

---

## In-app updates

The app can check **public GitHub Releases** for this repo (no account required for public releases). It compares your installed build to the **latest eligible release** (by **publish date**, then assets). **Nothing** is downloaded or installed unless **you** choose to. Details: [docs/UPDATE_SYSTEM.md](docs/UPDATE_SYSTEM.md).

---

## For developers

Prerequisites: Windows 10/11, .NET 8 SDK, PowerShell 5.1+, Inno Setup 6 (for installer builds).

The Inno script (`installer/ForgerEMS.iss`) includes a **Kyra Intelligence** wizard page: optional anonymous community sharing is **off by default** (all checkboxes unchecked). Choices are stored under `HKLM\Software\ForgerEMS` and applied the first time the app creates `copilot-settings.json` for a Windows profile; users can change everything later in **Settings → Kyra Intelligence**.

```powershell
dotnet restore .\ForgerEMS.sln
dotnet build .\ForgerEMS.sln -c Release --no-incremental
dotnet test .\ForgerEMS.sln -c Release --no-build
```

Staging without compiling the installer:

```powershell
.\tools\build-release.ps1 -DryRun
```

Full local release (version follows `src/ForgerEMS.Wpf/ForgerEMS.Wpf.csproj`, currently **1.2.3-preview.1** / **ForgerEMS v1.2.3 Public Preview**):

```powershell
.\tools\build-release.ps1 -Version 1.2.3-preview.1
```

Without Inno Setup (skips installer + dual ZIP bundle; still stages `release\current\` app + backend + `release.json` + checksums):

```powershell
.\tools\build-release.ps1 -Version 1.2.3-preview.1 -SkipInstaller
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

## Screenshots

Campaign-quality screenshots coming with launch. Add PNGs under `docs/screenshots/` when ready.

## License

Copyright © 2026 Forger Digital Solutions. See [LICENSE](LICENSE).
