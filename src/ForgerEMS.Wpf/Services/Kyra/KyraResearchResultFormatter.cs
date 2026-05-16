namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public static class KyraResearchResultFormatter
{
    public static IReadOnlyList<KyraGatewayResearchResultDto> RankForHardwareCompatibility(
        IEnumerable<KyraGatewayResearchResultDto>? results)
    {
        return (results ?? Array.Empty<KyraGatewayResearchResultDto>())
            .OrderBy(r => SourceRank(r.SourceType))
            .ThenByDescending(r => ConfidenceRank(r.Confidence))
            .ThenBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string FormatEvidenceSummary(IEnumerable<KyraGatewayResearchResultDto>? results)
    {
        var ranked = RankForHardwareCompatibility(results).Take(5).ToArray();
        if (ranked.Length == 0)
        {
            return string.Empty;
        }

        return string.Join(Environment.NewLine, ranked.Select(r =>
        {
            var type = NormalizeSourceType(r.SourceType);
            var label = type == "Marketplace" ? "candidate / verify label" : type;
            var source = string.IsNullOrWhiteSpace(r.SourceName) ? r.Url : r.SourceName;
            return $"- {label}: {r.Title} ({source}) confidence={NormalizeConfidence(r.Confidence)}";
        }));
    }

    private static int SourceRank(string? sourceType)
    {
        return NormalizeSourceType(sourceType) switch
        {
            "Official" => 0,
            "Vendor" => 1,
            "Marketplace" => 2,
            "Forum" => 3,
            _ => 4
        };
    }

    private static int ConfidenceRank(string? confidence)
    {
        return NormalizeConfidence(confidence) switch
        {
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };
    }

    private static string NormalizeSourceType(string? sourceType)
    {
        var s = (sourceType ?? string.Empty).Trim();
        if (s.Equals("Official", StringComparison.OrdinalIgnoreCase)) return "Official";
        if (s.Equals("Vendor", StringComparison.OrdinalIgnoreCase)) return "Vendor";
        if (s.Equals("Marketplace", StringComparison.OrdinalIgnoreCase)) return "Marketplace";
        if (s.Equals("Forum", StringComparison.OrdinalIgnoreCase)) return "Forum";
        return "Unknown";
    }

    private static string NormalizeConfidence(string? confidence)
    {
        var s = (confidence ?? string.Empty).Trim().ToLowerInvariant();
        return s is "high" or "medium" or "low" ? s : "unknown";
    }
}
