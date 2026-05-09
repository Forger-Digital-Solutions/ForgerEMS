using System.Text.RegularExpressions;

namespace Kyra.Core;

/// <summary>
/// Host-neutral detection of destructive, malicious, or credential-exfiltration prompts.
/// Returns <see langword="true"/> when a prompt matches a risk category; callers decide the response.
/// </summary>
public static class KyraDestructiveIntentDetector
{
    /// <summary>Returns <see langword="true"/> when <paramref name="t"/> references credential theft, ransomware, unauthorized access bypass, or similar malicious techniques.</summary>
    public static bool LooksLikeMaliciousOrUnauthorized(string t) =>
        ContainsAny(t,
            "ransomware", "keylogger", "steal password", "steal credentials", "dump sam", "mimikatz",
            "bypass bitlocker", "crack bitlocker", "hack into", "break into someone",
            "unauthorized access", "without permission hack") ||
        Regex.IsMatch(t, @"\b(bypass|reset|remove)\s+(someone|their|another|user|windows)\s+password\b");

    /// <summary>
    /// Returns <see langword="true"/> when the prompt describes broad, irreversible disk erasure.
    /// Generic removable-media cues (ventoy, removable, flash drive) are excluded here;
    /// hosts may add further allowlist entries (e.g. app-specific target names) in their own wrapper.
    /// </summary>
    public static bool LooksLikeMassDataDestruction(string t) =>
        ContainsAny(t,
            "diskpart clean", "clean all", "secure erase", "zero fill", "low level format",
            "wipe all drives", "wipe the disk", "erase all partitions", "format c:", "format c drive",
            "format system drive", "nvme format", "sanitize disk", "shred -", "dd if=",
            "destroy all data", "wipe hard drive completely") &&
        !ContainsAny(t, "ventoy", "removable", "flash drive");

    /// <summary>Returns <see langword="true"/> when <paramref name="t"/> appears to request sharing or exfiltrating API keys, passwords, or other secrets.</summary>
    public static bool LooksLikeCredentialExfiltration(string t) =>
        (ContainsAny(t, "paste your", "send your", "share your", "give me your") &&
         ContainsAny(t, "api key", "password", "secret", "token", "private key")) ||
        Regex.IsMatch(t, @"\b(exfil|exfiltrate|harvest)\b.*\b(password|token|secret|credential)\b");

    /// <summary>Returns <see langword="true"/> when <paramref name="haystack"/> contains any of the <paramref name="needles"/> using <see cref="StringComparison.Ordinal"/> comparison.</summary>
    public static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (haystack.Contains(n, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
