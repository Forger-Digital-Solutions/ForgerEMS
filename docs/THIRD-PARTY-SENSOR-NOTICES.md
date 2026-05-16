# Third-Party Sensor Notices

This document tracks reviewed local sensor-provider licensing for ForgerEMS.

## Current Beta

ForgerEMS may bundle the reviewed `LibreHardwareMonitorLib` package in installer and portable builds under `providers/sensors/`.

The default safe provider remains **Windows Native**, which uses local Windows and ForgerEMS data sources only. LibreHardwareMonitor is available when packaged and runs only when ForgerEMS Deep Sensor Mode resolves to `ReadOnly` through installer consent, Settings, or the testing environment variable.

## Bundled Reviewed Provider: LibreHardwareMonitor

- Name: LibreHardwareMonitor
- Package: LibreHardwareMonitorLib
- Version: 0.9.6
- License: MPL-2.0
- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Bundled path: `providers/sensors/LibreHardwareMonitorLib.dll`
- Packaged license path: `providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt`
- Packaged third-party notice path: `providers/sensors/THIRD-PARTY-NOTICES.txt`
- Status: reviewed local read-only provider, disabled by default
- Modified MPL-covered files: none

Release packaging must include:

- include MPL-2.0 license text
- include third-party notices
- document whether any MPL-covered files were modified
- provide covered-source modifications if required
- keep ForgerEMS proprietary code in separate files/projects
- verify the provider is read-only inside ForgerEMS

The provider is local and read-only. ForgerEMS does not expose fan control, voltage control, clock control, overclocking, undervolting, BIOS writes, or firmware writes. ForgerEMS does not redistribute HWiNFO, AIDA64, CPU-Z, or vendor tools, and users do not download sensor providers manually.

## Tools Not Redistributed By Default

ForgerEMS does not bundle HWiNFO, AIDA64, CPU-Z, GPU-Z, vendor tuning utilities, or proprietary sensor tools unless redistribution is explicitly licensed and reviewed.
