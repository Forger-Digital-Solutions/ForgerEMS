# ForgerEMS v1.2.3-preview.1 — Dev Beta Smoke Checklist

**Scope:** Confirm the v1.2.3 preview build behaves as expected, with focus on Driver Hub plus the managed-download / Download Plan / freshness workflow. Mark each line **OK** / **BLOCKED** / **N/A**. Stop on the first BLOCKED in section 3 or 4 — those are the new-surface gates.

For broader regression coverage, see [FINAL_MANUAL_SMOKE_TEST.md](../FINAL_MANUAL_SMOKE_TEST.md). This checklist is additive, not a replacement.

**Build under test:** `release/current/ForgerEMS-Setup-v1.2.3-preview.1.exe` or the matching ZIP. Single-file portable exe at `release/current/app/ForgerEMS.exe`.

---

## 1. Launch and identity

- [ ] Install (or unzip) the build. Launch ForgerEMS.
- [ ] Title bar reads **ForgerEMS**. Main window opens centered, not off-screen.
- [ ] About / status area shows **1.2.3-preview.1**.
- [ ] Network Pulse header renders. No "Internet: Paused" without an explicit pause.

## 2. Tab smoke

- [ ] USB Builder tab opens. With no USB attached, the no-drive state is clear.
- [ ] Kyra (Beta) tab opens. Send `Hi` — get a response (local or online).
- [ ] System Intelligence tab opens. Status reads without errors.
- [ ] Driver Hub tab opens. Header, safety note, search, filters, and catalog cards render.
- [ ] Logs tab opens. Filter dropdown populated.

## 3. Toolkit Manager — new surface

- [ ] Toolkit Manager tab opens. Status pill, readiness score, count pills all render.
- [ ] Search and filters (Status / Category / Family / Architecture / Boot mode / Source trust) populate and apply.
- [ ] DataGrid shows **159** rows (or close to it — depends on host detection state).
- [ ] Each row shows a **Plan** checkbox (leftmost column).
- [ ] Catalog status chips visible per row (e.g. **Official source**, **Community source**, **Manual ISO Required**, **Paid - vendor licence**, **Legacy / Lab Only**).
- [ ] **Update Toolkit** action remains the only download trigger — confirm tooltip wording does not imply auto-update.
- [ ] **Verify Links** runs HTTP HEAD-only probes. No downloads start from this action.

## 4. Download Plan and selected managed downloads

- [ ] Select one managed item (e.g. **Rufus 4.14 Portable**) via the Plan checkbox.
- [ ] Select one manual item (e.g. **MediCat Download Page** or any **Windows 11 ISO** entry).
- [ ] Click **Add selected** (or confirm plan rebuilds automatically). Plan grid populates.
- [ ] Plan grid columns: # · Item · Section · Need · Size · Checksum · Freshness — all readable.
- [ ] Managed row's **Section** reads "Ready to download"; manual row's **Section** reads "Manual required".
- [ ] Manual row's **Need** reflects the reason (e.g. "Paid/manual", "Legacy/lab only", "Manual ISO required").
- [ ] **Review update** button on a row whose Freshness is `Update available` opens the **vendor / release page only**. No manifest pin is touched, no download starts.
- [ ] **Download selected managed** kicks off only managed items; manual items remain blocked. (For a true offline smoke, you may skip clicking this — but if you do click it, expect Pending → Downloading → VerifyingChecksum → Completed on at least one small item; cancel mid-stream to also smoke **Cancel**.)
- [ ] **Cancel** clears in-flight items; **Retry failed** is enabled only when a previous run failed.

## 5. Workspace profile

- [ ] Load one of the built-in profiles (e.g. **Windows Recovery USB** or **Linux Admin Pack**) from the **Workspace Profile** dropdown. Plan grid re-flows accordingly.
- [ ] **Save** with a custom name; relaunch the app; **Load** the saved profile back; selections restore.

## 5b. Driver Hub

