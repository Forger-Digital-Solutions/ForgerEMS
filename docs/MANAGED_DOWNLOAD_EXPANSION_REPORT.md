# ForgerEMS Managed Download Catalog Expansion Report

Generated on 2026-05-27 for ForgerEMS 1.2.3-preview.1 (Ventoy core 2026.04.20.2).

This report covers the **Batch 6** managed-download promotion wave. The prior
pass (Batch 5 + downloadMode/actionLabel work) is captured in
`docs/CATALOG_PROMOTION_REPORT.md`; this report only documents the entries
that were added on top of that baseline.

## 1. Summary

- Managed downloads **before** this pass: **32**
- Managed downloads **after** this pass:  **50**
- Promoted in this pass:                  **18** (15 OS / ISO, 3 technician tool)
- Target reached: target was 50&ndash;60 (stretch 75+). Reached **50** after the
  focused second-look audit promoted three additional safe OS/server ISOs.
- Policy issues from `tools/Test-ForgerEMSCatalogPromotion.ps1`: **0**
- Shape-verifier `tools/Test-ForgerEMSManagedDownloadCandidates.ps1 -Offline`:
  **18 / 18 Promote (offline shape ok)**, 0 Blocked, 0 NeedsHumanReview.
- Live verifier `tools/Test-ForgerEMSManagedDownloadCandidates.ps1`:
  **18 / 18 Promote**, 0 KeepPage, 0 Blocked, 0 NeedsHumanReview.

## 2. Newly promoted entries

Each entry below was verified by:

1. Fetching the published checksum file from an official upstream URL.
2. Confirming the file contains a clean machine-readable line that binds the
   pinned digest to the exact artifact filename used in `url`/`dest`.
3. Confirming the artifact URL responds with HTTP 200 (or 302 to an
   approved mirror) and is not a login wall, EULA gate, or HTML page.
4. Confirming the artifact host or final-host matches an approved upstream
   regex (`fedoraproject.org`, `archlinux.org`, `ubuntu.com`, `debian.org`,
   `freedos.org`, `truenas.com`, `parrot.sh`, `github.com` /
   `objects.githubusercontent.com`, `cgsecurity.org`).

### 2.1 OS / ISO promotions (15)

