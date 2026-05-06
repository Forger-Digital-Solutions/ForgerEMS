# ForgerEMS — Legal / preview notice (practical disclaimer)

**Copyright © 2026 Forger Digital Solutions. All rights reserved.**

**Not legal advice.** This document is ordinary software disclaimer language for operators and beta testers; it is not legal counsel.

---

## Public Preview / prerelease — as-is

ForgerEMS Public Preview and beta-line builds are prerelease software provided **“as-is”**, without warranties express or implied, to the extent permitted by law. **Use at your own risk.**

---

## No collection of API keys; no “hidden” product telemetry

ForgerEMS **does not** implement a background “phone home” telemetry product that sends your usage analytics to Forger Digital Solutions.

- The app **does not** harvest or upload **API keys** or **passwords** you might use elsewhere on your PC.  
- **Session-only** credentials used inside the app for optional online Kyra paths are described in [PRIVACY.md](PRIVACY.md) and operator documentation; they are **not** written to ordinary settings files as a default design goal for those paths.

**Expected network use (not hidden “analytics”):** HTTPS calls to **GitHub** when you use **in-app update checks**, calls to the **ForgerEMS Gateway** when beta Gateway mode is configured, and calls to **third-party AI endpoints** only when an operator has enabled those Kyra providers. See [UPDATE_SYSTEM.md](UPDATE_SYSTEM.md) and [KYRA_PROVIDER_ENVIRONMENT_SETUP.md](KYRA_PROVIDER_ENVIRONMENT_SETUP.md).

Normal workflows store **logs and reports on your machine** under `%LOCALAPPDATA%\ForgerEMS\`. **You** choose what to email for support.

---

## USB, storage, and partitions

You are responsible for selecting the correct **USB**, **volume**, and **partition**. USB preparation and related operations can **erase or modify** data depending on the operation. ForgerEMS includes safety checks and confirmations but **cannot guarantee** protection against user error, faulty hardware, or operating-system quirks.

---

## Downloads, ISOs, and third-party tools

You are responsible for verifying **integrity** (checksums when published) and **legitimacy** of anything you download — including third-party installers, ISOs, and manifest-listed utilities. ForgerEMS may **reference**, **integrate with**, **download**, or **guide** you to third-party tools; those tools remain under their **own licenses and terms**. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

---

## LibreHardwareMonitor and sensor provider notices

ForgerEMS may include **LibreHardwareMonitorLib** as a bundled local read-only sensor provider for **Hardware X-Ray** when **Deep Sensor Mode** is enabled.

- License: **MPL-2.0**
- License path: `providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt`
- Third-party notice path: `providers/sensors/THIRD-PARTY-NOTICES.txt`
- Sensor notice documentation: [THIRD-PARTY-SENSOR-NOTICES.md](THIRD-PARTY-SENSOR-NOTICES.md)

ForgerEMS proprietary code remains separate from MPL-covered LibreHardwareMonitor code. If ForgerEMS modifies MPL-covered LibreHardwareMonitor files and distributes them, those modified files must be made available as required by MPL-2.0.

ForgerEMS does **not** redistribute HWiNFO, AIDA64, CPU-Z, or other proprietary sensor tools unless a license explicitly allows it.

Sensor providers are read-only. ForgerEMS does **not** control fans, voltage, clocks, overclocking, undervolting, BIOS, or firmware. There is no warranty that every sensor is exposed; firmware/vendor/admin limitations may prevent some readings. Unavailable readings are coverage limits, not hardware failures.

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

---

## Limitation of liability

To the extent permitted by law, Forger Digital Solutions is **not liable** for indirect, incidental, special, consequential, or punitive damages, or for loss of profits, data, or goodwill, arising from use or inability to use the beta software — including misuse of tools suggested or launched by the user.

---

## Support communications

**Beta feedback:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com)

**Do not send** API keys, tokens, passwords, product keys, serial numbers, private documents, or sensitive files in email.

**Security vulnerabilities:** use the process in [SECURITY.md](../SECURITY.md) (private report), not the general beta inbox.
