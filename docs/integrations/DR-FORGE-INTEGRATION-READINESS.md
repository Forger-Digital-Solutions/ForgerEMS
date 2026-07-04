# ForgerEMS / Dr. Forge integration readiness

Status: ForgerEMS ships a safe, local, user-mode Dr. Forge CLI bridge. Dr. Forge remains a separate companion app/tool. Dr. Forge driver support is dev-foundation / contract-first only and is **not** part of any ForgerEMS release.

This document is the ForgerEMS-side readiness summary. The packaged CLI contract lives in [FORGEREMS-DR-FORGE-INTEGRATION.md](../FORGEREMS-DR-FORGE-INTEGRATION.md); the sensor roadmap lives in [DR-FORGE-ADVANCED-SENSORS.md](../DR-FORGE-ADVANCED-SENSORS.md).

## What ForgerEMS can do with Dr. Forge today

- Accept an explicit path to a packaged `drforge.exe`, or discover one in app-local package folders only (`tools\drforge\...`, `drforge\...`). ForgerEMS never searches `PATH` or private developer paths.
- Inspect the package: read `drforge-cli-release-manifest.json` (schema `drforge-cli-release-manifest/1.0`) and verify `SHA256SUMS.txt` when present. Missing checksum files are reported as "not verified locally", never as verified.
- Run timeout-bounded, user-initiated readiness checks (`drforge --version`, `drforge sensor-core --help`).
- Probe `drforge sensors driver-status --json` when the configured CLI supports it, accepting `forger-sensor-driver-preflight/1.1` conservatively. Unsupported/missing driver-status output is non-fatal; the bridge keeps operating through the user-mode report contract.
- Run user-initiated safe scans through the CLI process boundary (`sensor-core snapshot / report / archive`) and render the `forge-hardware-intake-report/1.0` result honestly: null/missing readings show as **Unavailable**, never zero; ring-0/deep telemetry gaps are listed as gaps.
- Store generated reports and archives under the local Runtime reports folder (`%LOCALAPPDATA%\ForgerEMS\Runtime\reports\drforge`) and open that folder on request.
- Include Dr. Forge report/archive files in a support bundle only when the user selects them (visible checkbox) **and** confirms the export consent dialog. The exporter accepts only allowlisted files under the app-managed Dr. Forge reports root, and redacts content.

## What remains unavailable

- Driver-backed sensors (fan RPM, voltage rails, EC/SuperIO/MSR depth). No production Dr. Forge driver exists; ForgerEMS treats `forger-sensor-driver-preflight/1.1` "driver absent / user-mode fallback" as the normal, expected state — not an error.
- Fan, voltage, charging, clock, overclock/undervolt, BIOS, or firmware control. Not offered, not planned for the bridge.
- HWiNFO / CPU-Z / CrystalDiskInfo / LibreHardwareMonitor parity. ForgerEMS does not claim it.
- Any download/install CTA for Dr. Forge. There is intentionally no "Download Dr. Forge" or "Install Dr. Forge" button until a real release channel exists.

## Distribution status

- Dr. Forge is **not bundled** in ForgerEMS installer or portable ZIP packages today. The bridge shows a friendly "Not configured" state until the user provides a trusted packaged CLI (or a future release pipeline stages one app-locally).
- Dr. Forge is **not part of any USB Builder profile**. USB Builder copy references Dr. Forge only as the separate companion where deep diagnostics live.
- No auto-download, no auto-update, and no background scanning of any kind. Every Dr. Forge action in ForgerEMS is user-initiated.

## Safety boundaries (enforced)

ForgerEMS does not, and must not:

- install, start, load, or register any Dr. Forge (or other) kernel driver;
- run `sc create`, `sc start`, `pnputil`, `devcon`, or call `NtLoadDriver` / `ZwLoadDriver` / use `SeLoadDriverPrivilege`;
- ship `*.sys`, `*.inf`, or `*.cat` driver artifacts in normal release packages;
- request or require elevation for any Dr. Forge action (no "Run as Admin" driver buttons);
- fake readings or fake compatibility — unavailable stays **Unavailable**;
- upload Dr. Forge reports, telemetry, or support bundles anywhere automatically.

Enforcement points:

- `tools/build-release.ps1` (`Assert-NoDriverArtifacts`) fails the build if `*.sys` / `*.inf` / `*.cat` reach the staged app, backend, or portable ZIP package.
- `tools/Validate-ForgerEMSRelease.ps1` fails validation (`driver-artifacts`, `zip-driver-artifacts` rows) if driver artifacts appear in release output or inside the shipped ZIP.
- `tests/ForgerEMS.Wpf.Tests/DrForgeCliBridgeTests.cs` pins conservative `forger-sensor-driver-preflight/1.1` parsing, non-fatal older-CLI behavior, null-as-unavailable report rendering, and no-network/no-elevation bridge behavior.
- `tests/ForgerEMS.Wpf.Tests/DrForgeIntegrationSafetyTests.cs` pins the no-driver-verbs, no-driver-buttons, driver-absent-is-normal, and packaging-guard invariants.
- Existing suites pin honest copy (`DrForgeRoadmapAndAdminInventoryRenameTests`, `DrForgeCliBridgeTests`), consent wording (`TermsConsentGateTests`), support-bundle opt-in (`SupportBundleExporterTests`), and retired-feature cleanup (`InternetWidgetRemovalTests`, `DeepSensorDisclosureCopyTests`).

## Reports and privacy

- Dr. Forge reports/archives are local files under the app-managed Runtime reports folder. ForgerEMS does not scrape other folders (including Documents) and does not assume any Dr. Forge install location for reports.
- Support bundle inclusion is opt-in per bundle, path-contained, allowlisted, capped, and redacted. The consent dialog warns that exported files may contain local device/context information and that nothing is uploaded automatically.
- The bridge persists only the selected CLI path, last readiness state, and last local report/archive paths. Stale or out-of-root persisted paths are ignored on load.

## Retired-feature hygiene

- **Network Pulse** was retired in v1.2.3-preview.1. It has no active setting, widget, command, or bundle collection; regression tests keep it out.
- **Deep Sensor Mode settings UI** was retired from Settings. The read-only local sensor provider (bundled LibreHardwareMonitorLib, MPL-2.0) remains an installer/environment opt-in with test-enforced no-control disclosures; it is documented honestly and is unrelated to the future Dr. Forge driver.
- Old persisted config for retired features is ignored safely.

## Future integration path

The next phases, in order, each gated on the Dr. Forge side maturing first:

1. **Optional app-local packaging** — release pipeline stages a signed, checksummed Dr. Forge CLI package under the app-local tools folder (still user-mode, still no driver artifacts; packaging guards stay mandatory).
2. **Report browsing** — richer in-app viewing of app-managed Dr. Forge report history (still local, still explicit export only).
3. **Richer driver-status display** — the bridge already parses the current safe no-driver status; a later UI pass may show the parsed driver-status details separately from the readiness line. Unknown fields remain ignored, never errors.
4. **Driver-backed sensors** — only after the Dr. Forge driver foundation ships for real: threat model, legal review, signing/revocation plan, crash containment, rollback strategy, and beta hardware validation. Until then, ForgerEMS keeps presenting driver-required readings as unavailable.

No ForgerEMS release may move to phase 4 by bundling, installing, starting, or registering a driver itself; driver lifecycle stays owned by Dr. Forge's own signed installer flow, subject to its own review.
