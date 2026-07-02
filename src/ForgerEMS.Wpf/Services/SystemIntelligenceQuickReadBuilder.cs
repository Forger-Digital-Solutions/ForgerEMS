using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.Services;

public static class SystemIntelligenceQuickReadBuilder
{
    public static string Build(JsonElement root) => Build(root, null);

    public static string Build(JsonElement root, string? reportsDirectory)
    {
        var profile = SystemProfileMapper.FromJson(root);
        var health = SystemHealthEvaluator.Evaluate(profile);
        var machineClass = ReadMachineClass(root) ?? MachineClassifier.Classify(profile);
        var deviceFit = ReadDeviceFit(root) ?? new DeviceFitEngine().Evaluate(profile);
        var flipValue = profile.FlipValue;
        var strengths = BuildStrengths(profile, deviceFit);
        var watchOuts = BuildWatchOuts(profile, health, root);
        var workflowHints = TechnicianWorkflowPresetCatalog.BuildSystemActionHints(root);
        var toolRecommendations = BuildToolRecommendations(root);
        var missingInfo = BuildMissingInfo(root);
        var nextAction = BuildNextAction(profile, deviceFit, watchOuts);
        var needsSensorNote = watchOuts.Any(item =>
            item.Contains("not exposed", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
            item.Contains("verify", StringComparison.OrdinalIgnoreCase));

        var lines = new List<string>
        {
            "ForgerEMS System Intelligence — Quick Read",
            $"Machine: {JoinNonEmpty(" ", profile.Manufacturer, profile.Model)} — {machineClass.PrimaryClass}",
            $"Health: {health.HealthScore}/100 {HealthStatusLabel(profile, health)} | Scan Confidence: {ConfidenceLabel(health.ConfidenceScore)}",
            $"Best Use: {deviceFit.PrimaryFit}",
            $"Flip Value: {NormalizeRange(flipValue.EstimatedResaleRange)} | Basis: {BuildFlipBasis(flipValue)}",
            $"Key Strengths: {JoinList(strengths, "core specs captured")}",
            $"Watch-outs: {JoinList(watchOuts.Select(CompactPhrase).Take(3), "none obvious from local scan")} | Missing info: {CompactPhrase(missingInfo)}",
            $"Workflow suggestion: {JoinList(workflowHints, "Diagnose Slow Laptop (dry-run)")}",
            $"Next Action: {nextAction} | Tool recommendations: {JoinList(toolRecommendations, "System Intelligence + Toolkit Manager")}"
        };

        // Retired network-readiness reports are ignored; quick reads no longer
        // append pulse lines from stored reports.
        if (needsSensorNote && lines.Count < 8)
        {
            lines.Add("Sensor Notes: Unknown lowers confidence; NotExposed/PermissionRequired means Windows limited optional detail, not failure.");
        }

        return string.Join(Environment.NewLine, lines.Take(9));
    }

    private static MachineClassResult? ReadMachineClass(JsonElement root)
    {
        if (!root.TryGetProperty("machineClass", out var machineClass) || machineClass.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new MachineClassResult
        {
            PrimaryClass = GetJsonString(machineClass, "primaryClass", "Unknown / Mixed"),
            Confidence = GetJsonString(machineClass, "confidence", "Low"),
            SecondaryClasses = GetJsonStringArray(machineClass, "secondaryClasses"),
            TechnicianNote = GetJsonString(machineClass, "technicianNote", string.Empty)
        };
    }

    private static DeviceFitResult? ReadDeviceFit(JsonElement root)
    {
        if (!root.TryGetProperty("deviceFit", out var deviceFit) || deviceFit.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new DeviceFitResult
        {
            PrimaryFit = GetJsonString(deviceFit, "primaryFit", "Unknown / needs scan"),
            MachineClass = GetJsonString(deviceFit, "machineClass", "Unknown / Mixed"),
            Confidence = GetJsonString(deviceFit, "confidence", "Low"),
            StrongFits = GetJsonStringArray(deviceFit, "strongFits"),
            WeakFits = GetJsonStringArray(deviceFit, "weakFits"),
            ExampleWorkloads = GetJsonStringArray(deviceFit, "exampleWorkloads"),
            UpgradeFirstAdvice = GetJsonStringArray(deviceFit, "upgradeFirstAdvice"),
            ListingPositioning = GetJsonString(deviceFit, "listingPositioning", string.Empty)
        };
    }

    private static string[] BuildStrengths(SystemProfile profile, DeviceFitResult fit)
    {
        var strengths = new List<string>();
        var cpu = profile.Cpu ?? string.Empty;
        if (Regex.IsMatch(cpu, @"\bi[79]\b|ryzen\s+[79]|xeon|ultra\s+[79]", RegexOptions.IgnoreCase))
        {
            strengths.Add(CpuShortName(cpu));
        }
        else if (!cpu.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            strengths.Add("verified CPU");
        }

        if (profile.RamTotalGb is >= 32)
        {
            strengths.Add($"{profile.RamTotalGb.Value:0.#}GB RAM");
        }
        else if (profile.RamTotalGb is >= 16)
        {
            strengths.Add("16GB+ RAM");
        }

        if (profile.Disks.Any(d => IsFastDisk(d)))
        {
            strengths.Add("NVMe/SSD storage");
        }

        var dedicatedGpu = profile.Gpus.FirstOrDefault(g => IsDedicatedGpu(g.Name, g.GpuKind));
        if (dedicatedGpu is not null)
        {
            strengths.Add(ShortGpuName(dedicatedGpu.Name));
        }

        if (profile.OperatingSystem.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
        {
            strengths.Add("Windows 11");
        }

        foreach (var strong in fit.StrongFits)
        {
            if (strengths.Count >= 4)
            {
                break;
            }

            if (!strong.Contains("heavy", StringComparison.OrdinalIgnoreCase) &&
                !strengths.Any(item => strong.Contains(item, StringComparison.OrdinalIgnoreCase)))
            {
                strengths.Add(CompactPhrase(strong));
            }
        }

        return strengths.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
    }

    private static string[] BuildWatchOuts(SystemProfile profile, SystemHealthEvaluation health, JsonElement root)
    {
        var watch = new List<string>();
        if (profile.Batteries.Count > 0 && profile.Batteries.All(b => b.WearPercent is null && b.CycleCount is null))
        {
            watch.Add("battery wear not exposed");
        }

        if (IsUnknown(profile.TpmStatus) || IsUnknown(profile.SecureBootStatus) || profile.TpmReady is null || profile.SecureBoot is null)
        {
            watch.Add("TPM/Secure Boot need verification");
        }

        var fit = ReadDeviceFit(root);
        if ((fit?.WeakFits ?? Array.Empty<string>()).Any(item => item.Contains("heavy gaming", StringComparison.OrdinalIgnoreCase)) ||
            !profile.Gpus.Any(g => Regex.IsMatch(g.Name, "rtx\\s*(30|40)|radeon\\s+rx\\s*(6|7)", RegexOptions.IgnoreCase)))
        {
            watch.Add("not a heavy gaming laptop unless benchmarks prove it");
        }

        foreach (var issue in health.DetectedIssues)
        {
            if (watch.Count >= 4)
            {
                break;
            }

            if (ShouldSuppressIssue(profile, issue))
            {
                continue;
            }

            var normalized = NormalizeIssue(issue);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                watch.Add(normalized);
            }
        }

        return watch.Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
    }

    private static string BuildNextAction(SystemProfile profile, DeviceFitResult fit, IReadOnlyList<string> watchOuts)
    {
        if (watchOuts.Any(item => item.Contains("battery", StringComparison.OrdinalIgnoreCase)) ||
            watchOuts.Any(item => item.Contains("TPM", StringComparison.OrdinalIgnoreCase)))
        {
            return $"Verify battery + firmware security state, then list as {ListingPhrase(fit)}.";
        }

        if (profile.RamTotalGb is > 0 and < 16)
        {
            return "Upgrade to 16GB RAM if supported, then rerun System Intelligence before resale.";
        }

        if (!profile.Disks.Any(IsFastDisk))
        {
            return "Verify or install SSD storage, then rerun storage health before listing.";
        }

        return $"List around the strongest verified fit: {ListingPhrase(fit)}.";
    }

    private static string[] BuildToolRecommendations(JsonElement root)
    {
        var tools = new List<string>
        {
            "System Intelligence",
            "Hardware X-Ray"
        };

        if (root.TryGetProperty("usbDiagnostics", out var usb) && usb.ValueKind == JsonValueKind.Object)
        {
            tools.Add("USB benchmark");
        }

        tools.Add("Toolkit Manager");
        return tools.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    }

    private static string BuildMissingInfo(JsonElement root)
    {
        var parts = new List<string>();
        if (!root.TryGetProperty("summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            return "summary not available";
        }

        var tpm = summary.TryGetProperty("tpmInfo", out var tpmInfo) ? GetJsonString(tpmInfo, "status", "Unknown") : "Unknown";
        var secureBoot = summary.TryGetProperty("secureBootInfo", out var sbInfo) ? GetJsonString(sbInfo, "status", "Unknown") : "Unknown";
        if (IsUnknown(tpm) || IsUnknown(secureBoot))
        {
            parts.Add("TPM/Secure Boot verification");
        }

        if (root.TryGetProperty("batteries", out var batteries) && batteries.ValueKind == JsonValueKind.Array)
        {
            var hasUnknownWear = batteries.EnumerateArray()
                .Any(b => IsUnknown(GetJsonString(b, "wearDisplay", "Unknown")));
            if (hasUnknownWear)
            {
                parts.Add("battery wear/runtime confidence");
            }
        }

        return parts.Count == 0 ? "none critical" : string.Join("; ", parts);
    }

    private static string BuildFlipBasis(FlipValueProfile flip)
    {
        var provider = flip.ProviderStatus ?? string.Empty;
        var estimateType = flip.EstimateType ?? string.Empty;
        var parts = new List<string>();
        if (estimateType.Contains("local", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("LocalHeuristicProvider", StringComparison.OrdinalIgnoreCase) ||
            provider.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("offline/local heuristic");
        }
        else if (provider.Contains("sold", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("sold comps");
        }
        else if (provider.Contains("active", StringComparison.OrdinalIgnoreCase) ||
                 provider.Contains("asking", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add("active listings/asking prices");
        }
        else
        {
            parts.Add("offline/local heuristic");
        }

        parts.Add(provider.Contains("not configured", StringComparison.OrdinalIgnoreCase) ||
                  provider.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            ? "live comps not configured"
            : "live provider configured");

        parts.Add(provider.Contains("location", StringComparison.OrdinalIgnoreCase) &&
                  !provider.Contains("not configured", StringComparison.OrdinalIgnoreCase)
            ? "location-aware when provider returns comps"
            : "location missing/not configured");

        return string.Join(", ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string HealthStatusLabel(SystemProfile profile, SystemHealthEvaluation health)
    {
        if (health.DetectedIssues.Any(issue =>
                issue.Contains("critical", StringComparison.OrdinalIgnoreCase) ||
                issue.Contains("failure", StringComparison.OrdinalIgnoreCase)))
        {
            return "Warning";
        }

        if (profile.OverallStatus.Contains("ready", StringComparison.OrdinalIgnoreCase) ||
            profile.OverallStatus.Contains("ok", StringComparison.OrdinalIgnoreCase))
        {
            return health.ConfidenceScore < 75 ? "Watch" : "Ready";
        }

        return health.HealthScore >= 80 ? "Ready" : health.HealthScore >= 60 ? "Watch" : "Warning";
    }

    private static string ConfidenceLabel(int confidence) => confidence switch
    {
        >= 85 => "High",
        >= 72 => "Medium-High",
        >= 55 => "Medium",
        >= 35 => "Low-Medium",
        _ => "Low"
    };

    private static bool ShouldSuppressIssue(SystemProfile profile, string issue)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            return true;
        }

        if ((profile.InternetCheck || profile.HasActivePhysicalInternetAdapter) &&
            (issue.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
             issue.Contains("host-only", StringComparison.OrdinalIgnoreCase) ||
             issue.Contains("APIPA", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return issue.Contains("No obvious blocking", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIssue(string issue)
    {
        var normalized = issue.Trim().TrimEnd('.');
        normalized = normalized.Replace("missing or not ready", "needs verification", StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("failed", "warning evidence", StringComparison.OrdinalIgnoreCase);
        return normalized.Length > 90 ? normalized[..87] + "..." : normalized;
    }

    private static string ListingPhrase(DeviceFitResult fit)
    {
        if (fit.ListingPositioning.Contains("workstation", StringComparison.OrdinalIgnoreCase) ||
            fit.PrimaryFit.Contains("workstation", StringComparison.OrdinalIgnoreCase))
        {
            return "a mobile workstation/dev laptop";
        }

        if (fit.PrimaryFit.Contains("gaming", StringComparison.OrdinalIgnoreCase) &&
            fit.ListingPositioning.Contains("gaming", StringComparison.OrdinalIgnoreCase))
        {
            return "an entry/mid gaming laptop with tested settings";
        }

        if (fit.PrimaryFit.Contains("office", StringComparison.OrdinalIgnoreCase) ||
            fit.PrimaryFit.Contains("school", StringComparison.OrdinalIgnoreCase))
        {
            return "a school/office productivity machine";
        }

        return fit.PrimaryFit;
    }

    private static string CpuShortName(string cpu)
    {
        if (Regex.IsMatch(cpu, @"\bi[79].*H\b|H-series", RegexOptions.IgnoreCase))
        {
            return Regex.IsMatch(cpu, @"\bi9\b", RegexOptions.IgnoreCase) ? "i9 H-series" : "i7 H-series";
        }

        var match = Regex.Match(cpu, @"(i[3579]|Ryzen\s+[3579]|Xeon|Ultra\s+[579])", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : "strong CPU";
    }

    private static string ShortGpuName(string gpu)
    {
        var match = Regex.Match(gpu, @"(NVIDIA\s+)?(Quadro\s+[A-Z]?\d+|RTX\s+A?\d+|GTX\s+\d+|GeForce\s+RTX\s+\d+|Radeon\s+RX\s+\d+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.Replace("NVIDIA ", "NVIDIA ", StringComparison.Ordinal).Trim() : "dedicated GPU";
    }

    private static string CompactPhrase(string value)
    {
        var compact = value
            .Replace(" / ", "/", StringComparison.Ordinal)
            .Replace(" /", "/", StringComparison.Ordinal)
            .Replace("/ ", "/", StringComparison.Ordinal)
            .Trim();
        return compact.Length > 42 ? compact[..39] + "..." : compact;
    }

    private static bool IsFastDisk(SystemDiskProfile disk) =>
        disk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
        disk.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
        disk.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
        disk.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase);

    private static bool IsDedicatedGpu(string name, string kind) =>
        kind.Contains("dedicated", StringComparison.OrdinalIgnoreCase) ||
        Regex.IsMatch(name, "nvidia|geforce|quadro|rtx|gtx|radeon\\s+rx|radeon\\s+pro|arc", RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(name, "intel|uhd|iris", RegexOptions.IgnoreCase);

    private static bool IsUnknown(string value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("notexposed", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not exposed", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRange(string range) =>
        string.IsNullOrWhiteSpace(range) || range.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            ? "Unknown"
            : range.Replace(" - ", "-", StringComparison.Ordinal).Replace(" – ", "-", StringComparison.Ordinal);

    private static string JoinList(IEnumerable<string> values, string fallback)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return items.Length == 0 ? fallback : string.Join("; ", items);
    }

    private static string JoinNonEmpty(string separator, params string[] values) =>
        string.Join(separator, values.Where(value => !string.IsNullOrWhiteSpace(value) && !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase)));

    private static string GetJsonString(JsonElement element, string propertyName, string fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => property.ToString()
        };
    }

    private static string[] GetJsonStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }
}
