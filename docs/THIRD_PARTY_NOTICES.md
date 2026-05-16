# Third-party notices

ForgerEMS (**Forger Engineering Maintenance Suite**) is published by **Forger Digital Solutions**.

The application may **reference**, **download**, **verify**, or **guide users** to third-party software and resources — including but not limited to **Ventoy**, vendor drivers, ISOs, and toolkit items described in project manifests.

- ForgerEMS **does not claim ownership** of third-party tools.
- Each third-party item remains subject to its **own license**, **terms of use**, and **trademark** holders.
- Items marked **Manual Required** (or equivalent) must stay clearly labeled; the operator obtains those payloads directly from the vendor or authorized source.

For license texts distributed **inside** this repository (for example vendored components), see project `LICENSE` files and upstream notices where applicable.

## LibreHardwareMonitor / Deep Sensor Mode

ForgerEMS may include **LibreHardwareMonitorLib** as a bundled local read-only sensor provider for **Hardware X-Ray** when **Deep Sensor Mode** is enabled.

- License: **MPL-2.0**
- Packaged provider path: `providers/sensors/LibreHardwareMonitorLib.dll`
- Packaged notice path: `providers/sensors/THIRD-PARTY-NOTICES.txt`
- Packaged license path: `providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt`
- Sensor notice documentation: [THIRD-PARTY-SENSOR-NOTICES.md](THIRD-PARTY-SENSOR-NOTICES.md)

ForgerEMS proprietary code remains separate from MPL-covered LibreHardwareMonitor code. ForgerEMS does not redistribute HWiNFO, AIDA64, CPU-Z, or other proprietary sensor tools unless a license explicitly allows it.
