# About ForgerEMS

**ForgerEMS** = **Forger Engineering Maintenance Suite**  
**Status:** ForgerEMS v1.2.3 Public Preview (`1.2.3-preview.1`)
**Built by:** Forger Digital Solutions  
**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com)

---

## What it is

ForgerEMS is a **Windows desktop technician workbench** centered on **USB toolkit life cycle**, **honest local hardware/health signals**, vendor-first guidance, and support workflow helpers — with **Kyra**, an in-app assistant that defaults to **offline**, practical answers.

---

## What is included

- **USB Builder** for safer Ventoy-oriented USB toolkit preparation and maintenance, with a **USB Builder Profile** that lets technicians pick which packs (ForgerEMS Portable App, Windows, Legacy Windows, Linux Rescue, Diagnostic Tools for USB, OEM Tools, macOS, Android, iOS / iPadOS) get seeded or refreshed. Core ForgerEMS USB structure is always required; the portable app routes to `_apps\ForgerEMS`; macOS, Android, and iOS / iPadOS are off by default and treat all media as manual
- **Drive Validator** (opens as the **Drive Validator Wizard** from the USB Builder tab) for wizard-style, non-destructive sampled writes/reads against a removable USB's free space with a live media-integrity tile map. Quick / Sampled / Full Free-Space modes flag suspicious capacity, aliasing, short reads/writes, I/O errors, or failing regions before you build a toolkit on the drive. Safe modes do not format and do not delete user files. Results are advisory evidence, not a certification, and they cannot directly inspect NAND. Destructive Full Media validation is not available in this build
- **Toolkit Manager** for manifest-driven managed/manual tool health
- **System Intelligence** for local hardware, health, network, storage, security, and resale-oriented summaries
- **Hardware X-Ray** for detected hardware and available sensor coverage
- **FlipValue** for transparent resale estimates and assumptions
- **Best Use / Device Fit** for practical device-fit and listing guidance
- **Kyra** for grounded local assistance, with optional online providers only when configured; after a System Intelligence scan, Kyra can answer many **hardware, upgrade, and parts** questions from local evidence, and use sanitized gateway research for current compatibility/pricing when enabled
- **Kyra Intelligence Network** for local-first repair memory and optional anonymous community learning foundations

Hardware X-Ray uses the local Forger Sensor Stack and local read-only providers to show detected hardware and available sensor coverage. Forger Sensor Core is active by default; Deep Sensor Mode may use bundled reviewed local sensor technology, including LibreHardwareMonitor where packaged and enabled. Forger Sensor Service and Forger Deep Sensor Driver are future ForgerEMS-owned roadmap layers, not external tool requirements. The command-center background is a packaged static image so the preview app stays responsive.

---

## Why it exists

Repair and resale workflows still waste hours on **wrong USB ports**, **mystery hardware**, **scattered tools**, and **half-finished ISO hygiene**. ForgerEMS bundles the recurring steps into one disciplined surface: build and verify USB media, benchmark and map ports where possible, scan the machine locally, keep toolkit manifests honest, and get guidance without sending your whole life story to the cloud.

---

## Who it is for

- Bench technicians and small repair shops  
- Laptop flippers and refurb operations  
- IT pros and homelabbers who live around USB sticks  
- Advanced users who outgrew “random forum ISO afternoon”

---

## Philosophy

- **Repair-first:** prioritize workflows that get a machine back to trustworthy use.  
- **Offline-capable:** local scans, local logs, and **Kyra offline** should work without signing up for anything.  
- **Technician-friendly:** fewer mystery toggles; clearer “manual required” paths where auto-download is not safe or legal.  
- **Portable ZIP-first distribution:** official releases emphasize a verified portable app ZIP (`ForgerEMS.exe`, `START_HERE.bat`, docs, checksums) while still publishing a separate installer for users who prefer installed mode.
- **Official sources only for cross-platform packs:** macOS, iOS / iPadOS, and Android catalog links point to Apple, Google / AOSP, and OEM vendor pages. ForgerEMS does not redistribute macOS installers, iOS / iPadOS IPSW files, Android OEM firmware, or legacy Windows ISOs, and does not use third-party IPSW indexes or firmware mirrors.

