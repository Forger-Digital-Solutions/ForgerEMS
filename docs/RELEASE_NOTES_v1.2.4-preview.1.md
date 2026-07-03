# ForgerEMS v1.2.4-preview.1 Release Notes

ForgerEMS `v1.2.4-preview.1` is a preview/pre-release build for Windows technicians.

ForgerEMS is a local-device support workflow tool, USB toolkit/profile builder, Driver Hub/vendor guidance helper, port/USB mapping and drive-check workflow helper, and preview build by Forger Digital Solutions.

## Highlights

- USB Builder Profile picker opens real windows/dialogs and picker choices affect selected counts, estimates, and final profile selectors.
- ForgerEMS Portable App is included as a default technician USB profile category and routes to `_apps\ForgerEMS`, with docs under `_docs\ForgerEMS`.
- Live Logs cleanup keeps the persistent Live Logs panel under the tabs and does not re-add a dedicated Live Logs tab.
- The removed shell Internet widget remains removed, and the retired Network Pulse and Deep Sensor Mode / Deep Sense settings sections are no longer shown in Settings. The old Network Pulse implementation is not compiled into the app; stale saved files are ignored.
- USB plug/unplug refresh is event-driven with a short debounce, so connected device changes should appear in the USB lists in about a second.
- Port / USB Intelligence is now a results dashboard: connected USB devices, Mapping Wizard / Benchmark / Drive Validator actions, latest result cards, and a safe battery health + system specs summary.
- Toolkit Manager adds the first safe Dr. Forge Intake bridge for packaged CLI artifacts: selected/app-local `drforge.exe`, manifest/checksum inspection, readiness checks, local report/archive generation, unavailable readings rendered as Unavailable, and no full hardware-monitor parity claim.
- Port Mapping Wizard benchmark readings remain visible in the wizard final screen; benchmarks do not run automatically, and the dashboard shows the latest saved result.
- PC/laptop wording is limited to battery health, system specs, local device summaries, USB mapping, benchmark, and drive validation. It does not claim broad PC diagnostics, hardware stress, fan probing, thermal probing, or sensor deep scans.
- Terms of Use, Privacy/Data Handling, Legal/About docs, third-party notices, installer license page, and a First-run Terms gate were added.
- First-run Terms checkbox notices wrap inside the gate and remain readable at 1366x768; the header shows both the document revision and the ForgerEMS version it applies to.
- Kyra memory export, Kyra Intelligence memory export, and support bundle creation require a separate sharing/local-context confirmation.

## Downloads

`release/current` is expected to include:

- `ForgerEMS-Setup-v1.2.4-preview.1.exe`
- `ForgerEMS-v1.2.4-preview.1.zip` portable app ZIP
- `CHECKSUMS.sha256`

## Known Preview Limits

ForgerEMS is not production-ready enterprise software, a magic automatic fixer, a certified repair substitute, attorney-reviewed legal guidance, a complete hardware telemetry suite, or a hardware stress/thermal/fan diagnostic tool. Driver/vendor guidance is informational and vendor-first. Some features rely on internet access, vendor sites, manual downloads, user-supplied files, system permissions, or third-party licenses.

## Issue Reporting

Report issues with exact app version, Windows version, steps to reproduce, expected result, actual result, and redacted logs/support bundles only after review. Do not send secrets, private customer data, product keys, recovery keys, API keys, or private documents.
