using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

public enum ResaleAction
{
    SellNow,
    UpgradeFirst,
    PartsOnly
}

public sealed class PricingEstimate
{
    public decimal LowEstimate { get; init; }

    public decimal HighEstimate { get; init; }

    public double ConfidenceScore { get; init; }

    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();

    public ResaleAction RecommendedAction { get; init; } = ResaleAction.UpgradeFirst;

    public string ProviderName { get; init; } = "Local heuristic";

    public bool IsLocalEstimateOnly { get; init; } = true;
}

public sealed class PricingProviderResult
{
    public bool Succeeded { get; init; }

    public PricingEstimate? Estimate { get; init; }

    public string DiagnosticMessage { get; init; } = string.Empty;
}

public interface IPricingProvider
{
    string Id { get; }

    string DisplayName { get; }

    bool IsOnlineProvider { get; }

    bool EnabledByDefault { get; }

    bool IsConfigured { get; }

    PricingProviderResult Estimate(SystemProfile profile, SystemHealthEvaluation? healthEvaluation);
}

public sealed class PricingEngine
{
    public PricingEngine()
        : this([
            new LocalHeuristicPricingProvider(),
            new EbayPricingProvider(),
            new MarketplacePricingProvider(),
            new OfferUpPricingProvider()
        ])
    {
    }

    public PricingEngine(IReadOnlyList<IPricingProvider> providers)
    {
        Providers = providers;
    }

    public IReadOnlyList<IPricingProvider> Providers { get; }

    public PricingEstimate? Estimate(SystemProfile? profile, SystemHealthEvaluation? healthEvaluation = null)
    {
        if (profile is null)
        {
            return null;
        }

        foreach (var provider in Providers.Where(provider => provider.EnabledByDefault && provider.IsConfigured))
        {
            var result = provider.Estimate(profile, healthEvaluation);
            if (result.Succeeded && result.Estimate is not null)
            {
                return result.Estimate;
            }
        }

        return null;
    }
}

public sealed class LocalHeuristicPricingProvider : IPricingProvider
{
    public string Id => "local-heuristic";

    public string DisplayName => "Local Heuristic Pricing";

    public bool IsOnlineProvider => false;

    public bool EnabledByDefault => true;

    public bool IsConfigured => true;

    public PricingProviderResult Estimate(SystemProfile profile, SystemHealthEvaluation? healthEvaluation)
    {
        var assumptions = new List<string>
        {
            "Pricing Engine v0 uses local hardware heuristics only.",
            "No marketplace sold listings, scraping, or API pricing was used."
        };

        var cpu = ScoreCpu(profile.Cpu);
        var ramGb = profile.RamTotalGb ?? 0;
        var primaryDisk = SelectPrimaryDisk(profile);
        var storageGb = primaryDisk is null ? 0 : ParseStorageGigabytes(primaryDisk.Size);
        var storageIsSsd = primaryDisk is not null &&
                           (primaryDisk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                            primaryDisk.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                            primaryDisk.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                            primaryDisk.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase));
        var hasDedicatedGpu = profile.Gpus.Any(IsDedicatedGpu);
        var worstBatteryWear = profile.Batteries
            .Where(battery => battery.WearPercent.HasValue)
            .Select(battery => battery.WearPercent!.Value)
            .DefaultIfEmpty(0)
            .Max();

        var value = 75m;
        value += cpu.Tier switch
        {
            >= 8 => 260m,
            >= 6 => 190m,
            >= 4 => 130m,
            >= 2 => 75m,
            _ => 35m
        };
        assumptions.Add($"CPU classified as tier {cpu.Tier} ({cpu.Description}).");

        if (cpu.Generation is >= 12)
        {
            value += 85m;
            assumptions.Add($"CPU generation {cpu.Generation} receives a newer-platform bump.");
        }
        else if (cpu.Generation is > 0 and <= 7)
        {
            value -= 45m;
            assumptions.Add($"CPU generation {cpu.Generation} is older and limits Windows 11/resale appeal.");
        }
        else
        {
            assumptions.Add("CPU generation could not be confidently detected.");
        }

        if (ramGb >= 32)
        {
            value += 95m;
            assumptions.Add("32 GB or more RAM increases buyer appeal.");
        }
        else if (ramGb >= 16)
        {
            value += 55m;
            assumptions.Add("16 GB RAM meets the current resale baseline.");
        }
        else if (ramGb > 0)
        {
            value -= 35m;
            assumptions.Add($"{ramGb:0.#} GB RAM is below the 16 GB resale baseline.");
        }
        else
        {
            value -= 25m;
            assumptions.Add("RAM capacity is unknown.");
        }

