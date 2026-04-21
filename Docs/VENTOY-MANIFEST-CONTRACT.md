# Ventoy Manifest Contract

## Canonical Location

The canonical managed-content manifest is:

- `manifests/ForgerEMS.updates.json`

Related files:

- `manifests/ForgerEMS.updates.schema.json`
- `manifests/vendor.inventory.json`
- `manifests/vendor.inventory.schema.json`

`ForgerEMS.updates.json` is the single source of truth for:

- managed setup/update content
- bundle version metadata
- build timestamp metadata
- release classification metadata
- managed checksum release policy

## Top-Level Metadata

| Field | Required | Type | Purpose |
| --- | --- | --- | --- |
| `coreName` | No | non-empty string | Human-readable bundle name. |
| `coreVersion` | No | non-empty string | Canonical release/version identifier used by scripts and release build output. |
| `buildTimestampUtc` | No | non-empty string | Canonical build stamp used by scripts and release build output. |
| `releaseType` | No | `dev`, `candidate`, or `stable` | Release classification for lifecycle and distribution discipline. |
| `managedChecksumPolicy` | No | `warn` or `require-for-release` | Declares how managed checksum coverage is treated by the release process. |
| `manifestVersion` | No | integer >= 1 | Contract/schema version marker. |
| `settings` | No | object | Updater defaults. |
| `items` | Yes | non-empty array | Managed files and page shortcuts. |

## `settings` Fields

| Field | Required | Type | Notes |
| --- | --- | --- | --- |
| `downloadFolder` | No | string | Relative staging folder. |
| `archiveFolder` | No | string | Relative archive folder. |
| `logFolder` | No | string | Relative log folder. |
| `timeoutSec` | No | integer >= 1 | Default timeout. |
| `retryCount` | No | integer >= 1 | Default retry count. |
| `userAgent` | No | string | HTTP user-agent value. |
| `maxArchivePerItem` | No | integer >= 1 | Retained archives per item. |

## `items` Fields

| Field | Required | Type | Notes |
| --- | --- | --- | --- |
| `name` | Yes | string | Human-readable label for logs and archive names. |
| `type` | No | `file` or `page` | Defaults to `file`. |
| `dest` | Yes | string | Relative path under the selected target root. |
| `url` | Yes | string | Download URL or information page URL. |
| `sha256` | No | 64-char hex string | Pinned checksum when available. |
| `sha256Url` | No | string | Remote checksum source for `file` items only. |
| `sourceType` | No | `sourceforge`, `github-release`, `official-mirror`, or `official-version-path` | Resilience metadata for managed `file` items. |
| `fragilityLevel` | No | `low`, `medium`, or `high` | Operator-facing break-likelihood hint for managed `file` items. |
| `fallbackRule` | No | string | Short operator summary for repairing or demoting a managed `file` item safely. |
| `maintenanceRank` | No | integer >= 1 | Maintenance ordering for managed `file` items. Lower ranks should be checked first. |
| `borderline` | No | boolean | Explicit flag for safe items that are one upstream change away from demotion back to `review-first`. |
| `enabled` | No | boolean | Defaults to `true`. |
| `archive` | No | boolean | Defaults to `true` for file items. |
| `timeoutSec` | No | integer >= 1 | Per-item timeout override. |
| `notes` | No | string | Documentation-only metadata. |

## Catalog Semantics

The canonical manifest also expresses the practical support boundary for the
download catalog:

- `file` items are manifest-managed downloads. In the current baseline these
  are the `auto-download safe` entries and must keep checksum coverage.
- `page` items are manifest-managed shortcuts only. They represent `manual
  only` or `review-first` entries and do not claim that the third-party
  payload itself is updater-managed.
- A managed shortcut is still useful provenance, but it is intentionally not
  treated as equivalent to a managed artifact download.

## Managed File Resilience Metadata

Enabled `file` items in the safe bucket now carry lightweight resilience
metadata so operators can revalidate and repair them consistently:

- `sourceType`
- `fragilityLevel`
- `fallbackRule`
- `maintenanceRank`
- `borderline`

For `candidate` and `stable` releases, verification expects that metadata to be
present and coherent for every enabled managed `file` item.

`maintenanceRank` is treated as an ordered queue for upkeep. The verification
workflow expects the ranks to cover `1..N` without gaps or duplicates so that
future maintenance starts with the most fragile entries first.

`borderline` is optional and intentionally sparse. It is used only for the few
safe items that remain acceptable today but should be inspected first because
they are closest to demotion if upstream conditions change.

`page` items must not declare those fields because they are not treated as
artifact-managed downloads.

## Validation Rules Enforced Early

The shipping scripts reject the manifest before writes when:

- `items` is missing or empty
- an item is null
- `name`, `url`, or `dest` is blank
- `type` is not `file` or `page`
- `manifestVersion`, `timeoutSec`, `retryCount`, or `maxArchivePerItem` is not
  a positive integer
