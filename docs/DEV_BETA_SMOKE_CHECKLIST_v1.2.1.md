# ForgerEMS v1.2.1-preview.1 — Dev Beta Smoke Checklist

**Scope:** Confirm the v1.2.1 preview build behaves as expected, with focus on the new managed-download / Download Plan / freshness workflow. Mark each line **OK** / **BLOCKED** / **N/A**. Stop on the first BLOCKED in section 3 or 4 — those are the new-surface gates.

For broader regression coverage, see [FINAL_MANUAL_SMOKE_TEST.md](../FINAL_MANUAL_SMOKE_TEST.md). This checklist is additive, not a replacement.

**Build under test:** `release/current/ForgerEMS-Setup-v1.2.1-preview.1.exe` or the matching ZIP. Single-file portable exe at `release/current/app/ForgerEMS.exe`.

---

## 1. Launch and identity

- [ ] Install (or unzip) the build. Launch ForgerEMS.
- [ ] Title bar reads **ForgerEMS**. Main window opens centered, not off-screen.
- [ ] About / status area shows **1.2.1-preview.1**.
- [ ] Network Pulse header renders. No "Internet: Paused" without an explicit pause.

## 2. Tab smoke

- [ ] USB Builder tab opens. With no USB attached, the no-drive state is clear.
- [ ] Kyra (Beta) tab opens. Send `Hi` — get a response (local or online).
- [ ] System Intelligence tab opens. Status reads without errors.
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

## 6. Freshness governance

- [ ] In the catalog, locate **CrystalDiskInfo 9.8.0**. Confirm it shows **Update available** (or equivalent) — its pinned version stays 9.8.0 by design.
- [ ] All other managed items read **Up to date**.
- [ ] **No** UI text suggests ForgerEMS will auto-upgrade a pinned version.

## 7. Backend health from the bundled scripts

Run from a PowerShell prompt against the installed (or unzipped) `release/current/app/backend` directory:

- [ ] `Get-ForgerEMSToolkitHealth.ps1` runs to completion. It dot-sources the bundled `ChecksumResolver.ps1` (no "Checksum resolver helper was not found" error).
- [ ] `Verify-VentoyCore.ps1` exits 0 with 9/9 PASS.
- [ ] `Verify-VentoyCore.ps1 -RevalidateManagedDownloads` exits 0, reports `30 active managed items, OK 22 / OK-LIMITED 8 / DRIFT 0` (counts may shift in future preview cycles — DRIFT must be 0).

## 8. Release artifact integrity

- [ ] `release/current/CHECKSUMS.sha256` verifies against the on-disk artifacts (use `Get-FileHash -Algorithm SHA256` and compare).
- [ ] `release/current/release.json` reports `version: 1.2.1-preview.1`, `channel: preview`.

## 9. Hard "must not" gates

- [ ] No managed-download action ever wrote to a USB target.
- [ ] No catalog row was silently auto-updated to a new pinned version.
- [ ] No EULA / paid / personal-use item was auto-downloaded.
- [ ] No catalog row with empty checksum metadata was eligible for **Download selected managed**.
- [ ] No "beta", "nightly", or "RC" channel appears outside the explicit preview labeling.

---

If any item in **3**, **4**, or **9** is BLOCKED, do not proceed to wider tester distribution — open a triage ticket against the relevant component first.
