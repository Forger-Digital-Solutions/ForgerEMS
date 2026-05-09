namespace Kyra.Core;

/// <summary>
/// Host-neutral detection of Windows environment-variable configuration questions.
/// Callers supply host-app-specific alias tokens (e.g. "kyra", "gateway") so this
/// class remains free of any host-specific strings.
/// </summary>
public static class KyraEnvConfigDetector
{
    public static bool LooksLikeWindowsEnvConfigurationQuestion(string? prompt, IEnumerable<string> hostAliases)
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

        foreach (var alias in hostAliases)
        {
            if (t.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
