# ForgerEMS Sensor Provider Policy

ForgerEMS sensor providers are local, read-only diagnostics components. Users should not need to download HWiNFO, LibreHardwareMonitor, vendor tools, or separate sensor plugins to use approved ForgerEMS sensor coverage.

## Provider Tiers

- **Windows Native**: enabled by default. Uses built-in Windows APIs, WMI/CIM, registry reads, powercfg reports, storage reliability counters where exposed, security APIs, and ForgerEMS USB Intelligence evidence.
- **LibreHardwareMonitor Deep Sensor Provider**: optional bundled reviewed provider loaded from `providers/sensors/LibreHardwareMonitorLib.dll`. Disabled by default; enabled only when `FORGEREMS_DEEP_SENSOR_MODE=ReadOnly`.
- **ForgerEMS Admin Sensor Bridge**: future on-demand read-only bridge for sensors requiring elevation. Disabled by default and not included in the current beta.
- **Signed Driver Provider**: roadmap only. Not part of the current beta.

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

Missing sensor access is a coverage limitation, not hardware failure. ForgerEMS must report unavailable data honestly as not exposed, permission-limited, vendor-driver-limited, unsupported, or requiring a reviewed deep sensor provider.

## Distribution Rules

ForgerEMS does not redistribute unlicensed proprietary tools such as HWiNFO, AIDA64, CPU-Z, or vendor utilities unless a license explicitly allows redistribution.

Bundled providers must:

- ship inside the ForgerEMS installer or portable bundle
- be reviewed before enabling by default
- include required license texts and notices
- run locally with no cloud or paid sensor service requirement
- clearly label admin requirements
- be disabled by default if experimental

ForgerEMS v1.2.0 preview pins `LibreHardwareMonitorLib` 0.9.6 as the reviewed local read-only deep sensor provider. Users do not download it manually; release packaging ships the provider DLL and notices inside the installer and portable bundle.

## MPL-Style Library Handling

For MPL-style libraries, ForgerEMS must include the license text and third-party notices. If ForgerEMS modifies MPL-covered source files and distributes them, those modifications must be made available as required by the license.

ForgerEMS proprietary code should remain in separate files/projects from MPL-covered code.

ForgerEMS uses the unmodified LibreHardwareMonitor NuGet package. No MPL-covered LibreHardwareMonitor source files are copied into proprietary ForgerEMS files, and `ModifiedFilesDisclosureNeeded` remains false unless that changes.

## User Wording

Deep Sensor Mode wording should be:

> Deep Sensor Mode may require admin access. It only reads supported sensors and does not change fan, voltage, clock, or firmware settings.
