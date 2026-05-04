# Script audit — v1.1.12-rc.1

## tools/build-release.ps1

| Aspect | Status |
|---------|--------|
| **Purpose** | End-to-end: restore, build, publish, stage backend, installer, ZIP, checksums, `DOWNLOAD_BETA.txt`, `release.json`. |
| **Still used** | Yes — CI dry-run (`build.yml`) and `release.yml`. |
| **Risks found** | `ConvertTo-WindowsVersion` **failed** on semver prerelease (`1.1.12-rc.1`) before this RC — **fixed** by stripping `-prerelease` before quad mapping. |
| **Fixes made** | Prerelease-safe Windows version; richer `DOWNLOAD_BETA.txt`; `releaseIdentifierLabel` updated for mechanical RC. |
| **Still needed** | Operator must have Inno Setup locally for non–dry-run; `release/ventoy-core/<coreVersion>` must exist (from `build-backend-release.ps1`). |

## tools/build-backend-release.ps1

| Aspect | Status |
|---------|--------|
| **Purpose** | Assembles Ventoy core release bundle under `release/ventoy-core/...`. |
| **Still used** | Yes — prerequisite for full `build-release.ps1`. |
| **Risks** | Fails if upstream bundle inputs missing — expected; documented in release readiness. |

## tools/build-forgerems-installer.ps1

| Aspect | Status |
|---------|--------|
| **Purpose** | Wrapper around Inno / paths for installer-only builds in some workflows. |
| **Still used** | Verify locally when adjusting installer; primary path is `build-release.ps1` invoking `ISCC` directly. |
| **Risks** | Keep parameter names aligned with `ForgerEMS.iss` defines (`AppVersion`, `AppVersionInfo`, `ReleaseIdentifier`). |

## tools/check-secrets.ps1

| Aspect | Status |
|---------|--------|
| **Purpose** | Heuristic scan for token-like strings. |
| **Still used** | Yes — pre-tag. |
| **Risks** | False positives in tests — use `-Strict` intentionally. |

## backend/SystemIntelligence/Invoke-ForgerEMSSystemScan.ps1

| Aspect | Status |
|---------|--------|
| **Purpose** | WMI-heavy system scan; writes JSON consumed by UI and Kyra. |
| **Still used** | Yes. |
| **Risks** | OEM variability / timeouts — documented; no change in this pass. |

## backend/ToolkitManager/Get-ForgerEMSToolkitHealth.ps1

| Aspect | Status |
|---------|--------|
| **Purpose** | Toolkit health JSON from manifest expectations. |
| **Still used** | Yes. |

## backend/Verify-VentoyCore.ps1

| Aspect | Status |
|---------|--------|
| **Purpose** | Ventoy core verification entry. |
| **Still used** | Yes — `build.yml` invokes it. |

## backend/Update-ForgerEMS.ps1 (and USB toolkit scripts)

| Aspect | Status |
|---------|--------|
| **Purpose** | USB update / setup flows called from WPF via `BackendContext`. |
| **Still used** | Yes. |
| **Risks** | Elevation prompts — intentional for some operations; do not weaken UAC handling. |

## Duplicate / obsolete scripts

- No duplicate **release** entrypoint found: `build-release.ps1` is canonical; `build-forgerems-installer.ps1` is adjunct.
- **Obsolete names:** None flagged for deletion in this pass.

## manifests / .github

- `build.yml`: restore → build → test → JSON validate → `Verify-VentoyCore` → `build-release -DryRun` — sound.
- `release.yml`: tag / dispatch → backend bundle → full release → tests → `softprops/action-gh-release` with ZIP + EXE + checksums + `DOWNLOAD_BETA.txt` + `release.json` — sound (see CI audit for release-body formatting note).
