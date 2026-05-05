# Ventoy Download Catalog

The Ventoy core uses three practical catalog buckets:

- `auto-download safe`
- `manual only`
- `review-first`

Meaning:

- `auto-download safe` means a manifest-managed `file` item with an official
  direct artifact URL, no gated/account/clickthrough flow, and checksum
  coverage in the manifest.
- `manual only` means a manifest-managed `page` item where the updater should
  only create a shortcut or info file because licensing, redistribution,
  install flow, or vendor terms make automation inappropriate.
- `review-first` means a manifest-managed `page` item that stays manual today
  because checksum coverage, provenance confidence, or operational stability is
  not yet good enough for the stable manifest.

`safe` still depends on upstream availability. It does not mean that an
upstream URL is permanently stable; it means the current manifest points to an
official direct artifact with acceptable checksum coverage today.

Current safe count: `16`

Health snapshot:

- fragility: `high 6`, `medium 7`, `low 3`
- checksum posture: `pinned-only 7`, `pinned+remote 8`, `remote-only 1`
- baseline status: `OK 9`, `OK-limited 7`, `DRIFT 0`
- borderline today: `BlueScreenView`, `CrystalDiskInfo`, `Linux Mint`

For fragility ranking, fallback rules, operator failure guidance, and the
revalidation workflow, see
[`VENTOY-MANAGED-DOWNLOAD-MAINTENANCE.md`](./VENTOY-MANAGED-DOWNLOAD-MAINTENANCE.md).

## Auto-Download Safe

- `SystemRescue 13.00 (amd64)`
- `GParted Live 1.8.1-3 (amd64)`
- `Clonezilla Live 3.3.1-35 (amd64)`
- `Rescuezilla 2.6.1 (64-bit oracular)`
- `Ventoy 1.1.11 (Windows package)`
  Manual `Ventoy2Disk` installation still remains an operator step.
- `MemTest86+ 8.00 (x86_64 ISO archive)`
  Upstream ships a compressed ISO archive; extract the ISO before using it
  with Ventoy.
- `Ubuntu 24.04.4 LTS Desktop (amd64)`
- `Linux Mint 22.3 Cinnamon (64-bit)`
- `Kali Linux 2026.1 Installer (amd64)`
- `CrystalDiskInfo 9.8.0 (standard zip)`
  Versioned SourceForge artifact linked from the official CrystalDiskInfo
  download flow. The manifest pins a verified SHA-256 from the live official
  file because no machine-readable vendor checksum was found.
- `Angry IP Scanner 3.9.3 (Windows setup)`
  Official GitHub release asset with bundled Java runtime. Checksum coverage is
  taken from the asset-specific GitHub release digest endpoint.
- `BlueScreenView 1.55 (x64 zip)`
- `DriverStoreExplorer 1.0.26 (zip)`
  Official GitHub release ZIP. Checksum coverage is taken from the
  asset-specific GitHub release digest endpoint.
- `RustDesk 1.4.6 (x86_64 exe)`
  The pinned SHA-256 was refreshed from the live official GitHub release asset
  digest during the current review pass.
- `Rufus 4.13 Portable (x64)`
  Official GitHub release asset linked directly from `rufus.ie`. Checksum
  coverage is taken from the asset-specific GitHub release digest endpoint.
- `balenaEtcher 2.1.4 Setup (x64)`

Status note:

- `OK` means the entry has remote checksum coverage and should fully
  participate in live revalidation.
- `OK-limited` means the entry is still safe today, but live checksum
  confirmation remains limited to the pinned manifest hash.
  The manifest now also records the asset-specific GitHub release digest URL as
  an official checksum source.

## Manual Only

- `Windows 11 Download Page`
- `Windows 10 Download Page`
- `Debian Live Images Download Page`
- `Arch Linux Download Page`
- `FreeDOS Download Page`
- `Fedora Workstation Download Page`
- `CrystalDiskMark Download Page`
- `Samsung Magician Download Page`
- `WD Dashboard Download Page`
- `7-Zip Download Page`
- `Nmap Download Page`
- `Microsoft Visual C++ Redistributable Download Page`
- `.NET 8 Desktop Runtime Download Page`
- `Firefox All Languages Download Page`
- `Chrome Enterprise Browser Download Page`
- `MediCat Download Page`
- `Sergei Strelec WinPE Info`
- `Ultimate Boot CD Download Page`
- `AnyDesk Download Page`
- `HWiNFO Download Page`
- `OCCT Download Page`
- `CPU-Z Download Page`
- `HWMonitor Download Page`

## Review-First

