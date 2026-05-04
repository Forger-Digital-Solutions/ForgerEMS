using System.Text;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Builds unified user-side prompts so every provider (local HTTP, Groq, Gemini, OpenAI-compatible, LM Studio, Ollama)
/// sees the same recent Kyra conversation recap and follow-up hints.
/// </summary>
public static class KyraProviderPromptBuilder
{
    public static string AppendConversationRecap(string corePrompt, CopilotContext context, int maxTotalChars = 14_000)
    {
        var recap = FormatConversationRecap(context);
        var follow = BuildFollowUpHint(context);
        if (string.IsNullOrWhiteSpace(recap) && string.IsNullOrWhiteSpace(follow))
        {
            return Trim(corePrompt, maxTotalChars);
        }

        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(recap))
        {
            sb.AppendLine(recap.Trim());
            sb.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(follow))
        {
            sb.AppendLine(follow.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.Append(corePrompt.Trim());
        var merged = KyraSystemContextSanitizer.SanitizeForExternalProviders(CopilotRedactor.Redact(sb.ToString(), enabled: true));
        return Trim(merged, maxTotalChars);
    }

    public static string FormatConversationRecap(CopilotContext context)
    {
        var history = context.ConversationHistory;
        if (history.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Recent Kyra conversation (most recent last). For phrases like \"those issues\", \"that\", \"what you said\", or \"fix it\", continue from Kyra's last reply — do not claim there was no prior answer.");
        foreach (var message in history.TakeLast(40))
        {
            var role = message.Role.Equals("You", StringComparison.OrdinalIgnoreCase) ? "User" : "Kyra";
            var text = message.Text.ReplaceLineEndings(" ").Trim();
            if (text.Length > 1_200)
            {
                text = text[..1_200] + "…";
            }

            sb.AppendLine($"{role}: {text}");
        }

        if (context.ConversationMeta is { LastKyraResponseExcerpt.Length: > 0 } meta)
        {
            sb.AppendLine();
            sb.AppendLine("Last Kyra reply (sanitized excerpt for continuity):");
            var excerpt = meta.LastKyraResponseExcerpt.ReplaceLineEndings(" ").Trim();
            sb.AppendLine(excerpt.Length <= 2_000 ? excerpt : excerpt[..2_000] + "…");
        }

        return KyraSystemContextSanitizer.SanitizeForExternalProviders(CopilotRedactor.Redact(sb.ToString().TrimEnd(), enabled: true));
    }

    public static string BuildFollowUpHint(CopilotContext context)
    {
        var q = context.UserQuestion.Trim();
        if (!KyraFollowUpClassifier.LooksLikeConversationFollowUp(q))
        {
            return string.Empty;
        }

        return "Follow-up: the user is referring to the prior Kyra reply in this thread. Answer in the same topic and acknowledge the earlier points when helpful.";
    }

    private static string Trim(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max] + Environment.NewLine + "[trimmed for provider size]";
    }
}

public static class KyraFollowUpClassifier
{
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

    public static bool LooksLikeRepairContinuation(string prompt, KyraIntent previousIntent, bool lastListedIssues) =>
        (LooksLikeConversationFollowUp(prompt) || prompt.Contains("walk me through", StringComparison.OrdinalIgnoreCase)) &&
        (lastListedIssues ||
         previousIntent is KyraIntent.SystemHealthSummary
             or KyraIntent.PerformanceLag
             or KyraIntent.StorageIssue
             or KyraIntent.DriverIssue
             or KyraIntent.MemoryIssue
             or KyraIntent.SlowBoot
             or KyraIntent.AppFreezing
             or KyraIntent.GPUQuestion);
}
