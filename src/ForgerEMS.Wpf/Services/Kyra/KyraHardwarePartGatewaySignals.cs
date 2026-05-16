namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Gateway routing helpers for sanitized hardware part research (no PII in prompts).</summary>
public static class KyraHardwarePartGatewaySignals
{
    public static bool ShouldRouteHardwarePartLookup(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        if (!HasPartKeyword(prompt))
        {
            return false;
        }

        var l = prompt.ToLowerInvariant();
        if (KyraHardwarePartsAnswerBuilder.PromptRequestsLivePartPricing(prompt))
        {
            return true;
        }

        if ((l.Contains("buy", StringComparison.Ordinal) ||
             l.Contains("purchase", StringComparison.Ordinal) ||
             l.Contains("replacement", StringComparison.Ordinal) ||
             l.Contains("compatible", StringComparison.Ordinal) ||
             l.Contains("should i get", StringComparison.Ordinal) ||
             l.Contains("should i order", StringComparison.Ordinal)) &&
            HasPartKeyword(prompt))
        {
            return true;
        }

        if (l.Contains("find compatible", StringComparison.Ordinal) ||
            l.Contains("find a compatible", StringComparison.Ordinal) ||
            l.Contains("find replacement", StringComparison.Ordinal) ||
            l.Contains("lookup part", StringComparison.Ordinal) ||
            l.Contains("part lookup", StringComparison.Ordinal))
        {
            return true;
        }

        return l.Contains(" where can i buy", StringComparison.Ordinal) ||
               l.StartsWith("where can i buy", StringComparison.Ordinal) ||
               l.Contains(" where to buy", StringComparison.Ordinal) ||
               l.StartsWith("where to buy", StringComparison.Ordinal);
    }

    public static string? TryClassifyPartCategory(string prompt)
    {
        var l = prompt.ToLowerInvariant();
        if (l.Contains("battery", StringComparison.Ordinal))
        {
            return "battery";
        }

        if (l.Contains("charger", StringComparison.Ordinal) || l.Contains("power brick", StringComparison.Ordinal) ||
            l.Contains("ac adapter", StringComparison.Ordinal))
        {
            return "charger";
        }

        if (l.Contains("dock", StringComparison.Ordinal) || l.Contains("docking", StringComparison.Ordinal))
        {
            return "dock";
        }

        if (l.Contains("ram", StringComparison.Ordinal) || l.Contains("memory", StringComparison.Ordinal) ||
            l.Contains("ddr", StringComparison.Ordinal))
        {
            return "ram";
        }

        if (l.Contains("ssd", StringComparison.Ordinal) || l.Contains("nvme", StringComparison.Ordinal) ||
            (l.Contains("storage", StringComparison.Ordinal) && l.Contains("upgrade", StringComparison.Ordinal)))
        {
            return "ssd";
        }

        return "unknown";
    }

    private static bool HasPartKeyword(string prompt)
    {
        var l = prompt.ToLowerInvariant();
        return l.Contains("battery", StringComparison.Ordinal) ||
               l.Contains("ssd", StringComparison.Ordinal) ||
               l.Contains("nvme", StringComparison.Ordinal) ||
               l.Contains("ram", StringComparison.Ordinal) ||
               l.Contains("memory", StringComparison.Ordinal) ||
               l.Contains("charger", StringComparison.Ordinal) ||
               l.Contains("adapter", StringComparison.Ordinal) ||
               l.Contains("dock", StringComparison.Ordinal);
    }
}
