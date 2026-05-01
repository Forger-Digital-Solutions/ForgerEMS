# ForgerEMS Installer

This document covers the lightweight Windows installer strategy for the native
`ForgerEMS` frontend and the installed-mode v2 backend bundle.

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

- `1.1.1`

Installer output name:

- `ForgerEMS-Setup-v1.1.1.exe`

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

Build helper:

- `tools/build-forgerems-installer.ps1`

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

```powershell
.\tools\build-forgerems-installer.ps1
```

What the script does:

- runs the publish step unless `-SkipPublish` is used
- stages a minimal version-matched backend from a verified release bundle
- resolves `ISCC.exe`
- compiles the installer into the output folder

## Output Location

Expected installer output:

```text
dist\installer\ForgerEMS-Setup-v1.1.1.exe
```

Release staging output from `build-release.ps1` is generated under `release\current\`.
Treat this folder as local/CI output, not a versioned repo snapshot.

## Updating The Version Later

When you move to a new version:

1. update the WPF project version metadata in
   `src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj`
2. build/publish the new frontend
3. build the installer with:

   ```powershell
   .\tools\build-forgerems-installer.ps1 -Version 1.1.1
   ```

4. if desired, update any docs that explicitly mention the installer file name

Versioned distribution artifacts (for example `ForgerEMS-Setup-v1.1.1.exe`) should be attached to a GitHub Release for the matching tag, rather than committed under `release\vX.Y.Z\`.

The `AppId` should stay the same so upgrades keep working.

## Installed Layout

Installed layout for v2:

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
