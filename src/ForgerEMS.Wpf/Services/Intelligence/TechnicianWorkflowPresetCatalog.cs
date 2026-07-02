using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
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
            ["Dr. Forge", "Device Context", "Kyra"],
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
            ["Dr. Forge", "Kyra", "Export Summary"],
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
            ["Dr. Forge", "Port / USB Intelligence", "Kyra"],
            ["Do not expose full private IPs in shared logs/reports", "Avoid blind reset commands during initial triage"],
            ["Internet check and adapter role summary", "Unified diagnostics network items"],
            ["Test alternate network path", "Collect safe screenshots/log snippets"],
            "Document likely root cause and minimal next manual step."),
        Build(
            "Battery / Mobile Workstation Check",
            "Assess battery confidence and workstation viability without overclaiming.",
            ["Review battery wear/runtime availability", "Check power/thermal sensor coverage", "Classify confidence level", "Record disclosure-safe notes"],
            ["Dr. Forge", "Device Context", "Kyra"],
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

    public static bool TryBuildKyraWorkflowAnswer(string prompt, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var normalized = prompt.Trim();
        var hasCue = ContainsWorkflowCue(normalized);
        var requested = ResolvePreset(normalized);
        if (requested is null && hasCue)
        {
            requested = ResolvePresetByKeyword(normalized);
        }

        if (requested is null && !hasCue)
        {
            return false;
        }

        if (requested is null)
        {
            answer = "Technician Workflow Presets (dry-run guidance only):" + Environment.NewLine +
                     string.Join(Environment.NewLine, Presets.Select(p => $"- {p.Name}: {p.Purpose}")) + Environment.NewLine +
                     "Ask for one by name and Kyra will give a step-by-step checklist with safety warnings and manual-action boundaries.";
            return true;
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Workflow preset: {requested.Name}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Purpose: {requested.Purpose}");
        sb.AppendLine("Steps:");
        AppendBullets(sb, requested.Steps);
        sb.AppendLine("Required tools:");
        AppendBullets(sb, requested.RequiredTools);
        sb.AppendLine("Safety warnings:");
        AppendBullets(sb, requested.SafetyWarnings);
        sb.AppendLine("ForgerEMS can already check:");
        AppendBullets(sb, requested.ForgerChecks);
        sb.AppendLine("Manual action still required:");
        AppendBullets(sb, requested.ManualActions);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Next recommended action: {requested.NextRecommendedAction}");
        sb.AppendLine("Scope note: guidance/checklist only (no destructive automation).");
        answer = sb.ToString().TrimEnd();
        return true;
    }

    private static TechnicianWorkflowPreset? ResolvePreset(string prompt)
    {
        return Presets.FirstOrDefault(p => prompt.Contains(p.Name, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsWorkflowCue(string prompt) =>
        prompt.Contains("workflow", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("preset", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("checklist", StringComparison.OrdinalIgnoreCase) ||
        prompt.Contains("triage", StringComparison.OrdinalIgnoreCase);

    private static TechnicianWorkflowPreset? ResolvePresetByKeyword(string prompt) =>
        prompt.Contains("slow", StringComparison.OrdinalIgnoreCase) ? Presets.FirstOrDefault(p => p.Name.Contains("Diagnose Slow Laptop", StringComparison.Ordinal)) :
        prompt.Contains("drive health", StringComparison.OrdinalIgnoreCase) || prompt.Contains("smart", StringComparison.OrdinalIgnoreCase) ? Presets.FirstOrDefault(p => p.Name.Contains("Check Drive Health", StringComparison.Ordinal)) :
        prompt.Contains("resale", StringComparison.OrdinalIgnoreCase) ? Presets.FirstOrDefault(p => p.Name.Contains("Resale", StringComparison.Ordinal)) :
        prompt.Contains("boot", StringComparison.OrdinalIgnoreCase) ? Presets.FirstOrDefault(p => p.Name.Contains("Boot Triage", StringComparison.Ordinal)) :
        prompt.Contains("network", StringComparison.OrdinalIgnoreCase) ? Presets.FirstOrDefault(p => p.Name.Contains("Network Troubleshooting", StringComparison.Ordinal)) :
        prompt.Contains("battery", StringComparison.OrdinalIgnoreCase) ? Presets.FirstOrDefault(p => p.Name.Contains("Battery", StringComparison.Ordinal)) :
        prompt.Contains("usb repair toolkit", StringComparison.OrdinalIgnoreCase) || prompt.Contains("prep usb", StringComparison.OrdinalIgnoreCase) ? Presets.FirstOrDefault(p => p.Name.Contains("Prep USB Repair Toolkit", StringComparison.Ordinal)) :
        null;

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

    private static void AppendBullets(StringBuilder sb, IEnumerable<string> values)
    {
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"- {value}");
        }
    }
}