- [ ] Open **Driver Hub**.
- [ ] Confirm header copy: "Official driver tools, OEM support links, firmware helpers, and GPU utilities." and the safety note that ForgerEMS does not auto-flash BIOS/firmware or install drivers without user action.
- [ ] With no System Intelligence report, **Recommended for this PC** says: "Run System Intelligence to personalize recommendations."
- [ ] Run **System Intelligence**, return to Driver Hub, and confirm recommendations say "Recommended based on detected vendor/GPU" or similar detected-source wording, not "Needed", "outdated", or "latest installed".
- [ ] Filter **GPU**, **OEM**, and **Linux**. Cards wrap cleanly and buttons remain visible.
- [ ] Use search for `NVIDIA`, `Dell`, `fwupd`, and a nonsense term. The nonsense search shows "No Driver Hub cards match your filter."
- [ ] Click **Open Official Page** on one card. It opens the vendor/project page only; no download or installer starts automatically.
- [ ] Click **Copy Link** and verify the clipboard contains the official HTTPS URL.
- [ ] Select a USB target, then click **Add Shortcut to USB**. Confirm a `.url` appears under a logical `Drivers\...` path and does not overwrite an unrelated existing file.
- [ ] Deselect / remove the USB target and confirm **Add Shortcut to USB** is disabled or reports "Select a USB target first."
- [ ] Confirm logs do not expose service tags, serials, private paths, API keys, or query strings with device identifiers.

## 6. Freshness governance

- [ ] In the catalog, locate **CrystalDiskInfo 9.8.0**. Confirm it shows **Update available** (or equivalent) — its pinned version stays 9.8.0 by design.
- [ ] All other managed items read **Up to date**.
- [ ] **No** UI text suggests ForgerEMS will auto-upgrade a pinned version.

## 7. Backend health from the bundled scripts

Run from a PowerShell prompt against the installed (or unzipped) `release/current/app/backend` directory:

- [ ] `Get-ForgerEMSToolkitHealth.ps1` runs to completion. It dot-sources the bundled `ChecksumResolver.ps1` (no "Checksum resolver helper was not found" error).
- [ ] `Verify-VentoyCore.ps1` exits 0 with 9/9 PASS.
- [ ] `Verify-VentoyCore.ps1 -RevalidateManagedDownloads` exits 0, reports `30 active managed items, OK 22 / OK-LIMITED 8 / DRIFT 0` (counts may shift in future preview cycles — DRIFT must be 0).

## 7b. Drive Validator + Drive Validator Wizard (USB Builder)

> **USB Builder tab is intentionally compact.** Drive validation, USB benchmark, and port mapping all run from their wizards now — the main tab only shows summary cards.

### Compact summary card (USB Builder tab)

- [ ] The compact Drive Validator card renders inside the USB Builder tab with header + status pill, the **Target**, **Last check** (status · age), **Result** summary, and a single **Open Drive Validator** button.
- [ ] The USB Builder Drive Validator card has **no inline Start validation button**, **no validation mode dropdown**, **no progress bar / phase text**, and **no Evidence / details expander** — those live in the Drive Validator Wizard.
- [ ] With no USB target selected, the card shows "Not validated" / "—" / "Select a USB target to validate." and **Open Drive Validator** still opens the wizard so the technician can choose a target there.
- [ ] After a successful Quick run on D:\, the compact card shows "Passed" / "just now" within a few seconds. The card never gets stuck on a "CleaningUp" / 92% state because the heavy progress bar is no longer on the main tab at all.

### Drive Validator Wizard

- [ ] Clicking **Open Drive Validator** launches the Drive Validator Wizard window (not a placeholder message).
- [ ] Step 1 (Select target) preselects the currently selected USB Builder drive when it is in the list and safe.
- [ ] Selecting a system / EFI / VTOYEFI / boot partition shows "Blocked: …" with the safety reason, and **Next: Choose mode** is disabled.
- [ ] If the selected target's port is unmapped, the hint "Port mapping is ambiguous — run USB Mapping Wizard for better port evidence." is visible.
- [ ] Step 2 (Choose mode) lists all four modes with descriptions and heaviness. **Destructive Full Media Validation** is shown as "not available in this build" and the start command refuses it.
- [ ] Step 3 (Safety review) explains where temp files live (`.forgerems-drive-validator`), that safe modes do not format and do not delete user files, and that results are advisory evidence. Full Free-Space requires the acknowledgement checkbox before **Start validation** is enabled.
- [ ] Step 4 (Running) updates the phase text, progress bar, elapsed time, and the media integrity tile map in real time. Tiles flip from gray (planned) → blue (active) → green (passed) / yellow (warning) / red (mismatch/alias/I-O). Hover shows region evidence in the tooltip.
- [ ] If a long I/O phase stalls for >10 s, the heartbeat line reads "Still writing… (Ns)" / "Still verifying… (Ns)" / "Still waiting on drive I/O… (Ns)" so the wizard never looks frozen.
- [ ] **Cancel** during the running step stops the run; the result step reports Cancelled with the leftover cleanup status.
- [ ] Step 5 (Results) renders the verdict ("No issues found in sampled validation" / "Passed with warnings" / "Suspicious capacity behavior detected" / "Failed verification — do not trust this drive for a toolkit"), per-region evidence, identity confidence, and a limitations paragraph appropriate to the mode that ran.
- [ ] **Copy summary** writes a plain-text technician report to the clipboard. Verify the report contains mode, status, region map summary, identity confidence, and the limitations paragraph; verify it contains no "genuine", "certified", "100%", or "NAND" claims.
- [ ] **Run another mode** and **Choose another target** return to the appropriate earlier step without restarting the wizard.
- [ ] **Open USB Mapping Wizard** (visible on Step 1 and Step 5) routes through to the existing USB Mapping Wizard.

