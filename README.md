# ForgerEMS (Beta)

**ForgerEMS** is the Forger Engineering Maintenance Suite from **Forger Digital Solutions**: a Windows desktop maintenance tool for building, managing, and distributing a controlled USB engineering toolkit.

The repository is organized as the official source for the WPF application, PowerShell backend, Ventoy integration, update manifests, installer configuration, and release automation.

## What It Does

ForgerEMS gives operators a guided desktop interface for preparing and maintaining USB toolkit media. The WPF app wraps a PowerShell backend that validates manifests, manages downloads, stages the Ventoy-based toolkit flow, and supports installed-mode distribution through a Windows installer.

## Features

- USB toolkit builder for repeatable engineering and maintenance media.
- Managed downloads driven by versioned manifests and vendor inventory.
- Ventoy integration through the ForgerEMS backend scripts.
- Installed-mode support with bundled backend validation.
- Inno Setup installer configuration for Program Files deployment.
- Release scripting with staging, checksums, and CI-friendly dry runs.

## Repository Layout

```text
ForgerEMS/
├── src/                        # .NET 8 WPF app
├── backend/                    # PowerShell backend and Ventoy toolkit scripts
├── manifests/                  # updates.json and schema files
├── tools/                      # build, staging, and release scripts
├── installer/                  # Inno Setup installer configuration
├── docs/                       # product, backend, and release documentation
├── .github/workflows/          # GitHub Actions CI/CD
├── .gitignore
├── README.md
├── RELEASE_PROCESS.md
└── LICENSE
```

## Screenshots

Screenshots are intentionally not committed yet.

- Main dashboard: `docs/screenshots/main-dashboard.png` (placeholder)
- USB toolkit workflow: `docs/screenshots/usb-toolkit-workflow.png` (placeholder)
- Release/installer flow: `docs/screenshots/release-installer.png` (placeholder)

## Build

Prerequisites:

- Windows 10/11
- .NET 8 SDK
- PowerShell 5.1 or newer
- Inno Setup 6 for installer builds

Build the WPF app:

```powershell
dotnet restore .\ForgerEMS.sln
dotnet build .\ForgerEMS.sln -c Release
```

Create a release staging folder without compiling the installer:

```powershell
.\tools\build-release.ps1 -Version 1.0.0-beta.1 -DryRun
```

Create a full local installer release:

```powershell
.\tools\build-release.ps1 -Version 1.0.0-beta.1
```

## Release

Release outputs are written to:

```text
release/<version>/
```

The release script publishes the WPF app, builds a verified backend bundle, stages manifests, optionally compiles the Inno Setup installer, and writes SHA256 checksums for produced artifacts.

See [RELEASE_PROCESS.md](RELEASE_PROCESS.md) for the operator release checklist.

## License

Copyright (c) Forger Digital Solutions.

This project is distributed under the license terms in [LICENSE](LICENSE).
