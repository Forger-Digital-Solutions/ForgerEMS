using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Decides if a prompt should use the realtime gateway research path first.</summary>
public static class KyraRealtimeResearchClassifier
{
    public static bool ShouldUseRealtimeGateway(
        string prompt,
        CopilotSettings settings,
        out string gatewayIntent)
    {
        gatewayIntent = "chat";
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        if (!ForgerEmsEnvironmentConfiguration.KyraGatewayEnabled)
        {
            return false;
        }

        if (!settings.KyraRealtimeGatewayEnabled)
        {
            return false;
        }

        if (!ForgerEmsEnvironmentConfiguration.KyraGatewayConfigured)
        {
            return false;
        }

        var researchOn =
            ForgerEmsEnvironmentConfiguration.KyraResearchEnabled ||
            settings.KyraRealtimeGatewayResearchEnabled;
        if (!researchOn)
        {
            return false;
        }

        if (ForgerEmsEnvironmentConfiguration.KyraGatewayRequireConsent &&
            !settings.KyraRealtimeGatewayResearchConsent)
        {
            return false;
        }

        var p = prompt.Trim();
        if (IsLocalFirstPrompt(p))
        {
            return false;
        }

        if (KyraSimpleMathEvaluator.LooksLikeSimpleArithmeticQuestion(p))
        {
            return false;
        }

        gatewayIntent = MapGatewayIntent(p);
        if (HasRealtimeCue(p))
        {
            return true;
        }

        return gatewayIntent is not ("chat" or "web");
    }

    private static bool IsLocalFirstPrompt(string p)
    {
        var l = p.ToLowerInvariant();
        if (l.Contains("what device", StringComparison.Ordinal) && l.Contains("work", StringComparison.Ordinal))
        {
            return true;
        }

        if (l.Contains("best use", StringComparison.Ordinal) || l.Contains("device fit", StringComparison.Ordinal))
        {
            return true;
        }

        if (l.Contains("explain this warning", StringComparison.Ordinal) ||
            l.Contains("no likely usb", StringComparison.Ordinal))
        {
            return true;
        }

        if (l.Contains("fix this", StringComparison.Ordinal) && l.Contains("code", StringComparison.Ordinal))
        {
            return true;
        }

        if (l.Contains("public int add", StringComparison.Ordinal))
        {
            return true;
        }

        if (LooksLikeLocalOnlyHardwareFactsQuestion(l))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikeLocalOnlyHardwareFactsQuestion(string l)
    {
        if (l.Contains("cheapest", StringComparison.Ordinal) ||
            l.Contains("where to buy", StringComparison.Ordinal) ||
            l.Contains("find compatible", StringComparison.Ordinal) ||
            l.Contains("part lookup", StringComparison.Ordinal) ||
            l.Contains("lookup part", StringComparison.Ordinal))
        {
            return false;
        }

        if (l.Contains("nvme", StringComparison.Ordinal) ||
            l.Contains("sata", StringComparison.Ordinal) ||
            (l.Contains("m.2", StringComparison.Ordinal) && l.Contains("ssd", StringComparison.Ordinal)))
        {
            return true;
        }

        if ((l.Contains("ddr", StringComparison.Ordinal) ||
             l.Contains("lpddr", StringComparison.Ordinal) ||
             (l.Contains("ram", StringComparison.Ordinal) && l.Contains("type", StringComparison.Ordinal))) &&
            !l.Contains("buy", StringComparison.Ordinal))
        {
            return true;
        }

        return l.Contains("battery", StringComparison.Ordinal) &&
               (l.Contains("what", StringComparison.Ordinal) || l.Contains("which", StringComparison.Ordinal) ||
                l.Contains("need", StringComparison.Ordinal) || l.Contains("replacement", StringComparison.Ordinal));
    }

    private static bool HasRealtimeCue(string p)
    {
        var l = p.ToLowerInvariant();
        return l.Contains("today", StringComparison.Ordinal) ||
               l.Contains("right now", StringComparison.Ordinal) ||
               l.Contains("currently", StringComparison.Ordinal) ||
               l.Contains("latest", StringComparison.Ordinal) ||
               l.Contains("current ", StringComparison.Ordinal) ||
               l.Contains(" live", StringComparison.Ordinal) ||
               l.Contains("how's ", StringComparison.Ordinal) ||
               l.Contains("hows ", StringComparison.Ordinal) ||
               l.Contains("how is ", StringComparison.Ordinal) ||
               l.Contains("price", StringComparison.Ordinal) ||
               l.Contains("cheapest", StringComparison.Ordinal) ||
               l.Contains("lowest price", StringComparison.Ordinal) ||
               l.Contains("best deal", StringComparison.Ordinal) ||
               l.Contains("cve", StringComparison.Ordinal) ||
               l.Contains("advisory", StringComparison.Ordinal);
    }

    private static string MapGatewayIntent(string p)
    {
        var l = p.ToLowerInvariant();
        if (KyraHardwarePartGatewaySignals.ShouldRouteHardwarePartLookup(p))
        {
            return "hardware_part_lookup";
        }

        if (l.Contains("ventoy", StringComparison.Ordinal) &&
            (l.Contains("version", StringComparison.Ordinal) || l.Contains("latest", StringComparison.Ordinal)))
        {
            return "software_version";
        }

        if (l.Contains("bitcoin", StringComparison.Ordinal) || l.Contains("btc", StringComparison.Ordinal) ||
            l.Contains("ethereum", StringComparison.Ordinal) || l.Contains("eth", StringComparison.Ordinal) ||
            l.Contains("crypto", StringComparison.Ordinal) || l.Contains("doge", StringComparison.Ordinal))
        {
            return "crypto";
        }

        if (l.Contains("weather", StringComparison.Ordinal) || l.Contains("forecast", StringComparison.Ordinal))
        {
            return "weather";
        }

        if (l.Contains("news", StringComparison.Ordinal) || l.Contains("headline", StringComparison.Ordinal))
        {
            return "news";
        }

        if (l.Contains("stock", StringComparison.Ordinal) || l.Contains("ticker", StringComparison.Ordinal) ||
            l.Contains("nasdaq", StringComparison.Ordinal) || l.Contains("nyse", StringComparison.Ordinal))
        {
            return "finance";
        }

        if (l.Contains("sport", StringComparison.Ordinal) || l.Contains("score", StringComparison.Ordinal))
        {
            return "sports";
        }

        if (l.Contains("driver", StringComparison.Ordinal) && l.Contains("version", StringComparison.Ordinal))
        {
            return "driver_lookup";
        }

        if (l.Contains("resale", StringComparison.Ordinal) || l.Contains("market price", StringComparison.Ordinal))
        {
            return "resale_comps";
        }

        return "web";
    }
}
