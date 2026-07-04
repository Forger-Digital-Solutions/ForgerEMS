# Changelog

## v1.2.4-preview.1 — Public Preview (2026-07-03)

### Dr. Forge CLI Integration Bridge

- **Dr. Forge Intake bridge:** added a local packaged-CLI bridge for `drforge.exe` with app-local discovery, explicit path selection, manifest/checksum inspection, timeout-bounded process execution, and structured readiness/report/archive states.
- **Report rendering:** ForgerEMS parses `forge-hardware-intake-report/1.0` enough to show platform, safety mode, report schema, available readings, findings, notes, and deep telemetry gaps. Null and missing readings remain **Unavailable**, never zero.
- **Toolkit Manager UI:** added Dr. Forge Intake actions for Select CLI, Check Package, Generate Report, Generate Archive, Open Report Folder, and Copy Summary, with copy that avoids full hardware-monitor parity claims.
- **Support bundle/privacy:** Dr. Forge report/archive files stay local and are included in support bundles only when generated from the app or explicitly selected. Docs warn users to review reports before sharing.
- **Version bump:** app/project metadata, installer defaults, release tooling, validation defaults, docs, release notes, smoke checklist, and version-pinned tests moved from `1.2.3-preview.1` to `1.2.4-preview.1`. Windows assembly/file version is `1.2.4.0`.

### Dr. Forge integration readiness pass

- **Driver-artifact packaging guards:** `tools/build-release.ps1` now fails the build if any `*.sys` / `*.inf` / `*.cat` file reaches the staged app, bundled backend, or portable ZIP package (`Assert-NoDriverArtifacts`), and `tools/Validate-ForgerEMSRelease.ps1` adds `driver-artifacts` / `zip-driver-artifacts` FAIL rows for release output and shipped ZIP entries. ForgerEMS ships no kernel driver; Dr. Forge driver support stays dev-foundation / contract-first only.
- **Driver-status contract alignment:** ForgerEMS now probes and conservatively parses Dr. Forge `sensors driver-status --json` schema `forger-sensor-driver-preflight/1.1`; the safe no-driver/user-mode fallback state is treated as normal, older CLIs without driver-status remain usable, and driver-required readings stay unavailable rather than fabricated.
- **Safety regression tests:** new `DrForgeIntegrationSafetyTests` pin that app, packaging, and installer sources contain no driver install/start/load verbs (`sc create`, `sc start`, `pnputil`, `devcon`, `NtLoadDriver`, `ZwLoadDriver`, `SeLoadDriverPrivilege`), the shell offers no driver-install / run-as-admin buttons, driver absence renders as the normal user-mode state (never an error), unknown intake-report fields parse conservatively, USB Builder stages no Dr. Forge or driver payloads, and the packaging guards stay in place.
- **Readiness doc:** added `docs/integrations/DR-FORGE-INTEGRATION-READINESS.md` summarizing what ForgerEMS can do with Dr. Forge today, what remains unavailable, report privacy/support-bundle behavior, enforced safety boundaries, and the gated future integration path.

## v1.2.3-preview.1 — Public Preview (2026-05-23)

### Settings polish pass (release-ready)

- **Settings "What's included" summary:** replaced the "Public Preview — feature maturity" development checklist (Beta/Planned/Experimental labels, retired System Intelligence entry, marketplace-valuation and internet-speed-test placeholders, telemetry/crash-reporting environment variables) with a short customer-facing list of the features that actually ship: USB Builder, Toolkit Manager, Driver Hub, Port / USB Intelligence (Mapping Wizard, Drive Validator, USB Benchmark), Battery Health & System Specifications, and Kyra Assistant. Removed the "Beta honesty", "Preview build", and "Some coverage varies" chips.
- **Kyra section simplified:** the Settings card is now **Kyra Assistant (Beta)** with four short bullets (local-first, cloud providers optional, nothing uploaded automatically, review exports before sharing) instead of a long privacy paragraph duplicated from the Privacy document. "Community Intelligence Sharing: preview/off by default" is now "(off by default)" and its helper text says community upload is not active in this release.
- **App Updates card:** removed the "Update status" / "Include prereleases" text lines that duplicated the status chips at the top of the card.
- **Stale wording sweep:** in-app About/FAQ/Legal, published docs, and diagnostics strings no longer say "Dev Beta", "where stubbed", "experimental WSL helpers", "pricing provider stub", or "disabled for beta stability"; the retired Diagnostics-tab FAQ entry was removed, and doc navigation references now point at Settings → Kyra Assistant.