| name | url host | checksum source | checksum verification mode | reason it is safe to manage |
|---|---|---|---|---|
| Fedora Workstation 44-1.7 Live (x86_64) | download.fedoraproject.org &rarr; fcix.net mirror | Fedora-Workstation-44-1.7-x86_64-CHECKSUM | sha256-pinned | Signed CHECKSUM published per-release; resolver redirects through the official Fedora mirror network. |
| Arch Linux 2026.05.01 (x86_64) | archive.archlinux.org | sha256sums.txt (per dated release) | sha256-pinned | Arch publishes dated ISO directories with a clean sha256sums.txt; URL pins the date (never the rolling alias). The final hardening pass moved from the public `archlinux.org/iso/` path to the official archive path after the former returned 403. |
| Xubuntu 24.04.4 LTS Desktop (amd64) | cdimage.ubuntu.com | per-release SHA256SUMS | sha256-pinned | Ubuntu LTS point release with per-release SHA256SUMS in the release directory. |
| Lubuntu 24.04.4 LTS Desktop (amd64) | cdimage.ubuntu.com | per-release SHA256SUMS | sha256-pinned | Ubuntu LTS point release with per-release SHA256SUMS in the release directory. |
| Kubuntu 24.04.4 LTS Desktop (amd64) | cdimage.ubuntu.com | per-release SHA256SUMS | sha256-pinned | Ubuntu LTS point release with per-release SHA256SUMS in the release directory. |
| Debian Live 13.5.0 GNOME (amd64) | cdimage.debian.org | per-release SHA512SUMS | sha512-pinned | Debian publishes SHA512SUMS in the dated `13.5.0-live` iso-hybrid directory (no SHA256SUMS); same NetBSD-style pattern already used in Batch 5. |
| Debian Live 13.5.0 KDE (amd64) | cdimage.debian.org | per-release SHA512SUMS | sha512-pinned | Same as GNOME variant. |
| Debian Live 13.5.0 Xfce (amd64) | cdimage.debian.org | per-release SHA512SUMS | sha512-pinned | Same as GNOME variant. |
| FreeDOS 1.4 LiveCD | download.freedos.org | per-release verify.txt | sha256-pinned (prose) | FreeDOS publishes per-file MD5/SHA-256/SHA-512 in a prose verify.txt; resolver kind is `sha256-prose`. |
| FreeDOS 1.4 FullUSB | download.freedos.org | per-release verify.txt | sha256-pinned (prose) | Same as LiveCD. |
| TrueNAS SCALE 24.10.2 (amd64) | download.truenas.com | per-file .iso.sha256 companion | sha256-pinned | iX publishes a per-file .sha256 in the same versioned directory; Community Edition is free. |
| Proxmox Backup Server 4.2-1 ISO Installer | enterprise.proxmox.com | per-file .iso.sha256 companion | sha256-pinned | Official Proxmox ISO index and per-file checksum recovered cleanly on the second-look audit. |
| Rocky Linux 10.1 DVD (x86_64) | download.rockylinux.org | official CHECKSUM file | sha256-pinned | Official Rocky Linux 10 x86_64 CHECKSUM binds the DVD ISO filename exactly. |
| AlmaLinux 10.2 DVD (x86_64) | repo.almalinux.org | signed official CHECKSUM file | sha256-pinned | Official AlmaLinux 10 x86_64 signed CHECKSUM binds the DVD ISO filename exactly. |
| Parrot Security 7.2 (amd64) | deb.parrot.sh | per-release signed-hashes.txt | sha256-pinned | Parrot publishes a GPG-signed per-release signed-hashes.txt; authorized testing only. |

### 2.1.1 Refreshed existing managed entry

| name | change | reason |
|---|---|---|
| AlmaLinux 10.2 Minimal (x86_64) | Refreshed from 10.1 to 10.2 without changing managed count. | The previous 10.1 direct URL returned 404 during the 2026-05-27 second-look audit; the current official signed CHECKSUM binds the 10.2 minimal ISO exactly. |

### 2.2 Technician tool promotions (3)

| name | url host | checksum source | checksum verification mode | reason it is safe to manage |
|---|---|---|---|---|
| KeePassXC 2.7.12 Win64 Portable (zip) | github.com | api.github.com per-asset digest | github-asset-digest | Upstream tags signed releases on GitHub; the per-asset `digest` field is published by GitHub and binds to the exact asset. |
| TestDisk 7.2 Win64 Portable (zip) | cgsecurity.org | project-wide testdisk_sha256.txt | sha256-pinned | CGSecurity publishes a project-wide sha256 list updated for each release. |
| Microsoft PowerToys 0.99.1 (x64 user setup) | github.com | api.github.com per-asset digest | github-asset-digest | Official Microsoft GitHub release; per-asset digest endpoint binds the checksum. |

## 3. Maintenance ranks

The promotion wave assigns contiguous maintenance ranks 33..50 to the 18 new
entries, preserving the
`ManagedDownloadManifest_FileItemsHaveContiguousMaintenanceRanks`
invariant (file-item ranks form the sequence 1..N where N is the number of
file entries; N moved from 32 to 50). The existing AlmaLinux minimal entry kept
rank 30 while being refreshed from 10.1 to 10.2.

## 4. Second-look candidate audit

The user-requested C2 list was rechecked from the actual `repo/` worktree on
2026-05-27. The fields below record the promotion gate explicitly:

- **Artifact** means a direct, version-pinned artifact URL was found.
- **Proof** means SHA-256, SHA-512, GitHub asset digest, or an already supported
  checksum/signature mode was found.
- **Binding** means the proof binds to the exact filename in the URL/dest.
- **Gate** means a legal, EULA, account, firmware, OEM, dynamic mirror, or
  manual-review issue still blocks automation.

