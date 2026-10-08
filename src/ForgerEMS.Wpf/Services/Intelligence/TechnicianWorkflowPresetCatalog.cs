using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed record TechnicianWorkflowPreset(
    string Name,
    string Purpose,
    IReadOnlyList<string> Steps,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> SafetyWarnings,
    IReadOnlyList<string> ForgerChecks,
    IReadOnlyList<string> ManualActions,
    string NextRecommendedAction);

public static class TechnicianWorkflowPresetCatalog
{
    private static readonly IReadOnlyList<TechnicianWorkflowPreset> Presets =
    [
        Build(
            "Prep USB Repair Toolkit",
            "Prepare a safe, technician-ready USB toolkit with managed + manual items tracked.",
            ["Select correct removable target", "Run Toolkit Manager health", "Resolve managed missing and verification issues", "Confirm manual/license-limited tools and notes", "Run USB benchmark and record best port"],
            ["Toolkit Manager", "USB Builder", "USB Benchmark"],
            ["Never target C: or internal disks", "Do not write to EFI/VTOYEFI partitions", "Do not bundle third-party binaries without license review"],
            ["Toolkit health verdict", "Managed download/checksum status", "USB target safety signals", "Best known USB port guidance"],
            ["Place licensed/manual files yourself where expected", "Review official tool pages before redistribution", "Retest after changes"],
            "Export a compact technician summary before field use."),
        Build(
            "Diagnose Slow Laptop",
            "Find likely bottlenecks using local evidence before repair actions.",
            ["Review a local device snapshot", "Review health score and key watch-outs", "Check RAM/storage/GPU/driver pressure indicators", "Validate with Task Manager during symptom reproduction"],
            ["Dr. Forge", "Device Context"],
            ["Treat unknown/not-exposed as confidence limits, not failures", "Backup customer data before any destructive repair"],
            ["Health score and confidence", "Device Fit/Best Use", "Hardware X-Ray sensor coverage"],
            ["Reproduce issue once in user workflow", "Capture vendor diagnostics if needed"],
            "Choose the highest-confidence bottleneck and run a non-destructive fix first."),
        Build(
            "Check Drive Health",
            "Validate storage reliability before cloning, repair, or resale.",
            ["Review storage health summary", "Check SMART/wear availability", "Validate free space and error signals", "Capture results in report"],
            ["Dr. Forge", "Toolkit storage diagnostic tools"],
            ["Do not run repair writes on unstable drives before backup", "Never execute unknown downloaded binaries"],
            ["Disk status and health display", "Optional provider status for storage details"],
            ["Run vendor SMART utility when Windows data is limited", "Backup critical files before repairs"],
            "Decide: safe to continue, backup-first, or parts/repair path."),
        Build(
            "Prep Laptop for Resale",
            "Create an evidence-backed resale prep checklist.",
            ["Review local device snapshot and Quick Read", "Review Flip Value and Best Use", "Confirm battery/security/storage confidence limits", "Document honest disclosures"],
            ["Dr. Forge", "Export Summary"],
            ["Do not claim unknown signals as verified", "Require backup confirmation before wipe/reset steps"],
            ["Flip Value range and confidence", "Best Use / Device Fit", "TPM/Secure Boot verification status"],
            ["Collect cosmetic/runtime notes", "Capture listing photos and condition disclosures"],
            "Export report + listing notes with confidence-based language."),
        Build(
            "Windows Boot Triage",
            "Triage boot problems safely before advanced repair commands.",
            ["Review local device snapshot and checklist", "Check storage and boot/security readiness", "Review startup/service signals", "Stage rescue toolkit media"],
            ["Dr. Forge", "Diagnostic Tools for USB", "USB Builder toolkit"],
            ["No destructive boot repair without explicit owner authorization", "Backup-first before reset/reinstall operations"],
            ["Windows readiness and warning reason", "Toolkit readiness for rescue media"],
            ["Perform owner-approved manual recovery steps", "Escalate to offline rescue when needed"],
            "Run guided non-destructive checks, then decide on owner-approved repair path."),
        Build(
            "Network Troubleshooting",
            "Verify practical network health for repair and update workflows.",
            ["Review network summary and diagnostics", "Identify active physical adapter", "Separate virtual adapter noise from real outage", "Retest connectivity-dependent actions"],
            ["Dr. Forge", "Port / USB Intelligence"],
            ["Do not expose full private IPs in shared logs/reports", "Avoid blind reset commands during initial triage"],
            ["Internet check and adapter role summary", "Unified diagnostics network items"],
            ["Test alternate network path", "Collect safe screenshots/log snippets"],
            "Document likely root cause and minimal next manual step."),
        Build(
            "Battery / Mobile Workstation Check",
            "Assess battery confidence and workstation viability without overclaiming.",
            ["Review battery wear/runtime availability", "Check power/thermal sensor coverage", "Classify confidence level", "Record disclosure-safe notes"],
            ["Dr. Forge", "Device Context"],
            ["Unknown/not-exposed battery data is not failure evidence", "Do not promise runtime without verification"],
            ["Battery wear/cycle availability", "Sensor coverage confidence"],
            ["Run vendor battery diagnostics when data is limited", "Capture charger/runtime notes"],
            "Choose disclosure wording based on confidence, then continue resale/repair flow.")
    ];