        if (storageIsSsd)
        {
            value += 70m;
            assumptions.Add("SSD/NVMe storage improves perceived speed and resale value.");
        }
        else if (primaryDisk is not null)
        {
            value -= 50m;
            assumptions.Add("Storage is not clearly SSD/NVMe.");
        }
        else
        {
            value -= 60m;
            assumptions.Add("Storage details are unknown.");
        }

        if (storageGb >= 1000)
        {
            value += 55m;
            assumptions.Add("1 TB or larger storage adds listing value.");
        }
        else if (storageGb >= 512)
        {
            value += 25m;
            assumptions.Add("512 GB or larger storage is a solid resale baseline.");
        }
        else if (storageGb > 0 && storageGb < 256)
        {
            value -= 20m;
            assumptions.Add("Small storage capacity reduces buyer appeal.");
        }

        if (hasDedicatedGpu)
        {
            value += 140m;
            assumptions.Add("Dedicated GPU adds resale upside for creator/gaming buyers.");
        }
        else
        {
            assumptions.Add("No dedicated GPU was detected.");
        }

        if (IsPremiumBrand(profile))
        {
            value += 45m;
            assumptions.Add("Business/premium brand or model adds buyer confidence.");
        }

        if (worstBatteryWear >= 50)
        {
            value -= 85m;
            assumptions.Add($"Battery wear is high at {worstBatteryWear:0.#}%.");
        }
        else if (worstBatteryWear >= 35)
        {
            value -= 45m;
            assumptions.Add($"Battery wear is elevated at {worstBatteryWear:0.#}%.");
        }

        if (profile.Disks.Any(disk => IsBadDisk(disk)))
        {
            value -= 110m;
            assumptions.Add("Storage health/status reduces resale value.");
        }

        if (healthEvaluation?.HealthScore is < 55)
        {
            value -= 90m;
            assumptions.Add($"System health score is low ({healthEvaluation.HealthScore}/100).");
        }

        var action = DetermineAction(profile, healthEvaluation, ramGb, worstBatteryWear);
        var confidence = CalculateConfidence(profile, cpu, primaryDisk, healthEvaluation);
        var low = RoundToFive(Math.Max(25m, value * 0.78m));
        var high = RoundToFive(Math.Max(low + 20m, value * 1.18m));

