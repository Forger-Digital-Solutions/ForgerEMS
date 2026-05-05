# Third-Party Sensor Notices

This document tracks reviewed local sensor-provider licensing for ForgerEMS.

## Current Beta

ForgerEMS may bundle the reviewed `LibreHardwareMonitorLib` package in installer and portable builds under `providers/sensors/`.

The default active provider remains **Windows Native**, which uses local Windows and ForgerEMS data sources only. LibreHardwareMonitor is disabled unless Deep Sensor Mode is explicitly set to `ReadOnly`.

## Bundled Reviewed Provider: LibreHardwareMonitor

- Name: LibreHardwareMonitor
- Package: LibreHardwareMonitorLib
- Version: 0.9.6
- License: MPL-2.0
- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Bundled path: `providers/sensors/LibreHardwareMonitorLib.dll`
- Status: reviewed local read-only provider, disabled by default
- Modified MPL-covered files: none

Release packaging must include:

- include MPL-2.0 license text
- include third-party notices
- document whether any MPL-covered files were modified
- provide covered-source modifications if required
- keep ForgerEMS proprietary code in separate files/projects
- verify the provider is read-only inside ForgerEMS

The provider is local and read-only. ForgerEMS does not expose fan control, voltage control, clock control, overclocking, undervolting, BIOS writes, or firmware writes.

## Tools Not Redistributed By Default

ForgerEMS does not bundle HWiNFO, AIDA64, CPU-Z, GPU-Z, vendor tuning utilities, or proprietary sensor tools unless redistribution is explicitly licensed and reviewed.
