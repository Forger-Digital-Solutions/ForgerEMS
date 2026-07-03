# ForgerEMS — Legal / preview notice (practical disclaimer)

**Copyright © 2026 Forger Digital Solutions. All rights reserved.**

**Not legal advice.** This document is ordinary software disclaimer language for operators and beta testers; it is not legal counsel.

**Current v1.2.4-preview.1 legal docs:** see [TERMS_OF_USE.md](TERMS_OF_USE.md), [PRIVACY_AND_DATA_HANDLING.md](PRIVACY_AND_DATA_HANDLING.md), [LEGAL_NOTICES.md](LEGAL_NOTICES.md), and [USER_CONSENT_FLOW.md](USER_CONSENT_FLOW.md). Those files are the current first-run consent and packaging references; this page remains as a practical preview notice.

---

## Public Preview / prerelease — as-is

ForgerEMS Public Preview and beta-line builds are prerelease software provided **“as-is”**, without warranties express or implied, to the extent permitted by law. **Use at your own risk.**

---

## No collection of API keys; no “hidden” product telemetry

ForgerEMS **does not** implement a background “phone home” telemetry product that sends your usage analytics to Forger Digital Solutions.

- The app **does not** harvest or upload **API keys** or **passwords** you might use elsewhere on your PC.  
- **Session-only** credentials used inside the app for optional online Kyra paths are described in [PRIVACY.md](PRIVACY.md) and operator documentation; they are **not** written to ordinary settings files as a default design goal for those paths.

**Expected network use (not hidden “analytics”):** HTTPS calls to **GitHub** when you use **in-app update checks**, calls to the **ForgerEMS Gateway** (including **`/v1/kyra/research`** and **`/v1/kyra/status`** when enabled) when beta Gateway mode is configured, and calls to **third-party AI endpoints** only when an operator has enabled those Kyra providers. See [UPDATE_SYSTEM.md](UPDATE_SYSTEM.md) and [KYRA_PROVIDER_ENVIRONMENT_SETUP.md](KYRA_PROVIDER_ENVIRONMENT_SETUP.md).

