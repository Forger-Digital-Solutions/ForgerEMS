# Third-Party Sensor Notices

This document tracks reviewed local sensor-provider licensing for ForgerEMS.

## Current Beta

ForgerEMS may bundle the reviewed `LibreHardwareMonitorLib` package in installer and portable builds under `providers/sensors/`.

The default safe provider is **Forger Sensor Core**, which uses local Windows/native and ForgerEMS data sources only. LibreHardwareMonitor is available when packaged and runs only when ForgerEMS Deep Sensor Mode resolves to `ReadOnly` through installer consent, Settings, or the testing environment variable.

## Bundled Reviewed Provider: LibreHardwareMonitor

- Name: LibreHardwareMonitor
- Package: LibreHardwareMonitorLib
- Version: 0.9.6
- License: MPL-2.0
- Project: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor
- Bundled path: `providers/sensors/LibreHardwareMonitorLib.dll`
- Packaged license path: `providers/sensors/LICENSES/LibreHardwareMonitor-MPL-2.0.txt`
- Packaged third-party notice path: `providers/sensors/THIRD-PARTY-NOTICES.txt`
- Pinned upstream source: LibreHardwareMonitor commit `3d331e3370efb858411f19511373eff65a218701` (https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/3d331e3370efb858411f19511373eff65a218701)
- Status: reviewed local read-only provider, disabled by default
- Modified MPL-covered files: none
- Replaceability: the assembly is shipped as a loose `LibreHardwareMonitorLib.dll` (excluded from single-file bundling), so the LGPL-bearing dependency carrying the embedded PawnIO modules can be replaced or rebuilt by the user as LGPL obligations may require. Step-by-step replacement instructions ship as `providers/sensors/REPLACING-LGPL-COMPONENTS.md` (copied into `providers\sensors\` in packaged builds). The runtime binds the app-root `LibreHardwareMonitorLib.dll`; the `providers\sensors\` copy is the packaged provider copy — both must be replaced together. Counsel review of bundled-module and relinking obligations remains flagged; no compliance certification is asserted.

### Embedded PawnIO modules (inside LibreHardwareMonitorLib 0.9.6)

LibreHardwareMonitorLib 0.9.6 embeds PawnIO modules release **0.1.6** (upstream tag `ceb5e5b9fcbda1d3bb33705d0036fba1c3214532`, commit `4aa792beb020a14c8261072e9786d2dfb38489d9`). The modules are **LGPL-2.1-or-later** licensed.

- Corresponding source (unmodified upstream zip, SHA-256 `647BF55985837302B00AD2C05FC3FB700F140AF2E34693F390FF2DB47B608867`): `providers/sensors/SOURCE/PawnIO-Modules-0.1.6-source.zip`
- License text: `providers/sensors/LICENSES/PawnIO-Modules-LGPL-2.1.txt`
- Upstream: https://github.com/namazso/PawnIO.Modules (module source, includes, and compiler RPM — the RPM inside the zip is data only and is never executed by ForgerEMS)
- No PawnIO kernel driver or installer is bundled. The modules may use an already-installed PawnIO driver if the user installed one separately; ForgerEMS exposes no PawnIO controls itself.
- No categorical guarantee is made that upstream sensor probes perform no low-level I/O writes; ForgerEMS itself does not expose hardware-control actions.

### NuGet runtime dependency packages

These packages are resolved as NuGet runtime dependencies (declared by `LibreHardwareMonitorLib` nuspec plus ForgerEMS's own references). They are **not** asserted to be embedded inside the LibreHardwareMonitorLib assembly itself.

- **BlackSharp 1.0.7** — MPL-2.0 — https://github.com/Blacktempel/BlackSharp tree `c70b735c6cec123ee8a046ac4a0bc6c606f52cf0`
- **DiskInfoToolkit 1.1.2** — MPL-2.0 — https://github.com/Blacktempel/DiskInfoToolkit tree `25319eae5781e75bcf141e844ceab2afe94d40ea`
- **RAMSPDToolkit 1.4.2** — MPL-2.0 — https://github.com/Blacktempel/RAMSPDToolkit tree `3b47b960e0830fef344624ad5e389675d5f0a1ce`
- **HidSharp 2.6.4** — Apache-2.0 — license text: `providers/sensors/LICENSES/HidSharp-Apache-2.0.txt`
- **Mono.Posix.NETStandard 1.0.0** — package metadata declares a license URL (`https://go.microsoft.com/fwlink/?linkid=869050`) pointing at the Mono project license, not an SPDX identifier. Current upstream Mono text states the runtime and class libraries are generally MIT-licensed with some third-party (e.g. 3-clause BSD) code; the captured upstream text ships unmodified at `providers/sensors/LICENSES/Mono-project-license.txt`. Exact redistribution scope for Mono.Posix 1.0.0 requires counsel review — no clearance is asserted.

Microsoft packages actually resolved in this build (ForgerEMS pins them above the nuspec minimums declared by LibreHardwareMonitorLib — `System.Management` ≥10.0.2, `System.IO.Ports` ≥10.0.3, `System.Threading.AccessControl` ≥10.0.3):

- `System.Management` 10.0.12
- `System.IO.Ports` 10.0.12
- `System.Threading.AccessControl` 10.0.12

Only the PawnIO modules source zip ships as corresponding source in this package. Source for MPL-covered packages is available from the pinned upstream repositories listed above.

### .NET runtime

Self-contained builds include Microsoft .NET 8 runtime components (latest captured: runtime 8.0.31 / SDK 8.0.425, LTS maintenance, EOL 2026-11-10). Licenses and third-party notices ship at:

- `providers/sensors/LICENSES/dotnet-runtime-LICENSE.txt` (Microsoft.NETCore.App runtime pack)
- `providers/sensors/LICENSES/dotnet-runtime-THIRD-PARTY-NOTICES.txt` (NETCore third-party notices)
- `providers/sensors/LICENSES/dotnet-windowsdesktop-runtime-LICENSE.txt` (Microsoft.WindowsDesktop.App runtime pack — that pack ships no separate third-party notices file)

.NET 8 remains supported today; the upcoming migration deadline is tracked and must not be presented as indefinite platform support.

### Windows SDK / WinRT assemblies

`Windows.SDK.NET.Ref` reference assemblies are used at build time, and Windows SDK/WinRT metadata assemblies may also be present as runtime payloads in self-contained output. The license is reached via https://aka.ms/WinSDKLicenseURL (captured text ships as `providers/sensors/LICENSES/Microsoft-Windows-SDK-license.rtf`). The package metadata carries no SPDX identifier — **counsel redistribution review is flagged; no universal license clearance is claimed**.

Release packaging must include:

- include MPL-2.0 license text
- include third-party notices
- document whether any MPL-covered files were modified
- provide covered-source modifications if required
- keep ForgerEMS proprietary code in separate files/projects
- verify the provider is read-only inside ForgerEMS

The provider is local and read-only. ForgerEMS does not expose fan control, voltage control, clock control, overclocking, undervolting, BIOS writes, or firmware writes. ForgerEMS does not require or redistribute HWiNFO, AIDA64, CPU-Z, or vendor tools for hardware intelligence, and users do not download sensor providers manually.

## Optional Vendor-Detected Providers (no redistribution)

ForgerEMS may surface read-only data from tools that are **already installed by the user or by an official driver**, without bundling, downloading, or installing them itself:

- **NVIDIA SMI**: when `nvidia-smi.exe` is already present (System32, PATH, or `NVIDIA Corporation\NVSMI`) ForgerEMS runs a single short `--query-gpu` call to surface GPU temperature/load/clock/VRAM. No redistribution. If absent, the provider reports `Not detected` honestly and does nothing else. NVIDIA SMI is installed and licensed by NVIDIA as part of the official driver; ForgerEMS does not modify it.

## Tools Not Redistributed By Default

ForgerEMS does not bundle HWiNFO, AIDA64, CPU-Z, GPU-Z, vendor tuning utilities, smartctl/smartmontools, NVAPI/ADLX SDKs, or proprietary sensor tools unless redistribution is explicitly licensed and reviewed. The `nvidia-smi` integration above is detection-only — the binary itself is shipped by the NVIDIA driver, not by ForgerEMS.
