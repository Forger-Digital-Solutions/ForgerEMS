# Ventoy Operator Release Checklist

Use this checklist when preparing a human-reviewed release.

## Checklist

1. Confirm the repo state is intentional.
   - review `git status`
   - avoid publishing from a dirty or ambiguous source tree
2. Confirm manifest metadata.
   - check `coreVersion`
   - check `buildTimestampUtc`
   - check `releaseType`
   - check `managedChecksumPolicy`
3. Run offline verification.
   - `.\ventoy-core\Verify-VentoyCore.ps1`
4. Run managed download revalidation for any distribution-intended build.
   - `.\ventoy-core\Verify-VentoyCore.ps1 -RevalidateManagedDownloads`
   - review `.verify\managed-download-revalidation\latest\managed-download-summary.txt`
5. Confirm the top fragility slice is clean before building.
   - review maintenance ranks `1-7`
   - require no unresolved drift, checksum ambiguity, or provenance ambiguity
6. Build the release bundle.
   - `.\Tools\build-release.ps1`
7. Review integrity artifacts.
   - inspect `CHECKSUMS.sha256`
   - inspect `SIGNATURE.txt`
   - use `Docs/VENTOY-RELEASE-BUNDLE-REVIEW.md` for the human pass
   - confirm `docs/Release-Verification-History/<coreVersion>/` clearly shows
     the managed-download snapshot mode for the release
8. Verify the built bundle.
   - `.\release\ventoy-core\<version>\Verify-VentoyCore.ps1`
9. Optionally review upstream health.
   - `.\ventoy-core\Verify-VentoyCore.ps1 -Online`
10. Confirm the release classification matches intent.
   - `dev` for local/testing only
   - `candidate` for controlled review
   - `stable` for real distribution
11. Publish or hold.
   - publish only if required checks passed
   - hold if integrity, checksum, ownership boundaries, or managed-download
     maintenance signals are unclear
