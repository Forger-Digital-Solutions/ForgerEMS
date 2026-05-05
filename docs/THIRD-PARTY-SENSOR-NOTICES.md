# Third-Party Sensor Notices

This document tracks sensor-provider licensing before any reviewed provider is bundled with ForgerEMS.

## Current Beta

No third-party deep sensor provider binary is bundled in the current beta build.

The active provider is **Windows Native**, which uses local Windows and ForgerEMS data sources only.

## Review Candidate: LibreHardwareMonitor

- Name: LibreHardwareMonitor
- License: MPL-2.0
- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Planned bundled path: `providers/sensors/ForgerEMS.SensorProviders.LibreHardwareMonitor.dll`
- Status: review pending, disabled by default

Before bundling:

- include MPL-2.0 license text
- include third-party notices
- document whether any MPL-covered files were modified
- provide covered-source modifications if required
- keep ForgerEMS proprietary code in separate files/projects
- verify the provider is read-only inside ForgerEMS

## Tools Not Redistributed By Default

ForgerEMS does not bundle HWiNFO, AIDA64, CPU-Z, GPU-Z, vendor tuning utilities, or proprietary sensor tools unless redistribution is explicitly licensed and reviewed.

