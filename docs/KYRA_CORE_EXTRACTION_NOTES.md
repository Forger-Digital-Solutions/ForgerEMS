# Kyra Core Extraction Notes

**Status:** Phase 2A complete — CopilotService.cs split, boundary markers applied.
**Last updated:** 2026-05-09
**Next phase:** Phase 3 — create `Kyra.Core` project, move KYRA_CORE_CANDIDATE types.

---

## What Was Done in Phase 2A

The 6,236-line `CopilotService.cs` monolith was split into 21 targeted files within the same project
(`ForgerEMS.Wpf`) and same namespace (`VentoyToolkitSetup.Wpf.Services`). This is a mechanical
refactor only — no logic was changed, no public APIs were renamed, no namespaces were modified.

All files carry one of three extraction-boundary markers (see below). Build result after split:
**0 warnings, 0 errors.** Test result: **901 pass, 4 pre-existing installer-text failures** (unrelated
to the split; present on the unmodified git HEAD as well).

---

## File Map After Split

| File | Lines | Primary Marker | Notes |
|------|-------|----------------|-------|
| `CopilotService.cs` | 686 | FORGEREMS_KYRA_ADAPTER | Trimmed to CopilotService class + inner KyraOrchestrationHostAdapter only |
| `CopilotCoreTypes.cs` | 568 | MIXED | All enums + core models; KyraIntent and a few model fields are ForgerEMS-specific |
| `KyraIntentRouter.cs` | 415 | MIXED | Routing algorithm generic; keyword lists contain ForgerEMS product terms |
| `KyraSafetyAndRouting.cs` | 405 | MIXED | Core guards (KyraSafetyGuard, KyraOnlineSafetyGate) + ForgerEMS adapters (KyraMachineContextRouter, KyraToolRouter, KyraPrivacyGate) |
| `KyraProviderInfrastructure.cs` | 140 | KYRA_CORE_CANDIDATE | KyraResponseCache, KyraProviderUsageTracker, KyraApiKeyStore, KyraProviderUrlSafety |
| `CopilotInterfaces.cs` | 101 | KYRA_CORE_CANDIDATE | ICopilotProvider, IKyraProvider, KyraProviderPool, all I* interfaces |
| `SystemProfileModels.cs` | 532 | FORGEREMS_KYRA_ADAPTER | ForgerEMS scan result models; replace with generic ISystemContext in Kyra.Core |
| `SystemHealthEvaluator.cs` | 405 | FORGEREMS_KYRA_ADAPTER | ForgerEMS-specific health heuristics |
| `RecommendationEngine.cs` | 95 | FORGEREMS_KYRA_ADAPTER | ForgerEMS USB/pricing recommendations |
| `CopilotProviderRegistry.cs` | 68 | FORGEREMS_KYRA_ADAPTER | Wires providers from ForgerEmsEnvironmentConfiguration |
| `CopilotContextBuilder.cs` | 461 | FORGEREMS_KYRA_ADAPTER | Reads ForgerEMS runtime JSON paths |
| `CopilotSupportHelpers.cs` | 260 | MIXED | CopilotRedactor + CopilotSettingsStore (adapter); KyraProviderStatusPresenter (UI shell) |
| `PromptTemplates.cs` | 45 | KYRA_CORE_CANDIDATE | Generic system/tool prompt templates |
| `LocalOfflineCopilotProvider.cs` | 48 | KYRA_CORE_CANDIDATE | Generic offline fallback provider |
| `OpenAiStyleCopilotProvider.cs` | 326 | KYRA_CORE_CANDIDATE | Shared OpenAI-HTTP base class (used by 7 providers) |
| `OpenAICompatibleCopilotProvider.cs` | 163 | FORGEREMS_KYRA_ADAPTER | Reads `FORGEREMS_OPENAI_*` env-var prefix |
| `AnthropicClaudeCopilotProvider.cs` | 49 | KYRA_CORE_CANDIDATE | Generic Anthropic provider |
| `GeminiCopilotProvider.cs` | 128 | KYRA_CORE_CANDIDATE | Generic Gemini provider |
| `LmStudioCopilotProvider.cs` | 144 | FORGEREMS_KYRA_ADAPTER | Reads `FORGEREMS_LMSTUDIO_*` env-var prefix |
| `OllamaCopilotProvider.cs` | 124 | FORGEREMS_KYRA_ADAPTER | Reads `FORGEREMS_OLLAMA_*` env-var prefix |
| `StubCopilotProvider.cs` | 67 | KYRA_CORE_CANDIDATE | Generic stub/no-op provider |
| `LocalRulesCopilotEngine.cs` | 1342 | MIXED | Engine framework generic; rule content references ForgerEMS concepts |