### 4.1 OS / ISO candidates

| candidate | current manifest status | official source checked | artifact | proof | binding | gate | decision | reason |
|---|---|---|---|---|---|---|---|---|
| Proxmox Backup Server ISO | Download page only before this pass | `enterprise.proxmox.com/iso/` | yes | yes, `.iso.sha256` | yes | no | Promote | Direct ISO and per-file checksum both returned 200 OK and bind `proxmox-backup-server_4.2-1.iso`. |
| openSUSE Leap offline ISO / net installer | Leap 16.0 offline ISO already managed; page shortcut retained | `download.opensuse.org` | yes | yes | yes | no | AlreadyManaged | Stable Leap offline ISO coverage already exists; net/alternate media stay behind the page shortcut. |
| openSUSE Tumbleweed | Official download page | `download.opensuse.org/tumbleweed/iso/` | yes | yes | unstable | rolling snapshot | KeepOfficialPage | Snapshot filenames roll daily; managed policy forbids `snapshot` URLs that cannot be safely re-bound. |
| Fedora Server DVD / netinst | Fedora Server 44-1.7 DVD already managed | `download.fedoraproject.org` | yes | yes | yes | no | AlreadyManaged | DVD coverage already exists; the new Fedora Workstation Live entry covers the requested workstation media. |
| Rocky Linux minimal / DVD | Minimal already managed; DVD promoted in this pass | `download.rockylinux.org` | yes | yes, CHECKSUM | yes | no | Promote | DVD ISO was added; CHECKSUM binds `Rocky-10.1-x86_64-dvd1.iso`. |
| AlmaLinux minimal / DVD | Minimal already managed; DVD promoted in this pass | `repo.almalinux.org` | yes | yes, signed CHECKSUM | yes | no | Promote | Existing minimal was refreshed to 10.2 after the old 10.1 URL returned 404; DVD ISO was added. |
| KDE neon User Edition ISO | Official download page | `neon.kde.org` / KDE download links | partial | no | no | dynamic current media | KeepOfficialPage | No stable machine-readable hash binding was wired for the current user ISO. |
| Pop!_OS current stable ISO | Official download page | `pop.system76.com` | partial | no | no | dynamic current media | KeepOfficialPage | Current download page does not provide a clean, version-pinned checksum source for the manifest validator. |
| Zorin OS Core ISO | Official download page | `zorin.com/os/download/` | no | no | no | download gate / edition selector | KeepOfficialPage | Core media remains technician-selected because the page flow is not a stable artifact+hash contract. |
| MX Linux ISO | Official mirror/download page | `mxlinux.org` | partial | no | no | mirror selection | KeepDynamicMirrorOnly | Official media is mirror-driven and was not proven with a stable machine-readable hash binding. |
| EndeavourOS ISO | Official download page | `endeavouros.com` | partial | no | no | mirror selection | KeepDynamicMirrorOnly | The live ISO flow is mirror-oriented; no manifest-safe artifact+hash pair was proven. |
| Bazzite ISO | Not currently cataloged | `bazzite.gg` / Universal Blue links | partial | no | no | installer/image matrix | NeedsHumanReview | Image selection is variant-heavy and no stable checksum binding was proven for a generic technician target. |
| Nobara ISO | Not currently cataloged | `nobaraproject.org` | partial | no | no | dynamic release page | NeedsHumanReview | Official media remains page-driven; no stable exact artifact+checksum contract was proven. |
| Manjaro ISO | Official download page | `manjaro.org` / mirrors | partial | no | no | dynamic mirrors | KeepDynamicMirrorOnly | Edition mirror flow remains manual until a stable per-filename hash source is wired. |
| Garuda Linux ISO | Not currently cataloged | `garudalinux.org` | partial | no | no | dynamic mirrors | KeepDynamicMirrorOnly | Download flow is mirror/edition based; no stable checksum binding was proven. |
| CachyOS ISO | Not currently cataloged | `cachyos.org` / upstream release links | partial | no | no | rolling release media | NeedsHumanReview | Candidate needs a dedicated resolver audit before automation because current media rotates quickly. |
| OpenWrt generic x86 image | Not currently cataloged | `downloads.openwrt.org` | yes | yes | yes | firmware/target ambiguity | NeedsHumanReview | Generic x86 might be safe, but image target/flavor policy must be explicit before auto-download. |
| Proxmox VE / Backup related ISO variants | Proxmox VE already managed; Backup promoted | `enterprise.proxmox.com/iso/` | yes | yes | yes | no | AlreadyManaged | No additional distinct Proxmox ISO variant was promoted beyond VE and Backup Server. |

