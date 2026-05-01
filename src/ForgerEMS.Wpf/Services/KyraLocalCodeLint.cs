using System.Text.Json;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Lightweight local checks before sending snippets to providers.</summary>
public static class KyraLocalCodeLint
{
    public static IReadOnlyList<string> AnalyzeSnippet(string text, string languageHint)
    {
        var issues = new List<string>();
        var t = text.Trim();
        if (string.IsNullOrEmpty(t))
        {
            return issues;
        }

        var lang = languageHint.ToLowerInvariant();
        if (lang.Contains("json", StringComparison.Ordinal) || t.TrimStart().StartsWith('{') || t.TrimStart().StartsWith('['))
        {
            TryJson(t, issues);
        }

        if (lang.Contains("powershell", StringComparison.Ordinal) || lang.Contains("pwsh", StringComparison.Ordinal))
        {
            if (t.Contains('‘') || t.Contains('’'))
            {
                issues.Add("PowerShell: curly quotes often break parsing — use straight quotes.");
            }
        }

        if (lang.Contains("xaml", StringComparison.Ordinal) || t.Contains("<Window", StringComparison.Ordinal))
        {
            var open = Regex.Matches(t, @"<\w+").Count;
            var close = Regex.Matches(t, @"</\w+>").Count;
            if (open > close + 2)
            {
                issues.Add("XAML: possible unclosed tags — check matching elements.");
            }
        }

        var brace = CountBalance(t, '{', '}');
        if (brace != 0)
        {
            issues.Add($"Brace balance off by {brace} — check missing `{{` or `}}`.");
        }

        var paren = CountBalance(t, '(', ')');
        if (paren != 0)
        {
            issues.Add($"Parentheses balance off by {paren}.");
        }

        return issues;
    }

    private static void TryJson(string t, List<string> issues)
    {
        try
        {
            JsonDocument.Parse(t);
        }
        catch (JsonException ex)
        {
            issues.Add($"JSON parse: {ex.Message}");
        }
    }

    private static int CountBalance(string s, char open, char close)
    {
        var n = 0;
        foreach (var c in s)
        {
            if (c == open)
            {
                n++;
            }
            else if (c == close)
            {
                n--;
            }
        }

        return n;
    }
}
