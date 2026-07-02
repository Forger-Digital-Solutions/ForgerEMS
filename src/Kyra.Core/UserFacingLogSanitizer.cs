using System.Text.RegularExpressions;

namespace Kyra.Core;

/// <summary>
/// Context-aware variant of <see cref="CopilotRedactor"/> for user-facing log lines.
/// Always strips secrets/credentials and known-private machine paths (user profile,
/// AppData, Temp, OneDrive, etc.). Local destination paths under explicitly safe roots
/// (e.g. the validated USB toolkit drive the user selected) are preserved so technicians
/// can confirm where downloads are landing.
/// </summary>
public static class UserFacingLogSanitizer
{
    private const string PrivatePathPlaceholder = "[REDACTED_PRIVATE_PATH]";

    private static readonly string[] AlwaysPrivatePathFragments =
    {
        @"\users\",
        @"\appdata\",
        @"\local\temp\",
        @"\local\packages\",
        @"\roaming\",
        @"\onedrive",
        @"\desktop\",
        @"\documents\",
        @"\downloads\",
        @"\dropbox\",
        @"\icloud",
        @"\google drive",
    };

    /// <summary>
    /// Redacts secrets and private machine paths from <paramref name="value"/> while
    /// preserving normal local destination paths that fall under one of
    /// <paramref name="safeRoots"/>. Returns the redacted string. Pass an empty
    /// <paramref name="safeRoots"/> for the original strict behaviour where all
    /// drive-rooted paths are redacted.
    /// </summary>
    public static string Sanitize(string value, IEnumerable<string>? safeRoots = null, bool enabled = true)
    {
        if (!enabled || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var roots = NormalizeSafeRoots(safeRoots);

        // Secrets first — these must never leak regardless of where they appear.
        var redacted = Regex.Replace(value, @"(?i)(api[_-]?key|token|secret|password)\s*[:=]\s*['""]?[^'""\s;]+", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(bearer)\s+[A-Za-z0-9._-]{12,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(ghp|gho|github_pat)_[A-Za-z0-9_]{20,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\bsk-[A-Za-z0-9_-]{12,}\b", "[REDACTED_API_KEY]");
        redacted = Regex.Replace(redacted, @"(?i)\bxox[baprs]-[A-Za-z0-9-]+\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)[A-Z]:\\Program Files(?: \(x86\))?\\[^\r\n\t ""']+", PrivatePathPlaceholder);

        // Always redact user-profile / private cache paths up front (anchored on the
        // path fragment, not the drive letter) so we still catch e.g. relative or
        // shell-expanded variants.
        redacted = Regex.Replace(
            redacted,
            @"[A-Za-z]:\\Users\\[^\\\s""']+(?:\\[^\r\n\t ""']*)?",
            PrivatePathPlaceholder);

        // Remaining drive-rooted paths: keep if under a safe root, otherwise redact.
        redacted = Regex.Replace(
            redacted,
            @"[A-Za-z]:\\[^\r\n\t ""']*",
            match => ClassifyAndRedactPath(match.Value, roots));

        redacted = Regex.Replace(redacted, @"(?i)\b(service tag|serial|s/n)\s*[:#]?\s*[A-Z0-9-]{5,}\b", "[REDACTED_SERIAL]");
        redacted = Regex.Replace(redacted, @"(?i)\b(bitlocker|recovery)\s*key\s*[:=]?\s*[^\s\r\n]{8,}", "[REDACTED_RECOVERY_KEY]");
        redacted = Regex.Replace(redacted, @"(?i)\b(windows|product)\s*key\s*[:=]?\s*[A-Z0-9-]{10,}", "[REDACTED_LICENSE_KEY]");
        redacted = Regex.Replace(redacted, @"\b(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b", "[private ip redacted]");
        redacted = Regex.Replace(redacted, @"\b([0-9]{1,3}\.){3}[0-9]{1,3}\b", "[ip redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\b([0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", "[mac redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[email redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\b(username|user|owner)\s*[:=]\s*[^;\r\n\t ]+", "[REDACTED_USERNAME]");
        return redacted;
    }

    /// <summary>
    /// Local-log mode for the user's own machine. Strips secrets, tokens, API keys,
    /// recovery/license keys, serials, emails, IP/MAC addresses, and explicit
    /// <c>username=</c> assignments — but preserves all path-like strings, including
    /// install paths under <c>C:\Program Files</c>, working/backend directories,
    /// script paths, JSON/Markdown report paths under <c>%LOCALAPPDATA%</c>, and
    /// the user profile root. Local logs are read by the operator sitting at the
    /// PC, who needs to see exactly what is happening and where. For sanitized
    /// copies destined for support or sharing, use <see cref="Sanitize"/> with
    /// safe-root filtering or <c>SensitiveDataRedactor.SanitizeForSupportShare</c>.
    /// </summary>
    public static string SanitizeForLocalLog(string value, bool enabled = true)
    {
        if (!enabled || string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Secrets first — same patterns as the strict sanitizer, but no path
        // regex follows so the path stays intact.
        var redacted = Regex.Replace(value, @"(?i)(api[_-]?key|token|secret|password)\s*[:=]\s*['""]?[^'""\s;]+", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(bearer)\s+[A-Za-z0-9._-]{12,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(ghp|gho|github_pat)_[A-Za-z0-9_]{20,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\bsk-[A-Za-z0-9_-]{12,}\b", "[REDACTED_API_KEY]");
        redacted = Regex.Replace(redacted, @"(?i)\bxox[baprs]-[A-Za-z0-9-]+\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(service tag|serial|s/n)\s*[:#]?\s*[A-Z0-9-]{5,}\b", "[REDACTED_SERIAL]");
        redacted = Regex.Replace(redacted, @"(?i)\b(bitlocker|recovery)\s*key\s*[:=]?\s*[^\s\r\n]{8,}", "[REDACTED_RECOVERY_KEY]");
        redacted = Regex.Replace(redacted, @"(?i)\b(windows|product)\s*key\s*[:=]?\s*[A-Z0-9-]{10,}", "[REDACTED_LICENSE_KEY]");
        redacted = Regex.Replace(redacted, @"\b(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b", "[private ip redacted]");
        redacted = Regex.Replace(redacted, @"\b([0-9]{1,3}\.){3}[0-9]{1,3}\b", "[ip redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\b([0-9A-F]{2}[:-]){5}[0-9A-F]{2}\b", "[mac redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", "[email redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\b(username|user|owner)\s*[:=]\s*[^;\r\n\t ]+", "[REDACTED_USERNAME]");
        return redacted;
    }

    /// <summary>Returns <see langword="true"/> when the given absolute path is safe
    /// to display (under one of the supplied safe roots and not a private fragment).</summary>
    public static bool IsSafeDestinationPath(string path, IEnumerable<string>? safeRoots)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (ContainsPrivateFragment(path))
        {
            return false;
        }

        var roots = NormalizeSafeRoots(safeRoots);
        return IsUnderSafeRoot(path, roots);
    }

    private static string ClassifyAndRedactPath(string raw, IReadOnlyList<string> safeRoots)
    {
        // Trim trailing punctuation we don't want absorbed into the captured path
        // (regex captures up to whitespace/newline so commas etc. survive).
        var trimmed = raw.TrimEnd('.', ',', ';', ':', ')', ']', '}', '\'', '"');
        var suffix = raw[trimmed.Length..];

        if (ContainsPrivateFragment(trimmed))
        {
            return PrivatePathPlaceholder + suffix;
        }

        if (safeRoots.Count > 0 && IsUnderSafeRoot(trimmed, safeRoots))
        {
            return raw;
        }

        return PrivatePathPlaceholder + suffix;
    }

    private static bool ContainsPrivateFragment(string path)
    {
        foreach (var fragment in AlwaysPrivatePathFragments)
        {
            if (path.Contains(fragment, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUnderSafeRoot(string path, IReadOnlyList<string> safeRoots)
    {
        var normalized = NormalizePathForComparison(path);
        foreach (var root in safeRoots)
        {
            if (normalized.StartsWith(root, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> NormalizeSafeRoots(IEnumerable<string>? safeRoots)
    {
        if (safeRoots is null)
        {
            return System.Array.Empty<string>();
        }

        var list = new List<string>();
        foreach (var root in safeRoots)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            var normalized = NormalizePathForComparison(root);
            if (!normalized.EndsWith('\\'))
            {
                normalized += '\\';
            }

            list.Add(normalized);
        }

        return list;
    }

    private static string NormalizePathForComparison(string path)
    {
        var normalized = path.Trim().Replace('/', '\\');
        return normalized;
    }
}
