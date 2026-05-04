using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Unifies user-visible Kyra identity; providers are engines only.</summary>
public static class KyraResponseComposer
{
    public const string KyraIdentityLabel = "Kyra";

    public const string KyraLocalModeLabel = "Kyra · local mode";

    public const string KyraEnhancedLabel = "Kyra · enhanced with online assist";

    public const string KyraThinkingStatus = "Kyra is thinking…";

    public const string KyraLocalFallbackNote =
        "Kyra used local mode for this answer — online assist was unavailable or declined for safety.";

    public static string BuildChatSourceLabel(ICopilotProvider provider, bool onlineEnhancementApplied)
    {
        _ = provider;
        return onlineEnhancementApplied ? KyraEnhancedLabel : KyraLocalModeLabel;
    }

    public static string AppendEnhancementFooter(string body, bool onlineEnhancementApplied)
    {
        if (!onlineEnhancementApplied || string.IsNullOrWhiteSpace(body))
        {
            return body;
        }

        if (body.Contains("Enhanced with online assist", StringComparison.OrdinalIgnoreCase))
        {
            return body;
        }

        return body.TrimEnd() + Environment.NewLine + Environment.NewLine + "_Enhanced with online model assist._";
    }

    /// <summary>Soft-rewrite obvious provider self-identification when local facts are authoritative.</summary>
    public static string SanitizeProviderSelfIdentification(string text, KyraFactsLedger ledger)
    {
        if (!ledger.HasTrustedLocalHardwareFacts || string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var t = text;
        foreach (var (from, to) in new (string from, string to)[]
             {
                 ("I'm Groq", "Kyra"),
                 ("I am Groq", "Kyra"),
                 ("I'm ChatGPT", "Kyra"),
                 ("I am ChatGPT", "Kyra"),
                 ("as Groq", "as Kyra"),
                 ("as ChatGPT", "as Kyra"),
                 ("as an OpenAI", "as Kyra"),
                 ("I'm Claude", "Kyra"),
                 ("I am Claude", "Kyra")
             })
        {
            t = t.Replace(from, to, StringComparison.OrdinalIgnoreCase);
        }

        return t;
    }
}