        return new PricingProviderResult
        {
            Succeeded = true,
            Estimate = new PricingEstimate
            {
                LowEstimate = low,
                HighEstimate = high,
                ConfidenceScore = confidence,
                Assumptions = assumptions.Take(10).ToArray(),
                RecommendedAction = action,
                ProviderName = DisplayName,
                IsLocalEstimateOnly = true
            },
            DiagnosticMessage = "Local heuristic estimate generated."
        };
    }

    private static CpuPricingSignal ScoreCpu(string cpuName)
    {
        var text = cpuName ?? string.Empty;
        var generation = DetectCpuGeneration(text);
        if (Regex.IsMatch(text, @"(?i)\b(i9|ryzen\s*9|ultra\s*9|xeon)\b"))
        {
            return new CpuPricingSignal(9, generation, "high-end CPU");
        }

        if (Regex.IsMatch(text, @"(?i)\b(i7|ryzen\s*7|ultra\s*7)\b"))
        {
            return new CpuPricingSignal(7, generation, "upper-mid CPU");
        }

        if (Regex.IsMatch(text, @"(?i)\b(i5|ryzen\s*5|ultra\s*5)\b"))
        {
            return new CpuPricingSignal(5, generation, "mainstream CPU");
        }

        if (Regex.IsMatch(text, @"(?i)\b(i3|ryzen\s*3)\b"))
        {
            return new CpuPricingSignal(3, generation, "entry CPU");
        }

        if (Regex.IsMatch(text, @"(?i)\b(celeron|pentium|athlon)\b"))
        {
            return new CpuPricingSignal(1, generation, "budget CPU");
        }

        return new CpuPricingSignal(2, generation, "unknown CPU tier");
    }

    private static int? DetectCpuGeneration(string cpuName)
    {
        var intel = Regex.Match(cpuName, @"(?i)i[3579][-\s](?<model>\d{4,5})");
        if (intel.Success)
        {
            var model = intel.Groups["model"].Value;
            return model.Length == 5 ? int.Parse(model[..2], CultureInfo.InvariantCulture) : int.Parse(model[..1], CultureInfo.InvariantCulture);
        }

        var ryzen = Regex.Match(cpuName, @"(?i)ryzen\s+[3579]\s+(?<model>\d{4})");
        if (ryzen.Success)
        {
            return int.Parse(ryzen.Groups["model"].Value[..1], CultureInfo.InvariantCulture);
        }

        return null;
    }

    private static SystemDiskProfile? SelectPrimaryDisk(SystemProfile profile)
    {
        return profile.Disks
            .OrderByDescending(disk => ParseStorageGigabytes(disk.Size))
            .FirstOrDefault();
    }

    private static double ParseStorageGigabytes(string value)
    {
        var match = Regex.Match(value ?? string.Empty, @"(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>TB|GB|MB)", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups["value"].Value, out var number))
        {
            return 0;
        }

        return match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "TB" => number * 1024,
            "MB" => number / 1024,
            _ => number
        };
    }

    private static bool IsDedicatedGpu(SystemGpuProfile gpu)
    {
        var name = gpu.Name ?? string.Empty;
        return Regex.IsMatch(name, @"(?i)\b(nvidia|geforce|rtx|gtx|quadro|radeon\s+rx|amd\s+radeon|arc)\b") &&
               !Regex.IsMatch(name, @"(?i)\b(intel|uhd|iris|vega\s+\d?\b)\b");
    }

    private static bool IsPremiumBrand(SystemProfile profile)
    {
        var text = $"{profile.Manufacturer} {profile.Model}";
        return Regex.IsMatch(text, @"(?i)\b(latitude|precision|thinkpad|thinkbook|elitebook|probook|zbook|surface|xps|spectre|envy|macbook)\b");
    }

    private static bool IsBadDisk(SystemDiskProfile disk)
    {
        var healthBad = !string.IsNullOrWhiteSpace(disk.Health) &&
                        !disk.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) &&
                        !disk.Health.Equals("OK", StringComparison.OrdinalIgnoreCase) &&
                        !disk.Health.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
        var statusBad = disk.Status.Equals("WARNING", StringComparison.OrdinalIgnoreCase) ||
                        disk.Status.Equals("WATCH", StringComparison.OrdinalIgnoreCase);
        return healthBad || statusBad || disk.WearPercent is >= 80;
    }

    private static ResaleAction DetermineAction(SystemProfile profile, SystemHealthEvaluation? healthEvaluation, double ramGb, double worstBatteryWear)
    {
        if (healthEvaluation?.HealthScore is < 45 || profile.Disks.Any(IsBadDisk))
        {
            return ResaleAction.PartsOnly;
        }

        if ((ramGb > 0 && ramGb < 16) || worstBatteryWear >= 35 || profile.DiskStatus.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            return ResaleAction.UpgradeFirst;
        }

        return ResaleAction.SellNow;
    }

    private static double CalculateConfidence(SystemProfile profile, CpuPricingSignal cpu, SystemDiskProfile? primaryDisk, SystemHealthEvaluation? healthEvaluation)
    {
        var confidence = 0.42;
        if (!string.IsNullOrWhiteSpace(profile.Manufacturer) && !profile.Manufacturer.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.07;
        }

        if (!string.IsNullOrWhiteSpace(profile.Model) && !profile.Model.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            confidence += 0.07;
        }

        if (cpu.Tier > 2)
        {
            confidence += 0.08;
        }

        if (cpu.Generation.HasValue)
        {
            confidence += 0.06;
        }

        if (profile.RamTotalGb is > 0)
        {
            confidence += 0.07;
        }

        if (primaryDisk is not null)
        {
            confidence += 0.08;
        }

        if (profile.Batteries.Count > 0)
        {
            confidence += 0.05;
        }

        if (healthEvaluation is not null)
        {
            confidence += 0.05;
        }

        return Math.Round(Math.Min(0.85, confidence), 2);
    }

    private static decimal RoundToFive(decimal value)
    {
        return Math.Round(value / 5m, MidpointRounding.AwayFromZero) * 5m;
    }

    private sealed record CpuPricingSignal(int Tier, int? Generation, string Description);
}

public sealed class EbayPricingProvider : StubPricingProvider
{
    public EbayPricingProvider()
        : base("ebay-pricing", "eBay Pricing")
    {
    }
}

public sealed class MarketplacePricingProvider : StubPricingProvider
{
    public MarketplacePricingProvider()
        : base("marketplace-pricing", "Marketplace Pricing")
    {
    }
}

public sealed class OfferUpPricingProvider : StubPricingProvider
{
    public OfferUpPricingProvider()
        : base("offerup-pricing", "OfferUp Pricing")
    {
    }
}

public abstract class StubPricingProvider : IPricingProvider
{
    protected StubPricingProvider(string id, string displayName)
    {
        Id = id;
        DisplayName = displayName;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public bool IsOnlineProvider => true;

    public bool EnabledByDefault => false;

    public bool IsConfigured => false;

    public PricingProviderResult Estimate(SystemProfile profile, SystemHealthEvaluation? healthEvaluation)
    {
        return new PricingProviderResult
        {
            Succeeded = false,
            DiagnosticMessage = $"{DisplayName} is a pricing provider stub. API/scraping is not implemented."
        };
    }
}
