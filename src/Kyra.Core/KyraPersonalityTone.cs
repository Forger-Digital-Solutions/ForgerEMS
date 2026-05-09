namespace Kyra.Core;

/// <summary>Per-turn playful vs professional phrasing for Kyra (respects the host's PersonalityProfile setting and explicit user cues).</summary>
public static class KyraPersonalityTone
{
    public static bool UsePlayfulWording(string? personalityProfile, string userPrompt)
    {
        var p = userPrompt.ToLowerInvariant();
        if (p.Contains("be serious", StringComparison.Ordinal) ||
            p.Contains("less cute", StringComparison.Ordinal) ||
            p.Contains("normal mode", StringComparison.Ordinal) ||
            p.Contains("tone it down", StringComparison.Ordinal))
        {
            return false;
        }

        if (p.Contains("be cute", StringComparison.Ordinal) ||
            p.Contains("kyra mode", StringComparison.Ordinal) ||
            p.Contains("silly mode", StringComparison.Ordinal) ||
            p.Contains("more fun", StringComparison.Ordinal))
        {
            return true;
        }

        var profile = personalityProfile?.Trim().ToLowerInvariant() ?? "balanced";
        return profile is "bubbly-tech" or "bubbly" or "playful";
    }

    public static string CasualGreetingLine(bool playful)
    {
        return playful
            ? "Hey — Kyra here. What are we fixing, building, or joking about today?"
            : "Hey — Kyra here. What do you want to work on?";
    }

    public static string NormalConversationRelaxLine(bool playful)
    {
        return playful
            ? "Fair — I went full repair-drill-sergeant there. I can absolutely just chat too. When you want device stuff, I've got the scan ready; otherwise I'll keep it chill."
            : "Understood — I'll keep this conversational. When you want device help, say the word and I'll pull in the System Intelligence context.";
    }

    public static string FrustrationAckLine(bool playful)
    {
        return playful
            ? "Yeah, that sounds annoying — I'm on your side. Tell me what's going wrong (or if you just need a breather), and we'll tackle it step by step."
            : "I hear you — that sounds frustrating. Tell me what's going wrong and we'll work through it step by step.";
    }
}
