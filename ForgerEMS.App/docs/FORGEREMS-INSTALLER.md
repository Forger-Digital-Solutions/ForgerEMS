# ForgerEMS Installer

This document covers the lightweight Windows installer strategy for the native
`ForgerEMS` frontend.

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
- `ForgerEMS-Installed-README.txt`

Not installed:

- backend repo content
- release-bundle scripts
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

Installer note:

- the installer requires admin because it writes to `Program Files`

## Versioning

Current version:

- `1.0.0`

Installer output name:

- `ForgerEMS-Setup-v1.0.0.exe`

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
- resolves `ISCC.exe`
- compiles the installer into the output folder

## Output Location

Expected installer output:

```text
dist\installer\ForgerEMS-Setup-v1.0.0.exe
```

## Updating The Version Later

When you move to a new version:

1. update the WPF project version metadata in
   `src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj`
2. build/publish the new frontend
3. build the installer with:

   ```powershell
   .\tools\build-forgerems-installer.ps1 -Version 1.0.1
   ```

4. if desired, update any docs that explicitly mention the installer file name

The `AppId` should stay the same so upgrades keep working.

## Practical Limitation

Because the installer does not bundle the backend repo or a release bundle, the
installed frontend is still just the controller. Backend discovery still
depends on a valid repo or release-bundle working context being available.
