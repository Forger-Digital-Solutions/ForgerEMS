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

Current safe count: `16` (unchanged in the OS/toolkit expansion pass —
no fabricated checksums; every new OS/tool addition lands as `manual only`).

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

## Manual Only — Windows family

Current Windows manual entries (modern + full legacy chain). All Windows
entries are `Manual ISO Required`. ForgerEMS does not redistribute Windows
media; the technician supplies the ISO from a legitimate source.

- `Windows 11 Download Page` — supported, Microsoft Media Creation Tool / ISO.
- `Windows 10 Download Page` — supported (Extended Support window).
- `Windows Server Evaluation Center` — current-server 180-day eval.
- `Windows ADK and WinPE Info` — build official WinPE recovery media.
- `Windows 8.1 Lifecycle Info` — Microsoft Learn lifecycle page. EOL 2023-01-10.
- `Windows 8 Lifecycle Info` — Microsoft Learn lifecycle page. EOL 2016-01-12.
- `Windows 7 Lifecycle Info` — Microsoft Learn lifecycle page. EOL 2020-01-14.
- `Windows Vista Lifecycle Info` — Wikipedia info page. EOL 2017-04-11.
- `Windows XP Lifecycle Info` — Wikipedia info page. EOL 2014-04-08.
- `Windows 2000 Lifecycle Info` — Wikipedia info page. EOL 2010-07-13.
- `Windows ME Lifecycle Info` — Wikipedia info page. EOL 2006-07-11.
- `Windows 98 Lifecycle Info` — Wikipedia info page. EOL 2006-07-11.
- `Windows 95 Lifecycle Info` — Wikipedia info page. EOL 2001-12-31.
- `Sergei Strelec WinPE Info` — community WinPE bundle (provenance review).

Every legacy Windows entry carries an explicit `legacyWarning` and is tagged
`osCategory: Legacy` (or `Hobby` for the 9x line). The UI must render these
with the `Manual ISO Required` + `Unsupported by vendor` badges, never as
"Recommended".

## Manual Only — Linux desktop / live family

- `Ubuntu Download Page` (info shortcut for managed Ubuntu ISO).
- `Linux Mint Download Page` (info shortcut for managed Mint ISO).
- `Fedora Workstation Download Page`.
- `Debian Live Images Download Page`.
- `Debian netinst Download Page`.
- `Arch Linux Download Page`.
- `Endless OS Download Page`.
- `Pop!_OS Download Page`.
- `Zorin OS Download Page`.
- `elementary OS Download Page`.
- `MX Linux Download Page`.
- `EndeavourOS Download Page`.
- `KDE neon Download Page`.
- `Manjaro Download Page`.
- `openSUSE Download Page`.
- `Tails Download Page` — informed-consent / authorized use only.
- `Qubes OS Download Page` — Xen-based compartmentalised OS (hardware HCL).

## Manual Only — Linux server / admin / hypervisor / appliance

- `Ubuntu Server Download Page`.
- `Fedora Server Download Page`.
- `Rocky Linux Download Page`.
- `AlmaLinux Download Page`.
- `Alpine Linux Download Page`.
- `Proxmox VE Download Page` — KVM/LXC hypervisor.
- `TrueNAS SCALE Download Page` — Linux-based NAS appliance.
- `pfSense Community Edition Download Page` — FreeBSD-based firewall.
- `OPNsense Download Page` — FreeBSD-based firewall (community-first).

## Manual Only — Recovery / Forensic / Security distros

- `Kali Linux Download Page` (info shortcut for managed Kali ISO).
- `SystemRescue Download Page` (info shortcut for managed ISO).
- `Parrot OS Download Page`.
- `Rescatux Download Page`.
- `Super Grub2 Disk Download Page`.
- `MemTest86 (PassMark UEFI) Download Page` — UEFI-aware; complements the
  bundled MemTest86+ ISO.
- `Parted Magic Download Page` — paid commercial all-in-one rescue distro.
- `Hiren's BootCD PE Download Page` (review-first; see below).

## Manual Only — BSD / Other-Unix / Hobby / Nostalgia

- `FreeBSD Download Page`.
- `NetBSD Download Page`.
- `OpenBSD Download Page`.
- `OpenIndiana Download Page`.
- `ReactOS Download Page` — alpha quality.
- `Haiku Download Page`.
- `KolibriOS Download Page` — assembly hobby OS.
- `Tiny Core Linux Download Page`.
- `Puppy Linux Download Page`.
- `Slackware Download Page`.
- `Gentoo Linux Download Page`.
- `FreeDOS Download Page` — BIOS flashing / legacy DOS workflows.

## Manual Only — Technician utilities (expanded)

- Disk: `CrystalDiskMark`, `Samsung Magician`, `WD Dashboard`, `HDDScan`,
  `DiskGenius`, `CrystalDiskInfo (info)`, `MiniTool Partition Wizard Free`,
  `EaseUS Partition Master Free`, `Macrium Reflect Home Info`,
  `TestDisk and PhotoRec`, `Recuva`, `Smartmontools`.