### External Files Also Marked

| File | Marker | Coupling |
|------|--------|---------|
| `Services/Kyra/KyraOrchestrator.cs` | KYRA_CORE_CANDIDATE | Clean orchestration seam via `IKyraOrchestrationHost` |
| `Services/Intelligence/KyraSafeContextBuilder.cs` | FORGEREMS_KYRA_ADAPTER | Reads ForgerEMS-specific runtime JSON paths |
| `Configuration/KyraInstallerIntelligenceRegistry.cs` | FORGEREMS_KYRA_ADAPTER | Reads `HKLM\Software\ForgerEMS` registry key |
| `Configuration/ForgerEmsEnvironmentConfiguration.cs` | FORGEREMS_KYRA_ADAPTER | All `FORGEREMS_*` env-var bindings |

---

## Hard Couplings That Block Kyra.Core Extraction

These are the specific coupling points that must be abstracted before Kyra.Core can exist as a
standalone library.

### 1. `FORGEREMS_*` Environment Variable Prefix
**Files:** `ForgerEmsEnvironmentConfiguration.cs`, `OpenAICompatibleCopilotProvider.cs`,
`LmStudioCopilotProvider.cs`, `OllamaCopilotProvider.cs`, `CopilotProviderRegistry.cs`

**Issue:** All provider configuration reads `FORGEREMS_*` prefixed env vars via
`ForgerEmsEnvironmentConfiguration` and `KyraProviderConfigResolver`. A standalone Kyra.Core cannot
hard-code a host application's env-var prefix.

**Resolution:** Introduce `IKyraProviderConfigSource` (or a `KyraProviderConfig` record) that
ForgerEMS populates from its env vars and passes to the provider registry at startup.

---

### 2. `SystemProfile` and ForgerEMS Scan Data in `CopilotContext`
**Files:** `SystemProfileModels.cs`, `CopilotCoreTypes.cs` (`CopilotContext`, `SystemContext`),
`CopilotContextBuilder.cs`, `SystemHealthEvaluator.cs`, `RecommendationEngine.cs`

**Issue:** `CopilotContext` carries `SystemProfile` (ForgerEMS scan output), `PricingEstimate`
(ForgerEMS USB valuation), and `ToolkitHealthItemView[]` (ForgerEMS toolkit health). These types are
deeply ForgerEMS-specific and have no meaning in a standalone Kyra.

**Resolution:** Replace with `ISystemContext` (opaque host-provided context blob) in Kyra.Core.
ForgerEMS.KyraAdapter provides a `ForgerEmsSystemContext : ISystemContext` wrapper.

---

### 3. `KyraIntent` ForgerEMS Entries
**Files:** `CopilotCoreTypes.cs`, `KyraIntentRouter.cs`

**Issue:** `KyraIntent` enum contains `ForgerEMSQuestion`, `USBBuilderHelp`, `ToolkitManagerHelp`,
`SystemScanHelp` — all ForgerEMS-specific. The intent router keyword lists also contain ForgerEMS
product terms (USB, Ventoy, Toolkit).

**Resolution:** Define `KyraIntent` with only generic values (General, Technical, HardwareQuery, etc.)
in Kyra.Core. ForgerEMS extends the intent set via `IIntentExtensionProvider` or a keyword
registration mechanism.

---

### 4. `KyraMachineContextRouter`, `KyraToolRouter`, `KyraPrivacyGate` — ForgerEMS Data Paths
**Files:** `KyraSafetyAndRouting.cs`

**Issue:** `KyraMachineContextRouter` calls `MachineProfileStore` and `MachineProfileSnapshot`
(ForgerEMS machine profiling). `KyraToolRouter` dispatches to ForgerEMS-specific tool types
(USB intelligence, scan, toolkit). `KyraPrivacyGate` applies ForgerEMS-specific redaction rules.

**Resolution:** Move ForgerEMS-specific routing into `ForgerEMS.KyraAdapter` as implementations of
`IContextRouter`, `IToolDispatcher`, `IPrivacyGate` interfaces defined in Kyra.Core.

---

### 5. `KyraSafeContextBuilder` — ForgerEMS Runtime Report Paths
**File:** `Services/Intelligence/KyraSafeContextBuilder.cs`

**Issue:** Hard-codes `%LOCALAPPDATA%\ForgerEMS\Runtime\reports\*` path pattern; reads ForgerEMS
USB intelligence JSON, toolkit health JSON, and diagnostics JSON.

**Resolution:** Introduce `IKyraContextReportProvider` in Kyra.Core. ForgerEMS implements it
pointing to its runtime report directory.

---

