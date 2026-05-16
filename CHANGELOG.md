# Changelog

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