- Hardware: `CPU-Z`, `HWiNFO`, `HWMonitor`, `OCCT`, `GPU-Z`, `AIDA64`,
  `Speccy`.
- Network: `Advanced IP Scanner`, `Angry IP Scanner (info)`, `Nmap`,
  `Wireshark`, `PuTTY`, `WinSCP`.
- Security: `Malwarebytes AdwCleaner`, `Emsisoft Emergency Kit`,
  `KeePass`, `VeraCrypt`, `ClamWin / ClamAV`,
  `Kaspersky Virus Removal Tool Info`.
- System: `Autoruns`, `Process Explorer`, `Sysinternals Suite`, `7-Zip`,
  `BlueScreenView (info)`, `DriverStoreExplorer (info)`, `Everything Search`,
  `System Informer (Process Hacker)`, `NirLauncher`, `Notepad++`.
- GPU: `DDU`, `NVCleanInstall`.
- Remote: `AnyDesk`, `RustDesk (info)`.
- USB: `Ventoy (info)`, `Rufus (info)`, `balenaEtcher (info)`,
  `Win32 Disk Imager`.
- Browser / Runtime: `Firefox All Languages`, `Chrome Enterprise`,
  `Microsoft VC++ Redistributable`, `.NET 8 Desktop Runtime`.
- MediCat: `MediCat Download Page` — large community toolkit.

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

## OS / Tool Metadata (added in this pass)

Every catalog item now optionally carries technician metadata:

- `kind` — `os`, `tool`, `driver-shortcut`, `runtime`, `browser`.
- `family` — `Windows`, `Linux`, `BSD`, `Other-Unix`, `Hobby`, `DOS`,
  `Recovery`, `Security`, `Network-Appliance`, `Hypervisor`, etc.
- `osCategory` — `Desktop`, `Server`, `Recovery`, `Forensic`, `Security`,
  `Legacy`, `Hobby`, `Network-Appliance`, `Hypervisor`.
- `architecture` — single token or array (`amd64`, `arm64`, `x86`, …).
- `bootMode` — single token or array (`uefi`, `bios`, `secure-boot`,
  `uefi-csm`, `legacy-only`).
- `recommendedUse` — one-line technician-facing summary.
- `technicianNotes` — gotchas / driver caveats / install notes.
- `licenseNote` — `Free / open source`, `Microsoft EULA`,
  `Paid - vendor licence required`, `Discontinued / unsupported by vendor`,
  `Free for personal use`, etc.
- `manualOnly` — explicit boolean (redundant with `type: page`).
- `legacyWarning` — shown on the UI for EOL / unsupported items.
- `ventoyNotes` — Ventoy-specific compatibility note.
- `secureBootNote` — Secure Boot status note for the entry.
- `sourceTrust` — `official`, `community`, or `manual`.

These fields are optional and additive. Existing tooling
(`Get-ForgerEMSToolkitHealth.ps1`, `Setup_Toolkit.ps1`, `Update-ForgerEMS.ps1`,
`Verify-VentoyCore.ps1`) reads only the previously defined fields and ignores
the new ones, so this is fully backwards compatible. The schema
([`ForgerEMS.updates.schema.json`](../manifests/ForgerEMS.updates.schema.json))
documents the new field shape; both single-string and array forms are
accepted for `architecture` and `bootMode`.

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
- **Newly added Linux server distros (Debian netinst, Fedora Server,
  Rocky, AlmaLinux, Alpine, openSUSE Leap)** are kept as `manual only` pages
  for now — the safe path forward is to promote individual entries to `file`
  only after a real, hashed ISO has been verified against a vendor checksum
  file during a future maintenance pass. No checksums were fabricated for any
  new entry in this expansion.

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
| `MemTest86 (PassMark UEFI)` | `manual/info-only` | UEFI-only PassMark variant. Free vs. paid editions. Complements existing MemTest86+ managed download. |
| `FreeDOS` | `manual/info-only` | Useful for legacy firmware/DOS workflows, but boot mode and package choice are operator-specific. |
| `Legacy Windows (7 / Vista / XP / 2000 / ME / 98 / 95)` | `manual/info-only` | Unsupported by vendor; user must provide own retail/volume licensed ISO. Microsoft Learn / Wikipedia lifecycle pages are the only sources we link. |
| `BSD / Other-Unix (FreeBSD, NetBSD, OpenBSD, OpenIndiana)` | `manual/info-only` | Official multi-arch/mirror flow; no fabricated checksums. |
| `Hobby (ReactOS, Haiku, KolibriOS, Tiny Core, Puppy, Slackware, Gentoo)` | `manual/info-only` | Hobby/niche distros; redirect to official project pages. |
| `Server appliances (Proxmox, TrueNAS SCALE, pfSense CE, OPNsense)` | `manual/info-only` | Some require account/form (pfSense); all use multi-flavor downloaders. |

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
