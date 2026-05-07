# ForgerEMS beta tester quickstart (Public Preview v1.2.0-preview.1)

**Forger Engineering Maintenance Suite** — built by **Forger Digital Solutions**.

**Support:** [ForgerDigitalSolutions@outlook.com](mailto:ForgerDigitalSolutions@outlook.com) (sanitized logs only — no secrets).

---

## 1. Download the ZIP from GitHub Releases

Get **`ForgerEMS-v1.2.0-preview.1.zip`** or **`ForgerEMS-Beta-v1.2.0-preview.1.zip`** when published (same bundle style; pick one). Optionally verify **`CHECKSUMS.sha256`** from the same release. Older `v1.1.12-rc.*` assets remain on prior GitHub tags for history only.

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

Use **Run Scan** (System Intelligence). Wait for completion.

---

## 6. Select USB

Choose a **large removable data** partition (not tiny boot-only slices unless a flow explicitly asks for them).

---

## 7. Run USB benchmark

Use **Run USB Benchmark**. Confirm speeds update or show a clear “not measured” / cancelled state.

---

## 8. Try USB mapping

**Start USB Mapping** → **Capture Current Port** → move the device → **Detect Port Change** → label → **Save Port Label**.

---

## 9. Ask Kyra

Examples:

- “How do I map USB ports?”  
- “Which port should I use?”  
- “What device are we working on?”  
- “Is my drive NVMe or SATA?” (after System Intelligence)  
- “What RAM type does this machine show?” (after System Intelligence)  
- “What battery do I need?” / “What should I upgrade first?”  
- “How’s BTC doing today?”  
- “What is in the beta quickstart?”  

**Offline Kyra** works immediately — **no API keys** and **no sign-up** for the default beta path. Optional online help exists only when an **operator** has already configured the environment (advanced).

**Kyra Advanced → Realtime Gateway** explains secure research routing: provider keys stay on the ForgerEMS Worker; the app only needs gateway URL + beta token. Use **Check gateway status** for coarse server-side readiness (no secrets returned).

---

## 10. Check Kyra Intelligence Network settings

Open **Settings → Kyra Intelligence**.

- **Local repair memory** lets Kyra remember sanitized repair notes and diagnostic patterns on this PC.
- **Anonymous community intelligence sharing** is off by default.
- **View what would be shared** shows only sanitized preview fields.
- **Export Kyra memory** exports sanitized local Kyra memory.
- **Delete Kyra memory** deletes local Kyra memory after confirmation.

ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.

---

## 11. Report issues with safe logs only

Use [BETA_ISSUE_REPORT_TEMPLATE.md](BETA_ISSUE_REPORT_TEMPLATE.md). Attach screenshots and **redacted** log excerpts. **Do not** include passwords, API keys, product keys, serial numbers, or private documents.

Human testing checklists (prior builds still useful): [BETA_HUMAN_TESTING_CHECKLIST_v1.1.11.md](BETA_HUMAN_TESTING_CHECKLIST_v1.1.11.md).
