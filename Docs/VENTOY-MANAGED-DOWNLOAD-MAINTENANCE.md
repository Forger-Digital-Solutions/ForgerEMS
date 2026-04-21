# Ventoy Managed Download Maintenance

This guide covers the current `auto-download safe` bucket only.

Current safe count: `16`

`safe` still depends on upstream availability. An entry remains safe only while
the official direct artifact, checksum coverage, and non-gated flow all stay
intact.

## Health Snapshot

- Total safe items: `16`
- Fragility: `high 6`, `medium 7`, `low 3`
- Checksum posture: `pinned-only 7`, `pinned+remote 8`, `remote-only 1`
- Baseline status: `OK 9`, `OK-limited 7`, `DRIFT 0`
- Borderline today: `BlueScreenView`, `CrystalDiskInfo`, `Linux Mint`
- Inspect first next cycle: `BlueScreenView`, `CrystalDiskInfo`, `Ventoy`,
  `SystemRescue`, `GParted Live`

Status meanings:

- `OK` means the item has live URL coverage plus remote checksum coverage.
- `OK-limited` means the URL can be probed live, but checksum confirmation still
  depends on the pinned manifest hash.

## Revalidation Workflow

Run:

```powershell
.\Verify-VentoyCore.ps1 -RevalidateManagedDownloads
```

What it does:

- checks whether each enabled managed file URL still resolves
- checks whether each declared remote checksum source still resolves
- reports redirect drift and checksum limitations
- does not auto-change the manifest

Archive outputs are written to:

- `.verify\managed-download-revalidation\<timestamp>\`
- `.verify\managed-download-revalidation\latest\`

Expected files:

- `managed-download-summary.txt`
- `managed-download-revalidation.csv`
- `managed-download-revalidation.txt`

Release bundle history path:

- `docs/Release-Verification-History/<coreVersion>/`

That folder is the clean release-facing location for:

- what was verified for the release
- when it was verified
- what the managed-download status looked like at ship time

## Safe Bucket Table

| Rank | Name | Status | Source type | Fragility | Checksum posture | Borderline |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | `BlueScreenView 1.55 (x64 zip)` | `OK-limited` | `official-version-path` | `high` | `pinned-only` | `yes` |
| 2 | `CrystalDiskInfo 9.8.0 (standard zip)` | `OK-limited` | `sourceforge` | `high` | `pinned-only` | `yes` |
| 3 | `Ventoy 1.1.11 (Windows package)` | `OK` | `sourceforge` | `high` | `pinned+remote` | `no` |
| 4 | `SystemRescue 13.00 (amd64)` | `OK` | `sourceforge` | `high` | `remote-only` | `no` |
| 5 | `GParted Live 1.8.1-3 (amd64)` | `OK-limited` | `sourceforge` | `high` | `pinned-only` | `no` |
| 6 | `Clonezilla Live 3.3.1-35 (amd64)` | `OK-limited` | `sourceforge` | `high` | `pinned-only` | `no` |
| 7 | `Linux Mint 22.3 Cinnamon (64-bit)` | `OK-limited` | `official-mirror` | `medium` | `pinned-only` | `yes` |
| 8 | `Rufus 4.13 Portable (x64)` | `OK` | `github-release` | `medium` | `pinned+remote` | `no` |
| 9 | `DriverStoreExplorer 1.0.26 (zip)` | `OK` | `github-release` | `medium` | `pinned+remote` | `no` |
| 10 | `Angry IP Scanner 3.9.3 (Windows setup)` | `OK` | `github-release` | `medium` | `pinned+remote` | `no` |
| 11 | `RustDesk 1.4.6 (x86_64 exe)` | `OK` | `github-release` | `medium` | `pinned+remote` | `no` |
| 12 | `balenaEtcher 2.1.4 Setup (x64)` | `OK` | `github-release` | `medium` | `pinned+remote` | `no` |
| 13 | `Rescuezilla 2.6.1 (64-bit oracular)` | `OK` | `github-release` | `medium` | `pinned+remote` | `no` |
| 14 | `MemTest86+ 8.00 (x86_64 ISO archive)` | `OK` | `official-version-path` | `low` | `pinned+remote` | `no` |
| 15 | `Kali Linux 2026.1 Installer (amd64)` | `OK-limited` | `official-version-path` | `low` | `pinned-only` | `no` |
| 16 | `Ubuntu 24.04.4 LTS Desktop (amd64)` | `OK-limited` | `official-version-path` | `low` | `pinned-only` | `no` |

## Demotion Triggers

Demote a safe item back to `review-first` when any of these become true:

1. The URL no longer resolves cleanly from the official source.
2. The artifact can no longer be matched confidently to the intended versioned
   payload.
3. The checksum can no longer be confirmed or pinned safely from the official
   artifact or official checksum source.
4. The upstream flow becomes gated, clickthrough-based, account-bound,
   EULA-sensitive, or otherwise ambiguous.

Do not patch around those conditions with scraper URLs, third-party mirrors,
repacks, or unofficial checksum sites just to preserve automation.

## Operator Decision Guide

- High-fragility item drifts: stop automation for that item, confirm the
  official replacement path and checksum, then either patch it cleanly or
  demote it before release use.
- Pinned-only item changes upstream: recompute the hash from the live official
  artifact, update the pin only if the artifact match is still confident, and
  demote if confidence is lost.
- Checksum source disappears: keep automation only if the official artifact is
  still clear and a new pinned hash can be verified safely; otherwise demote.
- Formerly safe item must be demoted: convert it back to a `review-first` page
  entry, keep the official page shortcut, and remove automated-download claims.

## Borderline And Limited-Checksum Guidance

`BlueScreenView`, `CrystalDiskInfo`, and `Linux Mint` are flagged `borderline`
because they are the safe entries closest to demotion if upstream behavior
changes.

The broader `OK-limited` set is:

- `BlueScreenView`
- `CrystalDiskInfo`
- `GParted Live`
- `Clonezilla Live`
- `Linux Mint`
- `Kali`
- `Ubuntu`

Those remain safe today, but live revalidation confirms URL reachability more
strongly than checksum provenance because they depend on pinned manifest hashes.

## Pre-Release Gate

Before any `candidate` or `stable` build intended for distribution:

1. Run offline verification:
   `.\ventoy-core\Verify-VentoyCore.ps1`
2. Run managed download revalidation:
   `.\ventoy-core\Verify-VentoyCore.ps1 -RevalidateManagedDownloads`
3. Review `managed-download-summary.txt`.
4. Confirm there are no unresolved issues in the top fragility slice:
   maintenance ranks `1-7`.
5. Hold the release if any safe item in that slice has drift, provenance
   ambiguity, or checksum uncertainty.

## Maintenance Cadence

- monthly: run `.\Verify-VentoyCore.ps1 -RevalidateManagedDownloads`
- before shipping or rebuilding a toolkit USB
- after any manifest URL or checksum edit
- quarterly: manually review the highest-ranked slice first

## Retention Policy

- Timestamped revalidation snapshots: keep `12 months`, plus any snapshot tied
  to a shipped release.
- Release verification artifacts: keep them for every shipped `candidate` or
  `stable` release while that release is retained.
- Summary reports: treat `latest\` as the rolling working view only; keep
  timestamped summaries according to the same `12 months + shipped releases`
  rule.