    public static IReadOnlyList<TechnicianWorkflowPreset> GetAll() => Presets;

    public static IReadOnlyList<string> BuildSystemActionHints(JsonElement root)
    {
        var hints = new List<string>();
        if (HasBatteryConfidenceGap(root) || NeedsWindowsReadinessVerification(root))
        {
            hints.Add("Workflow: Diagnose Slow Laptop");
        }

        if (IsStorageLimitedOrWarning(root))
        {
            hints.Add("Workflow: Check Drive Health");
        }

        hints.Add("Workflow: Prep Laptop for Resale");
        return hints.Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
    }

    private static bool NeedsWindowsReadinessVerification(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        var tpm = GetProviderStatus(summary, "tpmInfo");
        var secureBoot = GetProviderStatus(summary, "secureBootInfo");
        return IsUnknownStatus(tpm) || IsUnknownStatus(secureBoot);
    }

    private static bool IsStorageLimitedOrWarning(JsonElement root)
    {
        var diskStatus = root.TryGetProperty("diskStatus", out var disk) ? disk.ToString() : string.Empty;
        if (diskStatus.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
            diskStatus.Contains("degraded", StringComparison.OrdinalIgnoreCase) ||
            diskStatus.Contains("fail", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!root.TryGetProperty("disks", out var disks) || disks.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var item in disks.EnumerateArray())
        {
            if (item.TryGetProperty("healthDisplay", out var hd) &&
                hd.ValueKind == JsonValueKind.String &&
                hd.GetString()!.Contains("not exposed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasBatteryConfidenceGap(JsonElement root)
    {
        if (!root.TryGetProperty("batteries", out var batteries) || batteries.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var battery in batteries.EnumerateArray())
        {
            var wear = battery.TryGetProperty("wearDisplay", out var wd) ? wd.ToString() : string.Empty;
            if (string.IsNullOrWhiteSpace(wear) || wear.Contains("not exposed", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string GetProviderStatus(JsonElement summary, string name)
    {
        if (!summary.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Object)
        {
            return "Unknown";
        }

        return element.TryGetProperty("status", out var status) ? status.ToString() : "Unknown";
    }

    private static bool IsUnknownStatus(string status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("notexposed", StringComparison.OrdinalIgnoreCase) ||
        status.Contains("not exposed", StringComparison.OrdinalIgnoreCase);

    private static TechnicianWorkflowPreset Build(
        string name,
        string purpose,
        IReadOnlyList<string> steps,
        IReadOnlyList<string> requiredTools,
        IReadOnlyList<string> safetyWarnings,
        IReadOnlyList<string> forgerChecks,
        IReadOnlyList<string> manualActions,
        string nextAction)
        => new(name, purpose, steps, requiredTools, safetyWarnings, forgerChecks, manualActions, nextAction);

}
