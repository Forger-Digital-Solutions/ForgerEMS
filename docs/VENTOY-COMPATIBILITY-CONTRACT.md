# Ventoy Compatibility Contract

This document defines the behaviors and interfaces that future changes should
not break without a deliberate compatibility decision.

## Supported Entrypoints

Canonical repo entrypoints:

- `ventoy-core/Setup-ForgerEMS.ps1`
- `ventoy-core/Update-ForgerEMS.ps1`
- `ventoy-core/Verify-VentoyCore.ps1`

Supported compatibility entrypoints:

- `ventoy-core/Setup_USB_Toolkit.ps1`
- `ventoy-core/Setup_Toolkit.ps1`
- `VentoyToolkitSetup/Setup-ForgerEMS.ps1`
- `VentoyToolkitSetup/Setup_USB_Toolkit.ps1`
- `VentoyToolkitSetup/Setup_Toolkit.ps1`
- `VentoyToolkitSetup/Update-ForgerEMS.ps1`
- `VentoyToolkitSetup/Verify-VentoyCore.ps1`

## Supported Parameters

### Setup family

Applies to:

- `Setup-ForgerEMS.ps1`
- `Setup_Toolkit.ps1`
- `Setup_USB_Toolkit.ps1`

Supported parameters:

- `-DriveLetter`
- `-UsbRoot`
- `-OwnerName`
- `-ManifestName`
- `-OpenCorePages`
- `-OpenManualPages`
- `-SeedManifest`
- `-ForceManifestOverwrite`
- `-ShowVersion`
- `-WhatIf`
- `-Confirm`

### Update

Applies to:

- `Update-ForgerEMS.ps1`

Supported parameters:

- `-DriveLetter`
- `-UsbRoot`
- `-ManifestName`
- `-Force`
- `-VerifyOnly`
- `-NoArchive`
- `-ShowVersion`
- `-WhatIf`
- `-Confirm`

### Verification

Applies to:

- `Verify-VentoyCore.ps1`

Supported parameters:

- `-VerifyRoot`
- `-Online`
- `-ShowVersion`
- `-EnforceManagedChecksums`

## Behavioral Guarantees

These guarantees are part of the compatibility contract:

1. Setup remains safe to rerun.
2. Update continues to prefer a target-root manifest when present and otherwise
   falls back to the bundled manifest.
3. Manifest-managed paths remain root-contained and reject path escape
   attempts.
4. `-WhatIf` remains a true dry-run for setup and update.
5. `-ShowVersion` reports metadata sourced from the bundled manifest.
6. Legacy wrapper names continue delegating without silently changing behavior.
7. Verification remains offline-safe by default.
8. `-Online` remains optional and does not slow default verification runs.
9. `candidate` and `stable` releases require managed checksum coverage for
   manifest-managed `file` items.
10. When a release bundle includes `CHECKSUMS.sha256` or `SIGNATURE.txt`,
    verification validates them automatically.

## Supported Boundary

Supported as part of the maintained Ventoy core product surface:

- `ventoy-core` setup, update, and verify entrypoints
- compatibility wrappers listed above
- `manifests/ForgerEMS.updates.json` and its schema
- the setup/update lifecycle driven by the managed-content manifest
- release assembly through `Tools/build-release.ps1`

Visible in the workspace, but not fully managed by this compatibility contract:

- `Tools\Portable`
- `Drivers`
- `MediCat.USB`
- vendor/community binaries without a tracked source/build workflow

## Explicit Non-Guarantees

These are not guaranteed as stable contracts:

- ownership of vendor binaries in `Tools\Portable`, `Drivers`, `MediCat.USB`,
  or packaged `ForgerTools`
- immutable upstream URLs for third-party projects
- automatic checksum coverage for every external binary, especially unmanaged
  vendor content
- compatibility of undocumented script internals or private helper functions

## Release Contract

A "shippable bundle" for this Ventoy core means the clean release folder
assembled by `Tools/build-release.ps1`, not an ad hoc mix of workspace files.
