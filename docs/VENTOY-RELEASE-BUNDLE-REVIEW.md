# Ventoy Release Bundle Review

Use this as the quick human inspection pass after `Tools/build-release.ps1`
completes.

## Inspect `VERSION.txt`

Confirm:

- `coreVersion` matches the intended release
- `buildTimestampUtc` matches the intended build
- `releaseType` matches the promotion target

## Inspect `RELEASE-BUNDLE.txt`

Confirm:

- the bundle describes the correct public entrypoints
- the supported vs unmanaged boundary is stated clearly
- the bundle still describes itself as a whole release folder, not an ad hoc
  workspace snapshot

## Inspect `CHECKSUMS.sha256`

Confirm:

- it exists
- it contains the expected public scripts
- it contains the manifest and schema files
- it contains `VERSION.txt` and `RELEASE-BUNDLE.txt`
- it does not pretend to hash unmanaged vendor payloads

## Inspect `SIGNATURE.txt`

Confirm:

- it exists
- `SignatureType` is `checksum-catalog-sha256`
- `SignedFile` is `CHECKSUMS.sha256`
- `CoreVersion`, `BuildTimestampUtc`, and `ReleaseType` match `VERSION.txt`
- it is clearly an integrity seal, not a publisher-identity signature

## Inspect Public Scripts

Review:

- `Setup-ForgerEMS.ps1`
- `Setup_USB_Toolkit.ps1`
- `Setup_Toolkit.ps1`
- `Update-ForgerEMS.ps1`
- `Verify-VentoyCore.ps1`

Confirm:

- the expected public entrypoints are present
- wrapper/compatibility names still exist
- the bundle looks like the maintained core, not a mixed workspace dump

## Inspect Manifest And Schemas

Review:

- `ForgerEMS.updates.json`
- `manifests/ForgerEMS.updates.schema.json`
- `manifests/vendor.inventory.json`
- `manifests/vendor.inventory.schema.json`

Confirm:

- version/build/release metadata are correct
- managed checksum coverage is present for managed `file` items
- vendor inventory still makes unmanaged content visible without overstating
  maintainability

## Pass / Hold Decision

Pass the bundle when:

- metadata is correct
- integrity artifacts are present
- supported boundaries are honest
- verification already passed

Hold the bundle when:

- version/build/classification metadata is wrong
- integrity artifacts are missing or inconsistent
- the bundle scope looks misleading
- unmanaged content appears to be claimed as source-owned
