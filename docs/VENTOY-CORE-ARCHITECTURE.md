# Ventoy Core Architecture

## Status

This document describes the maintainable Ventoy core after the
"product + lifecycle" baseline pass on `2026-04-20`.

The goal is no longer just safe scripts. The goal is a source-owned,
release-disciplined core that can be packaged repeatably without pretending
that every bundled third-party payload in the workspace is equally
maintainable.

## Canonical Repo Layout

The maintainable source now lives in these top-level locations:

- `ventoy-core/`
  Canonical PowerShell implementation and public entrypoints.
- `manifests/`
  Single-source manifest files, including version/build metadata, release
  classification, and vendor inventory.
- `Docs/`
  Architecture, contracts, provenance, CI samples, plans, and release-bundle
  documentation.
- `Tools/`
  Workspace tooling, including `build-release.ps1`.
- `release/`
  Clean build output for shippable bundles.
- `.verify/`
  Disposable verification scratch space.

The older `VentoyToolkitSetup/` folder is intentionally preserved as a
compatibility facade. Its PowerShell entrypoints forward into `ventoy-core/`.

## Source Of Truth Vs Generated Output

Source of truth:

- `ventoy-core/`
- `manifests/`
- `Docs/`
- `Tools/build-release.ps1`
- `.github/workflows/ventoy-core.yml`
- root policy/changelog files such as `.gitignore` and
  `CHANGELOG-VENTOY-CORE-2026-04-20.md`

Generated or disposable:

- `release/ventoy-core/*`
- `.verify/*`
- target-root workflow folders such as `_downloads`, `_archive`, and `_logs`

Visible but not source-owned in this baseline:

- `Tools\Portable`
- `Drivers`
- `MediCat.USB`
- bundled `ForgerTools/*` artifacts without their source pipeline

## Runtime Modes

The scripts now support three practical execution modes:

### Repo mode

Scripts are run from `ventoy-core/` and load canonical manifests from
`..\manifests\`.

### Release-bundle mode

Scripts are copied into a release folder where the release manifest sits beside
the public entrypoints. This is the shippable layout.

### Legacy compatibility mode

Older paths under `VentoyToolkitSetup/` still work through shim scripts that
delegate into `ventoy-core/`.

## Canonical Entrypoints

### Setup

- `ventoy-core/Setup-ForgerEMS.ps1`
- `ventoy-core/Setup_Toolkit.ps1`
- `ventoy-core/Setup_USB_Toolkit.ps1`

`Setup_Toolkit.ps1` remains the canonical implementation.
`Setup-ForgerEMS.ps1` is the preferred public name.
`Setup_USB_Toolkit.ps1` remains a legacy compatibility name.

### Update

- `ventoy-core/Update-ForgerEMS.ps1`

This is the canonical manifest-driven updater.

### Verification

- `ventoy-core/Verify-VentoyCore.ps1`

Default mode is offline-safe and fast.
Optional `-Online` adds HEAD-only upstream health checks and provenance
warnings.

### Release assembly

- `Tools/build-release.ps1`

This script builds the shippable bundle from canonical repo sources.

## Core Invariants

These are the invariants that future changes should preserve:

1. `manifests/ForgerEMS.updates.json` is the single source of truth for
   Ventoy core version/build metadata, release classification, and managed
   content.
2. Setup uses the bundled manifest as the single source of truth for managed
   page shortcut generation and manifest seeding.
3. Update falls back to the bundled manifest when the selected target root does
   not already contain one.
4. Manifest destinations and settings-based folders must remain root-contained.
5. `-WhatIf` must remain a true dry-run for setup and updater flows.
6. Compatibility entrypoints must continue delegating without changing
   behavior.
7. Version reporting must come from the bundled manifest, not a second
   hard-coded version constant.
8. Release output must be assembled from canonical source locations, not by
   hand-curating mixed workspace folders.

## Managed Vs Not Fully Managed

### Managed and reproducible in this Ventoy core

- canonical scripts under `ventoy-core/`
- canonical manifests under `manifests/`
- generated shortcuts and files explicitly driven by
  `ForgerEMS.updates.json`
- release-bundle docs under `Docs/ventoy-core/bundle/`
- release assembly under `Tools/build-release.ps1`
- offline and optional online verification behavior

### Visible but not fully maintainable here

- `Tools/Portable/*` payloads
- `Drivers/*` payloads
- `MediCat.USB`
- packaged `ForgerTools/*` artifacts without tracked build pipelines
- large community toolkits sourced outside this repo

Those assets are now explicitly surfaced through
`manifests/vendor.inventory.json`, but they are not claimed as fully owned
source outputs.

## Version + Build + Release Discipline

Release metadata now comes from the canonical manifest:

- `coreName`
- `coreVersion`
- `buildTimestampUtc`
- `releaseType`
- `managedChecksumPolicy`

Setup, update, and verification can display that metadata via `-ShowVersion`,
and setup/update log it at runtime.

Release classification values:

- `dev`
- `candidate`
- `stable`

## Release Bundle Standard

The release builder assembles:

`release/ventoy-core/<coreVersion>/`

with:

- root public and compatibility scripts
- `ForgerEMS.updates.json`
- `VERSION.txt`
- `RELEASE-BUNDLE.txt`
- `CHECKSUMS.sha256`
- `docs/`
- `manifests/`
- `tools/`

That bundle is the unit that should be shipped, copied, or zipped.

## Verification Model

### Offline-safe default

`Verify-VentoyCore.ps1`

Checks:

- setup wrapper behavior
- legacy wrapper behavior
- bundled-manifest fallback
- path escape protection
- true dry-run behavior
- vendor inventory contract validity

### Optional online mode

`Verify-VentoyCore.ps1 -Online`

Checks and warnings:

- HEAD reachability for managed URLs
- HEAD reachability for checksum URLs
- HEAD reachability for known vendor inventory source URLs
- optional validation of `CHECKSUMS.sha256` when a release bundle provides it
- warnings for missing checksum coverage
- warnings for unverified inventory items
- warnings for redirects to different hosts
- warnings for currently unverifiable downloads

## Path To Techbench OS

This Ventoy core is now a clean content-management and shipping layer.

It is not yet the Ubuntu-based "Techbench OS", but it is the right substrate
for that next phase because it already has:

- a declared manifest contract
- explicit ownership boundaries
- version/build/release metadata
- verification hooks
- a repeatable release builder
- a provenance plan

The OS phase should build on this rather than bypass it.