- `coreVersion` is present but blank
- `buildTimestampUtc` is present but not parseable as a date/time string
- `releaseType` is present but not `dev`, `candidate`, or `stable`
- `managedChecksumPolicy` is present but not `warn` or `require-for-release`
- `enabled` or `archive` is not a JSON boolean
- `sha256` is present but not a valid SHA-256 hex string
- `sha256Url` is attached to a `page` item
- `sourceType` is present but not an allowed managed-download source type
- `fragilityLevel` is present but not `low`, `medium`, or `high`
- `maintenanceRank` is present but not a positive integer
- `borderline` is present but not a JSON boolean
- a `page` item declares `sourceType`, `fragilityLevel`, `fallbackRule`, or
  `maintenanceRank`
- a `page` item declares `borderline`
- any managed path escapes the selected root
- any managed path is rooted instead of relative

## Path Rules

Managed paths are always relative to the selected target root.

Allowed:

- `ISO\Tools\clonezilla-live.iso`
- `Tools\Portable\System\DOWNLOAD - Autoruns.url`
- `_downloads`

Rejected:

- `..\outside.iso`
- `C:\outside.iso`
- `\\server\share\outside.iso`

## Release-Class Checksum Discipline

Managed checksum coverage applies only to manifest-managed `file` items.

Behavior by release class:

- `dev`
  Missing managed checksum coverage warns by default. Operators can still force
  strict verification with `-EnforceManagedChecksums`.
- `candidate`
  Missing managed checksum coverage fails verification and release build.
  `managedChecksumPolicy` must be `require-for-release`.
- `stable`
  Missing managed checksum coverage fails verification and release build.
  `managedChecksumPolicy` must be `require-for-release`.

Coverage means the item provides at least one of:

- `sha256`
- `sha256Url`

When both are present, the manifest has both a pinned local value and a
documented upstream checksum source.

`sha256Url` should point to a checksum-oriented resource for the exact artifact,
not a general release page. For public GitHub release assets, an
asset-specific API URL is acceptable because it returns the digest for that
single asset without scraping HTML.

## Revalidation Workflow

The lightweight operator workflow for the safe bucket is:

- `.\Verify-VentoyCore.ps1 -RevalidateManagedDownloads`

That mode is intentionally read-only with respect to the manifest:

- it checks whether each enabled managed download URL still resolves
- it checks whether each declared remote checksum source still resolves
- it reports drift under `.verify\<run>\` without auto-patching the manifest

For operator-friendly retention, the same run also writes stable archive copies
under:

- `.verify\managed-download-revalidation\<timestamp>\`
- `.verify\managed-download-revalidation\latest\`

Expected files:

- `managed-download-summary.txt`
- `managed-download-revalidation.csv`
- `managed-download-revalidation.txt`

See `Docs/VENTOY-MANAGED-DOWNLOAD-MAINTENANCE.md` for the ranked safe bucket,
fallback guidance, and demotion rules.

## Version Source-Of-Truth Rule

`coreVersion`, `buildTimestampUtc`, `releaseType`, and
`managedChecksumPolicy` belong to the canonical manifest so that:

- setup reports the same version the updater reports
- verification reports the same version the release builder uses
- the release output folder is named from one declared value
- release classification is visible without inventing a second source of truth
- checksum enforcement intent is visible in the same place as the release
  classification

## Relationship To Vendor Inventory

`manifests/vendor.inventory.json` is not the managed-content manifest.

It exists to make manual/vendor payloads visible and auditable, especially:

- `Tools\Portable`
- `Drivers`
- `MediCat.USB`
- bundled `ForgerTools` artifacts

The vendor inventory now carries provenance-oriented metadata:

- `checksum`
- `verified`
- `source_trust`
- `releaseType`

That metadata improves auditability, but it does not automatically make those
assets source-owned or updater-managed.

## Vendor Inventory Contract

Each vendor inventory item is expected to declare:

| Field | Required | Type | Purpose |
| --- | --- | --- | --- |
| `name` | Yes | non-empty string | Human-readable inventory label. |
| `path` | Yes | non-empty string | Workspace-relative location represented by the entry. |
| `sourceUrl` | Yes | string | Known upstream/source URL, or blank if not known. |
| `version` | Yes | non-empty string | Known version, `workspace-snapshot`, `mixed`, or `n/a`. |
| `managed` | Yes | boolean | Whether the Ventoy core directly manages this item. |
| `checksum` | Yes | SHA-256 hex or blank | Integrity data when available. |
| `verified` | Yes | boolean | Whether the current source/provenance has been manually verified. |
| `source_trust` | Yes | `official`, `community`, or `manual` | Provenance/trust classification. |
| `notes` | No | string | Human notes about ownership or maintenance limits. |

Offline validation ensures the vendor inventory structure is coherent. Optional
online verification adds warnings for missing checksums, missing source URLs,
upstream probe failures, and redirect drift to different hosts.
