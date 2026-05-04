# ForgerEMS Release Readiness

**Mechanical RC v1.1.12-rc.2:** read `docs/BETA_RC_GO_NO_GO_v1.1.12-rc.2.md`, `docs/FIRST_TESTER_DOWNLOAD_FLOW.md`, `docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md`, and `docs/DOWNLOAD_TROUBLESHOOTING.md` before the next beta wave. Prior human-testing pack: `docs/BETA_HUMAN_TESTING_CHECKLIST_v1.1.11.md`, `docs/MISSING_BEFORE_HUMAN_TESTING_v1.1.11.md`, `docs/BETA_TESTER_QUICKSTART.md`, `docs/BETA_ISSUE_REPORT_TEMPLATE.md`, `docs/RELEASE_NOTES_v1.1.12-rc.2.md` (older: `docs/RELEASE_NOTES_v1.1.12-rc.1.md`, `docs/RELEASE_NOTES_v1.1.11-beta.1.md`).

This document is the final operator-facing release prep pass for the native
`ForgerEMS` frontend `v1.1.1`.

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

1. Launch `ForgerEMS-Setup-v1.1.1.exe`.
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

- `1.1.1`

Recommended Git tag:

- `forgerems-v1.1.1`

If you want a simpler tag style:

- `v1.1.1`

Recommended default:

- `forgerems-v1.1.1`

Why:

- keeps the frontend release namespace explicit
- avoids confusion with the Ventoy core bundle versioning

### Release title

Recommended release title:

- `ForgerEMS v1.1.1 - Windows MVP frontend`

## Release Notes Draft

Suggested release notes:

```markdown
## ForgerEMS v1.1.1

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
- Installed mode includes a verified bundled backend under the app folder

### Included artifacts

- portable package for `ForgerEMS.exe`
- Windows installer: `ForgerEMS-Setup-v1.1.1.exe`
- SHA-256 checksums

### Important limitations

- The installer bundles only the small verified backend release bundle, not
  third-party payloads, ISOs, drivers, portable tools, or Ventoy binaries
- Ventoy installation remains an operator-confirmed step in the official
  Ventoy tool
- Full interactive installer/uninstaller validation should still be completed
  before wider public rollout
```

## Release Artifact List

Recommended upload set for the first public release:

- `ForgerEMS-v1.1.1-portable-win-x64.zip`
- `ForgerEMS-Setup-v1.1.1.exe`
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
- the installer includes a verified backend under
  `%ProgramFiles%\ForgerEMS\backend\`
- runtime data still goes to `%LOCALAPPDATA%\ForgerEMS\Runtime\`
- the app itself does not require admin for normal use
- installed mode prefers the bundled backend by default
- repo mode and external release-bundle mode remain available for advanced
  override and development scenarios

### Backend discovery behavior

Current behavior:

- installed mode first validates the bundled backend under the app folder
- if the bundled backend is missing, corrupted, or version-mismatched, it is
  ignored
- after that, discovery can still fall back to repo mode or an external
  release-bundle context when one is available

The bundled backend must contain:

- `Verify-VentoyCore.ps1`
- `Setup-ForgerEMS.ps1`
- `Update-ForgerEMS.ps1`
- `ForgerEMS.Runtime.ps1`
- manifests, docs, checksums, signature, and bundle metadata

## Final Risk Callout

Remaining known limitations before public release:

1. No full interactive installer/uninstaller validation has been completed yet.
   The installer compiled successfully, but the full user-facing install,
   shortcut, uninstall, and uninstall-preserves-runtime-data flow should still
   be exercised manually.

2. The installer does not bundle third-party payloads.
   This is intentional. The backend can create layout, shortcuts, managed
   downloads, and Ventoy handoff, but ISO/tool/driver payloads still come from
   the managed manifest or manual operator workflows.

3. Bundled backend validation is strict.
   If checksums, metadata, or frontend/backend version alignment fail, the app
   ignores the bundled backend and reports a backend discovery problem unless
   an external override context is available.

4. Ventoy operations still rely on the official external tool.
   This is intentional and legally safer, but it means the flow is not fully
   in-app.

## Installed-Mode Backend Strategy

### Current direction

Current `v1.1.1` path:

- bundle a verified release-bundle backend alongside the installed app

### Why this is the cleaner path

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

For public installed mode, keep:

- ship the frontend together with a small verified release-bundle backend

If a manual override is added later, treat it as an advanced option rather than
the default first-run path.
