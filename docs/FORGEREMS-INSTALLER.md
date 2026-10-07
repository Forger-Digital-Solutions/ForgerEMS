# ForgerEMS Installer

This document covers the lightweight Windows installer strategy for the native
`ForgerEMS` frontend and the installed-mode backend bundle.

## Current Release Status

Current release:

- App version: `1.2.4`
- Installer artifact: `ForgerEMS-Setup-v1.2.4.exe`
- Portable artifact: `ForgerEMS-v1.2.4.zip`

ForgerEMS now ships both a direct installer and a true portable app ZIP. The
portable ZIP contains `ForgerEMS.exe`, bundled backend/runtime content, docs,
`START_HERE.bat`, `VERIFY.txt`, `release.json`, and checksums. The installer is
for users who prefer installed mode under `%ProgramFiles%`.

The installer uses `installer/ForgerEMS-License.txt` as the Inno Setup license
page and installs the current Terms, Privacy/Data Handling, Legal Notices,
Third-party Notices, User Consent Flow, FAQ, About, and release notes under
`{app}\docs`. The first-run in-app Terms gate still applies after install.

## Installer Choice

Preferred installer:

- Inno Setup 6

Why:

- lightweight
- reliable
- easy to version and review in source control
- clean uninstall and upgrade behavior without enterprise overhead

## Installed Payload

The installer places the frontend under:

```text
%ProgramFiles%\ForgerEMS\
```

Installed files:

