# ForgerEMS Release Process

This checklist is the standard release path for ForgerEMS Beta builds and later stable releases.

## 1. Prepare

1. Confirm the working tree contains only intentional source, script, manifest, installer, and documentation changes.
2. Confirm large generated payloads are not staged: `*.iso`, `*.img`, `*.zip`, `*.exe`, `dist/`, `release/current/`, and `release/ventoy-core/` outputs stay ignored.
3. Update `src/ForgerEMS.Wpf/ForgerEMS.Wpf.csproj` version metadata when shipping a new app version.
4. Validate `manifests/ForgerEMS.updates.json` and `manifests/vendor.inventory.json`.

## 2. Build

Run a CI-style dry run first:

```powershell
.\tools\build-release.ps1 -Version 1.1.1-beta.1 -DryRun
```

Run a full local release build with Inno Setup installed:

```powershell
.\tools\build-release.ps1 -Version 1.1.1-beta.1
```

Release output is created under:

```text
release/current/
release/ventoy-core/<coreVersion>/
```

## 3. Verify

1. Confirm `release/current/app/ForgerEMS.exe` exists.
2. Confirm `release/current/app/backend/` contains the backend scripts, manifests, checksums, signature, and bundled metadata.
3. Confirm `release/current/app/manifests/` contains the public manifest files.
4. Review `release/current/CHECKSUMS.sha256`.
5. If an installer was built, install it on a clean Windows test machine or VM.
6. Launch ForgerEMS and verify backend status resolves in bundled mode.
7. Run the backend verification script from the installed backend if required by the release classification.

## 4. Publish

1. Create a Git tag using the release version, for example `v1.0.0-beta.1`.
2. Push the tag and confirm GitHub Actions passes.
3. Create a GitHub Release from the tag.
4. Attach the installer and checksum files produced under `release/current/`.
5. Mark Beta releases clearly in the release title, for example `ForgerEMS 1.1.1 Beta 1`.
6. Include verification notes and known limitations in the release body.

Do not commit versioned application snapshots such as `release/v1.1.1/` to the repo. Publish versioned artifacts through GitHub Releases instead.

## 5. Roll Forward

ForgerEMS releases should roll forward. If a release is bad, publish a corrected version with a new tag and GitHub Release rather than replacing prior artifacts silently.
