# ForgerEMS v1.2.0 Public Preview — operator checklist

Human QA: [PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md](PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md) · Automated gate: `.\tools\Validate-ForgerEMSRelease.ps1`

## Build

- [ ] `dotnet restore .\ForgerEMS.sln`
- [ ] `dotnet build .\ForgerEMS.sln -c Release --no-incremental`
- [ ] `dotnet test .\ForgerEMS.sln -c Release`
- [ ] Optional: `.\tools\build-release.ps1 -Version 1.2.0-preview.1` (or omit `-Version` to use `.csproj`)

## Safety

- [ ] USB Builder refuses `C:\` and internal/system targets (spot-check on a VM).
- [ ] Diagnostics / support bundle redacts `%USERPROFILE%`-style paths.
- [ ] No API keys in clipboard diagnostics for Kyra hub summary.

## Documentation

- [ ] README shows `1.2.0-preview.1` and Public Preview wording.
- [ ] `docs/ENVIRONMENT.md` present.
- [ ] Marketing pack under `docs/marketing/` present for campaign prep.

## GitHub Release

- [ ] Tag matches semver (`v1.2.0-preview.1` style).
- [ ] Attach ZIP + installer + `CHECKSUMS.sha256` + `release.json` from `release\current\` staging (per `build-release.ps1` output).
