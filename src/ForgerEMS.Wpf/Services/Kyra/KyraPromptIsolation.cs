using System.Text.RegularExpressions;
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

    public static bool LooksLikeExplicitThreadContinuation(string prompt)
    {
        var t = prompt.Trim().ToLowerInvariant();
        if (KyraFollowUpClassifier.LooksLikeConversationFollowUp(prompt))
        {
            return true;
        }

        return Regex.IsMatch(t,
            @"\b(continue|continuing|pick\s+up\s+where|where\s+we\s+left|what\s+were\s+we\s+doing|as\s+we\s+were\s+saying|going\s+back\s+to)\b");
    }

    public static bool LooksLikeKyraWindowsEnvConfigurationQuestion(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var t = prompt.Trim().ToLowerInvariant();
        var envCue = t.Contains("environment variable", StringComparison.OrdinalIgnoreCase) ||
                     t.Contains("environment variables", StringComparison.OrdinalIgnoreCase) ||
                     (t.Contains("env", StringComparison.OrdinalIgnoreCase) &&
                      (t.Contains("variable", StringComparison.OrdinalIgnoreCase) ||
                       t.Contains("var ", StringComparison.OrdinalIgnoreCase)));
        if (!envCue)
        {
            return false;
        }

        return t.Contains("kyra", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("forgerems", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("forger ems", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("for you", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("gateway", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("provider", StringComparison.OrdinalIgnoreCase) ||
               t.Contains("beta", StringComparison.OrdinalIgnoreCase);
    }
}