### 6. `KyraInstallerIntelligenceRegistry` — ForgerEMS Registry
**File:** `Configuration/KyraInstallerIntelligenceRegistry.cs`

**Issue:** Reads `HKLM\Software\ForgerEMS` registry key for Kyra consent flags set by the
Inno Setup installer. Entirely ForgerEMS installer integration.

**Resolution:** Stays in ForgerEMS shell entirely. Kyra.Core consent is exposed via
`CopilotSettings.KyraCommunitySharingEnabled` (generic flag); the registry sync is the
installer's concern.

---

### 7. `CopilotContextBuilder` — ForgerEMS JSON Report Loading
**File:** `CopilotContextBuilder.cs`

**Issue:** Reads USB intelligence JSON, toolkit health JSON, system intelligence JSON, and
diagnostics JSON from ForgerEMS runtime paths. Calls `SystemProfileMapper`, `SystemHealthEvaluator`,
`RecommendationEngine` — all ForgerEMS-specific.

**Resolution:** Split into `KyraContextBuilder` (generic, in Kyra.Core) and
`ForgerEmsContextBuilder : IKyraContextBuilder` (in ForgerEMS.KyraAdapter) that loads ForgerEMS
data and populates the generic context.

---

### 8. `static CopilotService.TryLoadSystemProfileFromReport()`
**File:** `CopilotService.cs`
**Caller:** `MainViewModel.cs` (~6 call sites)

**Issue:** `MainViewModel` calls this static method directly on `CopilotService`, bypassing the
`ICopilotService` interface. This is the tightest coupling point in the host ↔ service boundary.

**Resolution:** Move to `ICopilotService` interface as an async method, or extract to a dedicated
`ISystemProfileService`. MainViewModel should inject the interface.

---

## Clean Seams Already in Place

These are extraction boundaries that are already clean and require no additional work.

| Seam | File | Notes |
|------|------|-------|
| `IKyraOrchestrationHost` | `Services/Kyra/IKyraOrchestrationHost.cs` | Adapter interface between KyraOrchestrator and CopilotService |
| `KyraOrchestrator` | `Services/Kyra/KyraOrchestrator.cs` | Self-contained; takes host interface + registry + context builder |
| `ICopilotProvider` | `CopilotInterfaces.cs` | Clean provider contract; OpenAiStyleCopilotProvider implements it without ForgerEMS dep |
| `ICopilotProviderRegistry` | `CopilotInterfaces.cs` | Registry contract; concrete impl (CopilotProviderRegistry) is the adapter |
| `ICopilotContextBuilder` | `CopilotInterfaces.cs` | Context builder contract; implementation is the adapter |
| `ICopilotSettingsStore` | `CopilotInterfaces.cs` | Settings store contract; implementation reads ForgerEMS env vars |
| `KyraResponseCache` | `KyraProviderInfrastructure.cs` | Pure in-memory cache; no host dependency |
| `KyraApiKeyStore` | `KyraProviderInfrastructure.cs` | Reads env vars but via `Environment.GetEnvironmentVariable` (generic) |

---

## Suggested Phase 3 Migration Order

1. Create `Kyra.Core` project (no WPF/Windows dependency).
2. Move `CopilotInterfaces.cs` types first — they have zero dependencies.
3. Move `KyraProviderInfrastructure.cs` (depends only on interfaces).
4. Move `OpenAiStyleCopilotProvider.cs` and pure providers (Anthropic, Gemini, Stub, Offline).
5. Move `KyraOrchestrator.cs` (depends on interfaces from steps 2-4).
6. Move generic parts of `KyraSafetyAndRouting.cs` (KyraSafetyGuard, KyraOnlineSafetyGate, KyraProviderPriority, KyraPromptBuilder).
7. Move `LocalRulesCopilotEngine.cs` engine framework; keep rule content in ForgerEMS.KyraAdapter.
8. Move `PromptTemplates.cs` (zero deps).
9. Keep everything marked FORGEREMS_KYRA_ADAPTER in `ForgerEMS.Wpf` or a new `ForgerEMS.KyraAdapter` project.
10. Implement the abstraction interfaces (IKyraProviderConfigSource, ISystemContext, IContextRouter, etc.) to connect the two.

---

## Namespace Note

The current namespace `VentoyToolkitSetup.Wpf.Services` is a legacy artifact (Ventoy Toolkit Setup
was a predecessor product). When Kyra.Core is created, use `Kyra.Core` or `ForgerDigitalSolutions.Kyra.Core`
as its root namespace. Do **not** rename the existing namespace in ForgerEMS until a dedicated
namespace-migration pass is planned — renaming is a breaking change for all callers.
