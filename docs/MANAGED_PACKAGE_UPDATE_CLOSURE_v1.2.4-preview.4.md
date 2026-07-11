# Managed package update closure — v1.2.4-preview.4

## CrystalDiskInfo

The outstanding managed-package freshness record is **CrystalDiskInfo 9.8.0 (standard ZIP)**. Its canonical catalog identity is its destination: `Tools\\Portable\\Disk\\CrystalDiskInfo9_8_0.zip`; the manifest has no separate package-ID field.

- Pinned managed version: `9.8.0`
- Verified current upstream stable version: `9.9.1`
- Upstream artifact observed: `CrystalDiskInfo9_9_1.zip`
- Official sources: Crystal Dew World download/history pages and the `crystalmark` SourceForge project
- Status: `MinorUpdateAvailable` — intentionally unresolved

The previous freshness record named 9.9.0. The 2026-07-11 audit corrected that available-version metadata to 9.9.1. It did not promote the package: the official project exposes the versioned ZIP but no vendor-published machine-readable SHA-256/SHA-512 file binding that exact artifact. The existing 9.8.0 pinned SHA-256 remains unchanged. No checksum, URL, filename, size, or version was guessed.

## Safety behavior

Managed refresh is manifest-driven, not filename- or URL-digit parsing. Freshness records are explicit metadata; the updater only accepts a download after checksum verification, writes it to a temporary `.download` file, and moves the verified payload into place. A failed download, checksum mismatch, or cancellation does not replace the prior destination. Applying staged content to a toolkit remains a separate explicit USB Builder action. Automatic maintenance never starts at application startup or USB arrival, and downloaded installers are never silently executed.