---

## Kyra in one paragraph

Kyra helps with **system diagnostics explanations**, **USB guidance**, **upgrade and release orientation** (pointing to official docs and GitHub Releases), and **toolkit/manifest questions** using **local context** first. System Intelligence is treated as local device evidence, not a substitute for external compatibility or price truth. When configured, live research/gateway tools can answer current-data and hardware-part questions with source/confidence labeling; when unavailable, Kyra should say so and avoid exact SKUs, prices, or compatibility claims. **Kyra AI Settings** exposes Gateway, BYOK, local AI, live tools, privacy/context controls, and sanitized diagnostics without making normal users read provider internals. Normal chat keeps provider/debug routing details out of the message body; beta diagnostics and support bundles keep the technical trail for troubleshooting.

Public beta can optionally use the ForgerEMS Kyra Gateway so testers can access limited cloud Kyra time without receiving owner provider API keys. Gateway access uses a revocable beta token and preserves local/offline fallback. **Realtime research** may use `POST /v1/kyra/research` so current-data questions are answered with server-side tools/providers instead of shipping provider keys in the desktop app.

Direct BYOK provider keys are optional. Session keys last only until the app closes; saved keys use Windows protected local storage when available and are never written as plaintext appsettings. Environment variable setup remains supported for advanced operators. Do not send API keys, beta tokens, private documents, serials, service tags, or private paths in support email or screenshots.

---

## Kyra Intelligence Network

Kyra Intelligence Network is **Local-first repair memory + optional anonymous community learning**. Local Kyra Memory can remember sanitized repair and diagnostic patterns for this PC, such as machine class, hardware category summary, health score band, issue/warning category, user-confirmed fixes, USB target safety result, best-use category, resale prep category, scan timestamp, and confidence.

Community learning is off by default and must be explicitly enabled by the user. Community sharing is not active in this release — the setting is visible for preview only. Users can keep Kyra local-only, export Kyra memory, or delete Kyra memory from Settings.

ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

---

## Beta status

Prerelease software is provided **as-is**; behavior may change between builds. Prefer **GitHub Releases** portable ZIPs for testing. Read [TERMS_OF_USE.md](TERMS_OF_USE.md), [PRIVACY_AND_DATA_HANDLING.md](PRIVACY_AND_DATA_HANDLING.md), and [LEGAL_NOTICES.md](LEGAL_NOTICES.md).

ForgerEMS is **technician-assist software, not a replacement for professional judgement**. Public Preview builds **do not promise** guaranteed repair, guaranteed data recovery, guaranteed malware removal, guaranteed hardware diagnosis, guaranteed driver/component compatibility, guaranteed pricing or marketplace accuracy, or guaranteed legal/regulatory compliance. Technicians are responsible for confirming official vendor links, licensing, and safe use of any third-party tool or image surfaced by the app. System Intelligence and Hardware X-Ray may report **Unknown**, **NotExposed**, or **Inferred** when firmware, drivers, permissions, or sensor providers do not expose data — missing readings are coverage limits, not failures. Support bundles are user-controlled; ForgerEMS attempts to redact local usernames, private paths, API keys, tokens, and product keys, but you should still review every bundle before sharing it, and the app does not automatically upload bundles, sensor data, scan reports, or USB inventories anywhere.

Third-party notices are included in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md), [THIRD-PARTY-SENSOR-NOTICES.md](THIRD-PARTY-SENSOR-NOTICES.md), and packaged sensor notice files under `providers/sensors/`.

ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, hardware vendors, driver vendors, or third-party tools referenced in the app. Names are used only to identify compatibility, official resources, or supported technician workflows.

---

## Learn more

- [FAQ.md](FAQ.md)  
- [DOWNLOAD_TROUBLESHOOTING.md](DOWNLOAD_TROUBLESHOOTING.md)  
- [UPDATE_SYSTEM.md](UPDATE_SYSTEM.md)  
- [BETA_TESTER_QUICKSTART.md](BETA_TESTER_QUICKSTART.md)  
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)  
