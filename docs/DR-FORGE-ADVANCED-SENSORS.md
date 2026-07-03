# Dr. Forge Advanced Sensors - CLI Bridge and Roadmap

**Status:** A first ForgerEMS CLI bridge is available when a packaged Dr. Forge CLI is configured. Advanced sensor depth remains roadmap.

Dr. Forge is a separate local user-mode hardware intake/report tool. ForgerEMS consumes the packaged CLI/Core contract through a process boundary. It does not load Dr. Forge WPF or provider internals.

## Why a separate app

ForgerEMS is a stable Windows inventory + USB maintenance toolkit. Hardware intake belongs in a focused, read-only companion so ForgerEMS stays usable even if a provider stalls, throws, or cannot read a given board. Dr. Forge:

- Runs in its own process.
- Ships on its own cadence.
- Never blocks ForgerEMS.
- Is read-only in the first bridge.

If the Dr. Forge package is missing, not configured, malformed, or fails a readiness check, ForgerEMS still operates normally and shows a setup-needed or failed state.

## Packaged CLI behavior

ForgerEMS accepts an explicit path to `drforge.exe` and optionally checks app-local Dr. Forge package folders. It does not search private developer paths and does not rely on the user's `PATH`.

When a package is found, ForgerEMS:

- Reads `drforge-cli-release-manifest.json` when available.
- Accepts the `drforge-cli-release-manifest/1.0` manifest schema.
- Verifies `SHA256SUMS.txt` when present.
- Runs `drforge --version`.
- Runs `drforge sensor-core --help`.
- Generates reports and archives only through the CLI process boundary.
- Enforces timeouts and captures stdout/stderr safely.

## Honest truthfulness

ForgerEMS will not fake CPU/GPU temperature, fan RPM, voltage, wattage, amperage, charge speed, or unsupported telemetry - not now, not in Dr. Forge, ever. **Unavailable** means the OS / driver / firmware did not expose it on this hardware. Null readings remain unavailable; they are never converted to zero.

ForgerEMS does not claim full HWiNFO / CPU-Z / LibreHardwareMonitor parity.

## First bridge scope

The first bridge is local and read-only:

1. User selects a packaged Dr. Forge CLI, or the package exists under an app-local tools folder.
2. ForgerEMS checks manifest/checksums when present.
3. ForgerEMS runs readiness checks.
4. ForgerEMS runs Dr. Forge sensor-core snapshot/report/archive commands.
5. ForgerEMS parses `forge-hardware-intake-report/1.0` JSON enough to show platform, safety mode, schema, key readings, findings, notes, and deep telemetry gaps.

Explicitly out of scope for this bridge:

- Fan control.
- Voltage control.
- Charging control.
- Overclocking / undervolting.
- Firmware flashing.
- Kernel drivers, kexts, or kernel modules.
- Service install/start.
- Auto-elevation, `sudo`, or `pkexec`.
- Network calls, accounts, activation, licensing, or telemetry.

## What remains unavailable

Deep telemetry such as fan RPM, voltage rails, EC/SuperIO/MSR readings requires future safe providers or signed privileged components. Until those exist and pass review, Dr. Forge reports those gaps as unavailable and ForgerEMS renders them as unavailable.

## Future optional depth

A signed local helper service or driver only after:

- Threat model written, reviewed, and accepted.
- Legal review of any vendor code or third-party dependency.
- Signing and revocation plan in place.
- Crash containment between Dr. Forge and any helper.
- Rollback strategy.
- Beta hardware validation pass on representative OEM laptops and desktops.

None of that is part of the first ForgerEMS bridge.

## Handoff to ForgerEMS

See [FORGEREMS-DR-FORGE-INTEGRATION.md](FORGEREMS-DR-FORGE-INTEGRATION.md) for the packaged CLI bridge contract.

Generated reports and archives stay under the local ForgerEMS Runtime reports folder. The user can review them before sharing. Support bundles include Dr. Forge report/archive files only when the user generated them from the app or explicitly chooses to include them.

## Status states the UI may show

- Not configured.
- Package found.
- Ready.
- Running intake.
- Report ready.
- Archive ready.
- Unavailable.
- Failed.

## Why no "Download Dr. Forge" button

ForgerEMS does not present a fake download or installer CTA. Configure the bridge only with a trusted packaged CLI that is already available to the user or bundled app-locally by the release pipeline.