### Final cleanup pass

- **Terms / consent gate polish:** the first-run required agreement and sharing notice now wrap inside the checkbox content at 1366x768, and the gate separates the document revision from the app version it applies to.
- **USB hotplug refresh:** `WM_DEVICECHANGE` refreshes now use a 250 ms debounce and reuse the same USB enumeration for the visible target list, so plug/unplug changes surface in about a second without a polling loop.
- **Port / USB Intelligence dashboard:** the tab now starts with connected USB devices, then Mapping Wizard / Benchmark / Drive Validator actions, latest result cards for each workflow, and a safe battery health + system specs summary.
- **Safe PC/laptop boundary:** Port / USB copy was narrowed to battery health, system specs, local device context, drive validation, and USB mapping. Broad PC diagnostics, deep-scan, hardware-stress, thermal-probing, and fan-probing wording is guarded against returning.
- **Settings cleanup:** retired Network Pulse and Deep Sensor Mode / Deep Sense settings sections were removed from Settings, old persisted settings/report files are ignored, support bundles no longer collect stale Network Pulse reports, and the dormant Network Pulse implementation/test suite was removed so source scans cannot mistake it for an active feature.

### Final owner handoff pass

- **Terms / consent gate:** added project-provided Terms of Use, Privacy/Data Handling, Legal Notices, User Consent Flow, and in-app first-run Terms acceptance before the main tools unlock. Acceptance is stored locally with terms version, timestamp, app version/build, and Terms hash.
- **Support/Kyra export consent:** exporting Kyra memory, Kyra Intelligence memory, or support bundles now shows a separate review-before-sharing confirmation. General Terms acceptance is not treated as consent to package or share local context.
- **Installer and portable packaging:** Inno Setup now has a Terms/license page and includes the current docs. `tools/build-release.ps1` now produces `ForgerEMS-v1.2.3-preview.1.zip` as a true portable app ZIP with `ForgerEMS.exe`, backend/runtime content, docs, `START_HERE.bat`, `VERIFY.txt`, release metadata, and checksums.
- **USB Builder portable profile:** added a default **ForgerEMS Portable App** USB Builder profile routed to `_apps\ForgerEMS`, `_docs\ForgerEMS`, and `_logs\ForgerEMS`, with picker/selectors/docs/tests updated.
- **Docs / release notes:** README, FAQ, About, install/download docs, release readiness, GitHub release body workflow, and `docs/RELEASE_NOTES_v1.2.3-preview.1.md` now describe preview limitations, Driver Hub/vendor-first boundaries, retired Network Pulse / Deep Sensor settings, removed Internet widget, Live Logs cleanup, Port / USB dashboard results, and manual validation expectations.
- **Tests / validation gates:** added consent, docs, installer/license, portable package, and USB Builder portable-profile coverage; release validator now checks current legal docs and portable ZIP contents.

### Shell simplification pass (Dr. Forge handoff)

