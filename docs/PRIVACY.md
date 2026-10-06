# ForgerEMS — Privacy overview

**Built by Forger Digital Solutions.** This is a practical summary of how the app handles data on your PC; it is **not** a substitute for a formal privacy policy review by your counsel.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) — never send secrets in email.

**Current v1.2.4 data-handling docs:** see [PRIVACY_AND_DATA_HANDLING.md](PRIVACY_AND_DATA_HANDLING.md), [TERMS_OF_USE.md](TERMS_OF_USE.md), [LEGAL_NOTICES.md](LEGAL_NOTICES.md), and [USER_CONSENT_FLOW.md](USER_CONSENT_FLOW.md). ForgerEMS keeps Terms acceptance local and requires a separate warning before support bundles, logs, or exported reports are packaged for sharing.

---

## What is **not** collected by Forger Digital Solutions

Under normal, **offline-first** use:

- **Telemetry / crash reporting** default to **off** unless you explicitly set `FORGEREMS_TELEMETRY_ENABLED` / `FORGEREMS_CRASH_REPORTING_ENABLED` (see [ENVIRONMENT.md](ENVIRONMENT.md)).
- Forger Digital Solutions **does not** operate a hidden analytics pipeline that continuously uploads your desktop activity.  
- The app **does not** collect or centralize **API keys**, **passwords**, or **product keys** for upload to Forger Digital Solutions.  
- **Local scans** (System Intelligence, diagnostics, toolkit health) produce files on **your disk** — they are **not** silently exfiltrated to us when you are simply using those features.

**In-app update checks** query **public GitHub Releases** metadata over HTTPS when you use Settings or scheduled checks — that is GitHub’s API, not a private Forger “telemetry” endpoint. See [UPDATE_SYSTEM.md](UPDATE_SYSTEM.md).

---

## What **may** be stored locally

ForgerEMS stores configuration, reports, and logs on your machine, typically under:

