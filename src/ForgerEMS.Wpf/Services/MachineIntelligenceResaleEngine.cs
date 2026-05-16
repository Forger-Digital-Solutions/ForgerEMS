using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

public enum HardwareProbeStatus
{
    Ready,
    Partial,
    Failed,
    Timeout
}

public sealed class HardwareProbeWarning
{
    public string Field { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class HardwareProbeResult
{
    public HardwareProbeStatus Status { get; init; } = HardwareProbeStatus.Partial;
    public DeviceIdentityProfile Identity { get; init; } = new();
    public IReadOnlyList<HardwareProbeWarning> Warnings { get; init; } = Array.Empty<HardwareProbeWarning>();
}

public sealed class HardwareProbeTimeoutPolicy
{
    public int PerProbeTimeoutMs { get; init; } = 1800;
    public int OverallTimeoutMs { get; init; } = 5000;
}

public interface ISystemHardwareReader
{
    HardwareProbeResult Read(SystemProfile? profile, HardwareProbeTimeoutPolicy? timeoutPolicy = null);
}

public sealed class WindowsHardwareReader : ISystemHardwareReader
{
    public HardwareProbeResult Read(SystemProfile? profile, HardwareProbeTimeoutPolicy? timeoutPolicy = null)
    {
        timeoutPolicy ??= new HardwareProbeTimeoutPolicy();
        if (profile is null)
        {
            return new HardwareProbeResult
            {
                Status = HardwareProbeStatus.Failed,
                Warnings =
                [
                    new HardwareProbeWarning
                    {
                        Field = "SystemProfile",
                        Message = "Some hardware details could not be read. Try refreshing or running as administrator."
                    }
                ]
            };
        }

        var warnings = new List<HardwareProbeWarning>();
        var identity = DeviceIdentityProfile.FromSystemProfile(profile, warnings);
        var status = warnings.Count == 0 ? HardwareProbeStatus.Ready : HardwareProbeStatus.Partial;
        return new HardwareProbeResult { Status = status, Identity = identity, Warnings = warnings };
    }
}

public sealed class DeviceIdentityProfile
{
    public string Manufacturer { get; init; } = "Unknown";
    public string Model { get; init; } = "Unknown";
    public string ProductName { get; init; } = "Unknown";
    public string DeviceType { get; init; } = "unknown";
    public string OsVersion { get; init; } = "Unknown OS";
    public string CpuModel { get; init; } = "Unknown CPU";
    public string CpuClass { get; init; } = "unknown";
    public int? CpuCores { get; init; }
    public int? CpuThreads { get; init; }
    public string GpuSummary { get; init; } = "Unknown GPU";
    public string MemorySummary { get; init; } = "Unknown RAM";
    public string StorageSummary { get; init; } = "Unknown storage";
    public bool BatteryPresent { get; init; }

    public static DeviceIdentityProfile FromSystemProfile(SystemProfile profile, IList<HardwareProbeWarning> warnings)
    {
        var manufacturer = NormalizeUnknown(profile.Manufacturer, "manufacturer", warnings);
        var model = NormalizeUnknown(profile.Model, "model", warnings);
        var cpu = NormalizeUnknown(profile.Cpu, "cpu", warnings);
        var cpuClass = ClassifyCpu(cpu);
        var gpuSummary = profile.Gpus.Count == 0
            ? "Unknown GPU"
            : string.Join("; ", profile.Gpus.Select(g => g.Name).Take(3));
        if (profile.Gpus.Count == 0)
        {
            warnings.Add(new HardwareProbeWarning { Field = "gpu", Message = "GPU was not reported by this scan." });
        }

        var storageSummary = profile.Disks.Count == 0
            ? "Unknown storage"
            : string.Join("; ", profile.Disks.Select(d => $"{d.MediaType} {d.Size}").Take(3));
        if (profile.Disks.Count == 0)
        {
            warnings.Add(new HardwareProbeWarning { Field = "storage", Message = "Storage details are limited or unavailable." });
        }

        if (profile.RamTotalGb is null or <= 0)
        {
            warnings.Add(new HardwareProbeWarning { Field = "ram", Message = "RAM capacity was not fully reported." });
        }

        return new DeviceIdentityProfile
        {
            Manufacturer = manufacturer,
            Model = model,
            ProductName = $"{manufacturer} {model}".Trim(),
            DeviceType = GuessDeviceType(manufacturer, model, profile.Batteries.Count > 0),
            OsVersion = NormalizeUnknown(profile.OperatingSystem, "os", warnings),
            CpuModel = cpu,
            CpuClass = cpuClass,
            CpuCores = profile.CpuCores,
            CpuThreads = profile.CpuThreads,
            GpuSummary = gpuSummary,
            MemorySummary = $"{profile.RamTotal}; {profile.RamSpeed}",
            StorageSummary = storageSummary,
            BatteryPresent = profile.Batteries.Count > 0
        };
    }

    private static string NormalizeUnknown(string value, string fieldName, IList<HardwareProbeWarning> warnings)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add(new HardwareProbeWarning { Field = fieldName, Message = $"{fieldName} was not reported." });
            return "Unknown";
        }

        return value;
    }

