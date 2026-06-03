# Sensor Limitations

ForgerEMS reports local hardware intelligence honestly. Unknown or missing sensor readings are coverage limits, not proof of hardware failure.

## Common Missing Readings

Many systems, especially laptops, do not expose these through standard Windows APIs:

- CPU package temperature
- CPU package power
- fan RPM
- VRM telemetry
- embedded-controller telemetry
- board-level temperatures
- battery cycle count
- exact USB-C voltage/current
- per-port charging wattage

ForgerEMS must not fake these values. If a source does not expose a value, reports should show the source and limitation.

## Current Built-In Coverage

Forger Sensor Core can use local read-only sources such as:

- Windows APIs
- WMI/CIM
- SetupAPI/device inventory where available
- powercfg and battery APIs
- storage/NVMe/SMART paths where exposed
- USB Intelligence topology and benchmark evidence
- GPU/vendor-safe paths already supported
- ACPI thermal zones where available

LibreHardwareMonitor may be used only when already bundled, licensed, reviewed, and enabled by ForgerEMS policy.

## Future Deeper Coverage

Forger Sensor Service is planned as an optional local elevated service. It is not installed in this build.

Forger Deep Sensor Driver is planned as a future signed read-only driver. It is not included in this build. Some board-level sensors require that future driver path.

## What ForgerEMS Does Not Do

- no cloud upload for local sensor scans
- no paid third-party tool requirement
- no user-required HWiNFO, AIDA64, CPU-Z, or vendor-tool download
- no fake sensor values
- no fan, voltage, clock, BIOS, firmware, overclock, or undervolt control
- no unsafe kernel code in this pass
