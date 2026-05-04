using System;
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

    /// <summary>
    /// Detects confident online prose that names a different CPU family than the local System Intelligence ledger
    /// (common LLM failure mode: inventing a "representative" gaming laptop).
    /// </summary>
    public static bool ContradictsLocalHardwareLedger(string onlineText, KyraFactsLedger ledger)
    {
        if (!ledger.HasTrustedLocalHardwareFacts || string.IsNullOrWhiteSpace(onlineText))
        {
            return false;
        }

        var cpu = ledger.CpuSummary.Trim();
        if (cpu.Length == 0 || cpu.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var t = onlineText;
        if (cpu.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
            (t.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) ||
             t.Contains("AMD Ryzen", StringComparison.OrdinalIgnoreCase) ||
             t.Contains("Threadripper", StringComparison.OrdinalIgnoreCase) ||
             t.Contains("EPYC", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if ((cpu.Contains("Ryzen", StringComparison.OrdinalIgnoreCase) || cpu.Contains("AMD", StringComparison.OrdinalIgnoreCase)) &&
            !cpu.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
            (t.Contains("Intel Core", StringComparison.OrdinalIgnoreCase) ||
             t.Contains("Intel® Core", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var gpu = ledger.GpuSummary.Trim();
        if (gpu.Length > 0 && !gpu.Contains("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            if (gpu.Contains("Intel UHD", StringComparison.OrdinalIgnoreCase) &&
                (t.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                 t.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
                 t.Contains("Quadro", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            if ((gpu.Contains("Quadro", StringComparison.OrdinalIgnoreCase) || gpu.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) &&
                t.Contains("Intel UHD Graphics", StringComparison.OrdinalIgnoreCase) &&
                !gpu.Contains("Intel UHD", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var ram = ledger.RamSummary.Trim();
        if (TryParseRamGb(ram, out var ramGb) && ramGb > 0)
        {
            if (TryExtractRamGbClaim(t, out var claimed) && Math.Abs(claimed - ramGb) >= 12)
            {
                return true;
            }
        }

        var os = ledger.OsSummary.Trim();
        if (os.Length > 0 &&
            !os.Contains("Unknown", StringComparison.OrdinalIgnoreCase) &&
            os.Contains("Windows 11", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) &&
            !t.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static bool TryParseRamGb(string ramSummary, out int gb)
    {
        gb = 0;
        var m = Regex.Match(ramSummary, @"(\d+)\s*GB", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            return false;
        }

        return int.TryParse(m.Groups[1].Value, out gb);
    }

    private static bool TryExtractRamGbClaim(string onlineText, out int gb)
    {
        gb = 0;
        var m = Regex.Match(onlineText, @"(\d+)\s*GB\s*(?:of\s*)?(?:RAM|memory|DDR)", RegexOptions.IgnoreCase);
        if (!m.Success)
        {
            m = Regex.Match(onlineText, @"(\d+)\s*GB", RegexOptions.IgnoreCase);
        }

        return m.Success && int.TryParse(m.Groups[1].Value, out gb);
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
