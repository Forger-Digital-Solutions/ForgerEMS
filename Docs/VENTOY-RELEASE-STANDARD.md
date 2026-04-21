# Ventoy Release Standard

## Canonical Builder

Use:

```powershell
.\Tools\build-release.ps1
```

Optional zip output:

```powershell
.\Tools\build-release.ps1 -ZipOutput
```

## Version Source

Release version/build metadata comes from:

- `manifests/ForgerEMS.updates.json`

Specifically:

- `coreVersion`
- `buildTimestampUtc`
- `releaseType`
- `managedChecksumPolicy`

## Release Output Layout

The builder assembles:

```text
release/
  ventoy-core/
    <coreVersion>/
      Setup-ForgerEMS.ps1
      Setup_USB_Toolkit.ps1
      Setup_Toolkit.ps1
      Update-ForgerEMS.ps1
      Verify-VentoyCore.ps1
      ForgerEMS.updates.json
      VERSION.txt
      RELEASE-BUNDLE.txt
      CHECKSUMS.sha256
      SIGNATURE.txt
      docs/
      manifests/
      tools/
```

Current subfolders:

- `docs/`
  Release-bundle documentation copied from `Docs/ventoy-core/bundle/`
- `manifests/`
  Schema + vendor inventory + vendor inventory schema
- `tools/`
  Convenience helper artifacts such as `ScriptCommands.txt`

## Release Classification

The canonical manifest declares one release type:

- `dev`
  Local testing or in-progress baseline work. Not for distribution.
- `candidate`
  Verified build intended for review or controlled field testing.
- `stable`
  Approved bundle intended for real distribution.

That value is copied into `VERSION.txt` so the shipped bundle declares its own
classification.

## Integrity Artifacts

The bundle now generates:

- `CHECKSUMS.sha256`
- `SIGNATURE.txt`

`CHECKSUMS.sha256` covers:

- public PowerShell entrypoints
- `ForgerEMS.updates.json`
- manifest schemas
- `manifests/vendor.inventory.json`
- `VERSION.txt`
- `RELEASE-BUNDLE.txt`

`SIGNATURE.txt` is a simple integrity seal, not a cryptographic publisher
signature. It records:

- the release version/build/classification
- the SHA-256 of `CHECKSUMS.sha256`
- the SHA-256 of `ForgerEMS.updates.json`

This means the checksum catalog is sealed, and the catalog in turn seals the
public scripts and manifest content shipped in the bundle.

## Operator Verification

Recommended verification flow for a bundle:

1. Run `.\Verify-VentoyCore.ps1` from the release folder.
2. Let the script validate `CHECKSUMS.sha256` automatically when present.
3. Let the script validate `SIGNATURE.txt` automatically when present.
4. Treat any mismatch as bundle drift, tampering, or a packaging error.

## What Makes A Bundle Shippable

A bundle is considered shippable when:

1. it was assembled by `Tools/build-release.ps1`
2. the canonical manifest validated successfully
3. the vendor inventory validated successfully
4. managed checksum coverage satisfied the current release-class rule
5. the release folder contains the expected scripts, manifest, docs, and
   support metadata
6. offline verification passes on the canonical source and on the built bundle

## Rerun Safety

The builder is intended to be safe to rerun:

- it rebuilds the version folder in place
- it does not use `.verify` as a source of truth
- it excludes unrelated workspace artifacts from the release bundle

## Scope Boundary

The release bundle standard only guarantees the maintainable Ventoy core.

It does not claim that every vendor payload elsewhere in the workspace is fully
owned, reproducible, or source-built.
