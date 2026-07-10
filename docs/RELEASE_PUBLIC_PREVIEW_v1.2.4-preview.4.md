# ForgerEMS v1.2.4 Public Preview

## ForgerEMS v1.2.4-preview.4 — Public Preview

ForgerEMS is a locally validated public preview of a Windows maintenance, diagnostics, recovery, driver-guidance, and USB toolkit application developed by Forger Digital Solutions.

For technicians, small businesses, repair benches, resellers, and advanced home users, this preview combines safer Ventoy-oriented USB workflows, removable-drive validation, local device summaries, Driver Hub guidance, and Kyra's local-first assistance.

## Installation choices

- **Portable ZIP (recommended first):** download `ForgerEMS-v1.2.4-preview.4.zip`, wait for the download to finish, extract it to a short local path, then run `START_HERE.bat` or `ForgerEMS.exe`.
- **Installer:** `ForgerEMS-Setup-v1.2.4-preview.4.exe` installs under Program Files and requires administrator approval because Windows protects that location. Normal app use does not require administrator rights; elevated scans ask Windows for approval when requested.

Windows 10 version 19041 or later or Windows 11, x64 hardware, and sufficient local storage for the selected USB workflow are required. Review `docs/FORGEREMS-INSTALLER.md` for uninstall behavior and `docs/DOWNLOAD_TROUBLESHOOTING.md` for download help.

## What is included

- USB Builder and Toolkit Manager for Ventoy-oriented maintenance-media workflows.
- Non-destructive removable-drive validation and USB/port benchmark guidance.
- Local Device Snapshot / Hardware X-Ray, with readings shown only when Windows or an enabled local provider exposes them.
- Driver Hub links and guidance; it does not automatically install drivers or flash firmware.
- A local Dr. Forge CLI bridge when a separately packaged, trusted CLI is configured.
- Kyra local-first help, with optional online providers only when an operator configures them.

## Privacy and safety

Telemetry and crash reporting default to off. Reports, logs, support bundles, sensor data, and Kyra context stay local unless you choose to export or share them. Review every export before sending it. ForgerEMS does not collect payment information and does not ask users to bypass Windows security.

USB and hardware operations are safety-gated, but this is preview software. Back up important data, verify the selected removable target, and follow vendor documentation. Do not use ForgerEMS as a guarantee of media authenticity, hardware condition, driver compatibility, or data recovery.

## Known limitations

- Some sensor readings are unavailable on some systems; unavailable does not mean zero or failed hardware.
- Advanced deep telemetry, privileged services, and a signed driver are not included.
- Community intelligence upload is off/disabled in this phase.
- Dr. Forge advanced-sensor depth and automatic download are not provided by this build.
- USB topology and benchmark guidance are best-effort and hardware-dependent.

See `docs/SENSOR-LIMITATIONS.md` and `docs/DR-FORGE-ADVANCED-SENSORS.md`.

## Reported local validation results

This build was locally validated with a Release build reporting zero warnings and errors, 1,857 automated tests passing, a NuGet vulnerability audit reporting no known vulnerable direct or transitive packages, strict secret scanning, release validation, and backend/Ventoy verification. These are reported local validation results, not an independent security audit or certification.

## Verify your download

Download `CHECKSUMS.sha256` from the same release page. In PowerShell, run `Get-FileHash .\ForgerEMS-v1.2.4-preview.4.zip -Algorithm SHA256` and compare the result to the ZIP entry in `CHECKSUMS.sha256`. Do not run incomplete `.crdownload` files.

## Feedback and support development

Report bugs using `docs/BETA_ISSUE_REPORT_TEMPLATE.md` or the configured support contact. Include the app version, Windows version, steps, expected result, and sanitized evidence. Never send passwords, API keys, tokens, serial numbers, private documents, or raw sensitive logs.

Help build free, privacy-conscious software for technicians, small businesses, and everyday users. Donations, if a public support platform is later configured, are voluntary. They do not purchase ownership, guaranteed features, priority support, investment returns, or equity.

ForgerEMS is preview software provided as-is; behavior, packaging, and availability may change.