### 4.2 Technician-tool candidates

| candidate | current manifest status | official source checked | artifact | proof | binding | gate | decision | reason |
|---|---|---|---|---|---|---|---|---|
| Sysinternals Suite | ReviewFirst shortcut | `download.sysinternals.com` | yes | no | no | no machine hash | KeepReviewFirst | Microsoft still does not publish machine-readable SHA-256 for the ZIP. |
| Autoruns | ReviewFirst shortcut | `download.sysinternals.com` | yes | no | no | no machine hash | KeepReviewFirst | Same Sysinternals hash gap as the suite. |
| Process Explorer | ReviewFirst shortcut | `download.sysinternals.com` | yes | no | no | no machine hash | KeepReviewFirst | Same Sysinternals hash gap as the suite. |
| Process Monitor | ReviewFirst shortcut | `download.sysinternals.com` | yes | no | no | no machine hash | KeepReviewFirst | Same Sysinternals hash gap as the suite. |
| WinSCP | Official download page | `winscp.net` | yes | prose only | not validator-bound | manual hash/signature review | NeedsHumanReview | Hash evidence is embedded in prose release text, not a clean machine-readable binding. |
| Nmap | Official download page | `nmap.org` | yes | non-standard digest layout | not validator-bound | Npcap/EULA considerations | NeedsHumanReview | Digest format and bundled Npcap terms require technician review. |
| 7-Zip | Official download page | `7-zip.org` | yes | no SHA-256 source | no | no machine hash | NeedsHumanReview | Official page does not expose a validator-safe SHA-256/SHA-512 source. |
| Everything Search | Official download page | `voidtools.com` | yes | no | no | architecture/page selector | NeedsHumanReview | Download page selects architecture and lacks a stable machine-readable checksum. |
| SumatraPDF | Official download page | `sumatrapdfreader.org` / GitHub | partial | changelog prose only | no | no GitHub assets | KeepOfficialPage | GitHub latest had no assets; website hash evidence is not machine-readable. |
| KeePass classic | Official download page | `keepass.info` | yes | SHA-1/GPG only | no SHA-256/SHA-512 | legacy proof mode | KeepOfficialPage | KeePassXC was promoted instead because it has GitHub asset digest coverage. |
| smartmontools | Official download page | `smartmontools.org` / SourceForge | partial | no stable source wired | no | SourceForge fallback | KeepOfficialPage | Needs a stronger SourceForge/hash resolver before automation. |
| Microsoft .NET Desktop Runtime x64 | Official download page | `dotnet.microsoft.com` / CDN | yes | not wired | no | rotating CDN/channel | NeedsHumanReview | Keep page-only until the official checksums flow is bound to exact installer URLs. |
| Microsoft VC++ Redistributable x64 | Official download page | Microsoft Download Center | yes | no stable source wired | no | Microsoft redistributable terms | NeedsHumanReview | Installer URL/checksum contract is not clean enough for managed automation. |
| VS Code stable/user installer | Not currently cataloged | `code.visualstudio.com` | yes | no stable source wired | no | license/channel review | NeedsHumanReview | Official installer channel needs package verification and license review before cataloging. |
| Firefox offline installer | Official download page | `mozilla.org` | yes | not wired | no | locale/channel selector | NeedsHumanReview | Offline installer links rotate by locale/platform and no stable hash source was wired. |
| Chrome Enterprise offline installer | LicenseRestricted shortcut | `chromeenterprise.google` | yes | no stable source wired | no | Google terms/EULA | KeepLicenseRestricted | Enterprise installer requires terms acceptance and remains manual. |
| Microsoft Winget / App Installer | Not currently cataloged | Microsoft Store / winget channels | partial | package signature only | not manifest-bound | Store/package identity | KeepVendorPortal | Distribution should stay through the vendor portal until package verification is designed. |
| CrystalDiskInfo | Managed pinned 9.8.0 plus manual update page | Crystal Dew World / SourceForge | yes | pinned hash only | yes for pinned 9.8.0 | current update lacks vendor hash | AlreadyManaged | Existing managed entry stays pinned; newer page remains manual until vendor hash proof improves. |
| CrystalDiskMark | Official download page | Crystal Dew World / SourceForge | partial | no strong machine proof | no | vendor page/mirror | NeedsHumanReview | No strong vendor checksum proof was found for a new managed artifact. |
| CPU-Z | Official download page | `cpuid.com` | yes | no stable source wired | no | vendor terms/page flow | NeedsHumanReview | No exact artifact+hash contract was proven. |
| GPU-Z | Official download page | `techpowerup.com` | yes | no stable source wired | no | vendor page flow | NeedsHumanReview | No exact artifact+hash contract was proven. |
| HWiNFO | LicenseRestricted shortcut | `hwinfo.com` | yes | not applicable | no | licensing/commercial use | KeepLicenseRestricted | Licensing requires technician review. |
| DDU | ReviewFirst shortcut | Wagnardsoft / Guru3D | partial | no stable source wired | no | community mirror / ads | KeepReviewFirst | Manual review avoids wrong mirrors and bundled-ad pages. |

