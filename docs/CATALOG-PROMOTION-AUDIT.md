# ForgerEMS Catalog — Page → File Promotion Audit

**Pass date:** 2026-05-21
**Catalog size after managed-download expansion:** 159 items total = 30 managed-file + 129 manual-page.
**Outcome after selected-download execution pass:** **8 ISO promotions** were added after live source/checksum verification, and **6 managed entries** were updated to newer stable releases. CrystalDiskInfo stayed pinned at 9.8.0 because the 9.9.0 path did not meet the machine-readable checksum bar in this pass. Remaining candidates stay manual until their exact official artifact/checksum flow is verified. This is intentional: no SHA-256 was fabricated, no GitHub asset ID was guessed, and no rolling-mirror URL was pinned without verification.

## Verdict Legend

| Verdict | Meaning |
|---|---|
| **PROMOTE-A** | Strong candidate. Stable versioned URL pattern + vendor publishes per-file `.sha256` (single-hash file). Promote with `sha256Url` only; no pinned hash needed. Verifier resolves at runtime. |
| **PROMOTE-B** | Strong candidate, but vendor publishes a combined `SHA256SUMS` file. Needs a pinned `sha256` (operator must compute or copy from vendor file). Existing `Get-Sha256FromSourceUrl` grabs first match only. |
| **PROMOTE-C** | GitHub-release pattern (Rufus / RustDesk shape). Needs the **per-asset numeric ID** for the `sha256Url` API endpoint, which requires a one-time `gh api` or `curl` lookup. Then pin `sha256` from that endpoint. |
| **KEEP MANUAL — licence** | EULA / paid / personal-use-only / redistribution-restricted. Do not promote. |
| **KEEP MANUAL — account** | Vendor requires login, form fill, or click-through to deliver the ISO. Cannot script safely. |
| **KEEP MANUAL — rotating** | "Latest" / "current" URL or session-based CDN; file name embeds a version that rotates without notice. |
| **KEEP MANUAL — vendor source** | Driver portals, Sysinternals, Microsoft Learn / Wikipedia lifecycle pages. Inherently a page, not a payload. |
| **KEEP MANUAL — checksum unavailable** | Project publishes binaries but no machine-readable checksum (or MD5 only). |
| **NEEDS REVIEW** | Promote-able in principle but flagged for human eyes before encoding (provenance, hash collision risk, jurisdictional issues). |

## Operating Rules (must hold for every promotion)

