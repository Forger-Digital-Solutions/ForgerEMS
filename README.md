# ForgerEMS

**Forger Engineering Maintenance Suite** — a Windows desktop app for technicians who work with USB toolkits, repairs, and diagnostics.

**Current release line:** **v1.2.4** — **ForgerEMS v1.2.4** (safe Dr. Forge CLI Intake bridge with parsed local report sections, USB Builder Profile picker, portable app USB profile, Driver Hub/vendor guidance, near-instant USB hotplug detection, Port / USB Intelligence results dashboard, port/USB mapping, persistent Live Logs, first-run Terms consent gate, and semantic-version update checks).

**Kickstarter:** Coming soon.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) — send **sanitized** screenshots and short log excerpts only; never passwords, keys, or private files.

**Support development (optional):** ForgerEMS remains usable without donating. Public platform links are intentionally pending owner configuration: [Ko-fi](DONATION_LINK_TODO) · [Sponsor](SPONSOR_LINK_TODO) · [PayPal](PAYPAL_LINK_TODO). Donations do not purchase ownership, guaranteed features, priority support, investment returns, or equity. See [donation transparency](docs/marketing/DONATION_TRANSPARENCY.md).

---

## What is ForgerEMS?

ForgerEMS helps you **build and maintain a capable USB toolkit** and **understand what the PC is doing** (storage, health signals, diagnostics). It is built for repair benches, shops, resellers, and advanced home users who want fewer guess-and-check afternoons.

Review [Terms of Use](docs/TERMS_OF_USE.md), [Privacy/Data Handling](docs/PRIVACY_AND_DATA_HANDLING.md), [Legal Notices](docs/LEGAL_NOTICES.md), and [About ForgerEMS](docs/ABOUT_FORGEREMS.md). Operator environment variables: [docs/ENVIRONMENT.md](docs/ENVIRONMENT.md).

---

## Key features

| Feature | What it does |
|--------|----------------|
| **USB Builder** | Guided flows to verify, prepare, and update Ventoy-oriented USB maintenance media, with managed downloads and careful drive selection. The **USB Builder Profile** lets technicians enable or skip packs per run (ForgerEMS Portable App, Windows, Legacy Windows, Linux Rescue, Diagnostic Tools for USB, OEM Tools, macOS, Android, iOS / iPadOS). Core USB structure is required and cannot be turned off. The portable app profile routes to `_apps\ForgerEMS`, docs to `_docs\ForgerEMS`, and support folders to `_logs\ForgerEMS`. macOS, Android, and iOS / iPadOS are off by default and treat all media as manual. Unchecking a pack only skips seeding/updating it — files already on the USB are never deleted. |
| **Drive Validator** | Wizard-style non-destructive checks against a removable USB target's free space (Quick Safe Check, Sampled Capacity Check, Full Free-Space Validation) with a **live media-integrity tile map** to flag suspicious capacity, aliasing, short reads/writes, I/O errors, or failing regions before building a toolkit. The USB Builder tab keeps a compact summary card; **Open Drive Validator** launches the Drive Validator Wizard (Select target → Choose mode → Safety review → Running → Results). Safe modes write only into `.forgerems-drive-validator` on the chosen USB; never format, never delete user files, and never run against the Windows OS drive, system / boot / EFI / VTOYEFI partitions, or internal fixed disks by default. Results are advisory evidence for a technician, **not** a guarantee that the drive is genuine and **not** a direct inspection of NAND. Destructive full-media mode is **not available** in this build. |
| **USB Intelligence** | Measure write/read on a **safe removable** target, flag likely cached read samples honestly, map **which physical USB port** you used, and get practical guidance from benchmarks and topology hints (best-effort; varies by PC). Cache-suspected reads are treated as unverified and do not upgrade recommendation quality on their own. |
| **Dr. Forge (local hardware intake)** | ForgerEMS includes a safe bridge to a packaged **Dr. Forge CLI**. Select `drforge.exe` or place the package under an app-local Dr. Forge tools folder, then ForgerEMS verifies the release manifest/checksums when present and runs local report/archive commands through the CLI process boundary. Toolkit Manager can preview app-managed local reports with parsed read-only sections for known JSON schemas plus capped Raw Preview fallback. Missing packages show a setup-needed state. Unavailable readings stay **Unavailable**, not zero, and ForgerEMS does not claim full HWiNFO / CPU-Z / LibreHardwareMonitor parity. Deep telemetry such as fan RPM, voltage rails, EC/SuperIO/MSR readings remains unavailable until future safe providers or signed privileged components exist. See [docs/DR-FORGE-ADVANCED-SENSORS.md](docs/DR-FORGE-ADVANCED-SENSORS.md) and [docs/FORGEREMS-DR-FORGE-INTEGRATION.md](docs/FORGEREMS-DR-FORGE-INTEGRATION.md). |
| **Toolkit Manager** | Manifest-driven health for what is on your USB, now with technician-focused categories and catalog metadata (purpose, official URL, license/redistribution note, download/checksum status, distribution model, beta safety rating). Health checks distinguish verified managed tools, present-but-not-verified tools, manual/info shortcuts, shortcuts covered/suppressed by installed managed tools, and missing required items. **Verify Links** runs optional **HTTP metadata-only** checks (HEAD / tiny ranged GET): reachability, redirects, and trust hints — **no full downloads and no execution** of third-party payloads. |
| **Driver Hub** | Curated app-store-style hub for official GPU utilities, OEM support portals, chipset/network/audio driver pages, BIOS/firmware support links, and Linux driver guidance. Recommended cards use System Intelligence hints when available and show brand monograms, official-page/open-download actions, copy-link actions, and safe `.url` USB shortcuts. It does **not** auto-install drivers, auto-download OEM packages, upload service tags, or automate BIOS/firmware flashing. |
More context: [docs/ABOUT_FORGEREMS.md](docs/ABOUT_FORGEREMS.md).

