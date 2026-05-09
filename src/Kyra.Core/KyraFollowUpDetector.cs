using System.Text.RegularExpressions;

namespace Kyra.Core;

/// <summary>
/// Host-neutral detection of conversation follow-up signals and explicit thread continuations.
/// </summary>
public static class KyraFollowUpDetector
{
    /// <summary>Returns <see langword="true"/> when <paramref name="prompt"/> contains phrasing that refers back to a prior assistant reply (e.g. "those issues", "fix them", "what you said").</summary>
    public static bool LooksLikeConversationFollowUp(string prompt)
    {
        var t = prompt.ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(t))
        {
            return false;
        }

        if (t.Contains("those issues", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("these issues", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("that issue", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("that problem", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("what you said", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("what you mentioned", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("the things you listed", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("the usb thing", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("that usb", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("the usb", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("what about the usb", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("do the next step", StringComparison.OrdinalIgnoreCase) ||
            (t.Contains("next step", StringComparison.OrdinalIgnoreCase) && t.Contains("you", StringComparison.OrdinalIgnoreCase)) ||
            Regex.IsMatch(t, @"explain\s*#\s*\d", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(t, @"\bnumber\s+\d\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (t.Contains("how do i fix", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("how can i fix", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("fix those", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("fix them", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (t.Equals("fix it", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("fix it ", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("fix it.", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("fix it!", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="prompt"/> explicitly requests resuming or continuing the current conversation thread.</summary>
    public static bool LooksLikeExplicitThreadContinuation(string prompt)
    {
        var t = prompt.Trim().ToLowerInvariant();
        if (LooksLikeConversationFollowUp(prompt))
        {
            return true;
        }

        return Regex.IsMatch(t,
            @"\b(continue|continuing|pick\s+up\s+where|where\s+we\s+left|what\s+were\s+we\s+doing|as\s+we\s+were\s+saying|going\s+back\s+to)\b");
    }
}
