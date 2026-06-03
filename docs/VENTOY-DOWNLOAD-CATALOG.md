# Ventoy Download Catalog

The ForgerEMS USB Builder catalog now uses first-class `downloadMode` values.
`type=file` and `type=page` remain for backward compatibility, but the UI action
comes from `downloadMode` so technicians see the correct workflow instead of a
generic `Info` button.

Current source manifest summary:

- Total manifest items: 217
- `ManagedDownload`: 50
- `OfficialDownloadPage`: 94
- `ManualMediaRequired`: 22
- `ReviewFirst`: 5
- `VendorPortal`: 10
- `OEMSpecific`: 10
- `LicenseRestricted`: 8
- `DynamicMirrorOnly`: 4
- `FirmwareBlocked`: 9
- `CommunityToolkit`: 5

The promotion policy is documented in
[`docs/download/managed-download-policy.md`](./download/managed-download-policy.md).

## Managed Downloads

Managed entries are direct official artifacts with checksum or signature proof.
Under `managedChecksumPolicy=require-for-release`, a managed file without
checksum coverage is a release blocker.

Existing managed entries remain managed, including SystemRescue, GParted,
Clonezilla, Rescuezilla, Ubuntu Desktop, Linux Mint, Kali, Alpine, Proxmox VE,
Ubuntu Server, Debian netinst, Fedora Server, FreeBSD, OpenBSD, Rocky Linux,
AlmaLinux, NetBSD, openSUSE Leap, MemTest86+, Rufus, balenaEtcher, PuTTY,
Wireshark, VeraCrypt, RustDesk, DriverStoreExplorer, BlueScreenView,
Notepad++, System Informer, CrystalDiskInfo, Angry IP Scanner, and the pinned
Ventoy fallback package.

No new item was promoted to managed in this pass without complete official
artifact and checksum evidence.

## Official Download Pages

Official pages are not failures. They are the correct action when an upstream
download is official but cannot be safely automated because the page uses a
selector, current-release redirect, architecture choice, form, or human
verification flow. Fedora Workstation, Debian Live, Arch Linux, FreeDOS,
TrueNAS SCALE, Pop!_OS, Zorin OS, EndeavourOS, KDE neon, Debian netinst page,
Ubuntu/Fedora/Rocky/Alma/Alpine pages, BSD pages, many technician utilities,
and managed-entry companion pages use this mode.

These entries may carry `managedPromotionCandidate=true` when they look
promotable in a future maintenance pass, but they stay page-only until the exact
artifact and checksum binding are verified.

## Manual Media Required

Windows legacy media, macOS installers, iOS/iPadOS IPSW media, and other
restricted installer media remain user-supplied. ForgerEMS may create drop
folders and official workflow shortcuts, but it does not redistribute Windows,
Apple, mobile, unsupported, or abandonware media.

## Review First And Community Toolkits

Community WinPE/toolkit entries, provenance-sensitive utilities, and some
diagnostic tools remain review-first or community-toolkit pages. Examples
include Hiren's BootCD PE, Sergei Strelec, MediCat, Ultimate Boot CD,
Sysinternals shortcut pages, HDDScan, and DiskGenius.

Review-first means a technician must inspect licensing, provenance, download
chain, and client suitability before use. It is not a managed-download backlog
unless official artifact and checksum evidence can be produced.

## Vendor Portals And Firmware

OEM driver shortcuts are portal actions, not verified binaries. Vendor and OEM
support pages use `VendorPortal` or `OEMSpecific`; firmware, BIOS, UEFI, Surface
driver/firmware, Pixel factory/OTA images, and Android OEM firmware workflows use
`FirmwareBlocked`.

ForgerEMS does not auto-download BIOS/UEFI firmware, device firmware, or
model-specific driver bundles.

## License Restricted And Dynamic Mirrors

Paid, commercial, trial, EULA, or account/click-through flows use
`LicenseRestricted`. Examples include Parted Magic, AIDA64, HWiNFO, OCCT,
Macrium, AnyDesk, Chrome Enterprise, and PassMark MemTest86.

Official mirror flows that cannot be pinned safely use `DynamicMirrorOnly`.
Examples include MX Linux, Manjaro, GPU-Z, and NVCleanInstall.

## Metadata Contract

Every manifest item now carries or can infer:

- `downloadMode`
- `actionLabel`
- `actionReason`
- `promotionStatus`
- `promotionEvidence`
- `legalRisk`
- `checksumRequirement`
- `managedPromotionCandidate`

The vendor inventory also records `downloadMode`, `actionLabel`,
`managedReason`, and `verificationMode` so manual roots stay manual and official
driver shortcuts remain verified page shortcuts.
