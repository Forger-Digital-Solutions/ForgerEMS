# What is still missing before human testing — v1.1.10-beta.1

Honest gap analysis for the **v1.1.10-beta.1** hardening pass. This is **test planning**, not a completeness guarantee.

---

## Must fix before wide tester invite

- **SmartScreen & trust:** Unsigned or low-reputation binaries will still friction normal users. Keep the **ZIP + `START_HERE.bat`** path prominent; do not imply “no warnings” without a signed installer.
- **Manual verification on 3+ real PCs:** Different OEMs, USB chipsets, and Windows builds still need eyes on **USB detection**, **benchmark cancel**, **mapping**, and **Ventoy** messaging.
- **Log discipline:** Confirm testers know which logs to zip; automated upload is out of scope.

---

## Should fix soon (next beta)

- **Orchestration overlap:** Core intelligence chain uses a generation token; extend the same discipline to long-running PowerShell scans if double-starts are observed in the wild.
- **First-run diagnostics:** Checklist may be sparse until `diagnostics-latest.json` exists — confirm empty states read clearly.
- **WMI slowness:** Some machines may still show delayed cards; confirm UI does not hard-freeze (timeouts / fallbacks).

---

## Nice to have

- Local-only crash breadcrumbs (no telemetry upload).
- Richer WSL diagnostics without admin.
- Broader theme contrast audit on warning paths.

---

## Known limitations (by design for this beta)

- **No security bypass** guidance in Kyra or UI.
- **USB Intelligence** remains **Pro preview**; licensing **not** enforced.
- **Kyra** offline answers are heuristic; online adds variability.

---

## Manual verification steps (engineer sign-off)

1. Clean VM or spare PC: install from **ZIP → START_HERE.bat**.
2. USB Builder happy path + blocked EFI path.
3. USB benchmark: **removable only**; cancel mid-run; confirm temp cleanup and no benchmark on **C:\\**.
4. System Intelligence: malformed JSON report → **needs attention**, no crash.
5. **Copy** system summary → paste into Notepad; confirm obvious secrets/paths are **redacted** where sanitizer applies.
6. `build-release.ps1 -Version 1.1.10-beta.1` produces **installer**, **ZIP**, **CHECKSUMS.sha256**, **DOWNLOAD_BETA.txt**, **release.json**.

---

## Risk areas

| Area | Risk | Mitigation |
|------|------|------------|
| Installer / ZIP | Wrong asset or checksum mismatch | Verify GitHub assets; compare SHA256 |
| SmartScreen | Tester abandonment | Clear docs; ZIP-first |
| USB safety | Wrong partition | Blocking + banners |
| Benchmark | Media removed mid-run | Cancel + friendly status |
| Privacy | Leak via pasted logs | Redaction + templates |
| Toolkit wording | Manual vs missing confusion | Clear labels + explanation |
| Release notes | Over-promising | Template + checklist links |

---

## Bottom line

**v1.1.10-beta.1** is aimed at **controlled human testing** with technically literate testers who understand beta software, backups, and USB data-loss risk. It is **not** positioned as a fully signed, enterprise release.
