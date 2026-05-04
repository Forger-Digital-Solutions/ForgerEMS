# Changelog

## v1.2.0-preview.1 — Public Preview (2026-05-04)

- **Identity:** User-facing **ForgerEMS v1.2.0 Public Preview** with semantic version **1.2.0-preview.1** (`AppReleaseInfo`, assembly metadata).
- **Configuration:** `ForgerEmsEnvironmentConfiguration` + `ForgerEmsFeatureFlags` for env-driven GitHub update source, timeouts, user-agent, Kyra hints, telemetry defaults (**off**), and future marketplace flags.
- **Licensing foundation:** `LicenseTier`, `FeatureGateService`, `FeatureStatusService` (local tier resolution; `FORGEREMS_LICENSE_TIER=BetaTesterPro` merges with beta JSON entitlement).
- **Updates:** `GitHubReleaseUpdateCheckService` respects `FORGEREMS_GITHUB_OWNER` / `FORGEREMS_GITHUB_REPO` / timeout / user-agent env vars with safe fallbacks.
- **Diagnostics:** **Export Support Bundle** (ZIP) with README, redacted logs/JSON, update + config summaries (`SupportBundleExporter`).
- **UX copy:** Public Preview banner, Settings feature-maturity card, softer “partially staged” USB readiness messaging, About/FAQ/legal alignment.
- **Kyra Advanced:** Provider hub env health summary (`KyraProviderHubConfigHealthFormatter`) without exposing secrets.
- **Docs:** `docs/ENVIRONMENT.md`, `docs/ARCHITECTURE-INTEGRATION-v1.2.0.md`, `docs/UPDATE-SYSTEM-v1.2.0.md`, `docs/PUBLIC_PREVIEW_CHECKLIST_v1.2.0.md`, `docs/marketing/*`, root `CHANGELOG.md`.
- **Tools:** `tools/Test-ForgerEMSBackend.ps1`, `tools/Export-ForgerEMSDiagnostics.ps1`, `tools/Validate-ForgerEMSRelease.ps1`, `tools/New-ForgerEMSPreviewRelease.ps1`.

Prior release history remains in versioned `docs/RELEASE_NOTES_*.md` files.

### Release lock (ship tooling)

- `tools/Validate-ForgerEMSRelease.ps1` — expanded PASS/WARN/FAIL gate (version, docs, optional `release/current` artifacts, heuristic secret scan).
- `tools/build-release.ps1` — `release.json` **channel** set to `preview`; **releaseIdentifier** aligned with Public Preview wording.
- `tools/Export-ForgerEMSDiagnostics.ps1` — README + path redaction for operator bundles.
- `docs/PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md` — concise human QA before GitHub upload.