    private static string GuessDeviceType(string manufacturer, string model, bool batteryPresent)
    {
        var text = $"{manufacturer} {model}";
        if (batteryPresent || Regex.IsMatch(text, "(laptop|notebook|thinkpad|latitude|elitebook|surface)", RegexOptions.IgnoreCase))
        {
            return "laptop";
        }

        if (Regex.IsMatch(text, "(mini|nuc|tiny|micro)", RegexOptions.IgnoreCase))
        {
            return "mini pc";
        }

        if (Regex.IsMatch(text, "(tower|desktop|workstation|optiplex|prodesk)", RegexOptions.IgnoreCase))
        {
            return "desktop";
        }

        return "unknown";
    }

    private static string ClassifyCpu(string cpu)
    {
        if (Regex.IsMatch(cpu, "(i9|ryzen\\s*9|threadripper|xeon)", RegexOptions.IgnoreCase))
        {
            return "workstation";
        }

        if (Regex.IsMatch(cpu, "(i7|ryzen\\s*7|ultra\\s*7)", RegexOptions.IgnoreCase))
        {
            return "midrange";
        }

        if (Regex.IsMatch(cpu, "(i5|ryzen\\s*5|ultra\\s*5)", RegexOptions.IgnoreCase))
        {
            return "midrange";
        }

        if (Regex.IsMatch(cpu, "(i3|ryzen\\s*3|celeron|pentium|athlon)", RegexOptions.IgnoreCase))
        {
            return "low-end";
        }

        return "unknown";
    }
}

public static class HardwarePrivacyRedactor
{
    public static string Redact(string value)
    {
        var redacted = CopilotRedactor.Redact(value, enabled: true);
        redacted = Regex.Replace(redacted, "(?i)(serial|service\\s*tag)\\s*[:=]\\s*[a-z0-9\\-]{4,}", "$1=[redacted]");
        return redacted;
    }
}

public enum ResaleConfidenceLevel
{
    VeryLow,
    Low,
    Medium,
    High
}

public sealed class ResaleConditionProfile
{
    public string CosmeticCondition { get; init; } = "Unknown";
    public string ScreenCondition { get; init; } = "Unknown";
    public string KeyboardTrackpadCondition { get; init; } = "Unknown";
    public string HingeCondition { get; init; } = "Unknown";
    public bool ChargerIncluded { get; init; }
    public bool BatteryHoldsCharge { get; init; } = true;
    public bool WindowsActivated { get; init; } = true;
    public bool FreshInstallCompleted { get; init; }
    public bool CleanedOrRepasted { get; init; }
    public bool MissingScrewsOrDamage { get; init; }
    public string KnownDefects { get; init; } = string.Empty;
}

