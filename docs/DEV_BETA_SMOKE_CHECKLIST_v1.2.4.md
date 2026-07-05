# ForgerEMS v1.2.4-preview.1 — Dev Beta Smoke Checklist

**Scope:** Confirm the v1.2.4 preview build behaves as expected, with focus on Driver Hub, Dr. Forge Intake, managed-download / Download Plan / freshness workflow, and release packaging consistency. Mark each line **OK** / **BLOCKED** / **N/A**. Stop on the first BLOCKED in section 3 or 4 — those are the new-surface gates.

For broader regression coverage, see [FINAL_MANUAL_SMOKE_TEST.md](../FINAL_MANUAL_SMOKE_TEST.md). This checklist is additive, not a replacement.

**Build under test:** `release/current/ForgerEMS-Setup-v1.2.4-preview.1.exe` or the matching portable ZIP `release/current/ForgerEMS-v1.2.4-preview.1.zip`. Published app staging remains at `release/current/app/ForgerEMS.exe`.

**Trademark / non-endorsement check:** ForgerEMS is independent and is not affiliated with, sponsored by, or endorsed by Microsoft, Linux distributions, hardware vendors, driver vendors, or third-party tools referenced in the app. Names are used only to identify compatibility, official resources, or supported technician workflows.

---

## 1. Launch and identity

- [ ] Install (or unzip) the build. Launch ForgerEMS.
- [ ] Title bar reads **ForgerEMS**. Main window opens centered, not off-screen.
- [ ] About / status area shows **1.2.4-preview.1**.
- [ ] On a clean consent state, the **Terms of Use** first-run gate appears before main tools are usable.
- [ ] Terms, Privacy/Data Handling, Legal Notices, Third-party Notices, and About buttons open readable docs from the gate.
- [ ] Accepting both checkboxes unlocks the app; declining does not unlock it.
- [ ] Settings / About can reopen Terms, Privacy/Data Handling, Legal Notices, and Third-party Notices after acceptance.
- [ ] The removed shell Internet widget/status card is still gone, and **Network Pulse is fully retired** — Settings has no Network Pulse section and no "Internet: Paused" header/widget appears.
- [ ] Settings has no **Forger Sensor Stack / Deep Sensor Mode** section — retired features do not appear anywhere in Settings.
- [ ] Terms gate at 1366x768: both checkbox notices wrap onto multiple lines and are fully readable (no clipped "…and I s..." text). The header shows **Document revision** and **Applies to ForgerEMS** lines.
- [ ] Main background art uses ForgerEMS-owned shield/USB/circuit artwork and generic technician glyphs only; no Windows, Linux distro, Ventoy, Rufus, balenaEtcher, HWiNFO, CrystalDiskInfo, Clonezilla, GParted, SystemRescue, RustDesk, Angry IP Scanner, DriverStoreExplorer, or other third-party logos/icons/marks appear as decorative art.

## 2. Tab smoke

- [ ] Tab strip shows exactly: **USB Builder**, **Port / USB Intelligence**, **Toolkit Manager**, **Driver Hub**, **Kyra (Beta)**, **Settings** — plus the always-visible **Live Logs** side panel.
- [ ] No **System Intelligence** tab and no **Diagnostics** tab are present. (Both moved to **Dr. Forge**, the dedicated diagnostics / hardware-scan companion.)
- [ ] App launches without a post-launch lag spike — no automatic background system scan kicks off on startup.
- [ ] USB Builder tab opens. With no USB attached, the no-drive state is clear.
- [ ] USB Builder Profile includes **ForgerEMS Portable App** by default and routes it to `_apps\ForgerEMS`, docs to `_docs\ForgerEMS`, and support folders to `_logs\ForgerEMS`.
- [ ] Port / USB Intelligence tab opens (it does not depend on the removed Diagnostics tab).
- [ ] Kyra (Beta) tab opens. Send `Hi` — get a response (local or online).
- [ ] Driver Hub tab opens. Header, safety pill, detected-hardware summary, search, filters, recommendation cards, and compact catalog cards render.
- [ ] Settings tab opens.
- [ ] Live Logs side panel shows app activity; **View Full Logs** opens the full-logs overlay.

## 3. Toolkit Manager — new surface