**Command-center background:** The app uses one packaged static background image for public preview responsiveness.

**Forger Sensor Stack / Hardware X-Ray:** Forger Sensor Core is active by default and uses local Windows/native read-only sources. Deep Sensor Mode is optional and uses bundled reviewed local sensors when enabled, including LibreHardwareMonitorLib where packaged. No HWiNFO, AIDA64, CPU-Z, paid third-party tool, or separate user download is required for System Intelligence. ForgerEMS does not control fans, voltages, clocks, overclocking, undervolting, BIOS, or firmware. Missing readings are coverage limits, not failures. **Elevated Scan** is an optional deeper scan that asks Windows for administrator approval; Standard Scan is always available without it. Forger Sensor Service and Forger Deep Sensor Driver are future ForgerEMS-owned roadmap layers, not included as runtime dependencies in this build.

ForgerEMS does not sell user data. Diagnostics and support email must not include API keys, tokens, private documents, serial numbers, service tags, private paths, or raw exception chains.

## Cross-platform toolkit packs (Windows-first)

ForgerEMS is a Windows-first technician workbench. The macOS, Android, and iOS / iPadOS USB Builder packs are off by default. When a technician enables them, the catalog opens **official vendor pages** only:

- **macOS**: Apple support / `createinstallmedia` / recovery workflow shortcuts. Installers, DMGs, and PKGs remain **user-supplied** — drop them into `ISO\macOS\macOS-Manual-Installer-Drop\<version>\`. A compatible Mac may be required. ForgerEMS does **not** redistribute Apple installers.
- **Android**: official Android SDK Platform-Tools (adb / fastboot), Google Pixel factory / OTA images, AOSP documentation, and Samsung / Motorola / OnePlus support pages. OEM firmware is device, model, bootloader, region, and carrier specific and remains **user-supplied** — drop into `ISO\Android\Android-Manual-Firmware-Drop\<vendor>\`. Flashing the wrong firmware can wipe data or brick devices. ForgerEMS does **not** redistribute Android firmware and never uses random firmware mirrors.
- **iOS / iPadOS**: Apple Devices for Windows, Finder / iTunes, recovery mode, and Apple Configurator restore workflows. IPSW files are **user-supplied** — drop into `ISO\iOS-iPadOS\iOS-Manual-IPSW-Drop\<device>\`. Restores can erase devices. Activation Lock and Apple ID ownership are outside ForgerEMS. ForgerEMS does **not** use third-party IPSW indexes.

ForgerEMS never bypasses licenses, activation, DRM, account locks, or vendor authorization flows. Toolkit `.url` filenames use a fixed taxonomy: **AUTO DOWNLOAD / DOWNLOAD** (official, redistributable, machine-resolvable), **MANUAL DOWNLOAD** (official vendor page; user must choose / sign in / accept), **MANUAL MEDIA REQUIRED** (user supplies the ISO / installer / IPSW / firmware), **GUIDE** (official how-to), **INFO** (true reference material).

---

## Download (portable ZIP-first)

**Start with the portable ZIP from GitHub Releases unless you already know you want the installer.** The ZIP extracts to a runnable ForgerEMS app folder and includes legal/help docs for review before first launch.

1. Open **[Releases — Forger-Digital-Solutions/ForgerEMS](https://github.com/Forger-Digital-Solutions/ForgerEMS/releases)**.
2. Under **Assets**, download:
   - `ForgerEMS-v<version>.zip` portable app ZIP
3. **Wait** until the download finishes completely (see [docs/DOWNLOAD_TROUBLESHOOTING.md](docs/DOWNLOAD_TROUBLESHOOTING.md) if you see `.crdownload` or stalls).
4. Extract to a **short path** (for example `Desktop\ForgerEMS`).
5. Open the extracted folder and double-click **`START_HERE.bat`** or **`ForgerEMS.exe`**. The first launch shows the Terms of Use gate before the main tools unlock.

Optionally verify integrity using **`CHECKSUMS.sha256`** from the **same** release page before you run anything.

The standalone **`ForgerEMS-Setup-v<version>.exe`** on the release is the installed-app path and includes a license/terms page when built with Inno Setup.

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

**Pro labels** during beta are for feedback; licensing is not final. See [docs/RELEASE_NOTES_v1.2.4.md](docs/RELEASE_NOTES_v1.2.4.md) for this build.

---

## In-app updates

The app can check **public GitHub Releases** for this repo (no account required for public releases). It compares your installed build to the **highest eligible semantic-version release** (stable releases by default; drafts and malformed release metadata are never offered). **Nothing** is downloaded or installed unless **you** choose to. Details: [docs/UPDATE_SYSTEM.md](docs/UPDATE_SYSTEM.md).

---

## For developers

Prerequisites: Windows 10/11, .NET 8 SDK, PowerShell 5.1+, Inno Setup 6 (for installer builds).

The Inno script (`installer/ForgerEMS.iss`) offers an unchecked-by-default **Deep Sensor Mode** task, recorded under `HKLM\Software\ForgerEMS`.

```powershell
dotnet restore .\ForgerEMS.sln
dotnet build .\ForgerEMS.sln -c Release --no-incremental
dotnet test .\ForgerEMS.sln -c Release --no-build
```

Staging without compiling the installer:

```powershell
.\tools\build-release.ps1 -DryRun
```

Full local release (version comes from the repository `VERSION` file; a different `-Version` override is refused):

```powershell
.\tools\build-release.ps1
```

Without Inno Setup (skips installer; still stages `release\current\` app + backend + docs + `release.json` + checksums):

```powershell
.\tools\build-release.ps1 -SkipInstaller
```

Release layout, CI, and operator checklists: [RELEASE_PROCESS.md](RELEASE_PROCESS.md), [BETA_RELEASE_CHECKLIST.md](BETA_RELEASE_CHECKLIST.md), [BETA_TESTING_GUIDE.md](BETA_TESTING_GUIDE.md).

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
