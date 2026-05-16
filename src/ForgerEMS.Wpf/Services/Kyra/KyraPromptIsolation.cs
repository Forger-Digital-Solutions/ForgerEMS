using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Prompts that must not inherit unrelated chat history or “continue thread” provider instructions.</summary>
public static class KyraPromptIsolation
{
    public static bool ShouldIsolateFromConversationMemory(string? prompt, KyraIntent intent)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        if (intent == KyraIntent.CodeAssist || KyraCodeSnippetDetector.LooksLikeCodeSnippet(prompt))
        {
            return true;
        }

        if (KyraSimpleMathEvaluator.LooksLikeSimpleArithmeticQuestion(prompt))
        {
            return true;
        }

        if (LooksLikeKyraWindowsEnvConfigurationQuestion(prompt))
        {
            return true;
        }

        if (LooksLikePrivacyOrMemoryQuestion(prompt))
        {
            return true;
        }

        return false;
    }

    private static bool LooksLikePrivacyOrMemoryQuestion(string prompt)
    {
        var t = prompt.Trim().ToLowerInvariant();
        return t.Contains("what did you send", StringComparison.Ordinal) ||
               (t.Contains("what data", StringComparison.Ordinal) && t.Contains("kyra", StringComparison.Ordinal)) ||
               t.Contains("what is stored", StringComparison.Ordinal) ||
               t.Contains("kyra memory", StringComparison.Ordinal) ||
               t.Contains("delete my kyra", StringComparison.Ordinal) ||
               t.Contains("what would be shared", StringComparison.Ordinal) ||
               t.Contains("privacy mode", StringComparison.Ordinal);
    }

    public static bool LooksLikeExplicitThreadContinuation(string prompt) =>
        KyraFollowUpDetector.LooksLikeExplicitThreadContinuation(prompt);

    private static readonly string[] _envConfigAliases =
        ["kyra", "forgerems", "forger ems", "for you", "gateway", "provider", "beta"];

    public static bool LooksLikeKyraWindowsEnvConfigurationQuestion(string? prompt) =>
        KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(prompt, _envConfigAliases);
}
