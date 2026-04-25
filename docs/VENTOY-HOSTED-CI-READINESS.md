# Ventoy Hosted CI Readiness

## Active Workflow

Hosted CI workflow path:

- `.github/workflows/ventoy-core.yml`

Workflow name:

- `ventoy-core`

Current hosted steps:

1. offline verification: `.\ventoy-core\Verify-VentoyCore.ps1`
2. release build: `.\Tools\build-release.ps1`
3. optional non-blocking online verification:
   `.\ventoy-core\Verify-VentoyCore.ps1 -Online`
4. artifact upload for:
   - `release/ventoy-core/**`
   - `.verify/**`

Distribution note:

- hosted CI currently proves the offline and build path
- operator-reviewed distribution should still include
  `.\ventoy-core\Verify-VentoyCore.ps1 -RevalidateManagedDownloads`
  and review of the managed-download summary archive

## Windows Runner Compatibility Review

The workflow is ready for hosted Windows runners because:

- it uses `runs-on: windows-latest`
- it uses `shell: pwsh`
- the scripts require PowerShell 5.1 or later, which is satisfied on hosted
  Windows runners
- internal compatibility launches fall back to `powershell.exe` when needed
- all workflow paths are repo-relative
- no local drive letters are assumed in the workflow file
- output directories are inside the workspace and can be uploaded as artifacts

## Path And Casing Review

Reviewed path/casing assumptions:

- `.github/workflows/ventoy-core.yml`
  Actual workflow path is correct.
- `ventoy-core/Verify-VentoyCore.ps1`
  Actual repo path and workflow reference match.
- `Tools/build-release.ps1`
  Actual repo path and workflow reference match.

No local-only absolute path assumptions are present in the workflow.

## Push Readiness Checklist

Before the first remote push:

1. ensure the maintained source set is intentionally staged or committed
2. ensure `release/` and `.verify/` are not part of the source commit
3. confirm `.github/workflows/ventoy-core.yml` exists in the commit
4. confirm `manifests/ForgerEMS.updates.json` has the intended
   `coreVersion`, `buildTimestampUtc`, and `releaseType`
5. push `main` to the remote
6. confirm Actions is enabled on the remote repository
7. review the first hosted run for:
   - offline verify pass
   - build pass
   - uploaded release artifact
   - non-blocking online warnings only
   - use `Docs/VENTOY-HOSTED-CI-TROUBLESHOOTING.md` if anything fails

## What Still Needs A Real Push

This document confirms readiness, not hosted proof.

Hosted proof begins only after:

- the repository is pushed
- the workflow executes on GitHub-hosted Windows runners
- the run history is retained as evidence
