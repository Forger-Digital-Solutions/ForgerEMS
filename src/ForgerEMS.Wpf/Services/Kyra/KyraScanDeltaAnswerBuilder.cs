using System.Linq;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Compares the last two stored System Intelligence scan snapshots from local Kyra machine memory.</summary>
public static class KyraScanDeltaAnswerBuilder
{
    private const int MaxPromptLen = 220;

    public static bool TryBuild(string? prompt, string? machineMemoryStorePath, out CopilotResponse response)
    {
        response = new CopilotResponse();
        if (string.IsNullOrWhiteSpace(prompt) ||
            prompt.Length > MaxPromptLen ||
            string.IsNullOrWhiteSpace(machineMemoryStorePath))
        {
            return false;
        }

        var t = prompt.Trim().ToLowerInvariant();
        if (!ContainsAny(t,
                "what changed since last scan",
                "what changed since the last scan",
                "since last scan",
                "since the last system scan",
                "compare to last scan",
                "diff since last scan"))
        {
            return false;
        }

        KyraMachineMemoryProfile profile;
        try
        {
            profile = new KyraMachineMemoryStore(machineMemoryStorePath).Load();
        }
        catch
        {
            return false;
        }

        var scans = profile.Entries
            .Where(e => string.Equals(e.KyraActionCategory, "system_scan", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.OutcomeCategory, "scan_completed", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.ScanTimestamp)
            .Take(2)
            .ToArray();

        if (scans.Length < 2)
        {
            response = new CopilotResponse
            {
                Text =
                    "I only have one stored System Intelligence snapshot in local Kyra memory so far. Run System Intelligence again after you change hardware or drivers, then ask again — I’ll compare the newest stored scan to the previous one.",
                UsedOnlineData = false,
                OnlineStatus = "Kyra local memory comparison.",
                ProviderType = CopilotProviderType.LocalOffline,
                ProviderNotes = ["Kyra routing: scan delta — insufficient local history"],
                ResponseSource = KyraResponseSource.LocalKyra,
                SourceLabel = "Local memory",
                GroundedInSystemIntelligence = false,
                ActionSuggestions = [],
                KyraTransparencySummary =
                    "Route: Local memory (scan history). Context: sanitized Kyra machine memory entries only — no live gateway."
            };
            return true;
        }

        var newer = scans[0];
        var older = scans[1];
        var lines = new List<string>
        {
            "Here’s what changed between the last two System Intelligence snapshots stored in local Kyra memory (sanitized bands only):"
        };

        void AddIfChanged(string label, string a, string b)
        {
            if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            {
                lines.Add($"• {label}: was “{b}”, now “{a}”.");
            }
        }

        AddIfChanged("Health score band", newer.HealthScoreBand, older.HealthScoreBand);
        AddIfChanged("Issue focus", newer.IssueCategory, older.IssueCategory);
        AddIfChanged("Warning category", newer.WarningCategory, older.WarningCategory);
        AddIfChanged("Hardware category", newer.HardwareCategorySummary, older.HardwareCategorySummary);

        if (lines.Count == 1)
        {
            lines.Add("• No material band changes were recorded between those two scans (categories match).");
        }

        lines.Add("");
        lines.Add("If this doesn’t match what you see in System Intelligence, run a fresh scan and confirm local Kyra memory is enabled.");

        response = new CopilotResponse
        {
            Text = string.Join(Environment.NewLine, lines),
            UsedOnlineData = false,
            OnlineStatus = "Kyra local memory comparison.",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes =
            [
                "Kyra routing: scan delta from local machine memory",
                $"newerScanUtc={newer.ScanTimestamp:O}",
                $"olderScanUtc={older.ScanTimestamp:O}"
            ],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = "Local memory",
            GroundedInSystemIntelligence = true,
            ActionSuggestions = [],
            KyraTransparencySummary =
                "Route: Local memory. Context: last two system_scan entries — no raw JSON, no gateway."
        };

        return true;
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