- `ForgerEMS.exe`
- `backend\` verified backend release-bundle
- `docs\ForgerEMS-Installed-README.txt`
- current legal/help docs, including Terms of Use, Privacy/Data Handling, Legal Notices, Third-party Notices, User Consent Flow, FAQ, About, and release notes

Not installed:

- ISOs
- tool payloads
- Ventoy binaries

## Shortcuts

The installer creates:

- Start Menu shortcut: `ForgerEMS`
- optional Desktop shortcut: `ForgerEMS`

## Runtime Behavior

The installed app behaves like the portable build:

- runtime data stays in `%LOCALAPPDATA%\ForgerEMS\Runtime\`
- the app itself does not require admin for normal operation
- backend execution still uses the existing PowerShell script model
- installed mode now defaults to `%ProgramFiles%\ForgerEMS\backend\`

Installer note:

- the installer requires admin because it writes to `Program Files`

## Versioning

Current version example:

- `1.2.4`

Installer output name:

- `ForgerEMS-Setup-v1.2.4.exe`

Upgrade behavior:

- the stable Inno `AppId` is preserved
- future installers with the same `AppId` upgrade in place
- installed files are overwritten cleanly
- uninstall support remains intact

## Uninstall Behavior

Uninstall removes:

- files installed under `%ProgramFiles%\ForgerEMS\`
- Start Menu shortcut
- Desktop shortcut if it was created

Uninstall does NOT remove:

- `%LOCALAPPDATA%\ForgerEMS\Runtime\`

This preserves user runtime logs, diagnostics, and Ventoy cache state.

## Source Files

Installer script:

- `installer/ForgerEMS.iss`

Build helper (unsigned local candidates only):

- `tools/build-forgerems-installer.ps1`

Canonical production release builder (signed, gated):

- `tools/build-release.ps1`

Installed readme:

- `installer/ForgerEMS-Installed-README.txt`

Bundled backend staging helper:

- `tools/stage-bundled-backend.ps1`

## Build Steps

### Manual build

1. Publish the app:

   ```powershell
   dotnet publish .\src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
   ```

2. Open `installer\ForgerEMS.iss` in Inno Setup 6.
3. Compile the installer.

### Scripted build

`build-forgerems-installer.ps1` is an **unsigned-candidate-only** adjunct — it
refuses to run without `-UnsignedCandidate`, compiles with
`/DUnsignedCandidate=1`, and emits a `*.candidate.json` sidecar next to the
installer recording `unsignedCandidate=true`, `productionEligible=false`,
`signed=false`, plus the source HEAD and dirty-file count:

```powershell
.\tools\build-forgerems-installer.ps1 -UnsignedCandidate
```

What the script does:

- runs the publish step unless `-SkipPublish` is used
- stages a minimal version-matched backend from a verified release bundle
- resolves `ISCC.exe`
- compiles the installer into the output folder
- writes `<installer>.candidate.json` next to the installer

Production packaging is **not** done here — use the canonical signed path:

```powershell
.\tools\build-release.ps1 -RequireSigning -CertificateThumbprint <thumbprint>
```

`build-release.ps1` resolves the certificate store (CurrentUser first, then
LocalMachine), signs the frontend executable, and configures Inno Setup with a
`ForgerEMSRelease` SignTool callback (absolute Windows PowerShell invoking
`tools\sign-release-artifact.ps1`) so the installer, temporary copies, and the
captured signed uninstaller are each signed **and** verified during compile.
Production fails closed when credentials are missing/invalid, the source tree
is dirty, or any required signature is absent or invalid.

## Output Location

Expected installer output:

```text
dist\installer\ForgerEMS-Setup-v1.2.4.exe
```

Release staging output from `build-release.ps1` is generated under `release\current\`.
Treat this folder as local/CI output, not a versioned repo snapshot.

## Sensor Provider Payload

The installer includes `providers\sensors\` from the WPF publish output when
present. This folder carries the reviewed local LibreHardwareMonitor provider
DLL plus required notices/licenses:

- `LibreHardwareMonitorLib.dll`
- `THIRD-PARTY-NOTICES.txt`
- `LICENSES\LibreHardwareMonitor-MPL-2.0.txt`
- `LICENSES\LibreHardwareMonitor-THIRD-PARTY-LICENSES.txt`

Deep Sensor Mode remains off by default. Users do not download sensor
providers manually, and the provider is local/read-only.

## Updating The Version Later

When you move to a new version:

1. update the WPF project version metadata in
   `src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj`
2. build/publish the new frontend
3. build the installer. Production packaging is signed by default and fails
   closed when no certificate is configured:

   ```powershell
   .\tools\build-release.ps1 -Version 1.2.4 -RequireSigning -CertificateThumbprint <thumbprint>
   ```

   A local **unsigned candidate** for QA — never publishable — must be requested
   explicitly and is marked `unsignedCandidate` / `productionEligible=false` in
   its `release.json`:

   ```powershell
   .\tools\build-release.ps1 -Version 1.2.4 -UnsignedCandidate
   ```

4. if desired, update any docs that explicitly mention the installer file name

Versioned distribution artifacts (for example `ForgerEMS-Setup-v1.2.4.exe` and `ForgerEMS-v1.2.4.zip`) should be attached to a GitHub Release for the matching tag, rather than committed under `release\vX.Y.Z\`.

The `AppId` should stay the same so upgrades keep working.

## Retention / Downgrade Policy

- Installing an **older version over a newer install (downgrade) is not
  supported**. ForgerEMS does not test or guarantee downgrade paths; if an
  older build must be used, uninstall first and install the older build clean.
- Do not assert that the installer itself detects or blocks older published
  installers — treat downgrades as unsupported at the policy level.
- Upgrades in place preserve user data (settings, consent, profiles, reports
  under `%LOCALAPPDATA%\ForgerEMS` are outside the install directory and are
  not removed on upgrade or uninstall).
- Unsigned-candidate installers (`*.candidate.json` sidecar) are QA-only and
  must never be published or offered as updates.

## Installed Layout

Installed layout:

```text
%ProgramFiles%\ForgerEMS\
  ForgerEMS.exe
  backend\
    Verify-VentoyCore.ps1
    Setup-ForgerEMS.ps1
    Update-ForgerEMS.ps1
    manifests\
    docs\
    VERSION.txt
    RELEASE-BUNDLE.txt
    CHECKSUMS.sha256
    SIGNATURE.txt
    ForgerEMS.bundled-backend.json
  docs\
    ForgerEMS-Installed-README.txt
```

## Bundled Backend Rules

The staged backend bundle must:

- come from an existing verified `release\ventoy-core\<version>\` folder
- include the required scripts, manifests, and backend support files
- exclude large payloads, ISO content, Drivers, and `Tools\Portable`
- include metadata that pins the frontend version expected by the bundle

At runtime the app validates:

- required bundled files exist
- bundle metadata is readable
- frontend version matches the bundled backend expectation
- required checksum entries still match the bundled files

If that validation fails, the bundled backend is ignored and the app falls back
to repo mode or external release-bundle mode when available.
