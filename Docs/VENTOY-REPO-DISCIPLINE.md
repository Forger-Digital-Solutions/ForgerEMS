# Ventoy Repo Discipline

## Intent

This repository is now structured with a git-first mindset.

The goal is to keep the maintained Ventoy core small, explicit, reviewable,
and distinct from generated artifacts or vendor payloads that are only being
tracked for visibility.

## Stable Top-Level Structure

These top-level locations are intentional:

- `ventoy-core/`
  Canonical PowerShell implementation.
- `manifests/`
  Canonical manifest inputs and schemas.
- `Docs/`
  Product, lifecycle, architecture, and contract documentation.
- `Tools/`
  Build/release tooling.
- `release/`
  Generated release bundles only.
- `.verify/`
  Disposable verification scratch space only.
- `VentoyToolkitSetup/`
  Compatibility facade only.

## Source Of Truth

Treat these as the maintained source set for the Ventoy core product:

- `ventoy-core/`
- `manifests/`
- `Docs/`
- `Tools/build-release.ps1`
- `.github/workflows/ventoy-core.yml`
- `.gitignore`
- `CHANGELOG-VENTOY-CORE-2026-04-20.md`
- `release/README.md`
- `.verify/README.md`

## Generated Or Disposable

These are not authoritative source:

- `release/ventoy-core/*`
- `.verify/*` except `.verify/README.md`
- workspace or target-root `_downloads/`, `_archive/`, `_logs/`, `_reports/`
- ad hoc preview/test roots created during verification

## Visible But Not Source-Owned

These paths may remain in the workspace, but they are not the source-owned core
for the repository baseline:

- `Tools\Portable`
- `Drivers`
- `MediCat.USB`
- `ISO`
- bundled `ForgerTools/*` artifacts without a tracked source/build pipeline

They are represented by inventory and documentation, not by a claim of full
maintainability.

## `.gitignore` Policy

The repo ignore baseline excludes:

- `.verify/` scratch output
- `release/` generated bundles
- `_archive/`, `_downloads/`, `_logs/`, `_reports/`
- temporary artifacts such as `*.tmp`, `*.temp`, and `*.zip`

That ignore policy is encoded in the root `.gitignore`.

## Git Initialization And First Commit

Recommended source-only baseline:

```powershell
git init
git config --global --add safe.directory H:/ForgerEMS
git branch -M main
git add .gitignore
git add CHANGELOG-VENTOY-CORE-2026-04-20.md
git add .verify\README.md
git add release\README.md
git add .github\workflows\ventoy-core.yml
git add ventoy-core
git add manifests
git add Docs
git add Tools\build-release.ps1
git add VentoyToolkitSetup\MOVED-TO-VENTOY-CORE.txt
git add VentoyToolkitSetup\Setup-ForgerEMS.ps1
git add VentoyToolkitSetup\Setup_USB_Toolkit.ps1
git add VentoyToolkitSetup\Setup_Toolkit.ps1
git add VentoyToolkitSetup\Update-ForgerEMS.ps1
git add VentoyToolkitSetup\Verify-VentoyCore.ps1
git status
git commit -m "Establish Ventoy core shipping baseline"
```

Important discipline:

- do not use `git add .` for the first commit while large vendor payloads still
  live in the same workspace
- commit the maintained core first
- decide separately whether any vendor payload directories belong in version
  control, LFS, a separate repo, or only the inventory manifest

## Remote Push Plan

After the baseline commit:

```powershell
git remote add origin <your-repo-url>
git push -u origin main
```

Then confirm:

- GitHub Actions is enabled for the repository
- `.github/workflows/ventoy-core.yml` appears in the Actions tab
- the first hosted run uploads `release/ventoy-core/**` and `.verify/**`

## Outcome

If the repository follows this discipline, the baseline captures:

- canonical scripts
- canonical manifests and schemas
- release/repo discipline docs
- provenance/readiness docs
- release tooling

without accidentally claiming ownership of heavyweight vendor payloads.
