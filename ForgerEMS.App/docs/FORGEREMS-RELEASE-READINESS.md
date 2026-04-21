# ForgerEMS Release Readiness

This document is the final operator-facing release prep pass for the native
`ForgerEMS` frontend `v1.0.0`.

It focuses on:

- installer verification
- release publishing preparation
- portable vs installed distribution expectations
- remaining public-release limitations
- the recommended installed-mode strategy for a future `v2`

## Installer Verification Checklist

Use this checklist before the first public release and again for future
installer updates.

### Install

1. Launch `ForgerEMS-Setup-v1.0.0.exe`.
2. Confirm the installer requests elevation because it targets
   `%ProgramFiles%\ForgerEMS\`.
3. Confirm the install completes without errors.
4. Confirm the installed folder contains:
   - `ForgerEMS.exe`
   - `ForgerEMS-Installed-README.txt`

### First launch

1. Launch `ForgerEMS` from the post-install run option or the Start Menu.
2. Confirm the main window opens.
3. Confirm the app version and branding show as `ForgerEMS` from
   `Forger Digital Solutions`.
4. Confirm normal startup does not require admin rights.

### Self-test

1. Open PowerShell.
2. Run:

   ```powershell
   & "$env:ProgramFiles\ForgerEMS\ForgerEMS.exe" --self-test
   ```

3. Confirm exit code `0`.
4. Confirm this file exists:

   ```text
   %LOCALAPPDATA%\ForgerEMS\Runtime\diagnostics\published-self-test.txt
   ```

5. Confirm the self-test report shows:
   - runtime folder creation succeeded
   - PowerShell execution succeeded
   - backend discovery result is sensible for the chosen test location

### Shortcut validation

1. Confirm the Start Menu shortcut exists as `ForgerEMS`.
2. If the Desktop shortcut task was selected, confirm that shortcut exists too.
3. Launch the app from each created shortcut and confirm it opens successfully.

### Uninstall

1. Use the standard Windows uninstall entry for `ForgerEMS`.
2. Confirm `%ProgramFiles%\ForgerEMS\` is removed.
3. Confirm Start Menu and Desktop shortcuts are removed.
4. Confirm uninstall does not report file-in-use errors when the app is closed.

### Runtime data preservation

1. Before uninstall, create or confirm:
   - `%LOCALAPPDATA%\ForgerEMS\Runtime\logs\`
   - `%LOCALAPPDATA%\ForgerEMS\Runtime\diagnostics\`
2. Uninstall the app.
3. Confirm `%LOCALAPPDATA%\ForgerEMS\Runtime\` still exists afterward.
4. Confirm logs, diagnostics, and Ventoy cache content are preserved.

## First Public Release Recommendation

### Version and tag

Recommended version:

- `1.0.0`

Recommended Git tag:

- `forgerems-v1.0.0`

If you want a simpler tag style:

- `v1.0.0`

Recommended default:

- `forgerems-v1.0.0`

Why:

- keeps the frontend release namespace explicit
- avoids confusion with the Ventoy core bundle versioning

### Release title

Recommended release title:

- `ForgerEMS v1.0.0 - Windows MVP frontend`

## Release Notes Draft

Suggested release notes:

```markdown
## ForgerEMS v1.0.0

First public MVP release of the native Windows frontend for the existing
ForgerEMS PowerShell backend.

### Highlights

- Native WPF frontend for Verify, Setup USB, Update USB, and managed-download
  revalidation flows
- Self-contained `win-x64` build with no separate .NET install required
- Live PowerShell log streaming with clearer status handling
- USB target inspection with safer selection rules and system-drive blocking
- Managed-download summary surfacing inside the UI
- Controlled Ventoy handoff using the official package and `Ventoy2Disk`
- Portable distribution and Windows installer distribution

### Included artifacts

- portable package for `ForgerEMS.exe`
- Windows installer: `ForgerEMS-Setup-v1.0.0.exe`
- SHA-256 checksums