## 5. Safety / legal confirmation

This promotion wave preserves every hard rule from the user's brief:

- [x] **No Windows / macOS / iOS restricted media** auto-downloaded. Windows
      11/10/Server/ADK and all macOS / iOS / iPadOS / iPod entries remain
      `ManualMediaRequired` or `OfficialDownloadPage`.
- [x] **No firmware / BIOS / UEFI / OEM model-specific drivers** auto-downloaded.
      All `FirmwareBlocked` and `OEMSpecific` entries are unchanged.
- [x] **No paid / commercial / EULA-gated tools** auto-downloaded. All
      `LicenseRestricted` entries are unchanged (Parted Magic, HWiNFO, OCCT,
      AIDA64, Macrium Reflect, AnyDesk, MemTest86 PassMark, Chrome
      Enterprise).
- [x] **No community WinPE bundles** auto-downloaded. Hiren's, Strelec,
      MediCat, Ultimate Boot CD, NirLauncher, DDU all remain
      `CommunityToolkit` / `ReviewFirst`.
- [x] **No checksumless ManagedDownload entries.** `managedChecksumPolicy =
      require-for-release` is unchanged, and every new entry carries either a
      pinned SHA-256 + SHA-256 source URL, a pinned SHA-512 + SHA-512 source
      URL (Debian Live), or a GitHub asset-digest endpoint (KeePassXC /
      PowerToys).
- [x] **No pirated, abandonware, cracked, or random-mirror entries.**

The shape verifier additionally enforces these rules at run time via the
`OfficialHostPattern` regex per candidate. See
`tools/Test-ForgerEMSManagedDownloadCandidates.ps1`.

## 6. Validation results