public sealed class DeviceResaleProfile
{
    public DeviceIdentityProfile Identity { get; init; } = new();
    public SystemProfile RawSystemProfile { get; init; } = new();
    public ResaleConditionProfile Condition { get; init; } = new();
}

public sealed class MarketComparable
{
    public string Platform { get; init; } = "Manual";
    public string Title { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public string Condition { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
}

public sealed class MarketPricingResult
{
    public bool IsConfigured { get; init; }
    public bool HasData { get; init; }
    public string Status { get; init; } = "Unavailable";
    public IReadOnlyList<MarketComparable> Comparables { get; init; } = Array.Empty<MarketComparable>();
    public decimal? MedianPrice { get; init; }
}

public interface IMarketPricingService
{
    string ServiceName { get; }
    MarketPricingResult GetComparables(DeviceResaleProfile profile);
}

public sealed class EbayMarketPricingService : IMarketPricingService
{
    public string ServiceName => "eBay Active Comps";

    public MarketPricingResult GetComparables(DeviceResaleProfile profile)
    {
        return new MarketPricingResult
        {
            IsConfigured = false,
            HasData = false,
            Status = "Active eBay comps unavailable - API key/config not set. Sold comps are not configured."
        };
    }

    public static string BuildLaptopQuery(DeviceResaleProfile profile)
    {
        var ram = profile.RawSystemProfile.RamTotalGb is > 0
            ? FormattableString.Invariant($"{profile.RawSystemProfile.RamTotalGb:0.#}GB")
            : string.Empty;
        return string.Join(" ", new[]
        {
            profile.Identity.Manufacturer,
            profile.Identity.Model,
            profile.Identity.CpuModel,
            ram,
            "laptop"
        }.Where(item => !string.IsNullOrWhiteSpace(item)));
    }
}

public sealed class ManualComparablePricingService : IMarketPricingService
{
    private readonly IReadOnlyList<MarketComparable> _comparables;

    public ManualComparablePricingService(IReadOnlyList<MarketComparable> comparables)
    {
        _comparables = comparables;
    }

    public string ServiceName => "Manual Comparable Pricing";

    public MarketPricingResult GetComparables(DeviceResaleProfile profile)
    {
        var prices = _comparables.Select(c => c.Price).Where(p => p > 0).OrderBy(p => p).ToArray();
        if (prices.Length == 0)
        {
            return new MarketPricingResult { IsConfigured = true, HasData = false, Status = "No manual comps entered." };
        }

        var median = prices.Length % 2 == 1
            ? prices[prices.Length / 2]
            : (prices[(prices.Length / 2) - 1] + prices[prices.Length / 2]) / 2m;
        return new MarketPricingResult
        {
            IsConfigured = true,
            HasData = true,
            Status = "Manual comps available",
            Comparables = _comparables,
            MedianPrice = median
        };
    }
}

public sealed class FacebookMarketplacePricingService : IMarketPricingService
{
    public string ServiceName => "Facebook Marketplace";
    public MarketPricingResult GetComparables(DeviceResaleProfile profile) => new()
    {
        IsConfigured = false,
        HasData = false,
        Status = "Manual/future source only - no approved API configured."
    };
}

public sealed class OfferUpPricingService : IMarketPricingService
{
    public string ServiceName => "OfferUp";
    public MarketPricingResult GetComparables(DeviceResaleProfile profile) => new()
    {
        IsConfigured = false,
        HasData = false,
        Status = "Manual/future source only - no approved API configured."
    };
}

public sealed class ListingPriceEstimate
{
    public decimal MinimumAcceptablePrice { get; init; }
    public decimal QuickSalePrice { get; init; }
    public decimal FairListingPrice { get; init; }
    public decimal StretchListingPrice { get; init; }
    public decimal PartsPrice { get; init; }
    public ResaleConfidenceLevel Confidence { get; init; } = ResaleConfidenceLevel.Low;
    public string ConfidenceReason { get; init; } = string.Empty;
    public IReadOnlyList<string> ValueDrivers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ValueReducers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SuggestedUpgrades { get; init; } = Array.Empty<string>();
}

public enum FlipValueCondition
{
    Poor,
    Fair,
    Good,
    Excellent
}

public enum FlipValueSaleMode
{
    QuickSale,
    NormalLocal,
    ShippedOnline,
    PartsOnly
}

public sealed class LocationProfile
{
    public string PostalCode { get; init; } = string.Empty;
    public string City { get; init; } = string.Empty;
    public string StateOrRegion { get; init; } = string.Empty;
    public string Country { get; init; } = "US";
    public int Radius { get; init; } = 50;
    public string Currency { get; init; } = "USD";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(PostalCode) ||
        !string.IsNullOrWhiteSpace(City);
}

public sealed class FlipValueRequest
{
    public SystemProfile SystemProfile { get; init; } = new();
    public FlipValueCondition Condition { get; init; } = FlipValueCondition.Good;
    public bool? ChargerIncluded { get; init; }
    public LocationProfile Location { get; init; } = new();
    public FlipValueSaleMode SaleMode { get; init; } = FlipValueSaleMode.NormalLocal;
}

public sealed class FlipValueComparable
{
    public string Provider { get; init; } = "Manual";
    public string Title { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public bool IsSoldComp { get; init; }
    public bool IsLocal { get; init; }
    public DateTimeOffset? ObservedUtc { get; init; }
}

public sealed class FlipValueResult
{
    public decimal Low { get; init; }
    public decimal Expected { get; init; }
    public decimal High { get; init; }
    public decimal QuickSale { get; init; }
    public decimal PartsOnly { get; init; }
    public ResaleConfidenceLevel Confidence { get; init; } = ResaleConfidenceLevel.Low;
    public string ProviderStatus { get; init; } = string.Empty;
    public string LocationBasis { get; init; } = string.Empty;
    public int CompsUsed { get; init; }
    public int OutliersRemoved { get; init; }
    public IReadOnlyList<string> Assumptions { get; init; } = Array.Empty<string>();
    public string SuggestedListingTitle { get; init; } = string.Empty;
    public decimal RecommendedListingPrice { get; init; }
    public decimal RecommendedMinimumAcceptPrice { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

public interface IFlipValueProvider
{
    string Name { get; }
    bool IsConfigured { get; }
    bool UsesSoldComps { get; }
    IReadOnlyList<FlipValueComparable> GetComparables(FlipValueRequest request);
}

public sealed class LocalHeuristicFlipValueProvider : IFlipValueProvider
{
    public string Name => "Local heuristic";
    public bool IsConfigured => true;
    public bool UsesSoldComps => false;

    public IReadOnlyList<FlipValueComparable> GetComparables(FlipValueRequest request)
    {
        var profile = new DeviceResaleProfile
        {
            RawSystemProfile = request.SystemProfile,
            Identity = DeviceIdentityProfile.FromSystemProfile(request.SystemProfile, new List<HardwareProbeWarning>()),
            Condition = new ResaleConditionProfile
            {
                CosmeticCondition = request.Condition.ToString(),
                ChargerIncluded = request.ChargerIncluded == true
            }
        };
        var estimate = new OfflineResaleEstimator().Estimate(profile);
        return
        [
            new FlipValueComparable
            {
                Provider = Name,
                Title = "Offline heuristic estimate",
                Price = estimate.FairListingPrice,
                IsSoldComp = false,
                IsLocal = false,
                ObservedUtc = DateTimeOffset.UtcNow
            }
        ];
    }
}

public sealed class EbayActiveListingFlipValueProvider : IFlipValueProvider
{
    public string Name => "eBay active listings";
    public bool IsConfigured { get; init; }
    public bool UsesSoldComps => false;

    public IReadOnlyList<FlipValueComparable> GetComparables(FlipValueRequest request) => Array.Empty<FlipValueComparable>();
}

public sealed class ManualCompImportFlipValueProvider : IFlipValueProvider
{
    private readonly IReadOnlyList<FlipValueComparable> _comparables;

    public ManualCompImportFlipValueProvider(IReadOnlyList<FlipValueComparable> comparables)
    {
        _comparables = comparables;
    }

    public string Name => "Manual comparable import";
    public bool IsConfigured => _comparables.Count > 0;
    public bool UsesSoldComps => _comparables.Any(comp => comp.IsSoldComp);

    public IReadOnlyList<FlipValueComparable> GetComparables(FlipValueRequest request) => _comparables;
}

public sealed class FlipValueEngine
{
    private readonly IReadOnlyList<IFlipValueProvider> _providers;

    public FlipValueEngine()
        : this([new LocalHeuristicFlipValueProvider(), new EbayActiveListingFlipValueProvider()])
    {
    }

    public FlipValueEngine(IReadOnlyList<IFlipValueProvider> providers)
    {
        _providers = providers;
    }

    public FlipValueResult Estimate(FlipValueRequest request)
    {
        var assumptions = new List<string>();
        var providerStatuses = new List<string>();
        var comps = new List<FlipValueComparable>();
        foreach (var provider in _providers)
        {
            if (!provider.IsConfigured)
            {
                providerStatuses.Add($"{provider.Name}: not configured");
                continue;
            }

            var providerComps = provider.GetComparables(request);
            if (provider is EbayActiveListingFlipValueProvider ||
                provider.Name.Contains("active", StringComparison.OrdinalIgnoreCase))
            {
                assumptions.Add("eBay active listings are asking prices, not sold comps; they are weaker evidence.");
            }

            providerStatuses.Add(providerComps.Count > 0
                ? $"{provider.Name}: {providerComps.Count} comp(s)"
                : $"{provider.Name}: configured, no comps returned");
            comps.AddRange(providerComps);
        }

        if (!request.Location.IsConfigured)
        {
            assumptions.Add("Location not configured; using national/offline estimate basis.");
        }
        else
        {
            assumptions.Add($"Location basis: {request.Location.City} {request.Location.StateOrRegion} {request.Location.PostalCode}, radius {request.Location.Radius}.");
        }

        var deduped = comps
            .Where(comp => comp.Price > 0)
            .GroupBy(comp => $"{comp.Provider}|{comp.Title}|{comp.Price}")
            .Select(group => group.First())
            .OrderBy(comp => comp.Price)
            .ToArray();
        var filtered = RemoveOutliers(deduped, out var removed);
        var expected = filtered.Length == 0 ? 120m : Median(filtered.Select(comp => comp.Price).ToArray());
        var soldCount = filtered.Count(comp => comp.IsSoldComp);
        var activeCount = filtered.Length - soldCount;
        var confidence = soldCount >= 4 && request.Location.IsConfigured
            ? ResaleConfidenceLevel.High
            : filtered.Length >= 3
                ? ResaleConfidenceLevel.Medium
                : request.Location.IsConfigured
                    ? ResaleConfidenceLevel.Low
                    : ResaleConfidenceLevel.VeryLow;

        var profile = request.SystemProfile;
        var title = HardwarePrivacyRedactor.Redact($"{profile.Manufacturer} {profile.Model} {profile.Cpu} {profile.RamTotal}".Trim());
        return new FlipValueResult
        {
            Low = Round5(expected * 0.82m),
            Expected = Round5(expected),
            High = Round5(expected * 1.18m),
            QuickSale = Round5(expected * 0.72m),
            PartsOnly = Round5(Math.Max(20m, expected * 0.38m)),
            Confidence = confidence,
            ProviderStatus = string.Join("; ", providerStatuses),
            LocationBasis = request.Location.IsConfigured
                ? $"{request.Location.City} {request.Location.StateOrRegion} {request.Location.PostalCode}".Trim()
                : "Location not configured; national/offline basis",
            CompsUsed = filtered.Length,
            OutliersRemoved = removed,
            Assumptions = assumptions,
            SuggestedListingTitle = string.IsNullOrWhiteSpace(title) ? "Windows laptop - tested by ForgerEMS" : title,
            RecommendedListingPrice = Round5(expected * 1.08m),
            RecommendedMinimumAcceptPrice = Round5(expected * 0.75m),
            Reasons =
            [
                $"{soldCount} sold comp(s), {activeCount} active/asking comp(s).",
                request.Location.IsConfigured ? "Location profile configured." : "Location missing lowers confidence."
            ]
        };
    }

    private static FlipValueComparable[] RemoveOutliers(FlipValueComparable[] comps, out int removed)
    {
        removed = 0;
        if (comps.Length < 5)
        {
            return comps;
        }

        var prices = comps.Select(comp => comp.Price).OrderBy(price => price).ToArray();
        var median = Median(prices);
        var deviations = prices.Select(price => Math.Abs(price - median)).OrderBy(value => value).ToArray();
        var mad = Median(deviations);
        if (mad <= 0)
        {
            return comps;
        }

        var filtered = comps.Where(comp => Math.Abs(comp.Price - median) / mad <= 3.5m).ToArray();
        removed = comps.Length - filtered.Length;
        return filtered;
    }

    private static decimal Median(decimal[] values)
    {
        if (values.Length == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToArray();
        return sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2m;
    }

    private static decimal Round5(decimal value) => Math.Round(value / 5m, MidpointRounding.AwayFromZero) * 5m;
}

public sealed class RepairRoiEstimate
{
    public bool RecommendRepairBeforeSale { get; init; }
    public string Notes { get; init; } = string.Empty;
}

public enum ListingPlatformKind
{
    OfflineEstimate,
    EbayActiveComps,
    ManualLocalComparable
}

public sealed class ListingDraft
{
    public string Title { get; init; } = string.Empty;
    public string ShortDescription { get; init; } = string.Empty;
    public string FullDescription { get; init; } = string.Empty;
    public string SpecsBlock { get; init; } = string.Empty;
    public string ConditionNotes { get; init; } = string.Empty;
    public string IncludedAccessories { get; init; } = string.Empty;
    public IReadOnlyList<string> PhotoChecklist { get; init; } = Array.Empty<string>();
}

public interface IResalePricingService
{
    ListingPriceEstimate Estimate(DeviceResaleProfile profile);
    ListingDraft GenerateListingDraft(DeviceResaleProfile profile, ListingPriceEstimate estimate);
}

public sealed class OfflineResaleEstimator : IResalePricingService
{
    public ListingPriceEstimate Estimate(DeviceResaleProfile profile)
    {
        var baseEstimate = new PricingEngine().Estimate(profile.RawSystemProfile);
        var low = baseEstimate?.LowEstimate ?? 120m;
        var high = baseEstimate?.HighEstimate ?? 220m;
        var fair = Round5((low + high) / 2m);
        var quick = Round5(Math.Max(35m, fair * 0.85m));
        var min = Round5(Math.Max(25m, fair * 0.75m));
        var parts = Round5(Math.Max(20m, fair * 0.4m));
        var confidence = MapConfidence(baseEstimate?.ConfidenceScore);

        var reducers = new List<string>();
        if (profile.RawSystemProfile.Disks.Any(d => d.MediaType.Contains("HDD", StringComparison.OrdinalIgnoreCase)))
        {
            reducers.Add("HDD storage lowers resale readiness versus SSD/NVMe.");
        }

        if (!profile.Condition.ChargerIncluded)
        {
            reducers.Add("Missing charger reduces buyer confidence.");
        }

        if (!string.IsNullOrWhiteSpace(profile.Condition.KnownDefects))
        {
            reducers.Add($"Known defects: {profile.Condition.KnownDefects}");
        }

        var reason = confidence switch
        {
            ResaleConfidenceLevel.High => "High confidence: strong detected specs with enough condition details.",
            ResaleConfidenceLevel.Medium => "Medium confidence: strong specs but limited condition/comparable data.",
            ResaleConfidenceLevel.Low => "Low confidence: offline estimate with partial condition context.",
            _ => "Very low confidence: missing key hardware or condition details."
        };

        return new ListingPriceEstimate
        {
            MinimumAcceptablePrice = min,
            QuickSalePrice = quick,
            FairListingPrice = fair,
            StretchListingPrice = high,
            PartsPrice = parts,
            Confidence = confidence,
            ConfidenceReason = reason,
            ValueDrivers = baseEstimate?.Assumptions?.Take(4).ToArray() ?? Array.Empty<string>(),
            ValueReducers = reducers,
            SuggestedUpgrades = profile.RawSystemProfile.FlipValue.SuggestedUpgradeRecommendations.Take(5).ToArray()
        };
    }

    public ListingDraft GenerateListingDraft(DeviceResaleProfile profile, ListingPriceEstimate estimate)
    {
        var primaryDisk = profile.RawSystemProfile.Disks.Count > 0 ? profile.RawSystemProfile.Disks[0] : null;
        var title = $"{profile.Identity.Manufacturer} {profile.Identity.Model} / {profile.Identity.CpuModel} / {profile.RawSystemProfile.RamTotal} / {primaryDisk?.Size ?? "Storage unknown"} / {profile.RawSystemProfile.OperatingSystem}";
        title = HardwarePrivacyRedactor.Redact(title);

        var specs = string.Join(Environment.NewLine, new[]
        {
            $"CPU: {profile.Identity.CpuModel}",
            $"RAM: {profile.RawSystemProfile.RamTotal} ({profile.RawSystemProfile.RamSpeed})",
            $"GPU: {profile.Identity.GpuSummary}",
            $"Storage: {profile.Identity.StorageSummary}",
            $"OS: {profile.RawSystemProfile.OperatingSystem}"
        });

        return new ListingDraft
        {
            Title = title,
            ShortDescription = "Offline estimate only. Active comps/API data not required for this draft.",
            FullDescription = $"Specs{Environment.NewLine}{specs}{Environment.NewLine}{Environment.NewLine}Condition{Environment.NewLine}{BuildCondition(profile)}{Environment.NewLine}{Environment.NewLine}Recommended list: ${estimate.FairListingPrice:0}. Quick-sale: ${estimate.QuickSalePrice:0}.",
            SpecsBlock = specs,
            ConditionNotes = BuildCondition(profile),
            IncludedAccessories = profile.Condition.ChargerIncluded ? "Charger included." : "Charger not included.",
            PhotoChecklist =
            [
                "Front lid and overall exterior",
                "Keyboard + touchpad close-up",
                "Screen on with no dead pixels",
                "System info/spec screen",
                "Ports (both sides) and charger"
            ]
        };
    }

    private static string BuildCondition(DeviceResaleProfile profile)
    {
        return $"Cosmetic: {profile.Condition.CosmeticCondition}; Screen: {profile.Condition.ScreenCondition}; Keyboard/Trackpad: {profile.Condition.KeyboardTrackpadCondition}; Hinge: {profile.Condition.HingeCondition}; Battery holds charge: {profile.Condition.BatteryHoldsCharge}; Windows activated: {profile.Condition.WindowsActivated}.";
    }

    private static ResaleConfidenceLevel MapConfidence(double? score)
    {
        if (score is >= 0.75) return ResaleConfidenceLevel.High;
        if (score is >= 0.6) return ResaleConfidenceLevel.Medium;
        if (score is >= 0.45) return ResaleConfidenceLevel.Low;
        return ResaleConfidenceLevel.VeryLow;
    }

    private static decimal Round5(decimal value) => Math.Round(value / 5m, MidpointRounding.AwayFromZero) * 5m;
}
