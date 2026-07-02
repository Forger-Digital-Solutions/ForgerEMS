# Dr. Forge Advanced Sensors — Roadmap

**Status:** Planned. Not installed. Not running.
Dr. Forge is a planned, separate read-only technician sensor application. It is not
shipped or downloadable today. This document is the design and scope contract.

## Why a separate app

ForgerEMS is a stable Windows inventory + USB maintenance toolkit. Deep sensor
reads belong in a focused, read-only companion so ForgerEMS stays usable even if
a sensor provider stalls, throws, or cannot read a given board. Dr. Forge:

- Runs in its own process.
- Ships on its own cadence.
- Never blocks ForgerEMS.
- Is **read-only**.

If Dr. Forge crashes or cannot read a sensor on a given board, ForgerEMS still
operates normally — it simply reports the sensor as unavailable.

## Honest truthfulness

ForgerEMS will not fake CPU/GPU temperature, fan RPM, voltage, wattage,
amperage, charge speed, or unsupported telemetry — not now, not in Dr. Forge,
ever. "Unavailable" means the OS / driver / firmware did not expose it on this
hardware. Dr. Forge inherits the same rule.

## First-version scope (read-only)

Safe read-only sources, in roughly the order they should be tried:

1. Windows APIs (DXGI, IOCTL_DISK_*, GetSystemPowerStatus, etc.).
2. WMI / CIM where useful (`Win32_Processor`, `MSAcpi_ThermalZoneTemperature`,
   `MSStorageDriver_FailurePredictStatus`, etc.).
3. Performance counters (`\Processor Information(*)\*`, `\Thermal Zone Information(*)\*`).
4. Storage / SMART APIs where the OS exposes them.
5. Battery and power APIs (designed capacity, cycle count, charge rate where
   exposed).
6. LibreHardwareMonitor-style read-only access where safe, licensed, and
   compatible.

Explicitly out of scope for the first version:

- Fan control.
- Voltage control.
- Charging control.
- Overclocking / undervolting.
- Firmware flashing.
- Kernel driver hacks.
- Bypassing OS / driver security.

## Future optional depth

A signed local helper service or driver only after:

- Threat model written, reviewed, and accepted.
- Legal review of any vendor code we lean on.
- Signing & revocation plan in place (cert custody, EV signing, revocation
  process if a build leaks).
- Crash containment between Dr. Forge and any helper (helper death must not
  blue-screen the user's machine).
- Rollback strategy: old build must keep working after a bad Dr. Forge install.
- Beta hardware validation pass on a representative spread of OEM laptops &
  desktops.

None of the above is done yet, so the first version ships without any helper
service or driver.

## What Dr. Forge will surface

Where the OS / driver / firmware exposes them:

- CPU package & core temperatures.
- GPU temperatures and fan signals.
- System fan tachometer signals (informational only — no fan control).
- Battery: designed capacity, full charge capacity, cycle count, charge rate,
  charge state.
- Storage: SMART attributes, drive temperature, power-on hours, wear, reported
  read/write error counts.
- Sensor inventory (which sensors exist on this board, which are readable,
  which are blocked, and the reason).
- Board / device capability inventory (e.g., TPM version, Secure Boot state,
  available PCH thermal zones).

Where the OS / driver / firmware does **not** expose them, Dr. Forge reports
the sensor as unavailable and the reason. It does not estimate.

## Handoff to ForgerEMS

Dr. Forge will integrate with ForgerEMS via a local read-only JSON snapshot
file. See [FORGEREMS-DR-FORGE-INTEGRATION.md](FORGEREMS-DR-FORGE-INTEGRATION.md)
for the integration contract.

## Status states the UI may show

- Not installed.
- Planned (current state).
- Installed but not running.
- Running.
- Data available.
- Data unavailable (with a reason).
- Update available.

Today, ForgerEMS only ever shows **Planned**.

## Why no "Download Dr. Forge" button today

Because Dr. Forge does not exist as a downloadable artifact. A button that
opens a fake download or a 404 would be dishonest. The button intentionally
remains absent until the first preview is published.