1. The URL must be a **direct, versioned, vendor-controlled** artifact — no `/latest/` paths, no session redirects.
2. Either `sha256` (pinned) **or** `sha256Url` (single-hash file or per-asset digest endpoint) must be present. **No fabricated hashes.**
3. `sourceType` ∈ `{sourceforge, github-release, official-mirror, official-version-path}`.
4. `fragilityLevel`, `fallbackRule`, and a contiguous `maintenanceRank` must be added (next free rank is **18**).
5. License must permit redistribution at the download level (we are not bundling — we are linking + downloading on demand at the technician's request).

## A. PROMOTE-A — Per-file `.sha256` published by vendor

These projects publish a single hash file per binary (the SystemRescue / Linux Mint pattern already in use). Promotion only needs the binary URL and the `.sha256` URL; the existing PowerShell resolver works.

| Entry | Pattern | sha256Url shape | Notes for follow-up |
|---|---|---|---|
| `Alpine Linux Download Page` | `dl-cdn.alpinelinux.org/alpine/vMAJOR.MINOR/releases/x86_64/alpine-standard-X.Y.Z-x86_64.iso` | `…iso.sha256` co-located | **Promoted:** `Alpine Linux 3.20.0 Standard (x86_64)` after `tools/Add-ManagedDownloadCandidate.ps1` confirmed the direct ISO and exact filename match in the vendor `.sha256` file on 2026-05-21. |
| `Tiny Core Linux Download Page` | `tinycorelinux.net/MAJOR.x/x86/release/CorePlus-X.Y.iso` | none directly — md5 only | **Downgrade to KEEP MANUAL — checksum unavailable.** Project ships only `.md5`. Not promotable today. |
| `Puppy Linux Download Page` | mirror sites + flavour-specific subprojects | none consistent | **KEEP MANUAL — checksum unavailable.** Forks publish hashes inconsistently. |

→ Net PROMOTE-A result after selected-download pass: **Alpine Linux promoted**. Additional PROMOTE-A candidates still require live per-artifact verification.

## B. PROMOTE-B — Combined `SHA256SUMS` file published by vendor

These projects publish one file per release that lists hashes for multiple flavours. The existing PowerShell `Get-Sha256FromSourceUrl` returns the first 64-hex match, which is wrong for multi-line files. Promotion requires **pinning `sha256`** in the manifest, which means an operator must paste the correct line from the vendor's signed `SHA256SUMS` file.

| Entry | Combined-hash URL | Operator action |
|---|---|---|
| `Ubuntu Server Download Page` | `releases.ubuntu.com/24.04.4/SHA256SUMS` | Copy the line for `*-live-server-amd64.iso`. Pin `sha256`. `sourceType: official-version-path`, low fragility. |
| `Debian netinst Download Page` | `cdimage.debian.org/debian-cd/CURRENT/amd64/iso-cd/SHA256SUMS` | Lock to a specific point release directory, not `current/`. Pin per-file hash. Medium fragility. |
| `Fedora Server Download Page` | `getfedora.org/.../checksums` (signed) | Pin per-file hash. Verify signature out-of-band. |
| `Fedora Workstation Download Page` | same family | Pin per-file hash. |
| `Rocky Linux Download Page` | `download.rockylinux.org/pub/rocky/X.Y/isos/x86_64/CHECKSUM` | Pin per-file hash. |
| `AlmaLinux Download Page` | `repo.almalinux.org/almalinux/X.Y/isos/x86_64/CHECKSUM` | Pin per-file hash. |
| `openSUSE Download Page` | per-release `.sha256` per ISO | Mixed; some are per-file (would be PROMOTE-A), some combined. Verify per artifact. |
| `FreeBSD Download Page` | `download.freebsd.org/releases/ISO-IMAGES/X.Y/CHECKSUM.SHA256-FreeBSD-…` | Pin per-file hash. Vendor publishes signed checksum file. |
| `NetBSD Download Page` | `cdn.netbsd.org/pub/NetBSD/NetBSD-X.Y/images/CKSUMS` | Pin per-file hash. Mixed-algorithm file — pick SHA-256 lines only. |
| `OpenBSD Download Page` | `cdn.openbsd.org/pub/OpenBSD/X.Y/SHA256` | Pin per-file hash. |
| `Haiku Download Page` | release-page links to `.iso.sha256` (per-file) | Actually **PROMOTE-A**. Per-file hash available. |
| `Slackware Download Page` | `CHECKSUMS.md5` and per-file `.sha256` | If per-file `.sha256` confirmed → PROMOTE-A; else KEEP MANUAL. |
| `Gentoo Linux Download Page` | per-file `.DIGESTS` | PROMOTE-B per file. |
| `Proxmox VE Download Page` | per-ISO `.sha256sum.txt` | Per-file → PROMOTE-A. Verify URL stability. |

→ Net PROMOTE-B candidates: **~11**. Each is one operator-paste away from being promoted.

## C. PROMOTE-C — GitHub release with per-asset digest

These projects host releases on GitHub. The existing manifest uses `https://api.github.com/repos/OWNER/REPO/releases/assets/ASSET_ID` as `sha256Url` (Rufus / RustDesk / balenaEtcher / DriverStoreExplorer / Angry IP Scanner pattern). The asset ID is unique per upload. A one-time `gh api` lookup is required.

| Entry | Repo | Lookup command | Notes |
|---|---|---|---|
| `VeraCrypt Download Page` | `veracrypt/VeraCrypt` | `gh api repos/veracrypt/VeraCrypt/releases/latest --jq '.assets[] \| select(.name \| test("Setup.exe$")) \| {id, name}'` | Promote the official Windows Setup .exe. License: Apache 2.0 + TrueCrypt 3.0 — redistribution OK. |
| `System Informer (Process Hacker) Download Page` | `winsiderss/systeminformer` | same shape | Promote the release zip. MIT licensed. |
| `Notepad++ Download Page` | `notepad-plus-plus/notepad-plus-plus` | same shape | Promote portable zip. GPL — OK. |
| `KeePass Download Page` | (SourceForge, not GitHub) | n/a | Move to PROMOTE-A path; KeePass publishes per-file sigs/hashes. |
| `Smartmontools Download Page` | (SourceForge) | n/a | PROMOTE-A — per-file `.sha256` exists. |
| `ReactOS Download Page` | `reactos/reactos` | `gh api repos/reactos/reactos/releases/0.4.15-release --jq '.assets[]'` | Hobby OS — low risk. Stable tag. |
| `Wireshark Download Page` | n/a (vendor-hosted, signed BSD-format SIGNATURES file) | n/a | **Promoted 2026-05-21 (Batch 3):** `Wireshark 4.6.6 Win64 Installer` after live verification of `https://www.wireshark.org/download/SIGNATURES-4.6.6.txt` (signed with key `0xE6FEAEEA`). The file is BSD-format (`SHA256(filename)=hash`, no whitespace inside parens or around `=`); the existing filename-aware resolver matches it after a hardening test was added for the no-space BSD variant. |
| `PuTTY Download Page` | n/a | n/a | PROMOTE-A — chiark publishes `sha256sums` per release. Switch when version pinned. |
| `WinSCP Download Page` | n/a — vendor signs MSI itself, no separate hash | n/a | KEEP MANUAL — checksum unavailable. |
| `Win32 Disk Imager Download Page` | (SourceForge) | n/a | KEEP MANUAL — no recent release; project largely dormant since 2017. |

→ Net PROMOTE-C candidates: **~5** (VeraCrypt, System Informer, Notepad++, ReactOS, plus the SourceForge ones folded back to A/B).

## D. KEEP MANUAL — Licence / EULA / paid / personal-use

These cannot be auto-downloaded by ForgerEMS without violating vendor terms or our own redistribution policy.

| Entry | Reason |
|---|---|
| All **Windows ISOs** (Win 11, 10, 8.1, 8, 7, Vista, XP, 2000, ME, 98, 95) | Microsoft EULA. Some no longer hosted by Microsoft at all. **No mirrors permitted.** |
| `Windows Server Evaluation Center` | Microsoft eval EULA; time-limited bits. Evaluation download requires accepting terms. |
| `Windows ADK and WinPE Info` | Microsoft Software License Terms; build-your-own only. |
| `Sergei Strelec WinPE Info` | Bundles Microsoft components; provenance review required. |
| `HWiNFO Download Page` | Business / commercial licensing. |
| `OCCT Download Page` | Free Personal edition; paid commercial editions. Vendor terms prohibit redistribution. |
| `CPU-Z Download Page` | CPUID redistribution terms not safe for automation. |
| `HWMonitor Download Page` | Same. |
| `AIDA64 Extreme Download Page` | Paid; trial-only. |
| `Speccy Download Page` | Piriform — free Home edition, paid Professional. Vendor terms. |
| `AnyDesk Download Page` | Personal-use licensing; commercial use requires paid licence. |
| `HDDScan Download Page` | Mixed redistribution status. |
| `DiskGenius Download Page` | Free / Professional / Standard tiers; paid features. |
| `Parted Magic Download Page` | Paid commercial product. |
| `MemTest86 (PassMark UEFI) Download Page` | Free vs. Site editions; vendor doesn't publish a machine-readable single-file checksum at a stable URL. |
| `Macrium Reflect Home Info` | Free edition retired 2024; current product is paid. |
| `MiniTool Partition Wizard Free Download Page` | Vendor terms; free for personal use. |
| `EaseUS Partition Master Free Download Page` | Vendor terms. |
| `Recuva Download Page` | Piriform terms. |
| `Samsung Magician Download Page` | Vendor portal. |
| `WD Dashboard Download Page` | Vendor portal. |
| `Kaspersky Virus Removal Tool Info` | Region-sensitive. |
| `MediCat Download Page` | Bundle includes mixed-licence components; community provenance. |
| `Hiren's BootCD PE Download Page` | Bundles Microsoft components; provenance review required. |
| `Ultimate Boot CD Download Page` | Bundles mixed-licence tools; not safe to auto-distribute. |
| `Tails Download Page` | Vendor explicitly requires human-in-the-loop verification flow (USB + signature). |
| `Qubes OS Download Page` | Multi-step Hardware Compatibility List confirmation required. |
| `pfSense Community Edition Download Page` | Vendor form-gated mirror selection. |

## E. KEEP MANUAL — Account / form / rotating-CDN

| Entry | Reason |
|---|---|
| `Chrome Enterprise Browser Download Page` | Selector chooses platform/channel/terms. Not direct. |
| `Firefox All Languages Download Page` | Per-language selector — no single canonical artifact. |
| `Microsoft Visual C++ Redistributable Download Page` | Architecture selector; Microsoft Learn entry is the canonical source. |
| `.NET 8 Desktop Runtime Download Page` | Servicing builds rotate. |
| `Nmap Download Page` | Page bundles installer + Npcap; user must accept Npcap terms. |
| `7-Zip Download Page` | Architecture selector; existing manual entry is the right answer. |
| `Endless OS Download Page` | Mirror selection page. |
| `Manjaro Download Page` | Edition selector. |
| `EndeavourOS Download Page` | "latest-release" URL — rotates. |
| `KDE neon Download Page` | Edition selector. |
| `Pop!_OS Download Page` | NVIDIA/Intel split + dynamic CDN. |
| `Zorin OS Download Page` | Lite/Core/Pro split. |
| `elementary OS Download Page` | Pay-what-you-want flow. |
| `MX Linux Download Page` | Edition + DE selector. |
| `Arch Linux Download Page` | Rolling monthly ISO; recommend manual checksum/signature flow. |
| `Debian Live Images Download Page` | Multi-flavour live page; netinst is the file-promotable variant (PROMOTE-B). |
| `OpenIndiana Download Page` | Mirror page; rotating ISO names. |
| `Linux Mint Download Page` (info shortcut) | Cover for the existing managed Mint ISO. |
| `Ubuntu Download Page` (info shortcut) | Cover for the existing managed Ubuntu ISO. |
| `Kali Linux Download Page` (info shortcut) | Cover for the existing managed Kali ISO. |
| `SystemRescue Download Page` (info shortcut) | Cover for the existing managed SystemRescue ISO. |
| `Clonezilla Download Page` (info shortcut) | Cover for the existing managed Clonezilla ISO. |
| `Rescuezilla Download Page` (info shortcut) | Cover for the existing managed Rescuezilla ISO. |
| `GParted Download Page` (info shortcut) | Cover for the existing managed GParted ISO. |
| `MemTest86+ Download Page` (info shortcut) | Cover for the existing managed MemTest86+ archive. |
| `Ventoy Download Page` (info shortcut) | Cover for the existing managed Ventoy package. |
| `Rufus Download Page` (info shortcut) | Cover for the existing managed Rufus binary. |
| `balenaEtcher Download Page` (info shortcut) | Cover for the existing managed balenaEtcher installer. |
| `Angry IP Scanner Download Page` (info shortcut) | Cover for the existing managed installer. |
| `BlueScreenView Download Page` (info shortcut) | Cover for the existing managed NirSoft zip. |
| `DriverStoreExplorer Download Page` (info shortcut) | Cover for the existing managed GitHub asset. |
| `RustDesk Download Page` (info shortcut) | Cover for the existing managed binary. |
| `CrystalDiskInfo Download Page` (info shortcut) | Cover for the existing managed SourceForge artifact. |

## F. KEEP MANUAL — Vendor source portals (not payloads)

These are inherently pages, not payloads:

- All 16 driver shortcuts (Realtek, Intel, AMD, NVIDIA, Bluetooth, RST, Wi-Fi, Ethernet, Audio, …)
- All Sysinternals shortcuts (Autoruns, Process Explorer, Sysinternals Suite)
- `Everything Search Download Page` — vendor portal with multiple installers per arch.
- `Advanced IP Scanner Download Page` — vendor portal.
- `GPU-Z Download Page` — vendor portal (TechPowerUp).
- `DDU Download Page`, `NVCleanInstall Download Page` — vendor portals.
- `Malwarebytes AdwCleaner Download Page` — vendor portal.
- `Emsisoft Emergency Kit Download Page` — vendor portal.

## G. KEEP MANUAL — Lifecycle / info shortcuts

- All Windows `*-Lifecycle Info` entries (8.1 → 95). These are Microsoft Learn / Wikipedia URLs by design.
- `TestDisk and PhotoRec Download Page` — CGSecurity hosts versioned `.tar.bz2` / `.zip`, but no per-file sha256 file at a stable URL. **Could become PROMOTE-A** after follow-up checks per release.
- `ClamWin / ClamAV Download Page` — Cisco-managed; their dispatcher chooses platform, not a direct artifact.
- `Kaspersky Virus Removal Tool Info` — region-sensitive availability.
- `Super Grub2 Disk Download Page`, `Rescatux Download Page` — versioned ISOs, but the supergrubdisk.org artifacts are MD5-only as of recent releases. **KEEP MANUAL until SHA-256 publishing returns.**
- `Parrot OS Download Page` — vendor selector; per-flavour links exist but vendor surfaces them through a form/mirror selector.

## Promotion Math

| Bucket | Count |
|---|---:|
| Current managed `file` items | 17 |
| **PROMOTE-A** (per-file sha256 only — operator just pastes the URL) | Alpine completed — and potentially Haiku, Proxmox VE, PuTTY, KeePass, Smartmontools, TestDisk/PhotoRec, Notepad++ if per-file hash flow is confirmed |
| **PROMOTE-B** (combined SHA256SUMS — operator pastes hash line) | ~10 (Ubuntu Server, Debian netinst, Fedora Server/Workstation, Rocky, Alma, openSUSE Leap, FreeBSD, NetBSD, OpenBSD, Gentoo, Slackware) |
| **PROMOTE-C** (GitHub asset-digest lookup) | ~4 (VeraCrypt, System Informer, Notepad++, ReactOS) |
| **KEEP MANUAL** | All 129 retained until each PROMOTE-* is operator-verified |

If all remaining promotion candidates were realized, the managed catalog could grow from **17 → ~31** entries — roughly doubling the technician's "press a button and get a verified ISO" surface without sacrificing any of the operating rules.

## What This Pass Did (and why)

Further promotions of any of the above without me being able to:

1. Hit the URL to confirm the artifact exists at the version I'd encode,
2. Fetch the vendor's checksum file to confirm the format the resolver will parse,
3. Look up GitHub asset IDs for the digest-endpoint pattern,

would amount to either **fabricated metadata** (forbidden) or **fragile links** (forbidden). So this pass:

- Promoted Alpine only after the helper reached the official ISO URL and the filename-aware resolver matched the vendor `.sha256` content.
- Locked in the audit above so a follow-up promotion pass is mechanical.
- Surfaced the **rich metadata** added in the previous catalog-expansion pass (kind / family / osCategory / licenseNote / legacyWarning / sourceTrust / secureBootNote / etc.) through the Toolkit Health PowerShell report and the C# DTO, so the WPF Toolkit Manager can render the new badges immediately.
- Added a `Manual ISO Required` / `Legacy / Lab Only` / `Paid - vendor licence` / `Community source` / `Official source` normalised-label path so the catalog's intent is visible to the technician without UI redesign.

## Recommended Next Pass

When connectivity / vendor-verification is available:

1. Promote **VeraCrypt + System Informer + Notepad++ + ReactOS** (GitHub asset-digest pattern; one `gh api` call each).
2. Promote **Ubuntu Server + Debian netinst** (highest technician value; combined-SHA256SUMS requires manual pasting of the per-file hash).
3. Promote **FreeBSD + OpenBSD + NetBSD** (BSD lab coverage).
4. Promote **Rocky + AlmaLinux** (RHEL-alternative coverage).
5. Promote **Fedora Server / Workstation**.

Each promotion adds: `sourceType`, `fragilityLevel`, `fallbackRule`, `maintenanceRank` (next free: 18, 19, …, contiguous). The schema, tests, and verification pipeline are already prepared.

## Batch 3 — Trusted Technician Utilities (2026-05-21)

**Outcome:** 1 promotion (Wireshark). All other candidates in this batch stayed manual-only with documented integrity-evidence shortfalls.

| Candidate | Verdict | Evidence |
|---|---|---|
| **Wireshark** | **PROMOTED** (rank 22) | `SHA256(filename)=hash` BSD-format line in vendor `SIGNATURES-4.6.6.txt`, signed with key `0xE6FEAEEA`. Stable versioned URL at `https://2.na.dl.wireshark.org/win64/Wireshark-4.6.6-x64.exe`. Resolver re-fetches and cross-checks at runtime. The installer bundles Npcap; technician accepts the Npcap EULA at install time on the target machine (download-time legal posture unchanged from prior Notepad++ / VeraCrypt batch). |
| 7-Zip | KEEP MANUAL | Vendor publishes no machine-readable SHA-256 file on `7-zip.org/download.html`. Architecture selector page; no `.sha256` / `SHA256SUMS` / signed digest. |
| WinSCP | KEEP MANUAL | Vendor publishes per-release SHA-256 only inside the prose `WinSCP-X.Y.Z-ReadMe.txt` (`SHA-256: <hash>` lines under per-file headings). This is not a GNU / BSD / digest format the hardened resolver can safely consume; teaching the resolver a fifth prose format risks collateral mis-parses for other entries. Operator-paste path stays available but is not auto-resolved. |
| Nmap | KEEP MANUAL | Vendor `nmap-X.Y-setup.exe.digest.txt` uses a non-standard layout (`filename: ALGO = HH HH HH HH ...` with multi-line continuations and 4-byte grouping); the resolver cannot extract a SHA-256 from this without significant new parser surface. Plus the installer bundles Npcap under a separate EULA the vendor flags at download time. |
| Advanced IP Scanner | KEEP MANUAL | Vendor portal (`advanced-ip-scanner.com`) selects build/region at click time; no stable versioned URL or machine-readable checksum file. |
| Everything Search | KEEP MANUAL | Vendor portal (`voidtools.com`) selects per-architecture installer; no machine-readable checksum file at a stable URL. |
| GPU-Z | KEEP MANUAL | TechPowerUp vendor portal with mirror/CDN selection; no machine-readable checksum file. |
| DDU (Display Driver Uninstaller) | KEEP MANUAL | Guru3D vendor portal with rotating mirror selection; no machine-readable checksum file. |
| NVCleanInstall | KEEP MANUAL | TechPowerUp vendor portal with mirror selection; no machine-readable checksum file. |

Tests added in `tests/ForgerEMS.Wpf.Tests/ManagedDownloadManifestTests.cs`:
- `ManagedDownloadManifest_Batch3PromotedEntriesHaveValidChecksumAndMetadata` — full resilience contract for Wireshark.
- `ManagedDownloadManifest_Batch3PromotedSourceUrlsArePinned` — version-pinned URL and `SIGNATURES-4.6.6.txt` companion.
- `ManagedDownloadManifest_Batch3UnsafeCandidatesStayManualOnly` — locks all 8 KEEP-MANUAL entries in this batch with a documented "why this stayed manual" reason field that is itself asserted non-empty.

Test added in `tests/ForgerEMS.Wpf.Tests/ChecksumResolverTests.cs`:
- `Resolver_BsdNoSpaceFormat_PicksHashByTargetFilename` — hardens the BSD-format regex against the Wireshark `SHA256(filename)=hash` shape (no whitespace inside parens or around `=`), and confirms the SHA1 sibling line is skipped.

## Batch 4 — User-Safe ISO Promotion Pass (2026-05-21)

**Managed updates applied:** Rufus 4.14, Ventoy 1.1.12, balenaEtcher 2.1.6, Rescuezilla 2.6.2, MemTest86+ 8.10, Alpine 3.23.4.

**New managed ISO promotions:** Proxmox VE 9.2-1, Ubuntu Server 24.04.4 LTS, Debian 13.5.0 netinst, Fedora Server 44-1.7 DVD, FreeBSD 15.0-RELEASE disc1, OpenBSD 7.9 install ISO, Rocky Linux 10.1 Minimal, AlmaLinux 10.1 Minimal.

**Kept manual with stronger guidance:** Haiku (beta-labelled release media), Smartmontools and KeePass (SourceForge/vendor-hash verification not completed), NetBSD (official SHA512-only flow for current amd64 cdrom media), openSUSE Leap (tested checksum URL retired; needs a clean artifact decision), Gentoo (rolling/date-stamped media), Slackware (mirror-selection workflow).

Validation snapshot:
- `dotnet test .\ForgerEMS.sln --no-build`: 1135 passed.
- `Verify-VentoyCore.ps1`: 9 passed, 0 warnings.
- `Verify-VentoyCore.ps1 -RevalidateManagedDownloads`: 30 active managed items, 22 OK, 8 OK-LIMITED pinned-only, 0 DRIFT.
