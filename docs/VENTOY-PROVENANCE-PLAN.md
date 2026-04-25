# Ventoy Provenance Plan

## Goal

The goal is auditability without pretending that every bundled third-party
payload is already source-owned.

This baseline makes provenance visible, enforces checksum discipline for
manifest-managed downloads, and keeps unmanaged/vendor content explicitly
outside the supported ownership boundary.

## Managed Content Provenance

Canonical source:

- `manifests/ForgerEMS.updates.json`

Managed checksum discipline:

- `dev`
  Missing checksum coverage warns by default.
- `candidate`
  Missing checksum coverage fails verification and release build.
- `stable`
  Missing checksum coverage fails verification and release build.

Managed coverage applies only to manifest-managed `file` items.

Preferred provenance state for managed downloads:

- pinned `sha256`
- `sha256Url` when an upstream checksum source exists
- both fields when practical

## Vendor Inventory Provenance

Canonical file:

- `manifests/vendor.inventory.json`

Schema:

- `manifests/vendor.inventory.schema.json`

Each entry carries:

| Field | Meaning |
| --- | --- |
| `name` | Human-readable inventory label |
| `path` | Workspace-relative location |
| `sourceUrl` | Known upstream URL if available |
| `version` | Known version, `workspace-snapshot`, `mixed`, or `n/a` |
| `managed` | Whether the Ventoy core directly manages the item |
| `checksum` | SHA-256 when available |
| `verified` | Whether provenance has been manually verified |
| `source_trust` | `official`, `community`, or `manual` |
| `notes` | Maintenance caveats or context |

Inventory warnings do not mean the Ventoy core claims source ownership. They
exist to make unsupported/manual content visible.

## Integrity Sealing

Implemented now:

- `release/ventoy-core/<version>/CHECKSUMS.sha256`
- `release/ventoy-core/<version>/SIGNATURE.txt`

What is sealed:

- `CHECKSUMS.sha256` hashes the shipped scripts, manifest, schemas, and release
  metadata files
- `SIGNATURE.txt` hashes `CHECKSUMS.sha256` and `ForgerEMS.updates.json`

What this is:

- a simple release integrity seal

What this is not:

- a cryptographic publisher signature with private-key identity

## Verification Behavior

### Offline verification

`ventoy-core/Verify-VentoyCore.ps1`

Offline mode validates:

- path safety and dry-run guarantees
- wrapper entrypoints and manifest fallback
- vendor inventory structure and version/release alignment
- release checksums when present
- release signature when present

### Optional online verification

`ventoy-core/Verify-VentoyCore.ps1 -Online`

Online mode is warning-oriented and may report:

- missing checksum coverage
- inventory items not marked `verified`
- missing source URLs
- HEAD probe failures
- redirects to different hosts
- currently unverifiable downloads

Default verification remains offline-safe and internet-free.

## Supported Vs Not Fully Managed

Supported:

- `ventoy-core` scripts
- manifest system
- setup/update lifecycle
- release assembly and verification behavior

Not fully managed:

- `Tools\Portable`
- `Drivers`
- `MediCat.USB`
- vendor binaries without a tracked source/build workflow

The inventory exists to make this boundary explicit, not to blur it.

## Remaining Provenance Gaps

This provenance plan still does not claim:

- hosted CI proof on a pushed remote repository
- source ownership of community/vendor bundles
- immutable upstream URLs
- full cryptographic signing identity

It is the bridge from "manual but visible" to "auditable and operationally
controlled."
