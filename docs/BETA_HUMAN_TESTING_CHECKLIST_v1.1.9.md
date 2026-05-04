# ForgerEMS v1.1.9-beta.1 — Human testing checklist

Use this list on a **real Windows 10/11 PC** with a spare USB you can erase. Capture **screenshots** and **logs** for anything confusing.

---

## 1. Install / download

1. Download **`ForgerEMS-v1.1.9-beta.1.zip`** from the GitHub Release (recommended).
2. Verify checksum using `CHECKSUMS.sha256` when provided.
3. Extract the ZIP to a short path (e.g. Desktop\ForgerEMS).
4. Run **`START_HERE.bat`**.
5. Confirm the installer launches; complete install if desired.
6. Open the app: window should appear **centered** (normal and maximized).
7. Confirm **version / footer** shows **v1.1.9-beta.1** (or equivalent informational version).

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

---

## 3. USB Intelligence (Pro preview)

1. With a USB selected, open **USB Intelligence Pro**.
2. Read **guidance** and **numbered workflow**; confirm copy is actionable (no vague “Unknown” for primary fields).
3. Run **USB Benchmark**; confirm completion and that **Benchmark** / **Benchmark age** update.
4. Tap **Start USB Mapping** → **Capture Current Port** → move stick → **Detect Port Change** → enter label → **Save Port Label**.
5. In **Kyra**, ask: **“Which port should I use?”** — answer should mention benchmark/mapping when data is missing.

---

## 4. System Intelligence

1. Run **System Scan**.
2. Confirm **Automation summary** line appears when merged automation JSON includes `summaryLine`.
3. Confirm **one-line summary** has **no serial / service tag / product key**.
4. Artificially use an **old** `system-intelligence-latest.json` (>7 days) if testing stale UI: confirm **stale banner** copy.

---

## 5. Toolkit Manager

1. Run **Refresh Health** (or equivalent).
2. Confirm tiles: **Managed Ready**, **Managed Missing**, **Managed updates available**, **Verification issues**, **Manual Required**.
3. Confirm **Manual Required** tools show as **Manual required** in the grid — not as generic “missing”.
4. Read **Manual Required** explanation text.

---

## 6. Diagnostics

1. After scans/toolkit runs, open **Diagnostics**.
2. Confirm **Unified health checklist** lists items with **OK / Warning / Blocked / Unknown** style labels.
3. Confirm sections implied by items: system scan, USB, toolkit, WSL, network as applicable.
4. Confirm suggestions are **read-only** (no destructive repair buttons).

---

## 7. Kyra prompts (copy/paste)

- “How do I map USB ports?”
- “Why is my USB slow?”
- “Which port should I use?”
- “Is this USB good for Ventoy?”
- “What’s missing before beta testing?”
- “What should I upgrade on this PC?”

Expect **short answer → likely cause → next step** tone for local/offline mode; no raw PNP IDs, paths, keys, or serials in answers.

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
- **Marketplace / paid API providers** remain optional; free pool is capped and may be disabled in your build.
- Some **WMI / hardware fields** may read “not reported” depending on OEM drivers and permissions.
