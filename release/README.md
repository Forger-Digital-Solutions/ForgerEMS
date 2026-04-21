# Release Output

`release/` is build output space for clean Ventoy core bundles.

Canonical source lives in:

- `ventoy-core/`
- `manifests/`
- `Docs/ventoy-core/`
- `Tools/build-release.ps1`

Recommended workflow:

```powershell
.\Tools\build-release.ps1
```

That command assembles a deterministic bundle under:

- `release/ventoy-core/<coreVersion>/`

The bundle is intended to be shippable as a coherent unit, not hand-curated
from mixed workspace folders.