- `Hiren's BootCD PE Download Page`
- `Autoruns Download Page`
- `Process Explorer Download Page`
- `Sysinternals Suite Download Page`
- `Everything Search Download Page`
- `GPU-Z Download Page`
- `DDU Download Page`
- `NVCleanInstall Download Page`
- `HDDScan Download Page`
- `DiskGenius Download Page`
- `Advanced IP Scanner Download Page`
- `Intel Driver Download Center`
- `Realtek Downloads`

## Checksum Sourcing Notes

- Prefer asset-specific checksum sources over HTML pages whenever the upstream
  project provides them.
- GitHub-hosted safe entries now use official asset-digest metadata for:
  `Rufus`, `Angry IP Scanner`, `DriverStoreExplorer`, `RustDesk`, and
  `balenaEtcher`.
- `CrystalDiskInfo` stays on a pinned manifest SHA-256 because the official
  vendor flow exposes a versioned artifact but not a machine-readable checksum
  file or per-asset digest endpoint.
- Manual/runtime/browser shortcuts intentionally stay as `page` entries. The
  upstream flows are dynamic, license-sensitive, architecture-sensitive, or
  better verified by a human at install time.

## 2026 Candidate Decisions

| Candidate | Decision | Reason |
| --- | --- | --- |
| `Debian Live ISO` | `manual/info-only` | Official Debian live page offers multiple flavors plus checksum/signature guidance; keep human choice. |
| `Fedora Workstation ISO` | `manual/info-only` | Official Fedora page tracks the current release and architecture choices. |
| `Arch Linux ISO` | `manual/info-only` | Monthly rolling ISO; official page publishes checksums/signatures, so avoid stale pinned direct links. |
| `Hiren's BootCD PE` | `manual/info-only` | Existing official shortcut retained for licensing/provenance caution. |
| `Windows Media Creation Tool / Windows ISO` | `manual/info-only` | Existing Microsoft official Windows 10/11 shortcuts retained. |
| `Microsoft Sysinternals Suite` | `manual/info-only` | Existing Microsoft Learn shortcut retained; do not bundle blindly. |
| `Nmap / Zenmap` | `manual/info-only` | Official Nmap page includes installer, Npcap, signatures/hashes, and license notes. |
| `HWiNFO` | `manual/info-only` | Existing shortcut retained because business/commercial licensing applies. |
| `7-Zip` | `manual/info-only` | Official `7-zip.org` shortcut added; avoid lookalike domains. |
| `Firefox / Chrome offline installers` | `manual/info-only` | Official selectors require platform/channel/terms choices. |
| `Visual C++ Redistributables` | `manual/info-only` | Microsoft license and architecture selection should remain explicit. |
| `.NET Desktop Runtime` | `manual/info-only` | Official Microsoft page tracks current servicing builds and architectures. |
| `Ubuntu current/previous LTS` | `managed current LTS + manual page` | Existing managed Ubuntu LTS item retained; page shortcut covers operator choice. |
| `MemTest86+` | `managed download` | Existing official versioned artifact has checksum coverage. |
| `FreeDOS` | `manual/info-only` | Useful for legacy firmware/DOS workflows, but boot mode and package choice are operator-specific. |

## Fragility And Fallbacks

- `SourceForge-backed safe URLs`
  `SystemRescue`, `GParted Live`, `Clonezilla Live`, `Ventoy`, and
  `CrystalDiskInfo`.
  Expected break scenarios: `/download` wrapper behavior changes, anti-abuse
  changes, or project path reshuffles.
  Fallback: re-derive the versioned project file URL from the project's
  official release/download page and keep or refresh checksum coverage before
  promoting the path back into the stable manifest.
- `GitHub release-backed safe URLs`
  `Rescuezilla`, `Rufus`, `Angry IP Scanner`, `DriverStoreExplorer`,
  `RustDesk`, and `balenaEtcher`.
  Expected break scenarios: asset renames, tag changes, release pruning, or
  GitHub API/rate-limit issues affecting digest lookups.
  Fallback: use the project's official release page or official project site to
  locate the same versioned asset, refresh the pinned `sha256`, and update the
  asset-specific digest URL if the release asset ID changed.
- `Mirror-backed safe URLs`
  `Linux Mint 22.3 Cinnamon (64-bit)`.
  Expected break scenarios: mirror path rotation, stale mirror pruning, or
  directory layout changes.
  Fallback: move back to an official Mint-controlled download endpoint or a
  newly validated official mirror only after the exact version and checksum are
  re-confirmed.
- `Version-directory safe URLs`
  `Ubuntu`, `Kali`, and `MemTest86+`.
  Expected break scenarios: retired version directories or moved checksum
  manifests after upstream refreshes.
  Fallback: use the vendor release index for the same version, re-confirm the
  checksum source, and only then update the stable manifest.
