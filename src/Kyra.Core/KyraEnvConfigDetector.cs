namespace Kyra.Core;

/// <summary>
/// Host-neutral detection of Windows environment-variable configuration questions.
/// Callers supply host-app-specific alias tokens (e.g. "kyra", "gateway") so this
/// class remains free of any host-specific strings.
/// </summary>
public static class KyraEnvConfigDetector
{
    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="prompt"/> appears to ask about Windows environment-variable configuration for the host application.
    /// <paramref name="hostAliases"/> supplies the host-specific tokens (e.g. "kyra", "gateway") so this method stays host-neutral.
    /// </summary>
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
