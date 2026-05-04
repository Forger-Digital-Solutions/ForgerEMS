using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Boundary for live web/market data — never fabricate figures.</summary>
public static class KyraLiveToolRouter
{
    public const string LiveToolsUnavailableMessage =
        "I don't have live data tools enabled in this ForgerEMS build yet.";

    public static bool IsLiveDataIntent(KyraIntent intent) =>
        intent is KyraIntent.Weather
            or KyraIntent.News
            or KyraIntent.CryptoPrice
            or KyraIntent.StockPrice
            or KyraIntent.Sports
            or KyraIntent.LiveOnlineQuestion;

    /// <summary>General prompts that need real-time web/market/legal feeds (no fabrication when tools are off).</summary>
    public static bool PromptLooksLikeEphemeralExternalFacts(string prompt)
    {
        var t = prompt.ToLowerInvariant();
        if (ContainsAny(t, "forgerems", "forger ems"))
        {
            return false;
        }

        if (ContainsAny(t, "current law", "new law today", "statute today", "legal update today", "law today"))
        {
            return true;
        }

        if ((ContainsAny(t, "newest", "latest", "current") && ContainsAny(t, "release", "version")) &&
            ContainsAny(t, "software", "app", "application", "chrome", "firefox", "vscode", "visual studio"))
        {
            return true;
        }

        if (ContainsAny(t, "exchange rate", "interest rate today", "prime rate today"))
        {
            return true;
        }

        return false;
    }

    /// <summary>True when this turn should be answered locally with an honest “no live tools” message.</summary>
    public static bool RequiresUnavailableLiveDataLocalAnswer(
        KyraIntent intent,
        string prompt,
        KyraToolRegistry toolRegistry,
        CopilotSettings settings,
        KyraToolHostFacts hostFacts)
    {
        if (intent is KyraIntent.Weather or KyraIntent.News or KyraIntent.CryptoPrice or KyraIntent.StockPrice
            or KyraIntent.Sports or KyraIntent.LiveOnlineQuestion)
        {
            return !toolRegistry.HasOperationalLiveDataToolForIntent(intent, prompt, settings, hostFacts);
        }

        if (intent == KyraIntent.GeneralTechQuestion && PromptLooksLikeEphemeralExternalFacts(prompt))
        {
            return !toolRegistry.HasOperationalLiveDataToolForIntent(
                KyraIntent.LiveOnlineQuestion,
                prompt,
                settings,
                hostFacts);
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
