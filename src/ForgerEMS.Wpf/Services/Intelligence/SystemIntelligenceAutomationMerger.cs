using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Merges automation metadata (health narrative, issues, recommendations) into system-intelligence-latest.json.</summary>
public static class SystemIntelligenceAutomationMerger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool TryMerge(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(reportPath);
            using var doc = JsonDocument.Parse(text);
            var profile = SystemProfileMapper.FromJson(doc.RootElement);
            var health = SystemHealthEvaluator.Evaluate(profile);
            var recs = RecommendationEngine.Generate(profile, health);

            var automation = BuildAutomationNode(doc.RootElement, profile, health, recs);
            var root = JsonNode.Parse(text)?.AsObject();
            if (root is null)
            {
                return false;
            }

            root["forgerAutomation"] = JsonSerializer.SerializeToNode(automation, SerializerOptions);
            File.WriteAllText(reportPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            IntelligenceLogWriter.Append("system-intelligence.log", $"Automation metadata merged into {reportPath}");
            return true;
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("system-intelligence.log", $"Automation merge failed: {ex.Message}");
            return false;
        }
    }

    private static object BuildAutomationNode(
        JsonElement root,
        SystemProfile profile,
        SystemHealthEvaluation health,
        IReadOnlyList<string> recs)
    {
        var issues = new List<object>();
        foreach (var issue in health.DetectedIssues.Where(i =>
                     !string.IsNullOrWhiteSpace(i) &&
                     !i.Contains("No obvious blocking", StringComparison.OrdinalIgnoreCase)))
        {
            var blocked = issue.Contains("Storage needs review", StringComparison.OrdinalIgnoreCase) ||
                          issue.Contains("blocking", StringComparison.OrdinalIgnoreCase);
            issues.Add(new
            {
                severity = blocked ? "Blocked" : "Warning",
                code = "system_health",
                message = issue,
                suggestedFix = PickFix(issue)
            });
        }

        var breakdown = BuildHealthBreakdown(health.HealthScore, profile, issues.Count);

        var norm = BuildNormalizedHardware(root, profile);

        var summary =
            $"Health {health.HealthScore}/100. " +
            $"{norm.CpuTier}. " +
            $"GPUs: {string.Join(", ", norm.GpuClasses)}. " +
            $"Boot volume: {norm.BootVolume}. " +
            $"Network: {norm.NetworkAdapterSummary}.";

        return new
        {
            schemaVersion = "1.0",
            mergedUtc = DateTimeOffset.UtcNow,
            summaryLine = summary,
            healthScore = health.HealthScore,
            healthScoreBreakdown = breakdown,
            issues,
            recommendedActions = recs.ToArray(),
            normalizedHardware = norm
        };
    }

    private static object[] BuildHealthBreakdown(int score, SystemProfile profile, int issueCount)
    {
        return
        [
            new
            {
                factor = "Overall scan status",
                points = 0,
                rationale = $"Scan overall status: {profile.OverallStatus}."
            },
            new
            {
                factor = "Active issues",
                points = issueCount,
                rationale = issueCount == 0
                    ? "No issues were promoted from the evaluator."
                    : $"{issueCount} issue row(s) were generated for Kyra and diagnostics."
            },
            new
            {
                factor = "Final health score",
                points = score,
                rationale = "Composite 0-100 score from SystemHealthEvaluator (higher is healthier)."
            }
        ];
    }

    private static ForgerNormalizedHardwareSummary BuildNormalizedHardware(JsonElement root, SystemProfile profile)
    {
        var tier = InferCpuTier(profile.Cpu);
        var gpuClasses = ExtractGpuClasses(root, profile);
        var boot = Environment.GetEnvironmentVariable("SystemDrive") ?? "UNKNOWN";
        var (phys, virt) = CountAdapterRoles(root);
        var security = SummarizeSecurity(root);
        var ram = SummarizeRam(root);

        return new ForgerNormalizedHardwareSummary
        {
            CpuTier = tier,
            GpuClasses = gpuClasses,
            BootVolume = boot,
            RamConfiguredVsRated = ram,
            NetworkAdapterSummary = $"{phys} physical / {virt} virtual adapters (active scan)",
            SecuritySummary = security
        };
    }

    private static string SummarizeRam(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var s))
        {
            return "unknown";
        }

        var cfg = GetJsonString(s, "ramConfiguredSpeedDisplay");
        var rated = GetJsonString(s, "ramModuleRatedSpeedDisplay");
        return $"configured: {cfg}; rated: {rated}";
    }

    private static string SummarizeSecurity(JsonElement root)
    {
        if (!root.TryGetProperty("security", out var sec))
        {
            return "Security details unavailable.";
        }

        var bit = "BitLocker: ";
        if (sec.TryGetProperty("bitLockerSummary", out var bl))
        {
            bit += GetJsonString(bl, "friendlyDisplayText");
        }
        else
        {
            bit += "not summarized";
        }

        _ = root.TryGetProperty("summary", out var summary);
        var tpm = summary.ValueKind != JsonValueKind.Undefined
            ? $"TPM: {GetJsonString(summary, "tpmInfo")}"
            : "TPM: unknown";
        var sb = summary.ValueKind != JsonValueKind.Undefined
            ? GetJsonString(summary, "secureBootInfo")
            : "unknown";

        return $"{tpm}; Secure Boot: {sb}; {bit}";
    }

    private static (int physical, int virtualAdapters) CountAdapterRoles(JsonElement root)
    {
        if (!root.TryGetProperty("network", out var net) ||
            !net.TryGetProperty("adapters", out var adapters) ||
            adapters.ValueKind != JsonValueKind.Array)
        {
            return (0, 0);
        }

        var phys = 0;
        var virt = 0;
        foreach (var a in adapters.EnumerateArray())
        {
            var role = GetJsonString(a, "adapterRole");
            if (role.Contains("Virtual", StringComparison.OrdinalIgnoreCase))
            {
                virt++;
            }
            else if (role.Contains("Physical", StringComparison.OrdinalIgnoreCase) ||
                     role.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase))
            {
                phys++;
            }
        }

        return (phys, virt);
    }

    private static string[] ExtractGpuClasses(JsonElement root, SystemProfile profile)
    {
        if (!root.TryGetProperty("summary", out var s) || !s.TryGetProperty("gpus", out var gpus) ||
            gpus.ValueKind != JsonValueKind.Array)
        {
            return profile.Gpus.Count == 0
                ? ["Unknown"]
                : profile.Gpus.Select(_ => "Unknown").ToArray();
        }

        var list = new List<string>();
        foreach (var g in gpus.EnumerateArray())
        {
            var t = GetJsonString(g, "type");
            if (!string.IsNullOrWhiteSpace(t) && !string.Equals(t, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(t);
            }
        }

        return list.Count > 0 ? list.ToArray() : ["Unknown"];
    }

    private static string InferCpuTier(string cpu)
    {
        if (string.IsNullOrWhiteSpace(cpu))
        {
            return "CPU tier: unknown";
        }

        var u = cpu.ToUpperInvariant();
        if (u.Contains("RYZEN 9", StringComparison.Ordinal) || u.Contains("I9", StringComparison.Ordinal))
        {
            return "CPU tier: enthusiast / high";
        }

        if (u.Contains("RYZEN 7", StringComparison.Ordinal) || u.Contains("I7", StringComparison.Ordinal))
        {
            return "CPU tier: performance";
        }

        if (u.Contains("RYZEN 5", StringComparison.Ordinal) || u.Contains("I5", StringComparison.Ordinal))
        {
            return "CPU tier: mainstream";
        }

        if (u.Contains("RYZEN 3", StringComparison.Ordinal) || u.Contains("I3", StringComparison.Ordinal))
        {
            return "CPU tier: entry";
        }

        if (u.Contains("CELERON", StringComparison.Ordinal) || u.Contains("PENTIUM", StringComparison.Ordinal))
        {
            return "CPU tier: budget";
        }

        return "CPU tier: general desktop/mobile (manual review)";
    }

    private static string? PickFix(string issue)
    {
        if (issue.Contains("RAM", StringComparison.OrdinalIgnoreCase))
        {
            return "Plan a RAM upgrade to at least 16 GB if resale or heavy multitasking is the goal.";
        }

        if (issue.Contains("Storage", StringComparison.OrdinalIgnoreCase))
        {
            return "Back up data and test the drive with vendor tools; consider replacement if health is poor.";
        }

        if (issue.Contains("Battery", StringComparison.OrdinalIgnoreCase))
        {
            return "Calibrate battery reporting and plan replacement if wear is high.";
        }

        if (issue.Contains("TPM", StringComparison.OrdinalIgnoreCase) || issue.Contains("Secure Boot", StringComparison.OrdinalIgnoreCase))
        {
            return "Review firmware settings to enable TPM 2.0 and Secure Boot when Windows 11 readiness matters.";
        }

        if (issue.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("gateway", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("APIPA", StringComparison.OrdinalIgnoreCase))
        {
            return "Renew DHCP lease, verify router/cable, or disable conflicting virtual adapters.";
        }

        return "Review the System Intelligence recommendations list and rerun the scan after changes.";
    }

    private static string GetJsonString(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Undefined || !e.TryGetProperty(name, out var p))
        {
            return "unknown";
        }

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString() ?? "unknown",
            JsonValueKind.Object when p.TryGetProperty("friendlyDisplayText", out var f) => f.GetString() ?? p.ToString(),
            _ => p.ToString()
        };
    }

    public sealed class ForgerNormalizedHardwareSummary
    {
        public string CpuTier { get; init; } = string.Empty;

        public string[] GpuClasses { get; init; } = Array.Empty<string>();

        public string BootVolume { get; init; } = string.Empty;

        public string RamConfiguredVsRated { get; init; } = string.Empty;

        public string NetworkAdapterSummary { get; init; } = string.Empty;

        public string SecuritySummary { get; init; } = string.Empty;
    }
}
