# Forger Sensor Stack

ForgerEMS hardware intelligence is built around a ForgerEMS-owned local sensor stack. It does not require HWiNFO, AIDA64, CPU-Z, or other third-party hardware tools to run System Intelligence or Hardware X-Ray.

## Architecture

### Forger Sensor Core

Forger Sensor Core is the built-in provider that runs inside ForgerEMS today. It is active by default, local only, and read-only.

Current data sources include:

- Windows/native APIs
- WMI/CIM where useful
- SetupAPI/device inventory where available
- power and battery APIs/reports
- storage/NVMe/SMART paths where Windows exposes them
- USB topology paths already supported by ForgerEMS
- GPU/vendor-safe paths already supported by ForgerEMS
- ACPI thermal zones where available
- bundled reviewed LibreHardwareMonitor provider only when packaged, licensed, and enabled under ForgerEMS policy

Every reading should carry a source label. Missing values must be reported as coverage limits, not failures.

### Forger Sensor Service

Forger Sensor Service is a future optional ForgerEMS-owned local service. It is not installed in the current build.

Planned purpose:

- centralize elevated read-only telemetry
- reduce repeated UAC prompts after explicit install consent
- report status as Not installed, Installed, Running, Needs repair, Disabled, or Version mismatch

Boundaries:

- local only
- no network service endpoints by default
- no fan, voltage, clock, firmware, BIOS, overclock, or undervolt control
- no writes to hardware control paths

### Forger Deep Sensor Driver

Forger Deep Sensor Driver is roadmap only in this build. It is not included, installed, loaded, or experimented with here.

The driver goal is future HWiNFO-class depth through a ForgerEMS-owned signed read-only sensor driver. See [FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md](FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md).

## Product Status Copy

System Intelligence should summarize:

- Core: Active
- Elevated Scan: Complete / Partial / Recommended / Failed
- Sensor Service: Not installed / Future optional component
- Deep Sensor Driver: Not included / Roadmap
- External tools: Not required

Use this explanation when deep board-level readings are missing:

> This is not a failure. Many laptops do not expose CPU package power, fan speed, VRM, or EC telemetry through standard Windows APIs. Some board-level sensors require the future Forger Deep Sensor Driver.

## Privacy And Control Boundary

ForgerEMS does not upload local sensor telemetry to the cloud. Reports are local until the user exports or sends them.

ForgerEMS does not generate fake temperatures, voltages, fan speeds, package power, or charging wattage. It also does not perform fan control, voltage control, clock control, BIOS flashing, firmware flashing, overclocking, undervolting, or other hardware-control writes.
