using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public static class DriverHubRecommendationEngine
{
    private static readonly string[] UniversalEntryIds =
    {
        "nvidia-app",
        "nvidia-geforce-drivers",
        "amd-adrenalin",
        "amd-drivers-support",
        "intel-dsa",
        "intel-download-center",
        "dell-drivers",
        "hp-drivers",
        "lenovo-system-update",
        "msi-support",
        "asus-download-center"
    };

    public static IReadOnlyList<DriverHubRecommendation> Recommend(
        IEnumerable<DriverHubEntry> entries,
        SystemProfile? profile,
        bool linuxFilterRequested = false)
    {
        var catalog = entries.ToArray();
        var recommendations = new List<DriverHubRecommendation>();
        var recommendedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (profile is not null)
        {
            foreach (var entry in catalog)
            {
                foreach (var rule in entry.MatchRules)
                {
                    if (!MatchesRule(profile, rule))
                    {
                        continue;
                    }

                    recommendations.Add(new DriverHubRecommendation(entry, rule.Reason, BuildStatusText(rule, entry)));
                    recommendedIds.Add(entry.Id);
                    break;
                }
            }
        }

        if (linuxFilterRequested)
        {
            foreach (var entry in catalog.Where(entry => entry.IsLinuxGuidance))
            {
                if (recommendedIds.Add(entry.Id))
                {
                    recommendations.Add(new DriverHubRecommendation(
                        entry,
                        DriverHubRecommendationReason.LinuxGuidanceFilter,
                        "Linux guidance"));
                }
            }
        }

        if (recommendations.Count == 0)
        {
            foreach (var entry in GetUniversalEntries(catalog))
            {
                recommendations.Add(new DriverHubRecommendation(
                    entry,
                    DriverHubRecommendationReason.UniversalStartingPoint,
                    "Universal official starting point"));
            }
        }

        return recommendations
            .OrderBy(item => DriverHubDisplay.FormatCategory(item.Entry.Category), StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<DriverHubEntry> GetUniversalEntries(IReadOnlyList<DriverHubEntry> catalog)
    {
        foreach (var id in UniversalEntryIds)
        {
            var entry = catalog.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                yield return entry;
            }
        }
    }

    private static string BuildStatusText(DriverHubMatchRule rule, DriverHubEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(rule.StatusText))
        {
            return rule.StatusText;
        }

        return rule.Reason switch
        {
            DriverHubRecommendationReason.DetectedManufacturer => "Recommended based on detected vendor",
            DriverHubRecommendationReason.DetectedGpu => "Recommended based on detected GPU",
            DriverHubRecommendationReason.DetectedCpu => "Recommended based on detected CPU",
            DriverHubRecommendationReason.DetectedNetwork => "Recommended based on detected network vendor",
            DriverHubRecommendationReason.DetectedOperatingSystem when entry.IsLinuxGuidance => "Linux guidance",
            DriverHubRecommendationReason.DetectedOperatingSystem => "Recommended based on detected platform",
            _ => "Recommended for this PC"
        };
    }

    private static bool MatchesRule(SystemProfile profile, DriverHubMatchRule rule) =>
        MatchesAny(profile.Manufacturer, rule.ManufacturerContains) ||
        MatchesAny(profile.Model, rule.ModelContains) ||
        profile.Gpus.Any(gpu => MatchesAny(gpu.Name, rule.GpuContains)) ||
        MatchesAny(profile.Cpu, rule.CpuContains) ||
        MatchesAny(BuildNetworkText(profile), rule.NetworkContains) ||
        MatchesAny(profile.OperatingSystem, rule.OperatingSystemContains);

    private static string BuildNetworkText(SystemProfile profile)
    {
        return string.Join(
            " ",
            new[]
            {
                profile.NetworkStatus,
                string.Join(" ", profile.ReportRecommendations),
                string.Join(" ", profile.ObviousProblems)
            }.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static bool MatchesAny(string text, IReadOnlyList<string> needles)
    {
        if (string.IsNullOrWhiteSpace(text) || needles.Count == 0)
        {
            return false;
        }

        foreach (var needle in needles)
        {
            if (!string.IsNullOrWhiteSpace(needle) &&
                text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