Normal workflows store **logs and reports on your machine** under `%LOCALAPPDATA%\ForgerEMS\`. **You** choose what to email for support.

---

## USB, storage, and partitions

You are responsible for selecting the correct **USB**, **volume**, and **partition**. USB preparation and related operations can **erase or modify** data depending on the operation. ForgerEMS includes safety checks and confirmations but **cannot guarantee** protection against user error, faulty hardware, or operating-system quirks.

---

## Drive Validator

ForgerEMS includes a **Drive Validator** tool, surfaced as the **Drive Validator Wizard** from the USB Builder tab. It is provided to help technicians flag suspicious media before they trust it for toolkit work.

- **Safe modes (Quick Safe Check, Sampled Capacity Check, Full Free-Space Validation)** write temporary ForgerEMS test files into the **free space** of the selected removable USB target and read them back. They **do not format** the drive, **do not delete** existing user files, and only create or remove files inside a `.forgerems-drive-validator` folder on the target.
- Drive Validator operates at the **file-system level**. It **cannot directly inspect NAND chips** through normal Windows file I/O. It **does not certify** a drive is genuine, original, healthy, or fit for any specific use. A passing result is **advisory evidence for a technician**, not a 100% authenticity certificate. Sophisticated fake-capacity media can still evade a small sample.
- The wizard can detect many fake-capacity, aliasing, failing-media, short-read/write, zero/0xFF readback, and per-region I/O error patterns. **Full Free-Space Validation** is the **strongest non-destructive mode** but is slow, produces heavy writes, and requires an explicit acknowledgement checkbox before it can start.
- **Destructive Full Media mode** is **not available in this build**. If it is ever enabled in a future build, it will require an explicit typed confirmation and will erase the entire drive.
- ForgerEMS does **not** run Drive Validator against the Windows OS drive, system / boot / EFI / recovery / VTOYEFI partitions, BitLocker-encrypted volumes, or internal fixed disks by default.
- A drive that **fails** Drive Validator **should not be trusted for a ForgerEMS / Ventoy toolkit**. Consider replacing the drive.
- Drive Validator results are **advisory evidence for a technician**, not a warranty, conformance test, or replacement for vendor diagnostics. **Back up important data** before any heavy validation. ForgerEMS is **not responsible** for failing media, data loss, or any destructive action the user explicitly confirms.

---

## Downloads, ISOs, and third-party tools

You are responsible for verifying **integrity** (checksums when published) and **legitimacy** of anything you download — including third-party installers, ISOs, and manifest-listed utilities. ForgerEMS may **reference**, **integrate with**, **download**, or **guide** you to third-party tools; those tools remain under their **own licenses and terms**. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, hardware vendors, driver vendors, or third-party tools referenced in the app. Names are used only to identify compatibility, official resources, or supported technician workflows. Third-party logos, icons, and product marks must not be used as decorative ForgerEMS artwork unless explicit written permission or license proof is kept in the repo.

ForgerEMS catalog entries use a fixed set of action labels in their `.url` filenames or display: **DOWNLOAD** / **AUTO DOWNLOAD** (official, redistributable, machine-resolvable source), **MANUAL DOWNLOAD** (official vendor page where the user must choose / sign in / accept terms), **MANUAL MEDIA REQUIRED** (user must supply ISO / installer / IPSW / firmware themselves), **GUIDE** (official how-to instructions), and **INFO** (true reference material). ForgerEMS will not auto-download an item whose source is non-redistributable, requires a license acceptance flow, or requires a device / model / carrier choice.

---

## Driver Hub, drivers, BIOS, and firmware

Driver Hub is an official-link-first catalog. It opens vendor/project pages, opens official app/download pages only when that action is explicitly shown, copies official links, and writes `.url` shortcuts to a selected USB when you choose that action. It does **not** auto-install drivers, auto-run installers, auto-download model-specific OEM driver packages, submit service tags or serial numbers, compare installed driver versions, or automate BIOS/firmware flashing.

Driver, BIOS, and firmware compatibility remains the responsibility of the technician/user. Confirm the exact model, power and battery/AC state, vendor instructions, warranty terms, and rollback/recovery procedure before firmware updates. A Driver Hub recommendation means "based on detected vendor/GPU/CPU/platform," not "needed," "outdated," or "safe to install."

---

## Cross-platform toolkit packs (macOS, Android, iOS / iPadOS)

ForgerEMS is Windows-first. The macOS, Android, and iOS / iPadOS USB Builder packs are off by default and treat all media as **manual media required**.

- ForgerEMS **does not redistribute** legacy Windows ISOs, macOS installers / DMGs / PKGs, iOS / iPadOS IPSW files, or Android OEM firmware (Samsung, Motorola, OnePlus, etc.). The user supplies that media themselves.
- ForgerEMS **does not auto-download** files from third-party IPSW indexes, firmware mirrors, or unofficial software hosts.
- macOS, Android, and iOS / iPadOS catalog entries link only to official **Apple support**, **Google / Android / AOSP**, and OEM vendor pages.
- Mobile flashing / restore operations can **erase data or brick devices**. **Apple Activation Lock**, **Google FRP**, **OEM account locks**, and **ownership verification** are outside ForgerEMS — the app does not bypass any vendor authorization, DRM, account lock, or licensing flow.
- Unchecking a USB Builder pack only **skips seeding/updating** that pack on the next Setup USB or Update USB run. It does **not delete** files already on the USB.

---

## Forger Sensor Stack and sensor provider notices

ForgerEMS hardware intelligence is based on the local **Forger Sensor Stack**. **Forger Sensor Core** is active by default and uses Windows/native read-only sources. **Forger Sensor Service** is a future optional local service and is not installed in this build. **Forger Deep Sensor Driver** is roadmap only and is not included in this build.

ForgerEMS may include **LibreHardwareMonitorLib** as a bundled local read-only sensor provider for **Hardware X-Ray** when **Deep Sensor Mode** is enabled.

- License: **MPL-2.0**
- License path: `providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt`
- Third-party notice path: `providers/sensors/THIRD-PARTY-NOTICES.txt`
- Sensor notice documentation: [THIRD-PARTY-SENSOR-NOTICES.md](THIRD-PARTY-SENSOR-NOTICES.md)

ForgerEMS proprietary code remains separate from MPL-covered LibreHardwareMonitor code. If ForgerEMS modifies MPL-covered LibreHardwareMonitor files and distributes them, those modified files must be made available as required by MPL-2.0.

ForgerEMS does **not** require or redistribute HWiNFO, AIDA64, CPU-Z, or other proprietary sensor tools unless a license explicitly allows it.

Sensor providers are read-only. ForgerEMS does **not** control fans, voltage, clocks, overclocking, undervolting, BIOS, or firmware. There is no warranty that every sensor is exposed; firmware/vendor/admin limitations may prevent some readings. Unavailable readings are coverage limits, not hardware failures, and ForgerEMS must not generate fake sensor values.

---

## Pro / licensing

**Pro** or preview features may appear during beta for feedback. **Licensing is not enforced** in this beta line unless separately announced. Commercial terms are not final from preview labels alone.

---

## Acceptable use

Do not use ForgerEMS for unauthorized access, bypassing security on devices you do not own, malware, credential theft, software piracy, or other unlawful activity.

---

## System Intelligence and estimates

Hardware, diagnostics, Hardware X-Ray sensor coverage, and resale-oriented summaries are **informational** and may be incomplete or inaccurate. Confirm critical decisions with additional testing.

---

## Optional online providers

Kyra can use offline/local answers by default. If an operator enables ForgerEMS Gateway or online AI/API providers, prompts and optional sanitized context may be sent to the configured gateway/provider under that service's terms. Provider API keys must not be embedded in the desktop app, installer, release ZIP, appsettings, `.env.example`, docs, source code, or registry defaults. Do not paste API keys, tokens, passwords, product keys, private documents, or sensitive customer data into Kyra or support messages.

ForgerEMS Beta Gateway tokens are revocable access tokens, not provider API keys. They should be limited, rotatable, and replaceable during beta without shipping provider keys to testers.

Current prices, availability, latest versions, weather, news, stocks, crypto, and similar realtime facts require a configured live provider/tool. Offline/local Kyra may provide local observations and verification steps, but must not present invented realtime data as fact.

---

## Kyra Intelligence Network

Kyra Intelligence Network is **Local-first repair memory + optional anonymous community learning**.

- Local Kyra Memory stores sanitized machine-scoped repair notes on the user's PC.
- Anonymous community intelligence sharing is optional, off by default, and requires explicit opt-in.
- Community sharing is not active in this release. The setting is visible for preview only; no community data leaves the device in this build.
- Users can keep Kyra local-only, opt out in Settings, export Kyra memory, and delete Kyra memory.

ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

---

## Technician-assist scope (Public Preview)

ForgerEMS is **technician-assist software**, not a replacement for professional judgement. The Public Preview line specifically **does not promise**:

- Guaranteed **repair** of any device, OS, application, or partition.
- Guaranteed **data recovery** from failing, corrupted, encrypted, or wiped media.
- Guaranteed **malware removal** or guaranteed **clean** result after a scan.
- Guaranteed **hardware diagnosis** (sensors, drives, batteries, GPUs, etc.).
- Guaranteed **driver / component / accessory compatibility**.
- Guaranteed **pricing, marketplace, or resale-value accuracy**.
- Guaranteed **legal, regulatory, or licensing compliance** for any third-party tool you decide to download or use through ForgerEMS.

Technicians, refurbishers, and operators remain responsible for confirming official vendor links, licensing terms, and safe use before acting on any output, suggestion, or auto-prepared workflow from this app. Third-party tools surfaced or downloaded through ForgerEMS (Ventoy, OS images, vendor utilities, recovery images, mobile tooling, etc.) remain under their own licenses and terms — ForgerEMS does not claim ownership of them and does not represent itself as a legal authority on them.

---

## Limitation of liability

To the extent permitted by law, Forger Digital Solutions is **not liable** for indirect, incidental, special, consequential, or punitive damages, or for loss of profits, data, or goodwill, arising from use or inability to use the beta software — including misuse of tools suggested or launched by the user.

---

## Support communications

**Beta feedback:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com)

**Do not send** API keys, tokens, passwords, product keys, serial numbers, private documents, or sensitive files in email.

**Security vulnerabilities:** use the process in [SECURITY.md](../SECURITY.md) (private report), not the general beta inbox.
