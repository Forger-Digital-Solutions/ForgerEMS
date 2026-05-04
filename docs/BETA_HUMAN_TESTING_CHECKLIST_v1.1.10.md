# ForgerEMS v1.1.10-beta.1 — Human testing checklist

Use this list on a **real Windows 10/11 PC** with a spare USB you can erase. Capture **screenshots** and **sanitized logs** for anything confusing.

---

## 1. Install / download

1. Download **`ForgerEMS-v1.1.10-beta.1.zip`** from the GitHub Release (**recommended**).
2. Verify checksum using `CHECKSUMS.sha256` when provided.
3. Extract the ZIP to a short path (e.g. Desktop\ForgerEMS).
4. Run **`START_HERE.bat`**.
5. Confirm the installer launches; complete install if desired.
6. Open the app: window should appear **centered** (normal and maximized).
7. Confirm **version / footer** shows **v1.1.10-beta.1** (or equivalent informational version).
8. In **Settings**, open **About**, **FAQ**, **Legal**, and **Privacy** — confirm text loads and mentions support email and beta notice.

**Advanced:** Running the raw installer EXE directly is supported but may show stronger SmartScreen friction; prefer the ZIP flow for first contact.

---

## 2. USB Builder

1. Click **Refresh** on USB targets.
2. Confirm unsafe targets (EFI / VTOYEFI / tiny boot) show **blocking** warnings.
3. Select a **large data partition** on removable media.
4. Confirm **Selected USB target** banner and warning strip read clearly.
5. Run a **pre-flight** path (e.g. Ventoy prepare if you use it) and confirm prompts are non-destructive until you confirm.
6. Confirm **`%LOCALAPPDATA%\ForgerEMS\logs\`** receives entries (e.g. startup / USB-related).
7. Confirm **no crash** when switching targets and idle.
8. Rapidly change USB selection: benchmark should **cancel cleanly** (no hang; status may show cancelled / not measured).

---

## 3. USB Intelligence (Pro preview)

1. With a USB selected, open **USB Intelligence Pro** (preview).
2. Read **guidance** and workflow copy; primary fields should avoid vague “unknown” where a measurement is expected.
3. Run **Run USB Benchmark**; confirm completion and that benchmark fields update; confirm **system / fixed drives** are not benchmarked.
4. **Start USB Mapping** → **Capture Current Port** → move stick → **Detect Port Change** → enter label → **Save Port Label**.
5. In **Kyra**, ask: **“Which port should I use?”** — answer should reflect benchmark/mapping when data is missing.

---

## 4. System Intelligence

1. Run **System Scan** (**Run Scan**).
2. Confirm **Automation summary** line appears when merged automation JSON includes `summaryLine`.
3. Confirm **one-line summary** has **no serial / service tag / product key** in the system card.
4. Artificially use a **bad** `system-intelligence-latest.json` (invalid JSON) if testing: UI should show **needs attention**, not crash.
5. Artificially use an **old** report (>7 days): confirm **stale** banner copy.

---

## 5. Toolkit Manager

1. Run **Refresh Health**.
2. Confirm tiles: **Managed Ready**, **Managed Missing**, **Managed updates**, **Verification issues**, **Manual Required**.
3. Confirm **Manual Required** tools are labeled clearly — not confused with generic “missing” where manual is intended.
4. Read **Manual Required** explanation text.

---

## 6. Diagnostics

1. After scans/toolkit runs, open **Diagnostics**.
2. Confirm **unified health checklist** lists items with readable status labels.
3. Confirm suggestions are **read-only** (no hidden destructive repair).

---

## 7. Kyra prompts (copy/paste)

- “How do I map USB ports?”
- “Why is my USB slow?”
- “Which port should I use?”
- “Is this USB good for Ventoy?”
- “What’s missing before beta testing?”
- “What should I upgrade on this PC?”

Expect **short answer → likely cause → next step** tone for local/offline mode; no raw PNP IDs, private paths, keys, or serials in answers.

---

## 8. Logs & reports to attach to feedback

From **`%LOCALAPPDATA%\ForgerEMS\logs\`** (paths may vary slightly by build):

- `startup.log`
- `system-intelligence.log` (if present)
- `usb-intelligence.log` (if present)
- `diagnostics.log`
- `toolkit-manager.log` (if present)
- `update-check.log` (if present)

From **`%LOCALAPPDATA%\ForgerEMS\Runtime\reports\`**:

- Latest `system-intelligence-latest.json`
- `usb-intelligence-latest.json`
- `toolkit-health-latest.json`
- `diagnostics-latest.json`

**Do not** post product keys, tokens, or personal identifiers.

---

## 9. Known beta limitations

- **SmartScreen** may warn until signing and reputation improve.
- **USB topology** is best-effort without benchmarks and mapping.
- **Pro licensing** is not enforced in this beta.
- **Paid API providers** are optional; do not assume unlimited quotas.
- Some **WMI / hardware fields** may read “not reported” depending on OEM drivers and permissions.
