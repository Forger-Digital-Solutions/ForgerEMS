# ForgerEMS v1.1.11-beta.1 — Release notes (draft)

**Channel:** Beta (release candidate for human testing)  
**Recommended download:** **`ForgerEMS-v1.1.11-beta.1.zip`** — extract and run **`START_HERE.bat`**.  
**Advanced users only:** You may run the raw **`ForgerEMS-Setup-v1.1.11-beta.1.exe`** directly; expect possibly stronger **Windows SmartScreen** friction than the ZIP flow.

---

## Highlights

- **First-launch welcome:** Onboarding overlay and safer first-run guidance.
- **USB Intelligence Pro preview:** Clearer workflow copy, benchmark guidance, and mapping flow alongside the USB Builder.
- **USB benchmark / mapping:** Prompts and disabled states when unsafe or while a run is active; removable-only benchmarking.
- **Kyra:** Onboarding and help-style answers improved for USB and beta readiness (offline-first heuristics).
- **System Intelligence, Toolkit Manager, Diagnostics:** Polish on summaries, labels, scroll behavior, and read-only safety messaging.
- **Legal / privacy / FAQ / About / third-party notices:** Documentation aligned with this beta; support contact and beta disclaimers clarified where needed.

---

## Verification

- Compare **`CHECKSUMS.sha256`** for the ZIP, installer, **`release.json`**, and **`DOWNLOAD_BETA.txt`** when published.
- In the ZIP, use the bundled **`VERIFY.txt`** and in-app **About / Legal / Privacy** for support contact.

---

## Known limitations

- **SmartScreen** may warn until code signing and reputation improve.
- **USB topology** and port mapping are **best-effort**; results depend on drivers and hardware.
- **Pro licensing** is **not enforced** in this beta (preview features may appear without purchase).
- Some **WMI / hardware** fields may be unavailable or read “not reported” on certain OEM configurations.
- **Marketplace pricing providers** may be **disabled** if not implemented in this build.

---

## Human testing

- **Quickstart:** `docs/BETA_TESTER_QUICKSTART.md`
- **Checklist:** `docs/BETA_HUMAN_TESTING_CHECKLIST_v1.1.11.md`
- **Gaps:** `docs/MISSING_BEFORE_HUMAN_TESTING_v1.1.11.md`
- **Issue template:** `docs/BETA_ISSUE_REPORT_TEMPLATE.md`

---

## Safe feedback

Attach **sanitized** logs from `%LOCALAPPDATA%\ForgerEMS\logs\` and relevant `Runtime\reports\` JSON per the checklist. Do **not** share product keys, API tokens, or unrelated personal data.
