# Changelog: Ventoy Core Baseline

Date: `2026-04-20`

This changelog covers the maintainable Ventoy core only. It does not claim that
all bundled vendor or community payloads elsewhere in the workspace are fully
maintainable.

## Owned + Shippable Milestone

This pass moved the project from "clean + verified" to "owned + shippable":

- canonical repo layout introduced
- version/build metadata sourced from the canonical manifest
- compatibility facade preserved under `VentoyToolkitSetup/`
- release builder introduced
- vendor inventory introduced
- release contract and compatibility contract documented
- Techbench OS planning document added

## Provenance + Release Automation Milestone

This follow-on pass moved the project toward "product + lifecycle mode":

- release classification added (`dev`, `candidate`, `stable`)
- vendor inventory expanded with provenance fields
- vendor inventory contract validation added
- CI-ready workflow sample documented
- repo discipline and first-commit plan documented
- signing/integrity concept defined

## CI Activation + Checksum Enforcement Milestone

This pass moved the project from "operational plan" to "operational baseline":

- real GitHub Actions workflow added under `.github/workflows/`
- release bundles now generate `CHECKSUMS.sha256`
- verification can validate bundle checksums when present
- managed checksum coverage is tracked as a release discipline rule

## Checksum Completion + Release Signing Milestone

This pass closes the last managed checksum gap and turns the release discipline
into active enforcement:

- all manifest-managed `file` items now have checksum coverage
- candidate/stable releases now fail verification or build if managed checksum
  coverage is missing
- release bundles now generate `SIGNATURE.txt`
- release verification now validates both `CHECKSUMS.sha256` and
  `SIGNATURE.txt` when present
- hosted CI readiness and promotion/release checklists are now documented
- the git-first repo baseline is now ready to be initialized as a repository

## Added

- `ventoy-core/` canonical script home
- `manifests/` canonical manifest home
- `manifests/vendor.inventory.json`
- `manifests/vendor.inventory.schema.json`
- `Tools/build-release.ps1`
- `Docs/VENTOY-COMPATIBILITY-CONTRACT.md`
- `Docs/VENTOY-RELEASE-STANDARD.md`
- `Docs/VENTOY-REPO-DISCIPLINE.md`
- `Docs/VENTOY-PROVENANCE-PLAN.md`
- `Docs/VENTOY-DISTRIBUTION-READINESS.md`
- `Docs/VENTOY-HOSTED-CI-READINESS.md`
- `Docs/VENTOY-RELEASE-PROMOTION.md`
- `Docs/VENTOY-OPERATOR-RELEASE-CHECKLIST.md`
- `Docs/TECHBENCH-OS-PLAN.md`
- `Docs/examples/github-actions-ventoy-core.yml`
- `.github/workflows/ventoy-core.yml`
- release integrity artifact `SIGNATURE.txt`

## Changed

- canonical setup/update/verify scripts now run from `ventoy-core/`
- `VentoyToolkitSetup/` now acts as a compatibility facade instead of the
  canonical source location
- release metadata is now sourced from `manifests/ForgerEMS.updates.json`
- setup/update/verify can display manifest-sourced version metadata
- verification now supports optional `-Online` upstream checks
- verification now validates the vendor inventory contract offline
- verification now validates release checksums and signatures when a bundle
  provides them
- release output is assembled into `release/ventoy-core/<coreVersion>/`
- release output now carries an explicit release classification
- release output now generates `CHECKSUMS.sha256` and `SIGNATURE.txt`
- managed checksum policy is now enforced automatically for `candidate` and
  `stable` releases
- the Rescuezilla 2.6.1 managed entry now uses the official upstream
  `SHA256SUM` source plus a pinned SHA-256 value

## Fixed

- dry-run remains a real preview for setup and updater public entrypoints
- manifest destinations and settings-based folders remain root-contained
- public setup names remain backward compatible through wrappers instead of
  duplicated logic
- repo and release-bundle manifest resolution work from the canonical layout
- candidate/stable releases can no longer silently ship with missing managed
  checksum coverage

## Current Managed Baseline

The shipping Ventoy core is now centered on:

- canonical scripts under `ventoy-core/`
- canonical manifests under `manifests/`
- release-bundle documentation under `Docs/ventoy-core/`
- reproducible release assembly under `Tools/build-release.ps1`
- reproducible local verification under `.verify`
- checksum and integrity artifacts produced as part of the bundle

## Known Limits

- hosted CI has been prepared but not yet proven by a pushed remote run
- many third-party binaries in `Tools/Portable`, `Drivers`, `MediCat.USB`, and
  packaged `ForgerTools` folders are still manual/vendor payloads
- `SIGNATURE.txt` is an integrity seal, not a cryptographic publisher
  signature
- there is no source-backed release pipeline for every bundled artifact

## Next Maintainability Milestone

Move from "trusted local baseline" to "production-grade distribution" by:

- proving the workflow on hosted CI
- tightening vendor inventory provenance and checksum coverage
- separating or source-owning shipped `ForgerTools` artifacts
- adding true publisher signing if release identity verification becomes a
  requirement
