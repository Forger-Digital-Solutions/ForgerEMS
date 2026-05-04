# ForgerEMS v1.1.12-rc.1 — Release notes (mechanical RC)

**Channel:** Beta (mechanical / pre-tester release candidate)  
**Recommended download:** **`ForgerEMS-v1.1.12-rc.1.zip`** — extract, then double-click **`START_HERE.bat`**.  
**Advanced users only:** You may run the raw **`ForgerEMS-Setup-v1.1.12-rc.1.exe`** directly; expect **stronger Windows SmartScreen** friction than the ZIP flow.

---

## Why this build exists

Mechanical hardening pass before broader beta: **ZIP-first** messaging, **browser download** guidance (`docs/DOWNLOAD_TROUBLESHOOTING.md`), **semver-safe** installer versioning in `tools/build-release.ps1`, consolidated audits under `docs/*_v1.1.12-rc.1.md`, and regression tests for diagnostics JSON parse failures and documentation gates.

---

## Verification

- Compare **`CHECKSUMS.sha256`** for the ZIP, installer, **`release.json`**, and **`DOWNLOAD_BETA.txt`** when published.
- In the ZIP, use **`VERIFY.txt`** and in-app **About / Legal / Privacy** for support contact.

---

## Known limitations (unchanged from prior beta unless noted)

- **SmartScreen** may warn until code signing and reputation improve.
- **USB topology** and port mapping remain **best-effort**.
- **Pro licensing** is **not enforced** in beta (preview labels are informational).
- Some **WMI / hardware** fields may read “not reported” on certain OEM configurations.

---

## Human testing

- **Quickstart:** `docs/BETA_TESTER_QUICKSTART.md`
- **Checklist (v1.1.11 doc name retained):** `docs/BETA_HUMAN_TESTING_CHECKLIST_v1.1.11.md`
- **Go / No-Go:** `docs/BETA_RC_GO_NO_GO_v1.1.12-rc.1.md`
- **Issue template:** `docs/BETA_ISSUE_REPORT_TEMPLATE.md`

---

## Safe feedback

Attach **sanitized** logs from `%LOCALAPPDATA%\ForgerEMS\logs\` and relevant `Runtime\reports\` JSON per the checklist. Do **not** share product keys, API tokens, or unrelated personal data.
