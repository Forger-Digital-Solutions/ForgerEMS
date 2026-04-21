# ForgerEMS Installed Mode V2

This document defines the installed-mode v2 design where the native frontend is
installed together with a verified backend release-bundle.

## Goal

Move from:

- frontend-only installed mode

To:

- frontend plus bundled backend

Result:

- installed `ForgerEMS` works immediately without requiring a nearby repo or
  manual backend placement

## Bundled Backend Source Of Truth

The bundled backend is staged from an existing verified release-bundle under:

```text
release\ventoy-core\<backend-version>\
```

The installer does not invent backend contents in C#. It packages a curated
copy of the existing PowerShell release-bundle.

## Installed Layout

```text
%ProgramFiles%\ForgerEMS\
  ForgerEMS.exe
  backend\
    Verify-VentoyCore.ps1
    Setup-ForgerEMS.ps1
    Update-ForgerEMS.ps1
    Setup_Toolkit.ps1
    Setup_USB_Toolkit.ps1
    ForgerEMS.updates.json
    VERSION.txt
    RELEASE-BUNDLE.txt
    CHECKSUMS.sha256
    SIGNATURE.txt
    ForgerEMS.bundled-backend.json
    manifests\
    docs\
    tools\
  docs\
    ForgerEMS-Installed-README.txt
```

## What Is Excluded

The bundled backend must stay minimal and must not include:

- large vendor payloads
- ISO content
- `Drivers\`
- `Tools\Portable\`
- bundled Ventoy binaries or other third-party tools that should remain
  externally sourced at runtime

## Discovery Order

Default discovery order:

1. bundled backend
2. repo mode
3. external release-bundle mode

Advanced override:

- a caller can pass a preferred path into discovery
- an operator can set `FORGEREMS_BACKEND_ROOT` to force repo mode or an
  external release-bundle root

If the override path is invalid, discovery falls back gracefully to the normal
order.

## Integrity Rules

Before using the bundled backend the app must confirm:

- required scripts exist
- required manifests exist
- bundle metadata exists and is readable
- bundled frontend version matches the running frontend version
- required file hashes still match `CHECKSUMS.sha256`

If any of those checks fail, the bundled backend is rejected and the app must
not execute it.

## Version Handling

The bundle metadata file records:

- frontend version
- backend bundle version
- source release-bundle root hint
- generation timestamp

The frontend version is taken from the WPF project version during installer
build. The backend version comes from the staged release-bundle `VERSION.txt`.

Version coupling rule:

- the bundled backend is only accepted when its recorded frontend version
  matches the running frontend version exactly

This keeps installed mode deterministic and avoids silently mixing incompatible
frontend and backend snapshots.

## Migration From V1 To V2

v1 installed mode:

- installed frontend only
- required a nearby repo or release-bundle context

v2 installed mode:

- installs frontend plus bundled backend
- works immediately after install
- keeps repo mode and external release-bundle mode available for advanced use

Operationally this means:

1. continue generating or approving the verified backend release-bundle
2. stage that bundle into the installer build
3. publish installer artifacts that contain the backend under `backend\`
4. keep portable mode and repo workflows unchanged

## Future Update Strategy

Not implemented in this phase:

- full installer upgrade that replaces frontend and backend together
- backend-only updater
- in-app auto-update

For now the clean path is:

- future installer releases replace both frontend and bundled backend together
