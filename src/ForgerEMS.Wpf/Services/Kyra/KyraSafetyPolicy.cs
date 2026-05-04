using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Decides when online provider prose must be discarded in favor of local ForgerEMS truth.</summary>
public static class KyraSafetyPolicy
{
    /// <summary>
    /// When local facts exist, reject API answers that falsely deny access to this machine or contradict
    /// obvious local identifiers (simple heuristics — conservative discard only).
    /// </summary>
    public static bool ShouldDiscardOnlineAnswer(string onlineText, string? localReferenceText, KyraFactsLedger ledger)
    {
        if (string.IsNullOrWhiteSpace(onlineText))
        {
            return false;
        }

        if (!ledger.HasTrustedLocalHardwareFacts)
        {
            return false;
        }

        var t = onlineText.Trim();
        if (DeniesLocalAccess(t))
        {
            return true;
        }

        if (ContainsGenericModelDisclaimer(t))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ledger.CpuSummary) &&
            !ledger.CpuSummary.Contains("Unknown", StringComparison.OrdinalIgnoreCase) &&
            ClaimsUnknownCpu(t))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ledger.DeviceSummary) &&
            !ledger.DeviceSummary.Contains("Unknown", StringComparison.OrdinalIgnoreCase) &&
            ClaimsUnknownDevice(t))
        {
            return true;
        }

        _ = localReferenceText;
        return false;
    }

    private static bool DeniesLocalAccess(string t)
    {
        if (t.Contains("don't have access", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("do not have access", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("cannot see your", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("can't see your", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("no access to your", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("i don't have visibility", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("i do not have visibility", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("i cannot view your", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("i can't view your", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("i don't have information about your specific", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("i do not have information about your specific", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return t.Contains("as an ai", StringComparison.OrdinalIgnoreCase) &&
               (t.Contains("don't have", StringComparison.OrdinalIgnoreCase) ||
                t.Contains("cannot", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsGenericModelDisclaimer(string t) =>
        t.Contains("as a language model", StringComparison.OrdinalIgnoreCase) &&
        (t.Contains("don't have access", StringComparison.OrdinalIgnoreCase) ||
         t.Contains("cannot browse", StringComparison.OrdinalIgnoreCase));

    private static bool ClaimsUnknownCpu(string t) =>
        Regex.IsMatch(t, @"\b(unknown|unspecified)\s+cpu\b", RegexOptions.IgnoreCase) ||
        (t.Contains("cpu", StringComparison.OrdinalIgnoreCase) &&
         t.Contains("unknown", StringComparison.OrdinalIgnoreCase) &&
         t.Contains("your", StringComparison.OrdinalIgnoreCase));

    private static bool ClaimsUnknownDevice(string t) =>
        Regex.IsMatch(t, @"\b(unknown|unspecified)\s+(device|computer|pc|laptop)\b", RegexOptions.IgnoreCase);
}
