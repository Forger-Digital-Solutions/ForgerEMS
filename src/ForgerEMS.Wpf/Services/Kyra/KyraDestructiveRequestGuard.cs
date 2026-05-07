using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>
/// Blocks or safely deflects high-risk prompts (data destruction, credential abuse, unauthorized access)
/// before tools, gateway research, or providers run.
/// </summary>
public static class KyraDestructiveRequestGuard
{
    private const int MaxPromptLength = 2000;

    public static bool TryBuildSafeResponse(string? prompt, out CopilotResponse response)
    {
        response = new CopilotResponse();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaxPromptLength)
        {
            return false;
        }

        var t = prompt.Trim().ToLowerInvariant();

        if (LooksLikeMaliciousOrUnauthorized(t))
        {
            response = SafeResponse(
                """
                I can’t help with stealing credentials, bypassing someone else’s security, malware, or evasion.

                If this is your device, I can still help safely with account recovery, backing up data, malware removal, Windows repair, reinstall prep, or owner-authorized diagnostics.
                """,
                "Kyra routing: safety guard — unauthorized access / abuse topic.");
            return true;
        }

        if (LooksLikeMassDataDestruction(t))
        {
            response = SafeResponse(
                """
                I won’t give step‑by‑step instructions for wiping disks, erasing all partitions, or destroying data outside normal, in‑app technician workflows.

                Safer path in ForgerEMS: use USB Builder only on removable targets you intend to repurpose, keep backups, and use Windows Disk Management or vendor tools deliberately. If you’re unsure which disk is which, stop and verify in Disk Management before any destructive action.
                """,
                "Kyra routing: safety guard — broad data destruction topic.");
            return true;
        }

        if (LooksLikeCredentialExfiltration(t))
        {
            response = SafeResponse(
                """
                Don’t paste API keys, gateway tokens, passwords, or private certificates into chat or logs.

                For Kyra’s gateway URL and beta token, set Windows user environment variables (see Kyra Advanced / docs) and restart the app. Rotate anything that was exposed.
                """,
                "Kyra routing: safety guard — credential handling reminder.");
            return true;
        }

        return false;
    }

    private static bool LooksLikeMaliciousOrUnauthorized(string t) =>
        ContainsAny(t,
            "ransomware", "keylogger", "steal password", "steal credentials", "dump sam", "mimikatz",
            "bypass bitlocker", "crack bitlocker", "hack into", "break into someone",
            "unauthorized access", "without permission hack") ||
        Regex.IsMatch(t, @"\b(bypass|reset|remove)\s+(someone|their|another|user|windows)\s+password\b");

    private static bool LooksLikeMassDataDestruction(string t) =>
        ContainsAny(t,
            "diskpart clean", "clean all", "secure erase", "zero fill", "low level format",
            "wipe all drives", "wipe the disk", "erase all partitions", "format c:", "format c drive",
            "format system drive", "nvme format", "sanitize disk", "shred -", "dd if=",
            "destroy all data", "wipe hard drive completely") &&
        !ContainsAny(t, "usb builder", "ventoy", "removable", "flash drive"); // narrow USB/Ventoy questions go to normal routing

    private static bool LooksLikeCredentialExfiltration(string t) =>
        (ContainsAny(t, "paste your", "send your", "share your", "give me your") &&
         ContainsAny(t, "api key", "password", "secret", "token", "private key")) ||
        Regex.IsMatch(t, @"\b(exfil|exfiltrate|harvest)\b.*\b(password|token|secret|credential)\b");

    private static bool ContainsAny(string haystack, params string[] needles)
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

    private static CopilotResponse SafeResponse(string body, string routingNote) =>
        new()
        {
            Text = body.Trim(),
            UsedOnlineData = false,
            OnlineStatus = "Kyra safety guard (local).",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes =
            [
                routingNote,
                "Why this answer: local safety review only — no gateway, no providers, no live research."
            ],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = "Safety guard",
            GroundedInSystemIntelligence = false,
            ActionSuggestions = [],
            KyraTransparencySummary =
                "Route: Safety guard (local). No gateway or provider calls. Context: your question text only — no system dump."
        };
}
