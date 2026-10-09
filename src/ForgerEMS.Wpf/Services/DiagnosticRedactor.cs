using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Strips credentials, PII, private paths, and hardware serials from text before persistence or transmission.</summary>
public static partial class DiagnosticRedactor
{
    /// <summary>Returns <paramref name="value"/> with credentials/secrets, Windows paths, IP addresses, MAC addresses, email addresses, and hardware serials replaced by safe placeholder strings. Pass <paramref name="enabled"/> as <see langword="false"/> to bypass redaction (e.g. in tests).</summary>
    public static string Redact(string value, bool enabled = true)
    {
        if (!enabled || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = RedactSecrets(value);
        redacted = Regex.Replace(redacted, @"(?i)[A-Z]:\\Program Files(?: \(x86\))?\\[^\r\n\t ""']+", "[REDACTED_PRIVATE_PATH]");
        redacted = Regex.Replace(redacted, @"[A-Za-z]:\\Users\\([^\\\s]+)", @"[REDACTED_PRIVATE_PATH]");
        redacted = Regex.Replace(redacted, @"[A-Za-z]:\\[^\r\n\t ]+", "[REDACTED_PRIVATE_PATH]");
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

    /// <summary>Redacts credential material only (no path/PII rules). Used by log variants that must keep local paths visible, and by callers that need secret-scrubbing on top of their own rules. Handles environment-dump assignments, JSON key/value pairs, Authorization headers, URL userinfo, PEM-style multiline blocks, and common provider token formats.</summary>
    public static string RedactSecrets(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        // Multiline secret material first so line-oriented rules cannot see inside it.
        var redacted = Regex.Replace(
            value,
            @"-----BEGIN [A-Z0-9 ]*-----[\s\S]*?-----END [A-Z0-9 ]*-----",
            "[REDACTED_PRIVATE_BLOCK]");

        // Authorization headers (scheme + credential together, before the generic assignment rule can leave the token half-visible).
        redacted = Regex.Replace(
            redacted,
            @"(?i)\bauthorization\s*:\s*(?:basic|bearer|digest|negotiate|token|apikey)\s+[^\s;'""]+",
            "Authorization: [REDACTED_TOKEN]");

        // Credentials embedded in URLs: scheme://user:password@host
        redacted = Regex.Replace(
            redacted,
            @"(?i)\b([a-z][a-z0-9+.-]*://)[^/\s:@]+:[^/\s@]+@",
            "$1[REDACTED_CREDENTIAL]@");

        // JSON-style pairs: "password": "value", 'api_key': 'value', "token": value
        redacted = Regex.Replace(
            redacted,
            @"(?i)(['""])([A-Za-z0-9_.\-]*(?:api[_-]?key|token|secret|password|passwd|pwd|credential|authorization|bearer|private[_-]?key|client[_-]?secret|access[_-]?key|account[_-]?key|connection[_-]?string|refresh[_-]?token|session[_-]?key)[A-Za-z0-9_.\-]*)\1\s*:\s*(['""]?)[^'\""\s,}\]]+\3",
            "$1$2$1: \"[REDACTED_SECRET]\"");

        // Environment-dump / config-style assignments: NAME=value or NAME: value where the
        // name looks credential-bearing. Broad name match catches FORGEREMS_GITHUB_TOKEN=...
        // and similar long-form dumps even when the value is not a recognised token format.
        redacted = Regex.Replace(
            redacted,
            @"(?i)\b([A-Za-z0-9_]*(?:api[_-]?key|token|secret|password|passwd|pwd|credential|authorization|bearer|private[_-]?key|client[_-]?secret|access[_-]?key|account[_-]?key|connection[_-]?string|refresh[_-]?token|session[_-]?key)[A-Za-z0-9_]*)\s*[:=]\s*['""]?[^'""\s;,}\]\[]+",
            "$1=[REDACTED_TOKEN]");

        // Bare "Bearer <token>" occurrences outside an Authorization header.
        redacted = Regex.Replace(redacted, @"(?i)\bbearer\s+[A-Za-z0-9._~+/=-]{8,}\b", "Bearer [REDACTED_TOKEN]");

        // Provider-specific token formats.
        redacted = Regex.Replace(redacted, @"\b(?:ghp|gho|ghu|ghs|ghr|github_pat)_[A-Za-z0-9_]{16,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"\bsk-(?:ant-|live-|test-|proj-)?[A-Za-z0-9_-]{16,}\b", "[REDACTED_API_KEY]");
        redacted = Regex.Replace(redacted, @"\bxox[baprs]-[A-Za-z0-9-]{10,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"\bAKIA[0-9A-Z]{16}\b", "[REDACTED_ACCESS_KEY]");
        redacted = Regex.Replace(redacted, @"\bglpat-[A-Za-z0-9_-]{15,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"\bnpm_[A-Za-z0-9]{30,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"\bAIza[0-9A-Za-z_-]{35}\b", "[REDACTED_API_KEY]");
        redacted = Regex.Replace(redacted, @"\bya29\.[0-9A-Za-z_-]{10,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"\bcfut_[A-Za-z0-9_]{20,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{4,}\b", "[REDACTED_TOKEN]");

        return redacted;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="name"/> looks like a credential-bearing identifier (e.g. an environment-variable name or dictionary key such as <c>FORGEREMS_GITHUB_TOKEN</c>). Used so values logged under sensitive names are redacted even when the value itself does not match a known token format.</summary>
    public static bool IsSensitiveName(string? name)
    {
        return !string.IsNullOrEmpty(name) && SensitiveNameRegex().IsMatch(name);
    }

    [GeneratedRegex(@"(?i)(api[_-]?key|token|secret|password|passwd|pwd|credential|bearer|authorization|private[_-]?key|client[_-]?secret|access[_-]?key|account[_-]?key|connection[_-]?string|refresh[_-]?token|session[_-]?key)", RegexOptions.Compiled)]
    private static partial Regex SensitiveNameRegex();
}
