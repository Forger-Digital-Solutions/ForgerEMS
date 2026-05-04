# What is still missing before human testing — v1.1.9-beta.1

Honest gap analysis for the **v1.1.9-beta.1** beta hardening pass. This is not a promise of completeness; it is a **test planning** document.

---

## Must fix before wide tester invite

- **SmartScreen & trust:** Unsigned or low-reputation binaries will still friction normal users. Document the ZIP + `START_HERE.bat` path clearly; do not promise “no warnings” without a signed installer.
- **Manual verification on 3+ real PCs:** Different OEMs, USB chipsets, and Windows builds still need eyes on **USB detection**, **benchmark safety**, and **Ventoy pre-flight** messaging.
- **Log collection discipline:** Confirm testers know which logs to zip; automated upload is out of scope for this pass.

---

## Should fix soon (next beta)

- **Automation summary visibility:** When `forgerAutomation.summaryLine` is absent, the UI label still shows — consider hiding the heading when empty.
- **Diagnostics freshness:** Checklist depends on `diagnostics-latest.json` existing; first-run experience may show “not found” until intelligence pipelines run.
- **Toolkit filter rename migration:** Users with saved UI state might theoretically hold legacy filter tokens — watch for support tickets.

---

## Nice to have

- Stronger **contrast audit** on every themed control (orange/warning paths).
- **Telemetry-free** crash breadcrumbs (local only) for beta without sending data off-device.
- Richer **WSL** detection in diagnostics without requiring admin.

---

## Known limitations (by design for this beta)

- **No security bypass** helpers: Kyra and UI must not instruct users to disable SmartScreen, Defender, or UAC.
- **USB Intelligence** remains **Pro preview** — UX is polished; licensing enforcement is **not** active.
- **Kyra** offline answers are heuristic; online providers add variability and quota behavior.

---

## Manual verification steps (engineer sign-off)

1. Clean VM or spare PC: install from **ZIP → START_HERE.bat**.
2. Full USB Builder happy path + blocked EFI path.
3. USB benchmark writes **only** to selected removable media (temp files) — confirm in code review + one runtime test.
4. System Intelligence: confirm **no serial** in summary cards after this build.
5. Kyra: spot-check safe context builder with **redaction on** for long tokens.
6. Release artifact: `build-release.ps1 -Version 1.1.9-beta.1` produces **installer**, **ZIP**, **CHECKSUMS.sha256**, **DOWNLOAD_BETA.txt**.

---

## Risk areas

| Area | Risk | Mitigation |
|------|------|------------|
| Installer / ZIP pipeline | Wrong file served or checksum mismatch | Double-check GitHub Release assets; verify hashes |
| SmartScreen | Tester abandonment | Clear docs; no false signing claims |
| USB Builder safety | Wrong partition targeted | Strong blocking + visible banners |
| Benchmark | IO errors on bad media | Guard rails + skip unsafe targets |
| Privacy | Leak via logs | Redaction + avoid verbose WMI dumps in Kyra context |
| Toolkit wording | Confusion on Manual vs Missing | StatusDisplayUi + explanation string |
| Diagnostics | Noise / false warnings | Keep read-only; iterate on thresholds later |
| Release notes | Over-promising | Template updated; checklist linked |

---

## Bottom line

**v1.1.9-beta.1** is suitable for **controlled human testing** with technically literate testers who understand beta software, backup practices, and USB data loss risk. It is **not** positioned as a signed, enterprise-ready release.
