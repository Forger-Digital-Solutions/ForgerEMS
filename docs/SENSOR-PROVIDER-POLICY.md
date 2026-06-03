# ForgerEMS Sensor Provider Policy

ForgerEMS sensor providers are local, read-only diagnostics components in the **Forger Sensor Stack**. Users should not need to download HWiNFO, AIDA64, CPU-Z, LibreHardwareMonitor, vendor tools, or separate sensor plugins to use approved ForgerEMS sensor coverage.

## Provider Tiers

- **Forger Sensor Core**: enabled by default. Uses built-in Windows APIs, WMI/CIM, registry reads, powercfg reports, storage reliability counters where exposed, security APIs, and ForgerEMS USB Intelligence evidence.
- **LibreHardwareMonitor Deep Sensor Provider**: optional bundled reviewed provider loaded from `providers/sensors/LibreHardwareMonitorLib.dll`. It is enabled only when ForgerEMS Deep Sensor Mode resolves to `ReadOnly` through the environment variable, user setting, or installer default.
- **ACPI Thermal Zones (optional probe)**: built-in Windows-native optional probe via `MSAcpi_ThermalZoneTemperature` in the `root\WMI` namespace. Runs read-only. Reports zone temperatures when the firmware/ACPI table exposes them and `No zones exposed` honestly when it does not. No data is fabricated. No driver install, no fan/voltage/clock writes.
- **NVIDIA SMI (optional vendor-detected probe)**: detection-only. ForgerEMS never bundles or downloads `nvidia-smi.exe`. When the NVIDIA driver has already installed it on the machine (System32, PATH, or `NVIDIA Corporation\NVSMI`), ForgerEMS runs a single short read-only `--query-gpu` call, parses the CSV, and surfaces GPU temperature / load / graphics clock / VRAM used. When the binary is absent the provider reports `Not detected` honestly and does nothing else.
- **Forger Sensor Service**: future optional ForgerEMS-owned local elevated service. Not installed in this build. It must be local only and must not expose network endpoints by default.
- **Forger Deep Sensor Driver**: roadmap only. Not included in this build. It must be signed, read-only, explicitly installed by consent, allowlisted, and release-gated.

## Typed Data-Class Matrix

Each provider declares per-data-class availability so the Hardware X-Ray UI and JSON report can be honest about what each provider *can* expose, separate from what is actually exposed on any specific machine:

| Provider | CPU temp | CPU pkg power | CPU load/clock | GPU temp/load/clock | GPU VRAM | Fan RPM | Storage SMART/temp | Battery wear / cycles | Thermal zone | Board sensors |
|---|---|---|---|---|---|---|---|---|---|---|
| Forger Sensor Core | NotExposed | NotExposed | Available | NotExposed | NotExposed | NotExposed | SMART Available / Temp NotExposed | Wear Available / Cycles NotExposed | NotExposed | NotExposed |
| LibreHardwareMonitor | Available | Available | Available | Available | Available | Available | Temp Available / SMART NotExposed | NotApplicable | NotExposed | Available |
| ACPI Thermal Zones | — | — | — | — | — | — | — | — | Available / NotExposed | — |
| NVIDIA SMI | — | — | — | Available (NVIDIA only) | Available (NVIDIA only) | — | — | — | — | — |

A capability of `Available` means the provider *can* surface that data class when present; per-machine availability still depends on firmware, drivers, permissions, and Deep Sensor Mode. `NotExposed` / `NotPackaged` / `ProviderUnavailable` / `PermissionRequired` / `NotApplicable` are all reported honestly and **must not** be treated as hardware failure.

## Providers explicitly *not* added in v1.2.3

The provider expansion pass evaluated the following candidates and deferred them by design:

- **NVAPI / ADLX / AMD ADL**: vendor SDKs with redistribution and signing requirements that warrant their own legal review. Listed on the roadmap; not added blindly.
- **Intel Power Gadget / Intel PCM**: superseded / requires a kernel driver that ForgerEMS does not install.
- **HWiNFO Shared Memory SDK**: closed-source SDK; cannot be bundled without explicit license and is not the main path. ForgerEMS should not present HWiNFO as required for hardware intelligence.
- **smartctl / smartmontools**: GPL-2.0; not bundled in this pass. SMART/NVMe health is currently sourced through Windows MSFT_StorageReliabilityCounter. A future design pass may add an optional detection of an already-installed `smartctl.exe` analogous to the `nvidia-smi` pattern.
- **OpenHardwareMonitor (legacy)**: superseded by the actively maintained LibreHardwareMonitor fork already bundled.

In every case the rule is the same: *do not add a dependency just because it exists.*

## Safety Rules

ForgerEMS sensor providers must be read-only.

Providers must not expose or perform:

- fan control
- voltage control
- clock control
- overclocking
- undervolting
- BIOS writes
- firmware writes
- destructive storage or hardware actions

Missing sensor access is a coverage limitation, not hardware failure. ForgerEMS must report unavailable data honestly as firmware-limited, permission-limited, vendor-driver-limited, unsupported, or requiring the future Forger deep sensor layer. It must not generate fake temperatures, voltages, fan speeds, package power, or charging wattage.

## Distribution Rules

ForgerEMS does not redistribute unlicensed proprietary tools such as HWiNFO, AIDA64, CPU-Z, or vendor utilities unless a license explicitly allows redistribution.

Bundled providers must:

- ship inside the ForgerEMS installer or portable bundle
- be reviewed before enabling by default
- include required license texts and notices
- run locally with no cloud or paid sensor service requirement
- clearly label admin requirements
- be disabled by default if experimental

ForgerEMS v1.2.3 Public Preview pins `LibreHardwareMonitorLib` 0.9.6 as a reviewed local read-only deep sensor provider where packaged. Users do not download it manually; release packaging ships the provider DLL and notices inside the installer and portable bundle.

ForgerEMS Deep Sensor Mode is disclosed in the installer and Settings. It only reads supported local hardware sensor data while ForgerEMS is running or a System Intelligence / Hardware X-Ray scan is executing. It does not install a background service, create a startup task, send sensor telemetry, use cloud sensor services, require paid third-party tools, or auto-send reports.

The installer Deep Sensor Mode checkbox only sets the default sensor mode (`Off` or `ReadOnly`). It does not grant permanent administrator permission, skip Windows UAC, or make ForgerEMS always run elevated. Some deeper sensor/security checks may ask for Windows administrator approval when the user runs Elevated Scan.

## MPL-Style Library Handling

For MPL-style libraries, ForgerEMS must include the license text and third-party notices. If ForgerEMS modifies MPL-covered source files and distributes them, those modifications must be made available as required by the license.

ForgerEMS proprietary code should remain in separate files/projects from MPL-covered code.

ForgerEMS uses the unmodified LibreHardwareMonitor NuGet package. No MPL-covered LibreHardwareMonitor source files are copied into proprietary ForgerEMS files, and `ModifiedFilesDisclosureNeeded` remains false unless that changes.

## User Wording

Deep Sensor Mode wording should be:

> Deep Sensor Mode uses bundled LibreHardwareMonitorLib for local read-only sensor coverage. No fan control, voltage control, overclocking, BIOS, firmware, or hardware writes. Some deeper sensor checks may ask for Windows administrator approval when you run Elevated Scan.

Forger Sensor Stack wording should be:

> Forger Sensor Core active. Deep Sensor Service not installed. Deep Sensor Driver not included in this build. External tools are not required. Some board-level sensors require the future Forger Deep Sensor Driver.
