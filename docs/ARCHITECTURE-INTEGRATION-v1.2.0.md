# ForgerEMS architecture integration — v1.2.0 Public Preview

This note maps how major subsystems connect. Paths are typical for an installed user profile (`%LOCALAPPDATA%\ForgerEMS\` and `...\ForgerEMS\Runtime\`).

## Data flow overview

1. **System Intelligence** — PowerShell `Invoke-ForgerEMSSystemScan.ps1` writes JSON under `Runtime\reports\` (e.g. `system-intelligence-latest.json`). The WPF host parses cards for the System Intelligence tab and exposes summaries to **Kyra** via sanitized context builders.
2. **Kyra** — `KyraOrchestrator` / `CopilotService` prefer offline rules and local spec builders; optional online providers read env-based configuration (`ForgerEmsEnvironmentConfiguration`, legacy `GEMINI_*` / `OPENAI_*` names per `KYRA_PROVIDER_ENVIRONMENT_SETUP.md`). Failures fall back to offline answers.
3. **USB Builder** — `UsbDetectionService` + `UsbTargetInfo` enforce removable/safe targets. **USB benchmark** results are cached (`Runtime\cache\usb-benchmarks.json`) and surface in USB Builder / **USB Intelligence** copy (`UsbIntelligencePanelUiCopy`).
4. **Managed downloads** — backend scripts emit `ForgerEMS-managed-download-result.json` on the **USB root**. `ManagedDownloadRunArtifact` feeds Toolkit Manager rows and USB Builder banners (`ScriptStatusParser` readiness: READY / PARTIALLY STAGED / etc.).
5. **Toolkit Manager** — manifest-driven health; manual/info states are expected for licensed or vendor-hosted payloads.
6. **Update checker** — `GitHubReleaseUpdateCheckService` lists GitHub releases (owner/repo from `ForgerEmsEnvironmentConfiguration` with defaults). Results flow to **Settings** and **UpdateCheckDiagnosticsFormatter** for clipboard / support bundle.
7. **Diagnostics** — unified checklist from on-disk diagnostics JSON when present; **Export Support Bundle** zips redacted logs + SI JSON + benchmark cache + managed-download JSON + update summary (`SupportBundleExporter`).
8. **Configuration** — `ForgerEmsEnvironmentConfiguration` centralizes env reads; feature flags default telemetry **off**. **FeatureGateService** / **FeatureStatusService** provide tier labels for future Free vs Pro enforcement without blocking Public Preview testers.

## Integration contract (in-code)

- **SystemProfile / scan JSON → Kyra**: grounded answers win over generic web tone when `FORGEREMS_KYRA_REQUIRE_LOCAL_FACTS` is honored by orchestration paths.
- **Benchmark → USB Intelligence**: measured read/write and cache timestamps inform “not measured yet” vs populated summaries.
- **Managed-download JSON → Toolkit + USB**: partial staging shows as **partially staged** (not a hard failure for manual items).
- **Update checker → Settings + Diagnostics**: machine state + safe failure strings; no silent installs.
- **Config/env → Kyra / updates / diagnostics**: read-only; secrets never echoed in UI summaries (`KyraProviderHubConfigHealthFormatter`).

For release packaging, see `tools/build-release.ps1` and `docs/UPDATE-SYSTEM-v1.2.0.md`.
