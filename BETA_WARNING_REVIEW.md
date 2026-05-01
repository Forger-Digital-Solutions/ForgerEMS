# Beta Warning Review

## Build Snapshot (v1.1.4)

- Command: `dotnet build .\ForgerEMS.sln -c Release --no-incremental`
- Result: success
- Warning count: **0**
- Errors: 0

## v1.1.4 Cleanup (resolved)

All previously tracked **CA1822** (mark members static) and **CA1859** (concrete types / return types) warnings in the WPF project were fixed in a low-risk pass:

- `BackendContext.PrimaryManagedSummaryPath` is now `static`; call sites use `BackendContext.PrimaryManagedSummaryPath`.
- `VentoyIntegrationService.TryLoadPackageAsync` is `static`.
- `App.RunSelfTestAsync` uses concrete service types for the self-test entry point only.
- `SystemProfileMapper` mappers return concrete arrays where the analyzer suggested.
- `SystemHealthEvaluator.Evaluate` and `RecommendationEngine.Generate` are `static`; `CopilotContextBuilder` calls them directly.
- `CopilotService.CallExternalAPI` is `static`.
- `MainWindow.ResetPackageGroupGlow` is `static`.
- `MainViewModel` `AppVersionText` / `AppVersionFooterText` use get-only auto-properties with initializers (no instance state in accessors).

No WPF bindings or public ViewModel property names were changed.

## Test Project

- Analyzer **CA1707** (underscores in test names) was avoided by naming tests without underscores in the method name.

## If Warnings Return

- Re-run `dotnet build .\ForgerEMS.sln -c Release --no-incremental` and triage by rule ID.
- Do not suppress warnings to hide issues; document any that must remain with cause, file, and release priority.

## Release Recommendation

- **Beta v1.1.4:** build is clean (0 warnings) with current toolchain; safe to package after checklist sign-off.
