# `.verify` Policy

`.verify` is disposable workspace scratch space for Ventoy core verification.

## What belongs here

- temporary roots created by `ventoy-core/Verify-VentoyCore.ps1`
- preview-only setup and updater artifacts
- temporary manifests used to test validation behavior
- logs captured during local verification runs

## What does not belong here

- shipping USB payloads
- authoritative manifests
- curated vendor binaries
- anything you expect to keep as the source of truth

## Recommendation

- keep `.verify` in the workspace only
- do not copy `.verify` onto release media or customer-facing USB builds
- it is safe to delete the folder entirely and let verification recreate it
- if the project is later put under git, ignore `.verify`

The legacy compatibility wrapper under `VentoyToolkitSetup/Verify-VentoyCore.ps1`
may still be used, but the canonical verification entrypoint is now:

- `ventoy-core/Verify-VentoyCore.ps1`
