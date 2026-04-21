# ForgerEMS WPF Packaging

This document covers the portable-first publish flow for the native
`ForgerEMS` Windows frontend. The PowerShell backend remains the source of
truth. The WPF app only orchestrates discovery, execution, logging, and status
display.

## Publish Target

- deployment mode: self-contained
- target runtime: `win-x64`
- publish mode: single-file
- primary output: `ForgerEMS.exe`

Preferred publish profile:

- `src/ForgerEMS.Wpf/Properties/PublishProfiles/ForgerEMS-win-x64.pubxml`

Exact publish command:

```powershell
dotnet publish .\src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Equivalent profile-based publish:

```powershell
dotnet publish .\src\ForgerEMS.Wpf\ForgerEMS.Wpf.csproj /p:PublishProfile=ForgerEMS-win-x64
```

## Expected Output

Default publish folder:

```text
src\ForgerEMS.Wpf\bin\Release\net8.0-windows\win-x64\publish\
```

Expected portable artifact set:

- `ForgerEMS.exe`

Optional extra files:

- none are required for normal portable distribution when the publish succeeds
- a separate `.pdb` should not be required because debug symbols are embedded

## Runtime Layout

### Portable app layout

The published EXE is portable and self-contained. It does not require the .NET
desktop runtime to be preinstalled.

### Backend discovery strategy

The current MVP uses a practical portable layout:

- repo mode works when `ForgerEMS.exe` is launched from the repo root or from a
  child folder inside the repo tree
- release-bundle mode works when `ForgerEMS.exe` is launched from the release
  bundle root or from a child folder inside the release-bundle tree

This is effectively an Option B layout rule:

- keep the EXE inside the repo tree or the release-bundle tree if you want
  automatic backend discovery

The app does not rewrite backend paths and does not embed backend scripts into
the EXE.

## First-Run Runtime Folders

On startup, the app ensures these user-writable folders exist:

```text
%LOCALAPPDATA%\ForgerEMS\Runtime\
%LOCALAPPDATA%\ForgerEMS\Runtime\Ventoy\
%LOCALAPPDATA%\ForgerEMS\Runtime\Ventoy\packages\
%LOCALAPPDATA%\ForgerEMS\Runtime\Ventoy\extracted\
%LOCALAPPDATA%\ForgerEMS\Runtime\logs\
%LOCALAPPDATA%\ForgerEMS\Runtime\diagnostics\
```

Notes:

- Ventoy runtime downloads are cached under the current user profile
- extraction happens under the same runtime tree
- no admin rights are required just to create or use these folders

## Logging And Permissions

- frontend live logs are shown in the WPF log pane
- frontend session logs are also written under:

```text
%LOCALAPPDATA%\ForgerEMS\Runtime\logs\
```

- normal frontend operation does not require elevation
- backend verification and managed-download revalidation should run without
  admin rights in normal cases
- Ventoy installation itself can still require elevation when the official
  `Ventoy2Disk.exe` tool performs disk operations

## Published Self-Test

The published EXE includes a lightweight startup self-test mode:

```powershell
.\ForgerEMS.exe --self-test
```

This mode:

- initializes the `%LOCALAPPDATA%\ForgerEMS\Runtime\` workspace
- exercises the PowerShell runner with an inline command
- records a diagnostic report at:

```text
%LOCALAPPDATA%\ForgerEMS\Runtime\diagnostics\published-self-test.txt
```

Use this before wider distribution or on a clean validation machine.

## Clean-System Validation

Recommended smoke test on a clean Windows 10/11 machine:

1. Copy the published `ForgerEMS.exe` into a release bundle root or a repo root.
2. Run `ForgerEMS.exe --self-test`.
3. Confirm `published-self-test.txt` was generated under
   `%LOCALAPPDATA%\ForgerEMS\Runtime\diagnostics\`.
4. Launch `ForgerEMS.exe`.
5. Confirm the app opens without installing .NET.
6. Confirm backend discovery reports repo mode or release-bundle mode.
7. Confirm `Verify` starts PowerShell successfully.
8. Confirm session logs are written under `%LOCALAPPDATA%\ForgerEMS\Runtime\logs\`.

## Distribution Shape

Current recommendation:

- zip the publish folder
- distribute the zip as a portable frontend package
- place the EXE inside the backend repo or release bundle before use

Not included yet:

- MSI / installer
- auto-update installer flow
- shell integration