- [ ] Toolkit Manager tab opens. Status pill, readiness score, count pills all render.
- [ ] Search and filters (Status / Category / Family / Architecture / Boot mode / Source trust) populate and apply.
- [ ] DataGrid shows **159** rows (or close to it — depends on host detection state).
- [ ] Each row shows a **Plan** checkbox (leftmost column).
- [ ] Catalog status chips visible per row (e.g. **Official source**, **Community source**, **Manual ISO Required**, **Paid - vendor licence**, **Legacy / Lab Only**).
- [ ] **Update Toolkit** action remains the only download trigger — confirm tooltip wording does not imply auto-update.
- [ ] **Verify Links** runs HTTP HEAD-only probes. No downloads start from this action.
- [ ] In the **Dr. Forge Intake** card, confirm **Local Dr. Forge report preview** is read-only/local-only copy, the recent report selector appears, **Copy Report Summary** and **Open Containing Folder** are present, and no upload, driver install, elevation, fan/voltage/OC, or firmware-control action appears.

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
- [ ] Confirm header copy: "Official driver apps, OEM support, GPU tools, firmware guidance, and Linux driver help." and the safety pill: "Official links only • No auto BIOS flashing • No driver installs without your action".
- [ ] With no system scan report present, **Recommended for this PC** shows its generic state (e.g. "Run System Intelligence to personalize recommendations.") and the full catalog still renders. Personalized hardware detection can come from a packaged **Dr. Forge** CLI report when configured; ForgerEMS no longer runs that scan from its own tab.
- [ ] If a prior `system-intelligence-latest.json` report exists, confirm the detected-hardware card lists OEM, GPU, CPU, Network, and OS when available. (Skip if no report is present — Driver Hub must degrade gracefully to the generic state, not error.)
- [ ] Confirm **Recommended for this PC** shows 3-4 store-style cards: brand tile, app name, vendor, badges, **one** prominent primary action, and a small `⋯` overflow button. No visible **Copy Link** or **Add Shortcut** buttons clutter the primary row.
- [ ] Click `⋯` on a recommended card. The overflow popup shows **Open Page** (only when it differs from the primary action), **Copy Link**, and **Add Shortcut to USB**.
- [ ] Confirm recommendation copy says "Recommended based on detected ..." or similar detected-source wording, not "Needed", "outdated", "latest installed", or "required".
- [ ] Filter **Driver Apps**, **GPU**, **OEM**, and **Linux**. Cards wrap cleanly; the primary CTA and `⋯` overflow stay visible.
- [ ] Use search for `NVIDIA`, `Dell`, `fwupd`, and a nonsense term. The nonsense search shows "No Driver Hub cards match your filter."
- [ ] Click **Get**, **Open Driver Page**, or **Open Support Page** on representative cards. It opens the official vendor/project page only; no installer starts automatically.
- [ ] Click **Open Official Download** where shown. It opens the official app/download page only; it does not download, run, or stage an installer automatically.
- [ ] From the `⋯` popup, click **Copy Link** and verify the clipboard contains the official HTTPS URL.
- [ ] Select a USB target, then open `⋯` and click **Add Shortcut to USB**. Confirm a `.url` appears under a logical `Drivers\...` path and does not overwrite an unrelated existing file.
- [ ] Deselect / remove the USB target and confirm **Add Shortcut to USB** (in the `⋯` popup) is disabled or the UI says "Select a USB target to add Driver Hub shortcuts."
- [ ] Confirm Linux guidance cards use **Open Guidance**, not install/download wording.
- [ ] Confirm BIOS/Firmware cards show "Firmware guidance only" caution text.
- [ ] Confirm brand tiles are text monograms (for example NV, AMD, Intel, Dell, HP, MSI), not bundled vendor logo images.
- [ ] Click **Microsoft Surface Drivers and Firmware**. The official Microsoft Surface drivers and firmware page opens — no 404.
- [ ] (Optional) From PowerShell, run `pwsh tools/Test-DriverHubLinks.ps1`. Confirm there are **no `NotFound` results**. `ForbiddenLikelyOk` / `Timeout` rows are vendor bot-protection noise and are acceptable.
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
- [ ] `release/current/release.json` reports `version: 1.2.4-preview.1`, `channel: preview`.

## 9. Hard "must not" gates

- [ ] No managed-download action ever wrote to a USB target.
- [ ] No catalog row was silently auto-updated to a new pinned version.
- [ ] No EULA / paid / personal-use item was auto-downloaded.
- [ ] No catalog row with empty checksum metadata was eligible for **Download selected managed**.
- [ ] No "beta", "nightly", or "RC" channel appears outside the explicit preview labeling.

---

If any item in **3**, **4**, or **9** is BLOCKED, do not proceed to wider tester distribution — open a triage ticket against the relevant component first.
