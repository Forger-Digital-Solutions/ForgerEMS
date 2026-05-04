# Beta mechanical RC — GO / NO-GO — v1.1.12-rc.1

**Audience:** Release operator before inviting the next wave of beta testers.

---

## GO (ready)

- **Automated tests:** `dotnet test .\ForgerEMS.sln -c Release` — run on a clean agent; latest local run: **313** passed, **0** failed (includes new mechanical RC documentation + diagnostics tests).
- **Release script:** `tools/build-release.ps1` supports **semver prerelease** versions for Inno `VersionInfo` mapping (`1.1.12-rc.1` → `1.1.12.0`).
- **ZIP-first artifacts:** `ForgerEMS-v1.1.12-rc.1.zip`, inner `START_HERE.bat` → `ForgerEMS Installer.exe`, `VERIFY.txt`, inner + root checksums, `DOWNLOAD_BETA.txt`, `release.json`.
- **Documentation:** `docs/DOWNLOAD_TROUBLESHOOTING.md`, `docs/RELEASE_NOTES_v1.1.12-rc.1.md`, audits under `docs/*_v1.1.12-rc.1.md`.
- **CI:** `build.yml` exercises dry-run release; `release.yml` publishes ZIP as primary narrative and looks under **`docs/RELEASE_NOTES_v{version}.md`** first.

---

## NO-GO (blocks wider beta)

- **Failing tests** or **restore/build errors** on `main` at the tag you intend to ship.
- **Missing** `release/ventoy-core/<coreVersion>` on the machine building a **full** release (run `build-backend-release.ps1` first).
- **Inno Setup not installed** when trying to produce the real installer (non–dry-run).
- **Secret scan (`check-secrets.ps1 -Strict`)** reports hits outside test paths.

---

## WARNINGS (known limitations testers will see)

- **SmartScreen** on raw EXE and sometimes on `START_HERE.bat` launcher — expected until signing; mitigation is **ZIP + checksums + honest docs**, not bypass.
- **USB topology / mapping** best-effort across OEMs.
- **Pro licensing** not enforced — preview labels only.

---

## MANUAL verification required (human)

| Area | Action |
|------|--------|
| Clean VM | Install from ZIP; uninstall; reinstall |
| Chrome | Download **ZIP** asset only; confirm no stuck `.crdownload` |
| Edge | Same |
| Raw EXE | Advanced path only — capture SmartScreen screens for FAQ updates if new patterns |
| USB2 / USB3 / USB-C / hub | Benchmark + mapping persistence |
| Upgrade | Install older beta then newer over top |
| Logs | Confirm redaction before sharing |

---

## Verdict placeholder

Operator: after local `build-release.ps1 -Version 1.1.12-rc.1` + checksum verification + VM smoke, set final status:

- [ ] **GO** for broader beta
- [ ] **NO-GO** — reason: ___________________
