# ForgerEMS beta tester quickstart (v1.2.4)

**Forger Engineering Maintenance Suite** — built by **Forger Digital Solutions**.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) (sanitized logs only — no secrets).

---

## 1. Download the ZIP from GitHub Releases

Get **`ForgerEMS-v1.2.4.zip`** when published. Optionally verify **`CHECKSUMS.sha256`** from the same release. Older `v1.1.12-rc.*` assets remain on prior GitHub tags for history only.

If the browser download behaves oddly, read **[DOWNLOAD_TROUBLESHOOTING.md](DOWNLOAD_TROUBLESHOOTING.md)** and **[FIRST_TESTER_DOWNLOAD_FLOW.md](FIRST_TESTER_DOWNLOAD_FLOW.md)**.

**Do not start with the standalone `.exe`** unless you already know you want the advanced direct installer path.

---

## 2. Extract the ZIP

Use a short path (for example `Desktop\ForgerEMS`).

---

## 3. Run `START_HERE.bat`

Follow the prompts inside the extracted folder. This is the supported entry point.

---

## 4. Install or run ForgerEMS

Complete install if you want a traditional Program Files deployment; otherwise follow your test plan.

---

## 5. Run System Intelligence

Use **Run Standard Scan** (System Intelligence). Wait for completion.

For deeper coverage, use **Run Elevated Scan**. If ForgerEMS is not already running as administrator, it will ask Windows for UAC approval, relaunch itself elevated, and continue the scan automatically. Deep Sensor Mode is read-only local sensor coverage; it does not grant permanent admin permission during install.

---

## 6. Check responsiveness during normal work

ForgerEMS now uses a packaged static command-center background. During testing, focus on launch speed, scrolling, and toolkit checks while USB workflows are running.

## 7. Select USB

Choose a **large removable data** partition (not tiny boot-only slices unless a flow explicitly asks for them).

---

## 8. Run USB benchmark

Use **Run USB Benchmark**. Confirm speeds update or show a clear “not measured” / cancelled state.

---

## 9. Try USB mapping

**Start USB Mapping** → **Capture Current Port** → move the device → **Detect Port Change** → label → **Save Port Label**.

---

## 10. Report issues with safe logs only

Use [BETA_ISSUE_REPORT_TEMPLATE.md](BETA_ISSUE_REPORT_TEMPLATE.md). Attach screenshots and **redacted** log excerpts. **Do not** include passwords, API keys, product keys, serial numbers, or private documents.

Manual QA checklist reference: [PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md](PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md) (v1.2.0 checklist, updated for v1.2.1 where noted in release notes).
