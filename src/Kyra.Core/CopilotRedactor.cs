using System.Text.RegularExpressions;

namespace Kyra.Core;

/// <summary>Strips credentials, PII, private paths, and hardware serials from text before persistence or provider transmission.</summary>
public static class CopilotRedactor
{
    public static string Redact(string value, bool enabled = true)
    {
        if (!enabled || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = Regex.Replace(value, @"(?i)(api[_-]?key|token|secret|password)\s*[:=]\s*['""]?[^'""\s;]+", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(bearer)\s+[A-Za-z0-9._-]{12,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(ghp|gho|github_pat)_[A-Za-z0-9_]{20,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\bsk-[A-Za-z0-9_-]{12,}\b", "[REDACTED_API_KEY]");
        redacted = Regex.Replace(redacted, @"(?i)\bxox[baprs]-[A-Za-z0-9-]+\b", "[REDACTED_TOKEN]");
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
}