- **System Intelligence tab removed** and **Diagnostics tab removed** from the main ForgerEMS shell. Full diagnostics, system/hardware intelligence, elevated scan flows, and the link/file safety checkers are moving to **Dr. Forge**, a dedicated technician companion app. The tab strip now shows exactly: USB Builder, Port / USB Intelligence, Toolkit Manager, Driver Hub, Kyra (Beta), Settings — plus the always-visible Live Logs side panel.
- **No automatic system scan on launch:** startup no longer schedules the background ("quiet") System Intelligence PowerShell scan that ran on every launch — the main cause of post-launch lag. USB/port intelligence still refreshes on demand when a target is selected, and cached reports still hydrate from disk. The System Intelligence / Diagnostics services are kept dormant (never started automatically) for any remaining feature or support-bundle behavior that calls them explicitly.
- **Dr. Forge home on Toolkit Manager:** the still-needed **Create Support Bundle** export and an honest, read-only **Learn about Dr. Forge** roadmap link were relocated from the removed System Intelligence tab onto a new Toolkit Manager card ("Dr. Forge diagnostics will be available here as a dedicated download"). No fake download/installer button is shown.
- **Welcome Center:** the "Run Windows Inventory Scan" quick action and its system-scan recommendation were removed; the suggested flow now covers USB Builder + USB Benchmark only.
- **Docs / tests:** README feature table, `DEV_BETA_SMOKE_CHECKLIST_v1.2.3.md` (tab smoke + Driver Hub), and `FINAL_MANUAL_SMOKE_TEST.md` updated for the new tab list and Dr. Forge handoff. UI/tab tests updated to assert the six-tab strip and to guard against the System Intelligence / Diagnostics tabs (and their sidebar buttons / Welcome scan action) returning.
- **Duplicate Live Logs tab removed:** a leftover **Live Logs** TabItem and its `NavLiveLogsButton` sidebar entry duplicated the always-visible Live Logs side panel / View Full Logs overlay. There is now exactly one live-logging surface — the side panel (with the same Copy logs / Clear logs / filter / auto-scroll / support actions still reachable via **View Full Logs**). UI tests updated to assert the tab is gone and does not return.
- **USB Builder Profile item picker fixed:** clicking a category card's picker button opened nothing. Root cause: `CategoryBuilderWindow.xaml` bound `<Run Text="{Binding SourceDisplay}" />`, and `Run.Text` is TwoWay-by-default while `SourceDisplay` is a read-only projection — so realizing the item rows threw `XamlParseException` ("a TwoWay or OneWayToSource binding cannot work on the read-only property 'SourceDisplay'"), which the global dispatcher handler swallowed, leaving the picker to silently fail. Added `Mode=OneWay` (the sibling summary Runs already had it). The picker now opens for every category, wired to the correct category + item list; Apply commits the selection back to the card (updating selected count, managed/manual labels, USB-space estimate, and the build manifest selectors) and Cancel discards changes.
- **Picker button polish:** relabelled the heavier **"Choose items"** to a compact secondary **"Pick items"** (`UsbBuilderCategoryPickerButtonStyle`) so it no longer dominates the card and stays aligned/readable at 1366×768. Its enabled state is now refreshed on busy-state changes (`RaiseCommandStates`) so it no longer goes stale relative to `IsBusy`.
- **Picker regression tests:** added a runtime STA test that shows the picker and forces layout (reproduces the exact read-only-binding crash before the fix), a XAML/reflection contract test guarding the OneWay binding, VM-level tests for per-category wiring / selection count / Cancel-does-not-mutate / build-manifest selectors, a busy-state refresh test, and a UI contract test for the "Pick items" wording and compact styling.

### Dev Beta hardening pass

