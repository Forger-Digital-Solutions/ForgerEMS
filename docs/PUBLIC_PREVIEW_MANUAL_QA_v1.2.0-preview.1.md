# Manual QA — ForgerEMS v1.2.0-preview.1 (Public Preview)

Use this checklist on a **VM or spare PC** before uploading GitHub Release assets. Prerelease software — keep backups; USB work can erase data if mis-targeted.

## 1. Fresh launch

- [ ] Start `ForgerEMS.exe` from `release\current\app` (or installed shortcut).
- [ ] No crash on startup; backend discovery message is reasonable.

## 2. Header / banner version

- [ ] Header shows **ForgerEMS v1.2.0 Public Preview** (or equivalent display line).
- [ ] Public Preview banner line visible under support email strip.

## 3. Settings — App updates

- [ ] **Check for updates now** completes (or fails soft offline with clear copy).
- [ ] **Copy update diagnostics** produces clipboard text without raw secrets.

## 4. Diagnostics — support bundle

- [ ] **Export Support Bundle** saves a `.zip`.
- [ ] ZIP contains `README.txt`, `bundle-metadata.txt`, redacted logs where paths appeared.
- [ ] Optional: run `.\tools\Export-ForgerEMSDiagnostics.ps1` and confirm README + redacted logs.

## 5. Kyra — Provider Hub

- [ ] **Kyra Advanced → Providers** shows env-based hub summary (no API keys in clear text).
- [ ] **Refresh Provider Status** updates without crash.

## 6. Kyra — local answer

- [ ] Run **System Intelligence** scan first (if applicable).
- [ ] Ask a spec-style question; offline/local answer references scan facts when available.

## 7. System Intelligence

- [ ] Scan completes or explains script/backend missing.
- [ ] “Not exposed by Windows” style copy appears where data is missing (no fabricated sensors).

## 8. USB target safety

- [ ] `C:\` / system volumes **not** selectable for destructive USB Builder actions.
- [ ] Removable USB shows safety line; changing selection updates header USB line.

## 9. USB benchmark

- [ ] Benchmark on a **safe removable** target runs or refuses with clear reason.
- [ ] Unsafe `C:\` benchmark path is refused (self-test covers engine; UI spot-check).

## 10. Managed downloads — partial staging

- [ ] If backend reports **PARTIALLY STAGED**, UI uses **partially staged** wording (not “failed” for manual items).
- [ ] **Retry Failed Downloads** visible when applicable.

## 11. Toolkit Manager

- [ ] Manual / info rows read as **expected**, not failures.
- [ ] Refresh health completes.

## 12. About / FAQ / Legal

- [ ] About mentions **Public Preview**, support email, honest maturity labels.
- [ ] FAQ / Legal / Privacy headers match preview positioning.

## 13. Installer (only if `ForgerEMS-Setup-v1.2.0-preview.1.exe` was built)

- [ ] Install completes; app launches.
- [ ] Uninstall removes expected shortcuts (spot-check).
- [ ] Upgrade from prior beta: backup first, then verify version line.

## 14. GitHub Release assets

After `tools\build-release.ps1` **without** `-SkipInstaller` (when Inno is available):

- [ ] `ForgerEMS-v1.2.0-preview.1.zip` and `ForgerEMS-Beta-v1.2.0-preview.1.zip` (if produced).
- [ ] `ForgerEMS-Setup-v1.2.0-preview.1.exe`
- [ ] Root `CHECKSUMS.sha256`, `release.json`, `DOWNLOAD_BETA.txt` as per script output.

## Command-line self-test

From `release\current\app`:

```powershell
.\ForgerEMS.exe --self-test
```

Expect exit code **0** when backend scripts resolve and Kyra/USB safety checks pass.
