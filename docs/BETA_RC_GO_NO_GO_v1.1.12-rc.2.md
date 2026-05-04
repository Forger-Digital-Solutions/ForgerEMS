# Beta readiness GO / NO-GO — v1.1.12-rc.2

## GO

- **ZIP-first** messaging in release bundle + docs + workflow release header.  
- **Dual ZIP** (`ForgerEMS-v…` + `ForgerEMS-Beta-v…`) with matching SHA256 lines.  
- **START_HERE.bat** / **VERIFY.txt** hardened copy (no security bypass).  
- **Kyra env** documentation + in-app help pointers.  
- **Automated tests** must be green before tagging (see CI log for count).

## NO-GO

- Failing `dotnet test` / `build-release.ps1`.  
- Missing `release/ventoy-core/<coreVersion>` when running full release.  
- `check-secrets.ps1 -Strict` hits outside `tests/`.

## Manual before broad invite

- Chrome + Edge **ZIP-only** download on a non-dev PC.  
- Extract → `START_HERE.bat` → install → first-run overlay.  
- Kyra **offline** smoke; optional env provider only if you intend to test it.  
- USB benchmark on a real USB3 port.

## Operator sign-off

- [ ] GO for next beta wave  
- [ ] NO-GO — reason: ___________________