- **System Intelligence main tab:** continues to expose only the three primary actions — Elevated Scan, Open Files, Create Support Bundle. Open Files presents a chooser for JSON report, Markdown report, or reports folder. Old duplicate buttons (Standard Scan, Restart as Administrator, Copy Admin Command, Copy Quick Summary, Open JSON/Markdown, Refresh Results, duplicate Hardware X-Ray entries) stay removed from the main tab.
- **Elevation handoff:** Elevated Scan UAC handoff closes the non-elevated ForgerEMS instance after a successful relaunch so the elevated process owns the scan. UAC cancellation, blocked elevation, and runaway exit codes are surfaced as friendly status text; raw codes remain in advanced diagnostics only.
- **USB Builder main tab:** Drive Validator and USB Intelligence Pro continue as small summary cards with **Open Drive Validator** / **Open USB Mapping Wizard** as the only entry points; both wizards still drive the actual validation/intelligence work for clear progress and safe completion.
- **User-Agent strings:** Link-checker and quarantine-download HTTP `User-Agent` headers now bind to `AppReleaseInfo.Version` so they cannot drift behind the package version again.
- **Version bump:** Assembly, csproj, AppReleaseInfo, installer `.iss`, backend Identifier strings, and shipping docs updated from `1.2.1-preview.1` to `1.2.3-preview.1`. CHANGELOG retains all prior entries.
- **Docs / disclosures:** README, BETA_RELEASE_CHECKLIST, RELEASE_PROCESS, BETA_TESTER_QUICKSTART, FIRST_TESTER_DOWNLOAD_FLOW, DOWNLOAD_TROUBLESHOOTING, FAQ, UPDATE_SYSTEM, marketing docs, smoke checklist (renamed `DEV_BETA_SMOKE_CHECKLIST_v1.2.3.md`), and tooling scripts refreshed to `v1.2.3`. Required Dev Beta disclosures (beta technician-assist software, local-first diagnostics, Unknown/NotExposed sensor honesty, redacted support bundles, no auto-upload, Ventoy/third-party tools under their own licenses, no guaranteed repair/recovery) remain in place across in-app About / FAQ / Legal / Privacy and the docs bundle.
- **Tests:** `AppReleaseInfoTests`, `BetaDocumentationTests`, `InfoDocumentTextsTests`, `DeepSensorDisclosureCopyTests`, `KyraForgerEmsReleaseAnswerTests`, `UserAgentHeaderProbeTests`, and `DriveValidatorDocsTests` updated to assert v1.2.3 strings and the renamed smoke checklist. New disclosure assertions added (see `BetaDocumentationTests`).
- **Packaging:** Not rebuilt in this pass. `release/current/` artifacts still describe `1.2.1-preview.1`; rerun `tools/build-release.ps1 -Version 1.2.3-preview.1` (and supporting backend bundle scripts) to refresh installer/ZIP/release.json/CHECKSUMS.sha256 before publishing.

---

## v1.2.1-preview.1 — Public Preview (2026-05-08)

### Elevated Scan and LibreHWM pass

- **Standard Scan / Elevated Scan split:** System Intelligence now offers a Standard Scan (no admin required) and an optional Elevated Scan that requests Windows administrator access for deeper coverage.
- **ElevatedProcessTimedOut wired:** when the elevated helper process does not respond within the configured timeout, ForgerEMS now surfaces `ElevatedProcessTimedOut` as a first-class result instead of propagating a raw exit code.
- **Friendly UAC/admin handoff messaging:** raw exit code `-196608` (0xFFFD0000) is no longer the primary user-facing error. If Windows, UAC, SmartScreen, execution policy, or endpoint security blocks the admin handoff, the app shows a descriptive explanation. Raw codes appear only in advanced diagnostics and logs.
- **Restart as Administrator / Copy Admin Command:** optional helpers surfaced in UI for environments where UAC launch cannot be automated. Both are labeled as beta diagnostics and not presented as required steps.
- **LibreHWM probing aligned:** elevated scan probing and LibreHardwareMonitorLib packaging now match. The DLL is packaged under `app/providers/sensors/` with MPL notices; missing runtime shows "Not packaged / unavailable" rather than crashing.

### Stabilization pass

- **Label consistency:** "Run System Scan" → "Run Standard Scan" (welcome overlay, ViewModel next-action strings); "Local Only" → "Keep Local Only" (Settings Kyra Intelligence); "Export Support Bundle" → "Create Support Bundle" (docs and scripts).
- **Version bump:** Assembly, csproj, AppReleaseInfo, installer, and all docs updated from 1.2.0-preview.1 to 1.2.1-preview.1.
- **HTTP User-Agent:** Updated to `ForgerEMS/1.2.1-preview.1` in link-checker and quarantine-download headers.
- **Test alignment:** AppReleaseInfoTests, BetaDocumentationTests, InfoDocumentTextsTests, KyraForgerEmsReleaseAnswerTests, DeepSensorDisclosureCopyTests updated to assert v1.2.1 strings.

---

## v1.2.0-preview.1 — Public Preview (2026-05-04)

### Technician suite