### Important limitations

- The frontend does not bundle the backend repo or full release bundle
- Installed mode still depends on a valid backend working context nearby
- Ventoy installation remains an operator-confirmed step in the official
  Ventoy tool
- Full interactive installer/uninstaller validation should still be completed
  before wider public rollout
```

## Release Artifact List

Recommended upload set for the first public release:

- `ForgerEMS-v1.0.0-portable-win-x64.zip`
- `ForgerEMS-Setup-v1.0.0.exe`
- `SHA256SUMS.txt`

Optional supporting upload:

- a short release-readme text file if you want the GitHub release page to be
  mirrored in the artifact set

## Distribution Notes

### Portable mode expectations

Portable mode is best for operators who already work inside:

- the source repo tree
- or a generated release-bundle tree

Expectations:

- `ForgerEMS.exe` runs standalone as a self-contained frontend
- runtime data goes to `%LOCALAPPDATA%\ForgerEMS\Runtime\`
- the app still needs a valid backend repo or release bundle nearby if you want
  script discovery to succeed automatically

### Installed mode expectations

Installed mode is best for users who want:

- Start Menu access
- a standard uninstall entry
- a cleaner desktop-facing deployment

Expectations:

- the app installs under `%ProgramFiles%\ForgerEMS\`
- runtime data still goes to `%LOCALAPPDATA%\ForgerEMS\Runtime\`
- the app itself does not require admin for normal use
- backend discovery still has the same nearby-backend requirement as portable
  mode

### Backend discovery limitation

Current limitation:

- the installed frontend does not bundle the backend

That means the app still needs access to a valid working context containing the
existing PowerShell entrypoints:

- `Verify-VentoyCore.ps1`
- `Setup-ForgerEMS.ps1`
- `Update-ForgerEMS.ps1`

In practice, a user needs one of these nearby:

- the repo root
- or a release-bundle root

If those scripts are not discoverable from the current working context or
ancestor path, the UI will report that the backend is unavailable.

## Final Risk Callout

Remaining known limitations before public release:

1. Installed mode does not bundle the backend.
   The installer intentionally ships only the frontend and installed readme.

2. No full interactive installer/uninstaller validation has been completed yet.
   The installer compiled successfully, but the full user-facing install,
   shortcut, uninstall, and uninstall-preserves-runtime-data flow should still
   be exercised manually.

3. Backend discovery is path-dependent.
   Discovery works when the app is launched in or under a repo tree or release
   bundle tree. A standalone installed app in `Program Files` will not discover
   the backend on its own unless the user launches it in a context where the
   scripts are reachable.

4. Installed-mode user expectations may not match current architecture.
   Public users may assume an installed app is self-contained when it currently
   remains a frontend controller.

5. Ventoy operations still rely on the official external tool.
   This is intentional and legally safer, but it means the flow is not fully
   in-app.

## Installed-Mode Strategy V2 Recommendation

### Recommended future direction

Recommended `v2` path:

- bundle a verified release-bundle backend alongside the installed app

### Why this is the cleaner future path

Benefits:

- installed mode becomes meaningfully self-contained
- preserves the current script contracts and backend source of truth
- avoids rewriting backend logic in C#
- gives operators a predictable installed experience

Tradeoffs:

- installer size increases
- frontend and bundled backend must be versioned together
- release packaging must clearly state which backend snapshot shipped

### Alternative: user-selected backend root on first launch

Benefits:

- smaller installer
- no bundled backend maintenance burden
- works well for advanced operators who already manage repo or release-bundle
  locations

Tradeoffs:

- worse first-run experience
- more support friction
- users must understand backend layout immediately

### Final recommendation

For public installed mode, the cleaner `v2` is:

- ship the frontend together with a small verified release-bundle backend

If a manual override is added later, treat it as an advanced option rather than
the default first-run path.
