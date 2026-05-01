using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

public static partial class KyraCodeSnippetDetector
{
    public static bool LooksLikeCodeSnippet(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var t = prompt.Trim();
        if (t.Contains("```", StringComparison.Ordinal))
        {
            return true;
        }

        if (t.Length > 800)
        {
            t = t[..800];
        }

        return LooksLikePowerShell(t) ||
               LooksLikeCSharp(t) ||
               LooksLikeJson(t) ||
               LooksLikeYaml(t) ||
               LooksLikeXaml(t) ||
               LineLooksLikeCode(t);
    }

    public static string GuessLanguageHint(string prompt)
    {
        var t = prompt.Trim();
        if (t.Contains("```powershell", StringComparison.OrdinalIgnoreCase) || t.Contains("```pwsh", StringComparison.OrdinalIgnoreCase))
        {
            return "PowerShell";
        }

        if (t.Contains("```csharp", StringComparison.OrdinalIgnoreCase) || t.Contains("```cs", StringComparison.OrdinalIgnoreCase))
        {
            return "C#";
        }

        if (t.Contains("```json", StringComparison.OrdinalIgnoreCase))
        {
            return "JSON";
        }

        if (t.Contains("```yaml", StringComparison.OrdinalIgnoreCase) || t.Contains("```yml", StringComparison.OrdinalIgnoreCase))
        {
            return "YAML";
        }

        if (t.Contains("```xaml", StringComparison.OrdinalIgnoreCase))
        {
            return "XAML";
        }

        if (LooksLikePowerShell(t))
        {
            return "PowerShell";
        }

        if (LooksLikeJson(t))
        {
            return "JSON";
        }

        if (LooksLikeYaml(t))
        {
            return "YAML";
        }

        if (LooksLikeXaml(t))
        {
            return "XAML";
        }

        if (LooksLikeCSharp(t))
        {
            return "C#";
        }

        return "code";
    }

    private static bool LooksLikePowerShell(string t) =>
        t.Contains("Get-", StringComparison.Ordinal) ||
        t.Contains("Set-", StringComparison.Ordinal) ||
        t.Contains("$ErrorActionPreference", StringComparison.Ordinal) ||
        t.Contains("param(", StringComparison.Ordinal);

    private static bool LooksLikeCSharp(string t) =>
        t.Contains("namespace ", StringComparison.Ordinal) ||
        t.Contains("public class ", StringComparison.Ordinal) ||
        t.Contains("void Main(", StringComparison.Ordinal);

    private static bool LooksLikeJson(string t)
    {
        var s = t.TrimStart();
        return (s.StartsWith('{') && s.Contains(':', StringComparison.Ordinal)) ||
               (s.StartsWith('[') && s.Contains('{', StringComparison.Ordinal));
    }

    private static bool LooksLikeYaml(string t) =>
        t.Contains("runs-on:", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("steps:", StringComparison.OrdinalIgnoreCase) ||
        MultilineKeyRegex().IsMatch(t);

    private static bool LooksLikeXaml(string t) =>
        t.Contains("<Window", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("<Page", StringComparison.OrdinalIgnoreCase) ||
        t.Contains("xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"", StringComparison.Ordinal);

    private static bool LineLooksLikeCode(string t)
    {
        var lines = t.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var codeLike = 0;
        foreach (var line in lines.Take(12))
        {
            if (line.Contains('{') && line.Contains('}', StringComparison.Ordinal))
            {
                codeLike++;
            }

            if (line.Contains("();", StringComparison.Ordinal) || line.Contains("=>", StringComparison.Ordinal))
            {
                codeLike++;
            }
        }

        return codeLike >= 2;
    }

    [GeneratedRegex(@"(?m)^[A-Za-z0-9_-]+:\s*\S+", RegexOptions.Compiled)]
    private static partial Regex MultilineKeyRegex();
}
