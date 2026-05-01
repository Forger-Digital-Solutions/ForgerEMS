using System.Globalization;
using System.Linq;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class KyraDeviceInsight
{
    public string Summary { get; init; } = string.Empty;

    public int HealthScore { get; init; }

    public int ResaleReadinessScore { get; init; }

    public IReadOnlyList<string> UpgradePriority { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RiskFlags { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Strengths { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Weaknesses { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> RecommendedActions { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BestOSChoices { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> BuyerListingNotes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> TechnicianNotes { get; init; } = Array.Empty<string>();

    public string ToPromptBlock()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Kyra device insight (technician summary — use for reasoning, don’t dump raw JSON):");
        sb.AppendLine($"Summary: {Summary}");
        sb.AppendLine($"Health score (scan): {HealthScore}/100 | Resale readiness (heuristic): {ResaleReadinessScore}/100");
        AppendList(sb, "Upgrade priority", UpgradePriority);
        AppendList(sb, "Risk flags", RiskFlags);
        AppendList(sb, "Strengths", Strengths);
        AppendList(sb, "Weaknesses", Weaknesses);
        AppendList(sb, "Recommended actions", RecommendedActions);
        AppendList(sb, "Best OS choices", BestOSChoices);
        AppendList(sb, "Buyer / listing honesty notes", BuyerListingNotes);
        AppendList(sb, "Technician notes", TechnicianNotes);
        return sb.ToString().TrimEnd();
    }

    private static void AppendList(StringBuilder sb, string title, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            return;
        }

        sb.AppendLine($"{title}: {string.Join("; ", items.Take(12))}");
    }
}

/// <summary>Derives technician-facing signals from System Intelligence profile + health.</summary>
public static class KyraSystemAnalyzer
{
    public static KyraDeviceInsight Analyze(
        SystemProfile profile,
        SystemHealthEvaluation? health,
        IReadOnlyList<string> recommendations,
        PricingEstimate? pricing)
    {
        var risks = new List<string>();
        var strengths = new List<string>();
        var weaknesses = new List<string>();
        var upgrade = new List<string>();
        var actions = new List<string>();
        var osChoices = new List<string>();
        var listing = new List<string>();
        var tech = new List<string>();

        var healthScore = health?.HealthScore ?? 50;
        var ramGb = profile.RamTotalGb ?? 0;
        if (ramGb > 0 && ramGb < 8)
        {
            risks.Add("Low RAM (under 8 GB) — multitasking and Windows updates struggle");
            weaknesses.Add("RAM capacity");
            upgrade.Add("RAM upgrade if slots allow");
            listing.Add("Disclose RAM size honestly; budget buyer segment");
        }
        else if (ramGb >= 16)
        {
            strengths.Add("Healthy RAM capacity for Windows and light workloads");
        }

        var disks = profile.Disks.ToList();
        var hasHdd = disks.Any(d => d.MediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase));
        var hasSsd = disks.Any(d =>
            d.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
            d.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase));
        if (hasHdd && !hasSsd)
        {
            risks.Add("System on spinning HDD — slow boot and app launches");
            weaknesses.Add("Storage type (HDD)");
            upgrade.Add("SSD or NVMe migration for biggest perceived speed gain");
        }

        foreach (var disk in disks.Take(4))
        {
            if (disk.Health.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
                disk.Health.Contains("bad", StringComparison.OrdinalIgnoreCase) ||
                disk.Status.Contains("fail", StringComparison.OrdinalIgnoreCase))
            {
                risks.Add($"Storage health concern on {disk.MediaType} ({disk.Health}/{disk.Status})");
                weaknesses.Add("Disk health");
                upgrade.Add("Backup + replace failing drive before resale or daily use");
                actions.Add("Run SMART/backup verification; avoid heavy writes on suspect disk");
            }

            if (disk.WearPercent is >= 85)
            {
                risks.Add("High SSD wear reported — plan replacement");
                weaknesses.Add("SSD wear");
            }
        }

        foreach (var bat in profile.Batteries.Take(2))
        {
            if (bat.WearPercent is >= 40)
            {
                risks.Add($"Battery wear ~{bat.WearPercent.Value.ToString("0", CultureInfo.InvariantCulture)}% — resale red flag");
                weaknesses.Add("Battery health");
                upgrade.Add("Replace battery if model supports it before listing");
                listing.Add("Note battery cycle/wear if asked; buyers care on laptops");
            }
        }

        if (profile.SecureBoot == false)
        {
            risks.Add("Secure Boot disabled — Windows 11 readiness and some games/tools care");
            osChoices.Add("Windows 10 or Linux may be simpler if Secure Boot stays off");
            actions.Add("Enable Secure Boot in firmware if goal is Windows 11");
        }

        if (profile.TpmPresent == false || profile.TpmReady == false)
        {
            risks.Add("TPM missing or not ready — Windows 11 clean install requirements");
            osChoices.Add("Windows 10 22H2 still viable; Win11 needs TPM 2.0 fix or hardware");
        }

        var gpuNames = profile.Gpus.Select(g => g.Name).ToList();
        var hasDgpu = gpuNames.Any(n =>
            n.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("RTX", StringComparison.OrdinalIgnoreCase));
        var hasIgpu = gpuNames.Any(n =>
            n.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
            n.Contains("AMD Radeon Graphics", StringComparison.OrdinalIgnoreCase));
        if (hasDgpu && gpuNames.Count > 1 && hasIgpu)
        {
            strengths.Add("Dual GPU layout (iGPU + dGPU) — good for resale if drivers OK");
            tech.Add("Verify dGPU shows in Device Manager and isn’t stuck on iGPU only");
        }

        if (profile.InternetCheck == false && profile.MissingGatewayAdapterCount > 0)
        {
            risks.Add("Network/gateway issues detected in scan");
            weaknesses.Add("Connectivity");
        }

        var resaleScore = Math.Clamp(100 - risks.Count * 12 + strengths.Count * 5, 0, 100);
        if (healthScore < 60)
        {
            resaleScore = Math.Min(resaleScore, 72);
        }

        foreach (var r in recommendations.Take(6))
        {
            if (!actions.Contains(r, StringComparer.Ordinal))
            {
                actions.Add(r);
            }
        }

        if (pricing is not null)
        {
            listing.Add(
                $"Local pricing hint only: ~${pricing.LowEstimate:0}-${pricing.HighEstimate:0} ({pricing.ConfidenceScore:0.##} confidence) — not marketplace comps");
        }
        else
        {
            listing.Add("No local pricing estimate — run scan + enable pricing when configured");
        }

        osChoices.Add("If TPM+Secure Boot OK and CPU recent: Windows 11");
        osChoices.Add("If older CPU or TPM issues: Windows 10 22H2 or lightweight Linux");

        var summary =
            $"{profile.Manufacturer} {profile.Model}: {profile.OverallStatus}. " +
            $"Key watchlist: {(risks.Count == 0 ? "nothing critical flagged" : string.Join(", ", risks.Take(3)))}.";

        tech.Add("Answer in sections: Short answer → What I noticed → Likely cause → Next steps → Caution");

        return new KyraDeviceInsight
        {
            Summary = summary,
            HealthScore = healthScore,
            ResaleReadinessScore = resaleScore,
            UpgradePriority = upgrade.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            RiskFlags = risks.Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray(),
            Strengths = strengths.Take(8).ToArray(),
            Weaknesses = weaknesses.Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            RecommendedActions = actions.Take(12).ToArray(),
            BestOSChoices = osChoices.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray(),
            BuyerListingNotes = listing.Take(8).ToArray(),
            TechnicianNotes = tech.Take(8).ToArray()
        };
    }
}
