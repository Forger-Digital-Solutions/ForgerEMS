# ForgerEMS Beta Release Checklist

Target: **v1.1.4** — *ForgerEMS Beta v1.1.4 — Whole-App Intelligence Preview*

## Validation

- [ ] `dotnet restore .\ForgerEMS.sln`
- [ ] `dotnet build .\ForgerEMS.sln -c Release --no-incremental`
- [ ] `dotnet test .\ForgerEMS.sln -c Release --no-build`

## Beta Safety

- [ ] Offline Local Kyra works without API keys
- [ ] Free provider pool remains optional
- [ ] Kyra **Refresh Provider Status** updates labels after env/session changes (no restart)
- [ ] GitHub Releases update check is non-blocking and fails gracefully offline (no silent install)
- [ ] Beta support email + “do not email secrets” warning visible in header/logs and About/FAQ/Legal
- [ ] System context sharing defaults to OFF
- [ ] Session-only key storage warning is visible
- [ ] USB safety warning mentions VTOYEFI/tiny EFI partitions
- [ ] USB target drive letter + size are visible before destructive actions
- [ ] Toolkit Manual wording is clear (licensing/EULA)
- [ ] Logs location documented and accessible
- [ ] System Intelligence resale estimate is shown as offline/local when no market provider is configured
- [ ] Listing draft generation works and does not include serial/user-path data
- [ ] OfferUp/Facebook are labeled manual/future sources only

## Installer Readiness

- [ ] Verify installer builds to `dist\installer\ForgerEMS-Setup-v1.1.4.exe` (or current `<Version>` from csproj)
- [ ] Verify installer scripts and `.iss` inputs point to current publish output
- [ ] Verify branding/version text is correct for beta
- [ ] Verify no secrets/user paths are included in packaged output
- [ ] Verify install readme contains beta safety guidance

## Remaining Known Limitations

- Persistent Credential Manager/DPAPI key storage is pending
- Some providers are placeholders/future shells
- Free provider terms/limits may change without notice

## v1.1.4 launch limitation triage

| Item | Classification | Action |
|------|----------------|--------|
| Cloudflare account-id unit tests skip the strict “no account” branch when `CLOUDFLARE_ACCOUNT_ID` is already set in **user/machine** env on the developer PC | **Acceptable technical limitation** | Documented here and in README; not a product defect. CI/agents with clean env still exercise the branch. |
| **Machine**-scope env ordering is not covered by an automated test (would need elevation or a test seam) | **Acceptable technical limitation** | Code path matches **process → user → machine**; manual verification via Windows Environment Variables if needed. |
| GitHub Releases update check depends on **network** and repo **visibility** | **Beta limitation** | App fails soft (message/banner); no crash; no silent install. Private or blocked API yields “check failed / offline” style outcomes. |
| **Manual smoke** (`FINAL_MANUAL_SMOKE_TEST.md`) not yet executed | **Launch blocker for calling it “launch-ready”** | Complete checklist or record **PENDING** with owner/date before external “launch” language. |
| **Git** tree mixes multiple workstreams | **Process / beta limitation** | Use grouped commits (see README “Commit plan”); do not commit `dist/` or `release/` (gitignored). |

## Pre-ship automation (operator)

- [ ] `.\tools\check-secrets.ps1` (review output; test projects may show benign example strings)
- [ ] Optional: `.\tools\check-secrets.ps1 -Strict` — fails if hits occur outside `*\tests\*` / `*.Tests\*` paths

## Manual smoke (required for launch wording)

- [ ] Complete [FINAL_MANUAL_SMOKE_TEST.md](FINAL_MANUAL_SMOKE_TEST.md) or attach signed results