| command | result |
|---|---|
| `dotnet restore .\ForgerEMS.sln` | exit 0; all projects restored or up-to-date |
| `dotnet build .\ForgerEMS.sln -c Release --no-restore` | exit 0; 52 warnings, 0 errors |
| `dotnet test .\ForgerEMS.sln -c Release --no-build` | 1565 passed, 0 failed, 0 skipped |
| `.\tools\Test-ForgerEMSCatalogPromotion.ps1` | exit 0; 217 total items; 50 ManagedDownload; 0 policy issues |
| `.\tools\Test-ForgerEMSManagedDownloadCandidates.ps1 -Offline` | exit 0; 18/18 Promote (offline shape ok), 0 Blocked, 0 NeedsHumanReview |
| `.\tools\Test-ForgerEMSManagedDownloadCandidates.ps1` | exit 0; 18/18 Promote, 0 KeepPage, 0 Blocked, 0 NeedsHumanReview |
| `.\backend\Verify-VentoyCore.ps1` | exit 0; 10 passed, 0 failed, 0 warnings; backend readiness READY |
| `.\tools\Validate-ForgerEMSRelease.ps1 -Version 1.2.3-preview.1` | exit 0; 32 pass rows, 0 warn, 0 fail; verdict READY |
| `.\tools\check-secrets.ps1` | exit 0; only existing fake/test placeholder examples |
| `git diff --check` | exit 0; no whitespace errors; line-ending conversion warnings only |
| `.\tools\build-release.ps1 -Version 1.2.3-preview.1` | exit 0; `release/current` rebuilt; Inno Setup installer compiled |
| packaged manifest subset tests | 152 passed, 0 failed, 0 skipped |

## 7. Live (online) verification

Live URL + checksum-binding probing was performed with
`tools/Test-ForgerEMSManagedDownloadCandidates.ps1` against each upstream.
Full payload downloads were not performed for large ISO artifacts (Fedora
Workstation: ~2 GB; Debian Live: 3.5&ndash;3.9 GB; TrueNAS SCALE: ~1 GB) per
the user's brief ("for huge ISOs, do not redownload everything"); metadata /
checksum proof was confirmed and recorded above.

For the smaller artifacts (FreeDOS 1.4 LiveCD/FullUSB, TestDisk Win64 zip,
KeePassXC Win64 zip, PowerToys user setup) HEAD/range probes returned 200
OK with the expected Content-Type and Content-Length; e.g. the TestDisk
probe returned `Content-Type: application/zip` and `Content-Length:
27354549`.

## 8. Second-look audit notes

The second-look audit was run from the actual `repo/` worktree. It changed the
target outcome from 47 to 50 only where the same official-source and checksum
discipline held:

- Proxmox Backup Server: official ISO index and per-file `.sha256` are reachable
  at `enterprise.proxmox.com/iso/`.
- Rocky Linux DVD: official CHECKSUM includes a BSD-format SHA-256 line for the
  exact DVD filename.
- AlmaLinux DVD: official signed CHECKSUM includes a BSD-format SHA-256 line for
  the exact DVD filename.
- AlmaLinux Minimal: existing managed entry refreshed from 10.1 to 10.2 because
  the prior version-pinned URL now returns 404.

Everything else from the requested C2 list is recorded in section 4 and stayed
page-only/manual/review-first/license-restricted/vendor-portal unless already
managed. No checksumless, EULA-gated, firmware, OEM-driver, or restricted media
entry was promoted.

## 9. Re-derivation guide for next pass

Stretch candidates to chase next (each needs ~30 minutes of upstream probing
plus manifest + test work):

- **Tails** &mdash; per-release signed `.iso.sig` + `.sha256` exist; resolver
  needs a small `gpg-detached-sig + sha256` mode if we want signature
  enforcement, otherwise sha256-pinned works fine.
- **Endless OS** &mdash; per-release SHA256SUMS at
  `download.endlessos.com/eos/<ver>/<flavor>/SHA256SUMS`. ECONNREFUSED at
  probe time this round; retry after upstream stabilizes.
- **Microsoft .NET Desktop Runtime** &mdash; check the official
  `dotnetcli.azureedge.net/dotnet/Runtime/<ver>/checksums.json` flow for a
  resolver-binding path that does not require Microsoft account login.
- **Sysinternals individual tools** &mdash; if Microsoft publishes per-tool
  hashes in a machine-readable file in 2026/2027, the entire Sysinternals
  bundle could be promoted at once.

Each next-pass candidate must clear the same proof gate that the 18
promotions in this report cleared: official artifact host, machine-readable
checksum file at a stable URL, and a clean line binding the digest to the
exact filename.