- **Toolkit catalog metadata:** Each toolkit item now carries purpose, official source URL, license/redistribution note, distribution model, and a beta safety rating — so you can see what you are building into a repair kit and why.
- **Technician Workflow Presets:** Kyra can now guide you through seven read-only checklist workflows — Prep USB Repair Toolkit, Diagnose Slow Laptop, Check Drive Health, Prep Laptop for Resale, Windows Boot Triage, Network Troubleshooting, and Battery / Mobile Workstation Check. Each preset includes required tools, safety warnings, what ForgerEMS can already check, and steps that still require manual action. No destructive automation.
- **Toolkit Readiness Score:** The Toolkit Manager now produces a 0–100 score per toolkit / USB target. Score factors: missing required items, checksum failures, managed update availability, Verify Links results, USB target health signals, and Ventoy status. Labels: Ready (≥85), Mostly Ready (70–84), Needs Attention (45–69), Not Ready (<45 or hard blockers present). Score, top strengths, top blockers, and next recommended action are all surfaced.
- **Local Machine Profiles (privacy-first):** ForgerEMS now persists a local profile per machine using a ForgerEMS-generated ID — not a hardware serial — to carry health scores, toolkit readiness, USB benchmark summaries, and resale estimates between sessions. Profiles live under `%LOCALAPPDATA%\ForgerEMS\Runtime\profiles\` and are never uploaded automatically.
- **Verify Links — safe metadata-only checks:** Toolkit Manager can now check whether toolkit source URLs are reachable using HTTP HEAD requests, with a small ranged GET fallback for hosts that block HEAD. No full downloads; no execution. Results are timeout-bounded and user-cancellable. Offline gracefully shows Unknown / Offline rather than a false pass or fail. Broken links reduce the Readiness Score (−6 each, max −18); warnings apply −2 each.
- **Kyra awareness — technician suite:** Kyra can now summarize the current Toolkit Readiness Score, explain workflow preset steps, reference the local machine profile context, summarize Verify Links results when scope-aligned with the active report, and describe checksum coverage — all from local data, no online provider required.
- **Copy Summary / export additions:** Export Support Bundle produces a redacted ZIP (paths and secrets filtered) safe for email attachment. Quick Read Summary provides a sanitized, shareable snapshot of the current machine state. Both are separate from raw log exports.

### Infrastructure and polish

- **Identity:** User-facing **ForgerEMS v1.2.0 Public Preview** with semantic version **1.2.0-preview.1** (`AppReleaseInfo`, assembly metadata).
- **Configuration:** `ForgerEmsEnvironmentConfiguration` + `ForgerEmsFeatureFlags` for env-driven GitHub update source, timeouts, user-agent, Kyra hints, and telemetry defaults (**off**).
- **Licensing foundation:** `LicenseTier`, `FeatureGateService`, `FeatureStatusService` for local tier resolution. Licensing is not enforced during beta; Pro labels are informational only.
- **Updates:** `GitHubReleaseUpdateCheckService` respects `FORGEREMS_GITHUB_OWNER` / `FORGEREMS_GITHUB_REPO` / timeout / user-agent env vars with safe fallbacks.
- **UX copy:** Public Preview banner, Settings feature-maturity card, softer USB readiness messaging, About / FAQ / legal alignment.
- **Kyra Advanced:** Provider hub env health summary without exposing secrets.
- **Docs:** `docs/ENVIRONMENT.md`, `docs/ARCHITECTURE-INTEGRATION-v1.2.0.md`, `docs/UPDATE-SYSTEM-v1.2.0.md`, `docs/PUBLIC_PREVIEW_CHECKLIST_v1.2.0.md`, marketing subdocs.
- **Tools:** `tools/Test-ForgerEMSBackend.ps1`, `tools/Export-ForgerEMSDiagnostics.ps1`, `tools/Validate-ForgerEMSRelease.ps1`, `tools/New-ForgerEMSPreviewRelease.ps1`.

### Release gate (ship tooling)

- `tools/Validate-ForgerEMSRelease.ps1` — expanded PASS/WARN/FAIL gate (version, docs, optional `release/current` artifacts, heuristic secret scan).
- `tools/build-release.ps1` — `release.json` **channel** set to `preview`; **releaseIdentifier** aligned with Public Preview wording.
- `tools/Export-ForgerEMSDiagnostics.ps1` — README + path redaction for operator bundles.
- `docs/PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md` — human QA checklist for this build.

Prior release history: versioned `docs/RELEASE_NOTES_*.md` files.
