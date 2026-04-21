# Techbench OS Plan

## Purpose

This document sketches how the current Ventoy core could evolve into your
Ubuntu-based "Techbench OS" without implementing that phase yet.

The goal is to carry forward the parts that are already disciplined:

- manifest-driven ownership
- explicit managed vs unmanaged boundaries
- repeatable verification
- release packaging

## Proposed Filesystem Mapping

In a live Ubuntu-based system, the current Ventoy toolkit concept maps cleanly
to:

- `/opt/forgerems/toolkit`
  Canonical installed toolkit content
- `/opt/forgerems/manifests`
  Managed package/content manifests
- `/var/lib/forgerems`
  Mutable runtime state, caches, archives, reports
- `/var/log/forgerems`
  Logs
- `/usr/local/bin/forgerems-*`
  Operator-facing entrypoints and maintenance commands

## Mapping From Current Ventoy Core

Current USB-era concept -> likely OS-era destination:

- `ventoy-core/` -> `/opt/forgerems/toolkit/core`
- `manifests/ForgerEMS.updates.json` -> `/opt/forgerems/manifests/toolkit.packages.json`
- `manifests/vendor.inventory.json` -> `/opt/forgerems/manifests/vendor.inventory.json`
- `_downloads`, `_archive`, `_reports`, `_logs` -> `/var/lib/forgerems/*` and
  `/var/log/forgerems`
- portable/manual bundles -> either package recipes, managed artifact sources,
  or explicitly unsupported manual layers

## How The Manifest System Can Evolve

The current Ventoy manifest already models:

- item identity
- source URL
- destination path
- checksum intent
- enable/disable state

For Techbench OS, the next-generation manifest can grow into an OS package
manifest by adding concepts such as:

- package type
  apt / deb / snap / appimage / tarball / script / container / manual
- install method
  install / extract / copy / symlink / systemd unit / desktop entry
- architecture
  amd64 / arm64 / all
- channels
  stable / beta / pinned
- dependencies
  packages, services, or platform features
- post-install hooks
  permissions, udev, desktop integration, service enablement

## Update Model: USB vs Live OS

### USB-era Ventoy core

- updates write directly into a selected USB/toolkit root
- content is mostly files, ISOs, and shortcuts
- dry-run means "do not modify the target root"

### Live OS era

- updates should separate immutable installed content from mutable runtime state
- package installation likely targets `/opt/forgerems` and system package
  managers
- dry-run should model:
  - package plan
  - disk impact
  - service changes
  - rollback implications

## Ownership Strategy For The OS Phase

To avoid repeating the "collection of scripts" problem at OS scale:

1. move each real Forger tool into its own tracked source/build pipeline
2. reduce the vendor inventory by replacing manual folders with declared
   package sources
3. keep explicit inventory for any remaining manual/vendor artifacts
4. require release metadata and verification for each OS image build

## Suggested Milestones

1. Finish the Ventoy core maintainability baseline.
2. Convert the Forger tools into source-owned build outputs.
3. Introduce a package-oriented manifest alongside the current file-oriented one.
4. Build a prototype Ubuntu image with `/opt/forgerems/toolkit`.
5. Add OS-level verification for install, update, rollback, and offline use.

## Important Boundary

Techbench OS should be treated as a new architecture layer, not as a direct
renaming of the current USB toolkit.

The Ventoy core gives you the discipline and metadata foundation. The OS phase
should reuse that discipline while changing the delivery model from "USB folder
layout" to "installed system image".
