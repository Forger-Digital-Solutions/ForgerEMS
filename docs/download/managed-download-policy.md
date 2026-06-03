# Managed Download Policy

ForgerEMS treats USB Builder catalog actions as technician workflows, not as a
download counter. A larger managed catalog is only better when each automated
entry is safe, legal, and independently verifiable.

## Download Modes

- `ManagedDownload`: direct managed file download. The artifact is official,
  version-pinned or deterministic, and backed by SHA-256, SHA-512, an official
  checksum file, or an official GitHub release asset digest.
- `OfficialDownloadPage`: official vendor or project page. The technician opens
  the page, chooses the right artifact, and verifies manually.
- `ManualMediaRequired`: restricted OS/mobile/legacy media. The user supplies
  legally obtained media; ForgerEMS does not redistribute it.
- `ReviewFirst`: official/community page that needs licensing, provenance, or
  operational review before use.
- `VendorPortal` / `OEMSpecific`: driver or OEM support flow. Use model, serial,
  hardware ID, or vendor support lookup.
- `LicenseRestricted`: paid, commercial, trial, EULA, account, or click-through
  flow blocks unattended automation.
- `DynamicMirrorOnly`: official mirror or download flow cannot be safely pinned
  with a stable checksum binding.
- `FirmwareBlocked`: firmware, BIOS, UEFI, or device image download. Always
  manual.
- `CommunityToolkit`: community toolkit page. Review provenance and licensing
  before client use.
- `Unsupported` / `InfoOnly`: reference-only entries when no better action is
  available.

## Managed Promotion Gate

A catalog item may become `ManagedDownload` only when all of these are true:

- The URL is official/project-controlled or an official upstream release asset.
- The artifact URL is direct and version-pinned or deterministically resolvable.
- The manifest records `sha256`, `sha512`, `sha256Url`, `sha512Url`, or an
  official GitHub asset digest/checksum source bound to that exact artifact.
- The license allows normal download/use without a manual EULA acceptance step.
- There is no login wall, store flow, click-through gate, JS-only gate, or
  model picker.
- The item is not firmware, BIOS, UEFI, model-specific driver media, malware, a
  security payload, beta/nightly/RC media, abandonware, or pirated media.
- Under `managedChecksumPolicy=require-for-release`, checksumless managed
  downloads are release blockers.

## Why Some Items Stay Manual

Windows, macOS, iOS/iPadOS, Android firmware, legacy Windows, OEM drivers, BIOS,
UEFI, commercial utilities, remote support tools, and community WinPE bundles
remain manual or review-first by policy. The manifest may link official pages or
vendor portals, but ForgerEMS does not automate downloads where terms, device
selection, or provenance require a human decision.

## UI Labels

The WPF UI now derives technician-facing labels from `downloadMode` instead of
showing a generic `Info` action:

- `ManagedDownload`: `Managed Download`
- `OfficialDownloadPage`: `Official Download Page`
- `ManualMediaRequired`: `Manual Media Required`
- `ReviewFirst`: `Review First`
- `VendorPortal` / `OEMSpecific`: `Vendor Portal`
- `LicenseRestricted`: `License / EULA Required`
- `DynamicMirrorOnly`: `Official Mirror Page`
- `FirmwareBlocked`: `Firmware / BIOS Portal`
- `CommunityToolkit`: `Community Toolkit Page`
- `Unsupported`: `Unsupported / Reference Only`
- `InfoOnly`: `Reference Info`

The manifest still keeps `type=file` and `type=page` for backward
compatibility. If `downloadMode` is missing, legacy manifests infer the action
from `type`, `manualOnly`, `kind`, `sourceTrust`, notes, and legacy warnings.
