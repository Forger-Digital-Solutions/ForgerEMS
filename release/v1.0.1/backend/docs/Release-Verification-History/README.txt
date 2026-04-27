Release Verification History
============================

Purpose:
- show what was verified for a release
- show when it was verified
- show what the managed download status looked like at ship time

Per-release folder:
- docs\Release-Verification-History\<coreVersion>\

Managed download artifact mode:
- included-current-verification-artifacts
  Current managed-download summary/revalidation files were copied from
  .verify\managed-download-revalidation\latest at build time.
- included-tracked-verification-artifacts
  Tracked release-history summary/revalidation files were bundled because no
  fresher .verify\managed-download-revalidation\latest snapshot was present.
- operator-generated-after-build
  No current managed-download snapshot was bundled. Run
  .\Verify-VentoyCore.ps1 -RevalidateManagedDownloads after build and record
  the new files here before shipping a candidate/stable release.

Retention:
- keep one history folder for every shipped candidate/stable release
- keep timestamped repo-side revalidation snapshots for the last 12 months
- keep latest\ as the rolling working view only