### Result propagation

- [ ] After a wizard run, the compact USB Builder card reflects the new status and age without restarting the app.
- [ ] After a Failed wizard run, **Setup USB**, **Update USB**, and **Install / Update Ventoy** prompt with the Drive Validator warning and ask to continue.
- [ ] After a Not-Validated state, those same actions prompt the non-blocking recommendation.
- [ ] Mapped USB port card / diagnostics show the Drive Validator status as a separate item (Not validated / OK / Warnings / Failed), distinct from the speed/benchmark result. A history older than 30 days renders as "stale".

## 7c. USB Intelligence Pro compact card + USB Mapping Wizard

### Compact summary card (USB Builder tab)

- [ ] The USB Intelligence Pro card on USB Builder is a single compact summary: header + confidence pill, **Target**, **Class**, **Speed** (write · read, or "Read verification needed" when read is cache-suspected), **Port** (mapped label or "Ambiguous — mapping recommended"), and **Recommendation**.
- [ ] The card has **no inline Run USB Benchmark button**, **no Cancel Benchmark button**, **no "Advanced: inline port mapping (legacy)" expander**, no confidence detail paragraph, no benchmark age row — the wizard owns those.
- [ ] The only primary action is **Open USB Mapping Wizard**, with the hint "Run benchmarks and port mapping from the wizard."
- [ ] When the last successful benchmark had read cache suspected, the **Speed** line reads "Write verified: … MB/s · Read ignored: cache suspected · Rerun recommended" (no inflated trusted MB/s read).

### USB Mapping Wizard reachability

- [ ] Clicking **Open USB Mapping Wizard** opens the existing USB Mapping Wizard window.
- [ ] The wizard's capture-current-port / detect-port-change / save-label flow still works end-to-end after the inline legacy controls were removed from the USB Builder tab.

### USB Builder tab layout

- [ ] After both cards are compact, the Ventoy section and the Actions row (Verify / Setup USB / Update Toolkit / Rename Drive / Refresh Backend) are visible without scrolling past large technical blocks.

### Safety must-nots (Drive Validator)

- [ ] Drive Validator never wrote outside `.forgerems-drive-validator\` on the chosen USB target.
- [ ] Drive Validator never deleted a pre-existing user file. (Verify by leaving a junk file in `.forgerems-drive-validator\` before a run — it remains afterward.)
- [ ] Destructive Full Media was not available, not selectable, and not triggered.

## 8. Release artifact integrity

- [ ] `release/current/CHECKSUMS.sha256` verifies against the on-disk artifacts (use `Get-FileHash -Algorithm SHA256` and compare).
- [ ] `release/current/release.json` reports `version: 1.2.3-preview.1`, `channel: preview`.

## 9. Hard "must not" gates

- [ ] No managed-download action ever wrote to a USB target.
- [ ] No catalog row was silently auto-updated to a new pinned version.
- [ ] No EULA / paid / personal-use item was auto-downloaded.
- [ ] No catalog row with empty checksum metadata was eligible for **Download selected managed**.
- [ ] No "beta", "nightly", or "RC" channel appears outside the explicit preview labeling.

---

If any item in **3**, **4**, or **9** is BLOCKED, do not proceed to wider tester distribution — open a triage ticket against the relevant component first.
