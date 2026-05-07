using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Realistic upgrade / resale guidance from local scan signals (no invented parts or prices).</summary>
public static class KyraUpgradePathEngine
{
    public static string BuildUpgradeFirstSummary(SystemProfile profile, SystemHealthEvaluation? health)
    {
        var lines = new List<string>();
        var wear = KyraHardwareFactsEngine.PrimaryBatteryWear(profile);
        var laptop = KyraHardwareFactsEngine.IsLikelyLaptop(profile);
        var nvmeHealthy = KyraHardwareFactsEngine.StorageLooksHealthyNvmeSsd(profile);
        var ramGb = profile.RamTotalGb ?? 0;

        if (wear is >= 35)
        {
            lines.Add("Battery wear is high in the scan — replacing the battery (or planning around shorter runtime) is usually the best first hardware move for a laptop if unplugged time matters.");
        }
        else if (wear is null && laptop)
        {
            lines.Add("Battery wear wasn’t fully exposed — if runtime matters, verify wear with vendor tools or a fresh System Intelligence run before spending on other upgrades.");
        }

        if (nvmeHealthy)
        {
            lines.Add("Primary storage already looks like a healthy NVMe-class SSD in the scan — an SSD swap usually isn’t upgrade #1 unless you need more capacity.");
        }
        else if (profile.Disks.Count > 0)
        {
            var worst = profile.Disks
                .OrderByDescending(d => d.WearPercent ?? 0)
                .ThenBy(d => d.Health.Contains("Healthy", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .First();
            if (worst.WearPercent is >= 80 ||
                worst.Status.Equals("WARNING", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("At least one drive shows elevated wear or a warning status — plan storage before chasing CPU/GPU upgrades.");
            }
        }

        if (ramGb >= 32)
        {
            lines.Add($"You already show about {ramGb:0.#} GB RAM — only chase more memory if the platform supports it and your workload actually pages.");
        }
        else if (ramGb > 0 && ramGb < 8)
        {
            lines.Add("RAM is on the tight side — a memory upgrade can be worthwhile if slots/support allow.");
        }

        if (laptop)
        {
            lines.Add("Most laptops: GPU and CPU are not practical field upgrades — assume you’re tuning RAM, storage, battery, and thermals instead.");
        }

        if (health?.HealthScore is < 55)
        {
            lines.Add($"Overall health score is lower ({health.HealthScore}/100) — fix stability/thermal/storage warnings before cosmetic upgrades.");
        }

        if (lines.Count == 0)
        {
            lines.Add("No single urgent upgrade jumps out from the scan — clean cooling, verify firmware/drivers, then decide based on your bottleneck (runtime vs capacity vs thermals).");
        }

        return string.Join(" ", lines);
    }

    public static string BuildBeforeSellingSummary(SystemProfile profile)
    {
        var lines = new List<string>
        {
            "Disclose battery wear honestly; include a realistic unplugged-runtime note if wear is high.",
            "Confirm TPM / Secure Boot state in firmware or Windows Security if the buyer cares about Windows 11 readiness.",
            "Mention charger inclusion, SSD health snapshot from the scan, and any cosmetic issues.",
            "Wipe accounts and storage per your policy; factory reset is usually expected."
        };

        if (KyraHardwareFactsEngine.StorageLooksHealthyNvmeSsd(profile))
        {
            lines.Add("NVMe SSD health looks fine in the scan — highlight that, but still don’t promise SMART beyond what you can show.");
        }

        return string.Join(" ", lines);
    }
}