- **`%LOCALAPPDATA%\ForgerEMS\`**  
- Subfolders often include **`Runtime\reports`**, **`Runtime\logs`**, and **`logs`**.

These files can contain **paths**, **device names**, **diagnostics**, and similar technician-oriented detail. **Review and redact** before you attach anything to a bug report.

---

## System Intelligence, Hardware X-Ray, and Deep Sensor Mode

ForgerEMS runs diagnostics locally through the **Forger Sensor Stack**. **Forger Sensor Core** is active by default and uses local Windows/native sources. **Deep Sensor Mode** reads supported local hardware sensor data only while the app is running or System Intelligence / Hardware X-Ray scans are executed.

- Sensor data is **not sold**.
- Sensor data is **not automatically uploaded**.
- Reports are **not automatically sent** to support.
- You choose when to copy, export, or share reports.
- Deep Sensor Mode is read-only. It does not control fans, voltage, clocks, overclocking, undervolting, BIOS, firmware, or device settings.
- Deep Sensor Mode is not permanent administrator permission. Windows may ask for UAC approval at runtime when you choose Elevated Scan, and security policy can still block that approval.
- Forger Sensor Service is a future optional local component and is not installed in this build.
- Forger Deep Sensor Driver is roadmap only and is not included in this build.
- ForgerEMS does not require user-downloaded HWiNFO, AIDA64, CPU-Z, or paid third-party sensor tools for hardware intelligence.

Reports may include hardware model, CPU/GPU/RAM/storage info, battery info, network adapter details, USB device details, Windows version, Forger Sensor Stack state, source labels, provider status, sensor limitations, and diagnostic notes.

Default support reports should be redacted where supported, but you should still review reports before sharing. Do not send product keys, serial numbers, service tags, API keys, tokens, passwords, private documents, or sensitive personal files to support.

---

## Dr. Forge Intake

ForgerEMS can bridge to a packaged **Dr. Forge CLI** when you select `drforge.exe` or place the package in an app-local Dr. Forge tools folder. The bridge stores only the selected CLI path, last readiness state, and last local report/archive paths in Runtime config.

Generated Dr. Forge reports and archives stay local under `%LOCALAPPDATA%\ForgerEMS\Runtime\reports\drforge`. The in-app recent report history reads that app-managed folder only; it does not crawl Documents or arbitrary user folders. Reports may include local device/context information, sensor availability, findings, notes, and unavailable telemetry reasons. Unavailable readings are displayed as **Unavailable**, not zero, and ForgerEMS does not claim full HWiNFO / CPU-Z / LibreHardwareMonitor parity.

Dr. Forge report/archive files are included in support bundles only when you explicitly choose to include them and confirm the support-bundle export. Review the report/archive before sharing.

---

## Logs and sharing

Full local logs may contain sensitive context. Before you share:

1. Open **View Full Logs** (or your log folder) and **read** what you are about to send.  
2. Prefer **sanitized** excerpts or in-app “safe summary” features when available.  
3. **Never** paste API keys, tokens, or private documents into email or chat.

---

## Third-party network endpoints

**Driver Hub:** The desktop app opens official vendor/project pages when you choose actions such as **Get**, **Open Driver Page**, **Open Support Page**, or **Open Guidance**, copies official URLs when you choose **Copy Link**, and writes local `.url` shortcuts to the selected USB when you choose **Add Shortcut**. It does **not** auto-install drivers, auto-run installers, auto-download model-specific OEM packages, auto-flash BIOS/firmware, submit service tags/serial numbers, or add device identifiers to vendor URLs. Driver Hub logs use the card name and relative USB shortcut path; normal log redaction still applies.

**Toolkit Manager → Verify Links:** When you opt in, the desktop app issues short **HEAD** or minimal **ranged GET** requests to official URLs from your toolkit manifest/catalog so it can record HTTP metadata (status, redirects, length hints). **Those checks do not download complete payloads and do not execute downloaded third-party files.** Logs avoid embedding raw query strings or secret-bearing URLs.

---

## Third-party tools

Tools you install separately are governed by their own policies.

---

## Cross-platform toolkit packs (macOS, Android, iOS / iPadOS)

ForgerEMS is Windows-first. The macOS, Android, and iOS / iPadOS USB Builder packs are off by default and require manual media.

- ForgerEMS **does not redistribute** macOS installers, DMGs, PKGs, iOS / iPadOS IPSW files, or Android OEM firmware.
- ForgerEMS **does not auto-download** files from third-party IPSW indexes, firmware mirrors, or unofficial software hosts.
- The catalog only links to **official Apple, Google, AOSP, Samsung, Motorola, and OnePlus** support pages and Android Platform-Tools.
- User-supplied installers, IPSW files, and firmware live in their drop folders on the user's USB; the user remains responsible for legality, licensing, and device suitability.
- Mobile flashing / restore operations can **erase data or brick devices**. **Apple Activation Lock**, **Google FRP**, **OEM account locks**, and **ownership verification** are outside ForgerEMS — the app does not bypass any vendor authorization or DRM flow.

## Drive Validator

The optional **Drive Validator** tool, opened from the USB Builder tab as the **Drive Validator Wizard**, writes temporary ForgerEMS test files into the free space of a selected removable USB target so it can read them back and look for verification errors, aliasing, short reads/writes, or suspicious capacity behavior. The wizard's media-integrity map is computed in-memory and never reads or transmits the contents of your existing files.

- Safe modes create files **only** inside a `.forgerems-drive-validator` folder on the chosen USB target and remove them afterward. They do **not** read your existing files, do **not** delete user data, and do **not** format the drive.
- Cached results are written to `%LOCALAPPDATA%\ForgerEMS\Runtime\cache\drive-validation-results.json` and contain: target volume root, drive model, file system, label, total/free-space sizes at the time of the run, a best-effort volume serial, a composite identity fingerprint, and the run's evidence summary (region/sample count, bytes written/verified, write/read speed, mismatch / alias-flag / I/O-error counters, per-region map summary, and the cleanup status string). The cached file does **not** include the contents of user files, and it does **not** include API keys, tokens, passwords, or product keys.
- The cache is keyed by a composite identity (root path + best-effort volume serial + drive model + reported size + label) so a different drive that mounts on the same letter is **not** treated as already validated. Entries older than 30 days are ignored.
- The wizard's region/tile data and signatures are deterministic ForgerEMS markers — they do not contain user data. The wizard does not upload validation results anywhere; the Copy summary action writes a plain-text technician report to the local clipboard only.
- Drive Validator results may be summarized in support bundles when you choose to export one. Review the bundle before sharing.

---

## USB Builder Profile persistence

When a technician changes the USB Builder Profile selection, ForgerEMS saves the choice locally at `%LOCALAPPDATA%\ForgerEMS\Runtime\config\usb-builder-profile.json`. That file contains only the list of included pack IDs (for example `core`, `windows`, `legacy-windows`, `linux-rescue`, `diagnostics`, `oem-tools`, `macos`, `android`, `ios-ipados`). It does not contain hardware identifiers, USB serial numbers, or personal information. It is not uploaded automatically.

Unchecking a pack only skips seeding/updating it on the next Setup USB or Update USB run. **It does not delete any file already on the USB.** Existing user-supplied installers, IPSW files, firmware packages, and drop-folder contents are left alone.

---

## Beta

Privacy-related behavior may change between builds; check in-app **Settings → App updates** and the current docs for the behavior on your build.
