using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public enum CopilotMode
{
    OfflineOnly,
    OnlineAssisted,
    HybridAuto
}

public enum CopilotProviderType
{
    LocalOffline,
    OpenAICompatible,
    AnthropicClaude,
    OllamaLocal,
    EbayPricing,
    GitHubReleases,
    ManufacturerSupport,
    MicrosoftDocs,
    LinuxReleaseInfo
}

public enum CopilotPromptMode
{
    General,
    Troubleshooting,
    FlipResale,
    Technician,
    ToolkitBuilder
}

public sealed class CopilotSettings
{
    public CopilotMode Mode { get; set; } = CopilotMode.OfflineOnly;

    public CopilotProviderType ProviderType { get; set; } = CopilotProviderType.LocalOffline;

    public string ModelName { get; set; } = "local-rules";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 12;

    public bool OfflineFallbackEnabled { get; set; } = true;

    public bool RedactContextEnabled { get; set; } = true;

    public int MaxContextCharacters { get; set; } = 6000;

    public bool UseLatestSystemScanContext { get; set; } = true;

    public Dictionary<string, CopilotProviderConfiguration> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CopilotProviderConfiguration
{
    public bool IsEnabled { get; set; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 12;

    public int MaxRequestsPerMinute { get; set; } = 12;

    public int MaxRetries { get; set; } = 1;
}

public sealed class CopilotRequest
{
    public string Prompt { get; init; } = string.Empty;

    public string SystemIntelligenceReportPath { get; init; } = string.Empty;

    public string ToolkitHealthReportPath { get; init; } = string.Empty;

    public string AppVersion { get; init; } = string.Empty;

    public IReadOnlyList<string> RecentLogLines { get; init; } = Array.Empty<string>();

    public UsbTargetInfo? SelectedUsbTarget { get; init; }

    public CopilotSettings Settings { get; init; } = new();
}

public sealed class CopilotResponse
{
    public string Text { get; init; } = string.Empty;

    public bool UsedOnlineData { get; init; }

    public string OnlineStatus { get; init; } = "Offline fallback";

    public CopilotProviderType ProviderType { get; init; } = CopilotProviderType.LocalOffline;

    public IReadOnlyList<string> ProviderNotes { get; init; } = Array.Empty<string>();
}

public sealed class CopilotContext
{
    public string UserQuestion { get; init; } = string.Empty;

    public string ContextText { get; init; } = string.Empty;

    public CopilotPromptMode PromptMode { get; init; }

    public SystemProfile? SystemProfile { get; init; }

    public SystemHealthEvaluation? HealthEvaluation { get; init; }

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public PricingEstimate? PricingEstimate { get; init; }
}

public sealed class CopilotProviderRequest
{
    public string Prompt { get; init; } = string.Empty;

    public CopilotContext Context { get; init; } = new();

    public CopilotSettings Settings { get; init; } = new();

    public CopilotProviderConfiguration ProviderConfiguration { get; init; } = new();
}

public sealed class CopilotProviderResult
{
    public bool Succeeded { get; init; }

    public bool UsedOnlineData { get; init; }

    public bool IsTransientFailure { get; init; }

    public string UserMessage { get; init; } = string.Empty;

    public string DiagnosticMessage { get; init; } = string.Empty;
}

public interface ICopilotProvider
{
    string Id { get; }

    string DisplayName { get; }

    CopilotProviderType ProviderType { get; }

    string Category { get; }

    bool IsOnlineProvider { get; }

    bool IsPaidProvider { get; }

    bool EnabledByDefault { get; }

    string DefaultBaseUrl { get; }

    string DefaultModelName { get; }

    string DefaultApiKeyEnvironmentVariable { get; }

    string StatusText { get; }

    bool IsConfigured(CopilotProviderConfiguration configuration);

    bool CanHandle(CopilotProviderRequest request);

    Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken);
}

public interface ICopilotProviderRegistry
{
    IReadOnlyList<ICopilotProvider> Providers { get; }

    ICopilotProvider? FindById(string id);

    ICopilotProvider? FindByType(CopilotProviderType providerType);
}

public interface ICopilotContextBuilder
{
    CopilotContext Build(CopilotRequest request);
}

public interface ICopilotSettingsStore
{
    CopilotSettings Load();

    void Save(CopilotSettings settings);
}

public interface ICopilotService
{
    Task<CopilotResponse> GenerateReplyAsync(CopilotRequest request, CancellationToken cancellationToken = default);
}

public sealed class SystemProfile
{
    public string Manufacturer { get; init; } = "Unknown";

    public string Model { get; init; } = "Unknown";

    public string OperatingSystem { get; init; } = "Unknown OS";

    public string OsBuild { get; init; } = "UNKNOWN";

    public string Cpu { get; init; } = "Unknown CPU";

    public int? CpuCores { get; init; }

    public int? CpuThreads { get; init; }

    public string RamTotal { get; init; } = "Unknown";

    public double? RamTotalGb { get; init; }

    public string RamSpeed { get; init; } = "UNKNOWN";

    public int? RamSlotsFree { get; init; }

    public string RamUpgradePath { get; init; } = string.Empty;

    public string RamStatus { get; init; } = "UNKNOWN";

    public IReadOnlyList<SystemGpuProfile> Gpus { get; init; } = Array.Empty<SystemGpuProfile>();

    public IReadOnlyList<SystemDiskProfile> Disks { get; init; } = Array.Empty<SystemDiskProfile>();

    public IReadOnlyList<SystemBatteryProfile> Batteries { get; init; } = Array.Empty<SystemBatteryProfile>();

    public bool? TpmPresent { get; init; }

    public bool? TpmReady { get; init; }

    public bool? SecureBoot { get; init; }

    public string OverallStatus { get; init; } = "UNKNOWN";

    public string DiskStatus { get; init; } = "UNKNOWN";

    public string BatteryStatus { get; init; } = "UNKNOWN";

    public string NetworkStatus { get; init; } = "UNKNOWN";

    public bool InternetCheck { get; init; }

    public int ApipaAdapterCount { get; init; }

    public int MissingGatewayAdapterCount { get; init; }

    public IReadOnlyList<string> ObviousProblems { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReportRecommendations { get; init; } = Array.Empty<string>();

    public FlipValueProfile FlipValue { get; init; } = new();
}

public sealed class SystemGpuProfile
{
    public string Name { get; init; } = "Unknown GPU";

    public string DriverVersion { get; init; } = "UNKNOWN";
}

public sealed class SystemDiskProfile
{
    public string Name { get; init; } = "Disk";

    public string MediaType { get; init; } = "UNKNOWN";

    public string Size { get; init; } = "UNKNOWN";

    public string Health { get; init; } = "Unknown";

    public string Status { get; init; } = "UNKNOWN";

    public double? TemperatureC { get; init; }

    public double? WearPercent { get; init; }
}

public sealed class SystemBatteryProfile
{
    public string Name { get; init; } = "Battery";

    public int? ChargePercent { get; init; }

    public double? WearPercent { get; init; }

    public int? CycleCount { get; init; }

    public bool? AcConnected { get; init; }

    public string Status { get; init; } = "UNKNOWN";
}

public sealed class FlipValueProfile
{
    public string EstimateType { get; init; } = "local estimate only";

    public string ProviderStatus { get; init; } = "Pricing provider not configured";

    public string EstimatedResaleRange { get; init; } = "UNKNOWN";

    public string RecommendedListPrice { get; init; } = "UNKNOWN";

    public string QuickSalePrice { get; init; } = "UNKNOWN";

    public string PartsRepairPrice { get; init; } = "UNKNOWN";

    public double? ConfidenceScore { get; init; }

    public IReadOnlyList<string> ValueDrivers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ValueReducers { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SuggestedUpgradeRecommendations { get; init; } = Array.Empty<string>();
}

public sealed class SystemHealthEvaluation
{
    public int HealthScore { get; init; }

    public IReadOnlyList<string> DetectedIssues { get; init; } = Array.Empty<string>();
}

public static class SystemProfileMapper
{
    public static SystemProfile FromJson(JsonElement root)
    {
        var summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement : default;
        var network = root.TryGetProperty("network", out var networkElement) ? networkElement : default;
        var flipValue = root.TryGetProperty("flipValue", out var flipValueElement) ? flipValueElement : default;

        return new SystemProfile
        {
            Manufacturer = GetJsonString(summary, "manufacturer", "Unknown"),
            Model = GetJsonString(summary, "model", "Unknown"),
            OperatingSystem = GetJsonString(summary, "os", "Unknown OS"),
            OsBuild = GetJsonString(summary, "osBuild", "UNKNOWN"),
            Cpu = GetJsonString(summary, "cpu", "Unknown CPU"),
            CpuCores = GetJsonInt(summary, "cpuCores"),
            CpuThreads = GetJsonInt(summary, "cpuLogicalProcessors"),
            RamTotal = GetJsonString(summary, "ramTotal", "Unknown"),
            RamTotalGb = ParseGigabytes(GetJsonString(summary, "ramTotal", string.Empty)),
            RamSpeed = GetJsonString(summary, "ramSpeed", "UNKNOWN"),
            RamSlotsFree = GetJsonInt(summary, "ramSlotsFree"),
            RamUpgradePath = GetJsonString(summary, "ramUpgradePath", string.Empty),
            RamStatus = GetJsonString(summary, "ramStatus", "UNKNOWN"),
            Gpus = MapGpus(summary),
            Disks = MapDisks(root),
            Batteries = MapBatteries(root),
            TpmPresent = GetJsonNullableBool(summary, "tpmPresent"),
            TpmReady = GetJsonNullableBool(summary, "tpmReady"),
            SecureBoot = GetJsonNullableBool(summary, "secureBoot"),
            OverallStatus = GetJsonString(root, "overallStatus", "UNKNOWN"),
            DiskStatus = GetJsonString(root, "diskStatus", "UNKNOWN"),
            BatteryStatus = GetJsonString(root, "batteryStatus", "UNKNOWN"),
            NetworkStatus = GetJsonString(network, "status", "UNKNOWN"),
            InternetCheck = GetJsonBool(network, "internetCheck"),
            ApipaAdapterCount = CountNetworkAdapters(network, "apipaDetected"),
            MissingGatewayAdapterCount = CountMissingGateways(network),
            ObviousProblems = GetStringArray(root, "obviousProblems"),
            ReportRecommendations = GetStringArray(root, "recommendations"),
            FlipValue = new FlipValueProfile
            {
                EstimateType = GetJsonString(flipValue, "estimateType", "local estimate only"),
                ProviderStatus = GetJsonString(flipValue, "providerStatus", "Pricing provider not configured"),
                EstimatedResaleRange = GetJsonString(flipValue, "estimatedResaleRange", "UNKNOWN"),
                RecommendedListPrice = GetJsonString(flipValue, "recommendedListPrice", "UNKNOWN"),
                QuickSalePrice = GetJsonString(flipValue, "quickSalePrice", "UNKNOWN"),
                PartsRepairPrice = GetJsonString(flipValue, "partsRepairPrice", "UNKNOWN"),
                ConfidenceScore = GetJsonDouble(flipValue, "confidenceScore"),
                ValueDrivers = GetStringArray(flipValue, "valueDrivers"),
                ValueReducers = GetStringArray(flipValue, "valueReducers"),
                SuggestedUpgradeRecommendations = GetStringArray(flipValue, "suggestedUpgradeRecommendations")
            }
        };
    }

    private static IReadOnlyList<SystemGpuProfile> MapGpus(JsonElement summary)
    {
        if (!summary.TryGetProperty("gpus", out var gpus) || gpus.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SystemGpuProfile>();
        }

        return gpus.EnumerateArray()
            .Select(gpu => new SystemGpuProfile
            {
                Name = GetJsonString(gpu, "name", "Unknown GPU"),
                DriverVersion = GetJsonString(gpu, "driverVersion", "UNKNOWN")
            })
            .ToArray();
    }

    private static IReadOnlyList<SystemDiskProfile> MapDisks(JsonElement root)
    {
        if (!root.TryGetProperty("disks", out var disks) || disks.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SystemDiskProfile>();
        }

        return disks.EnumerateArray()
            .Select(disk => new SystemDiskProfile
            {
                Name = GetJsonString(disk, "name", "Disk"),
                MediaType = GetJsonString(disk, "mediaType", "UNKNOWN"),
                Size = GetJsonString(disk, "size", "UNKNOWN"),
                Health = GetJsonString(disk, "health", "Unknown"),
                Status = GetJsonString(disk, "status", "UNKNOWN"),
                TemperatureC = GetJsonDouble(disk, "temperatureC"),
                WearPercent = GetJsonDouble(disk, "wearPercent")
            })
            .ToArray();
    }

    private static IReadOnlyList<SystemBatteryProfile> MapBatteries(JsonElement root)
    {
        if (!root.TryGetProperty("batteries", out var batteries) || batteries.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SystemBatteryProfile>();
        }

        return batteries.EnumerateArray()
            .Select(battery => new SystemBatteryProfile
            {
                Name = GetJsonString(battery, "name", "Battery"),
                ChargePercent = GetJsonInt(battery, "estimatedChargeRemaining"),
                WearPercent = GetJsonDouble(battery, "wearPercent"),
                CycleCount = GetJsonInt(battery, "cycleCount"),
                AcConnected = GetJsonNullableBool(battery, "acConnected"),
                Status = GetJsonString(battery, "status", "UNKNOWN")
            })
            .ToArray();
    }

    private static int CountNetworkAdapters(JsonElement network, string propertyName)
    {
        if (!network.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return adapters.EnumerateArray().Count(adapter => GetJsonBool(adapter, propertyName));
    }

    private static int CountMissingGateways(JsonElement network)
    {
        if (!network.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return adapters.EnumerateArray().Count(adapter => !GetJsonBool(adapter, "gatewayPresent"));
    }

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        return property.EnumerateArray()
            .Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() : item.ToString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static double? ParseGigabytes(string value)
    {
        var match = Regex.Match(value, @"(?<value>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>GB|TB|MB)", RegexOptions.IgnoreCase);
        if (!match.Success || !double.TryParse(match.Groups["value"].Value, out var number))
        {
            return null;
        }

        return match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "TB" => number * 1024,
            "MB" => number / 1024,
            _ => number
        };
    }

    private static string GetJsonString(JsonElement element, string propertyName, string fallback)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => fallback
        };
    }

    private static int? GetJsonInt(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out var value))
        {
            return value;
        }

        return int.TryParse(property.ToString(), out var parsed) ? parsed : null;
    }

    private static double? GetJsonDouble(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDouble(out var value))
        {
            return value;
        }

        return double.TryParse(property.ToString(), out var parsed) ? parsed : null;
    }

    private static bool GetJsonBool(JsonElement element, string propertyName)
    {
        return GetJsonNullableBool(element, propertyName) ?? false;
    }

    private static bool? GetJsonNullableBool(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(property.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}

public sealed class SystemHealthEvaluator
{
    public SystemHealthEvaluation Evaluate(SystemProfile? profile)
    {
        if (profile is null)
        {
            return new SystemHealthEvaluation
            {
                HealthScore = 0,
                DetectedIssues = ["No System Intelligence scan is available."]
            };
        }

        var score = 100;
        var issues = new List<string>();

        ApplyStatusPenalty(profile.OverallStatus, "Overall scan status needs attention.", 12, 22);
        ApplyStatusPenalty(profile.DiskStatus, "Storage status needs attention.", 18, 30);
        ApplyStatusPenalty(profile.BatteryStatus, "Battery status needs attention.", 8, 15);
        ApplyStatusPenalty(profile.RamStatus, "Memory pressure was detected during the scan.", 8, 15);

        if (profile.RamTotalGb is > 0 and < 16)
        {
            score -= 12;
            issues.Add($"RAM is below the 16 GB resale/performance baseline ({profile.RamTotal}).");
        }

        foreach (var disk in profile.Disks)
        {
            if (!IsHealthyDisk(disk))
            {
                score -= 18;
                issues.Add($"Storage needs review: {disk.Name} reports health {disk.Health} / status {disk.Status}.");
            }

            if (disk.WearPercent is >= 80)
            {
                score -= 10;
                issues.Add($"Storage wear is elevated on {disk.Name}: {disk.WearPercent:0.#}%.");
            }

            if (disk.TemperatureC is >= 55)
            {
                score -= 8;
                issues.Add($"Storage temperature is high on {disk.Name}: {disk.TemperatureC:0.#} C.");
            }
        }

        foreach (var battery in profile.Batteries)
        {
            if (battery.WearPercent is >= 35)
            {
                score -= 10;
                issues.Add($"Battery wear is high at {battery.WearPercent:0.#}%.");
            }

            if (battery.CycleCount is >= 700)
            {
                score -= 6;
                issues.Add($"Battery cycle count is high ({battery.CycleCount}).");
            }
        }

        if (profile.ApipaAdapterCount > 0)
        {
            score -= 10;
            issues.Add("An active network adapter has an APIPA address, which usually points to DHCP/network trouble.");
        }

        if (profile.MissingGatewayAdapterCount > 0)
        {
            score -= 8;
            issues.Add("An active network adapter has no default gateway.");
        }

        if (profile.TpmPresent == false || profile.TpmReady == false)
        {
            score -= 8;
            issues.Add("TPM is missing or not ready.");
        }

        if (profile.SecureBoot == false)
        {
            score -= 5;
            issues.Add("Secure Boot is disabled.");
        }

        foreach (var problem in profile.ObviousProblems.Where(problem => !problem.Contains("No obvious", StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            if (!issues.Any(issue => issue.Equals(problem, StringComparison.OrdinalIgnoreCase)))
            {
                score -= 4;
                issues.Add(problem);
            }
        }

        if (issues.Count == 0)
        {
            issues.Add("No obvious blocking problems detected locally.");
        }

        return new SystemHealthEvaluation
        {
            HealthScore = Math.Clamp(score, 0, 100),
            DetectedIssues = issues.Take(10).ToArray()
        };

        void ApplyStatusPenalty(string status, string issue, int watchPenalty, int warningPenalty)
        {
            if (status.Equals("WARNING", StringComparison.OrdinalIgnoreCase))
            {
                score -= warningPenalty;
                issues.Add(issue);
            }
            else if (status.Equals("WATCH", StringComparison.OrdinalIgnoreCase) || status.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                score -= watchPenalty;
                issues.Add(issue);
            }
        }
    }

    private static bool IsHealthyDisk(SystemDiskProfile disk)
    {
        var healthy = string.IsNullOrWhiteSpace(disk.Health) ||
                      disk.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ||
                      disk.Health.Equals("OK", StringComparison.OrdinalIgnoreCase);
        var ready = disk.Status.Equals("READY", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(disk.Status);
        return healthy && ready;
    }
}

public sealed class RecommendationEngine
{
    public IReadOnlyList<string> Generate(SystemProfile? profile, SystemHealthEvaluation evaluation)
    {
        if (profile is null)
        {
            return ["Run System Intelligence first so Kyra can use local hardware facts."];
        }

        var recommendations = new List<string>();
        AddRange(profile.ReportRecommendations);

        if (profile.RamTotalGb is > 0 and < 16)
        {
            Add("Upgrade to at least 16 GB RAM before selling or for smoother Windows 11 use.");
        }

        if (profile.Disks.Count == 0 || profile.DiskStatus.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            Add("Run elevated SMART/storage diagnostics before pricing or diagnosing lag.");
        }
        else if (profile.Disks.Any(disk => !disk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) && !disk.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase)))
        {
            Add("Replace slow or unknown storage with a known-good SSD before resale when practical.");
        }

        if (profile.Disks.Any(disk => !disk.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) && !disk.Health.Equals("OK", StringComparison.OrdinalIgnoreCase)))
        {
            Add("Replace questionable storage or list the machine as parts/repair.");
        }

        if (profile.Batteries.Any(battery => battery.WearPercent is >= 35))
        {
            Add("Replace the battery before sale or disclose battery wear clearly.");
        }

        if (profile.ApipaAdapterCount > 0 || profile.MissingGatewayAdapterCount > 0)
        {
            Add("Fix network/DHCP or gateway issues before relying on updates, downloads, or online pricing.");
        }

        if (profile.TpmPresent == false || profile.TpmReady == false || profile.SecureBoot == false)
        {
            Add("Confirm TPM and Secure Boot state before presenting this as Windows 11-ready.");
        }

        AddRange(profile.FlipValue.SuggestedUpgradeRecommendations);

        if (evaluation.HealthScore < 55)
        {
            Add("Treat this as repair-first or parts/repair until the highest severity scan issues are resolved.");
        }

        return recommendations.Count == 0
            ? ["No urgent upgrade is required from the local scan; clean, update, verify drivers, and photograph condition before listing."]
            : recommendations.Take(10).ToArray();

        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !recommendations.Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                recommendations.Add(value);
            }
        }

        void AddRange(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }
    }
}

public sealed class CopilotProviderRegistry : ICopilotProviderRegistry
{
    public CopilotProviderRegistry()
    {
        Providers =
        [
            new LocalOfflineCopilotProvider(),
            new OpenAICompatibleCopilotProvider(),
            new AnthropicClaudeCopilotProvider(),
            new OllamaCopilotProvider(),
            new StubCopilotProvider(CopilotProviderType.EbayPricing, "ebay-sold-listings", "eBay Sold Listings", "Pricing", "Provider hook ready; configure API access later for real sold-listing comps."),
            new StubCopilotProvider(CopilotProviderType.GitHubReleases, "github-releases", "GitHub Releases", "Toolkit updates", "Provider hook ready; public release lookup can be added without paid dependencies."),
            new StubCopilotProvider(CopilotProviderType.ManufacturerSupport, "manufacturer-support", "Manufacturer Support Lookup", "Drivers/BIOS", "Provider hook ready; future lookup must use sanitized model/manufacturer only."),
            new StubCopilotProvider(CopilotProviderType.MicrosoftDocs, "microsoft-support-docs", "Microsoft/Windows Support Docs", "Windows docs", "Provider hook ready; docs lookup should never send service tags or usernames."),
            new StubCopilotProvider(CopilotProviderType.LinuxReleaseInfo, "linux-release-info", "Ubuntu/Mint/Xubuntu Release Info", "Linux support", "Provider hook ready for public distro support-window checks.")
        ];
    }

    public IReadOnlyList<ICopilotProvider> Providers { get; }

    public ICopilotProvider? FindById(string id)
    {
        return Providers.FirstOrDefault(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ICopilotProvider? FindByType(CopilotProviderType providerType)
    {
        return Providers.FirstOrDefault(provider => provider.ProviderType == providerType);
    }
}

public sealed class CopilotService : ICopilotService
{
    private readonly ICopilotProviderRegistry _providerRegistry;
    private readonly ICopilotContextBuilder _contextBuilder;
    private readonly Dictionary<string, Queue<DateTimeOffset>> _providerRequests = new(StringComparer.OrdinalIgnoreCase);

    public CopilotService(ICopilotProviderRegistry providerRegistry)
        : this(providerRegistry, new CopilotContextBuilder())
    {
    }

    public CopilotService(ICopilotProviderRegistry providerRegistry, ICopilotContextBuilder contextBuilder)
    {
        _providerRegistry = providerRegistry;
        _contextBuilder = contextBuilder;
    }

    public async Task<CopilotResponse> GenerateReplyAsync(CopilotRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = request.Settings ?? new CopilotSettings();
            EnsureProviderDefaults(settings);
            var context = _contextBuilder.Build(request);
            var notes = new List<string>();
            var localProvider = _providerRegistry.FindByType(CopilotProviderType.LocalOffline) ?? new LocalOfflineCopilotProvider();
            var localResult = await RunProviderSafeAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);

            if (settings.Mode == CopilotMode.OfflineOnly)
            {
                return BuildResponse(localResult, localProvider, notes, "Offline Only - no data leaves this machine.");
            }

            var candidates = SelectOnlineProviders(request, settings, context).ToArray();
            if (candidates.Length == 0)
            {
                var status = settings.Mode == CopilotMode.OnlineAssisted
                    ? "Online Assisted selected, but no configured provider is available. Offline fallback shown."
                    : "Hybrid Auto used offline fallback; no useful configured online provider was available.";
                return BuildResponse(localResult, localProvider, notes, status);
            }

            foreach (var provider in candidates)
            {
                var result = await RunProviderSafeAsync(provider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    return BuildResponse(result, provider, notes, "Online lookup enabled - sanitized provider context used.");
                }
            }

            if (settings.OfflineFallbackEnabled)
            {
                return BuildResponse(localResult, localProvider, notes, "Provider lookup failed or timed out. Offline fallback shown.");
            }

            return new CopilotResponse
            {
                Text = "Copilot could not get a provider response and offline fallback is disabled. Re-enable offline fallback or check provider settings.",
                OnlineStatus = "Error state - no fallback available.",
                ProviderNotes = notes
            };
        }
        catch (OperationCanceledException)
        {
            return new CopilotResponse
            {
                Text = "Copilot generation was stopped.",
                OnlineStatus = "Stopped",
                ProviderNotes = ["Request cancelled by operator."]
            };
        }
        catch (Exception exception)
        {
            return new CopilotResponse
            {
                Text = "Copilot hit an internal error and fell back safely. Try again after refreshing the System Intelligence scan.",
                OnlineStatus = "Error state - safe fallback",
                ProviderNotes = [$"Internal Copilot error: {exception.Message}"]
            };
        }
    }

    private async Task<CopilotProviderResult> RunProviderSafeAsync(
        ICopilotProvider provider,
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var providerConfig = GetProviderConfig(settings, provider);
        if (!provider.IsConfigured(providerConfig))
        {
            notes.Add($"{provider.DisplayName}: not configured");
            return new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = $"{provider.DisplayName} is not configured.",
                DiagnosticMessage = provider.StatusText
            };
        }

        if (!TryEnterRateLimit(provider, providerConfig, notes))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = $"{provider.DisplayName} rate limit reached.",
                DiagnosticMessage = "Rate limit reached."
            };
        }

        var providerRequest = new CopilotProviderRequest
        {
            Prompt = request.Prompt,
            Context = context,
            Settings = settings,
            ProviderConfiguration = providerConfig
        };

        var attempts = Math.Clamp(providerConfig.MaxRetries, 0, 3) + 1;
        CopilotProviderResult lastResult = new()
        {
            Succeeded = false,
            UserMessage = $"{provider.DisplayName} did not return a response."
        };

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(providerConfig.TimeoutSeconds, 2, 60)));
                lastResult = await provider.GenerateAsync(providerRequest, timeout.Token).ConfigureAwait(false);
                notes.Add($"{provider.DisplayName}: {(lastResult.Succeeded ? "OK" : lastResult.DiagnosticMessage)}");
                if (lastResult.Succeeded || !lastResult.IsTransientFailure)
                {
                    return lastResult;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    IsTransientFailure = true,
                    UserMessage = $"{provider.DisplayName} timed out.",
                    DiagnosticMessage = "Provider timeout."
                };
                notes.Add($"{provider.DisplayName}: timeout on attempt {attempt}");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException exception)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    IsTransientFailure = true,
                    UserMessage = $"{provider.DisplayName} network request failed.",
                    DiagnosticMessage = exception.Message
                };
                notes.Add($"{provider.DisplayName}: network failure on attempt {attempt}");
            }
            catch (Exception exception)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    UserMessage = $"{provider.DisplayName} failed safely.",
                    DiagnosticMessage = exception.Message
                };
                notes.Add($"{provider.DisplayName}: failed safely ({exception.Message})");
                return lastResult;
            }

            if (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        return lastResult;
    }

    private IEnumerable<ICopilotProvider> SelectOnlineProviders(CopilotRequest request, CopilotSettings settings, CopilotContext context)
    {
        if (settings.Mode == CopilotMode.HybridAuto && !ShouldUseOnline(context))
        {
            return Array.Empty<ICopilotProvider>();
        }

        return _providerRegistry.Providers
            .Where(provider => provider.ProviderType != CopilotProviderType.LocalOffline)
            .Where(provider => GetProviderConfig(settings, provider).IsEnabled)
            .Where(provider => provider.CanHandle(new CopilotProviderRequest
            {
                Prompt = request.Prompt,
                Context = context,
                Settings = settings,
                ProviderConfiguration = GetProviderConfig(settings, provider)
            }))
            .OrderBy(provider => provider.ProviderType == settings.ProviderType ? 0 : 1)
            .ThenBy(provider => provider.IsPaidProvider ? 1 : 0);
    }

    private static bool ShouldUseOnline(CopilotContext context)
    {
        return context.PromptMode is CopilotPromptMode.FlipResale or CopilotPromptMode.ToolkitBuilder ||
               context.UserQuestion.Contains("driver", StringComparison.OrdinalIgnoreCase) ||
               context.UserQuestion.Contains("bios", StringComparison.OrdinalIgnoreCase) ||
               context.UserQuestion.Contains("research", StringComparison.OrdinalIgnoreCase) ||
               context.UserQuestion.Contains("lookup", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryEnterRateLimit(ICopilotProvider provider, CopilotProviderConfiguration configuration, List<string> notes)
    {
        var now = DateTimeOffset.UtcNow;
        if (!_providerRequests.TryGetValue(provider.Id, out var queue))
        {
            queue = new Queue<DateTimeOffset>();
            _providerRequests[provider.Id] = queue;
        }

        while (queue.Count > 0 && now - queue.Peek() > TimeSpan.FromMinutes(1))
        {
            queue.Dequeue();
        }

        if (queue.Count >= Math.Max(1, configuration.MaxRequestsPerMinute))
        {
            notes.Add($"{provider.DisplayName}: rate limit reached");
            return false;
        }

        queue.Enqueue(now);
        return true;
    }

    public void EnsureProviderDefaults(CopilotSettings settings)
    {
        foreach (var provider in _providerRegistry.Providers)
        {
            _ = GetProviderConfig(settings, provider);
        }
    }

    private static CopilotProviderConfiguration GetProviderConfig(CopilotSettings settings, ICopilotProvider provider)
    {
        if (!settings.Providers.TryGetValue(provider.Id, out var providerConfig))
        {
            providerConfig = new CopilotProviderConfiguration
            {
                IsEnabled = provider.EnabledByDefault,
                BaseUrl = provider.DefaultBaseUrl,
                ModelName = provider.DefaultModelName,
                ApiKeyEnvironmentVariable = provider.DefaultApiKeyEnvironmentVariable,
                TimeoutSeconds = settings.TimeoutSeconds,
                MaxRequestsPerMinute = 12,
                MaxRetries = provider.IsOnlineProvider ? 1 : 0
            };
            settings.Providers[provider.Id] = providerConfig;
        }

        if (string.IsNullOrWhiteSpace(providerConfig.BaseUrl))
        {
            providerConfig.BaseUrl = provider.DefaultBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(providerConfig.ModelName))
        {
            providerConfig.ModelName = provider.DefaultModelName;
        }

        if (string.IsNullOrWhiteSpace(providerConfig.ApiKeyEnvironmentVariable))
        {
            providerConfig.ApiKeyEnvironmentVariable = provider.DefaultApiKeyEnvironmentVariable;
        }

        if (providerConfig.TimeoutSeconds <= 0)
        {
            providerConfig.TimeoutSeconds = Math.Max(2, settings.TimeoutSeconds);
        }

        return providerConfig;
    }

    private static CopilotResponse BuildResponse(CopilotProviderResult result, ICopilotProvider provider, IReadOnlyList<string> notes, string status)
    {
        return new CopilotResponse
        {
            Text = string.IsNullOrWhiteSpace(result.UserMessage)
                ? "Copilot could not produce a response."
                : result.UserMessage,
            UsedOnlineData = result.UsedOnlineData,
            OnlineStatus = status,
            ProviderType = provider.ProviderType,
            ProviderNotes = notes
        };
    }
}

public sealed class CopilotContextBuilder : ICopilotContextBuilder
{
    private readonly SystemHealthEvaluator _healthEvaluator = new();
    private readonly RecommendationEngine _recommendationEngine = new();
    private readonly PricingEngine _pricingEngine = new();

    public CopilotContext Build(CopilotRequest request)
    {
        var settings = request.Settings ?? new CopilotSettings();
        var promptMode = DetectPromptMode(request.Prompt);
        var profile = settings.UseLatestSystemScanContext
            ? LoadSystemProfile(request.SystemIntelligenceReportPath)
            : null;
        var health = _healthEvaluator.Evaluate(profile);
        var recommendations = _recommendationEngine.Generate(profile, health);
        var pricingEstimate = _pricingEngine.Estimate(profile, health);
        var parts = new List<string>
        {
            PromptTemplates.GetSystemPrompt(promptMode),
            $"User question: {CopilotRedactor.Redact(request.Prompt, settings.RedactContextEnabled)}",
            $"App version: {CopilotRedactor.Redact(request.AppVersion, settings.RedactContextEnabled)}"
        };

        if (settings.UseLatestSystemScanContext)
        {
            parts.Add(BuildSystemSummary(request.SystemIntelligenceReportPath, profile, health, recommendations, pricingEstimate, settings.RedactContextEnabled));
        }

        parts.Add(BuildUsbSummary(request.SelectedUsbTarget, settings.RedactContextEnabled));
        parts.Add(BuildToolkitSummary(request.ToolkitHealthReportPath, settings.RedactContextEnabled));
        parts.Add(BuildLogSummary(request.RecentLogLines, settings.RedactContextEnabled));

        var contextText = string.Join(Environment.NewLine + Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (settings.MaxContextCharacters > 0 && contextText.Length > settings.MaxContextCharacters)
        {
            contextText = contextText[..settings.MaxContextCharacters] + Environment.NewLine + "[context trimmed]";
        }

        return new CopilotContext
        {
            UserQuestion = request.Prompt,
            ContextText = contextText,
            PromptMode = promptMode,
            SystemProfile = profile,
            HealthEvaluation = health,
            Recommendations = recommendations,
            PricingEstimate = pricingEstimate
        };
    }

    private static CopilotPromptMode DetectPromptMode(string prompt)
    {
        var text = prompt.ToLowerInvariant();
        if (text.Contains("worth") || text.Contains("sell") || text.Contains("resale") || text.Contains("price") || text.Contains("upgrade"))
        {
            return CopilotPromptMode.FlipResale;
        }

        if (text.Contains("usb") || text.Contains("toolkit") || text.Contains("iso") || text.Contains("ventoy"))
        {
            return CopilotPromptMode.ToolkitBuilder;
        }

        if (text.Contains("repair") || text.Contains("fix") || text.Contains("diagnose") || text.Contains("step"))
        {
            return CopilotPromptMode.Technician;
        }

        if (text.Contains("slow") || text.Contains("lag") || text.Contains("not showing") || text.Contains("missing") || text.Contains("os"))
        {
            return CopilotPromptMode.Troubleshooting;
        }

        return CopilotPromptMode.General;
    }

    private static SystemProfile? LoadSystemProfile(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            return SystemProfileMapper.FromJson(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSystemSummary(
        string reportPath,
        SystemProfile? profile,
        SystemHealthEvaluation health,
        IReadOnlyList<string> recommendations,
        PricingEstimate? pricingEstimate,
        bool redact)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return "System Intelligence: not available. Ask the user to run System Scan for better local context.";
        }

        if (profile is null)
        {
            return "System Intelligence: report could not be parsed. Ask the user to rerun System Scan.";
        }

        var gpuLine = profile.Gpus.Count == 0
            ? "Unknown GPU"
            : string.Join("; ", profile.Gpus.Select(gpu => $"{gpu.Name} driver {gpu.DriverVersion}").Take(4));
        var storageLine = profile.Disks.Count == 0
            ? "No disk health counters available"
            : string.Join("; ", profile.Disks.Select(disk => $"{disk.Name} {disk.MediaType} {disk.Size} health {disk.Health} status {disk.Status} wear {FormatNullable(disk.WearPercent, "%")} temp {FormatNullable(disk.TemperatureC, " C")}").Take(4));
        var batteryLine = profile.Batteries.Count == 0
            ? "No battery detected"
            : string.Join("; ", profile.Batteries.Select(battery => $"{battery.Name} wear {FormatNullable(battery.WearPercent, "%")} cycles {FormatNullable(battery.CycleCount)} AC {FormatNullableBool(battery.AcConnected)} status {battery.Status}").Take(3));

        var lines = new List<string>
        {
            "System Intelligence summary:",
            $"Model: {profile.Manufacturer} {profile.Model}",
            $"OS: {profile.OperatingSystem} build {profile.OsBuild}",
            $"CPU: {profile.Cpu}; cores {FormatNullable(profile.CpuCores)}; threads {FormatNullable(profile.CpuThreads)}",
            $"RAM: {profile.RamTotal} @ {profile.RamSpeed}; free slots {FormatNullable(profile.RamSlotsFree)}; upgrade path {profile.RamUpgradePath}",
            $"GPU: {gpuLine}",
            $"Storage: {storageLine}",
            $"Battery: {batteryLine}",
            $"Security: TPM present {FormatNullableBool(profile.TpmPresent)}, TPM ready {FormatNullableBool(profile.TpmReady)}, Secure Boot {FormatNullableBool(profile.SecureBoot)}",
            $"Network: {profile.NetworkStatus}; APIPA adapters {profile.ApipaAdapterCount}; missing gateway adapters {profile.MissingGatewayAdapterCount}; internet check {profile.InternetCheck}",
            $"Overall status: {profile.OverallStatus}",
            $"Health score: {health.HealthScore}/100",
            $"Detected issues: {string.Join("; ", health.DetectedIssues.Take(8))}",
            $"Recommendations: {string.Join("; ", recommendations.Take(8))}",
            pricingEstimate is null
                ? "Pricing Engine v0: not available"
                : $"Pricing Engine v0: ${pricingEstimate.LowEstimate:0} - ${pricingEstimate.HighEstimate:0}; confidence {pricingEstimate.ConfidenceScore:0.##}; action {FormatResaleAction(pricingEstimate.RecommendedAction)}; provider {pricingEstimate.ProviderName}; local estimate only {pricingEstimate.IsLocalEstimateOnly}",
            pricingEstimate is null
                ? string.Empty
                : $"Pricing assumptions: {string.Join("; ", pricingEstimate.Assumptions.Take(8))}",
            $"Flip estimate: {profile.FlipValue.EstimatedResaleRange} ({profile.FlipValue.EstimateType}; {profile.FlipValue.ProviderStatus}; confidence {FormatNullable(profile.FlipValue.ConfidenceScore)})",
            $"Value drivers: {string.Join("; ", profile.FlipValue.ValueDrivers.Take(5))}",
            $"Value reducers: {string.Join("; ", profile.FlipValue.ValueReducers.Take(5))}",
            $"Problems: {string.Join("; ", profile.ObviousProblems.Take(8))}"
        };

        return CopilotRedactor.Redact(string.Join(Environment.NewLine, lines), redact);
    }

    private static string FormatNullable(double? value, string suffix = "")
    {
        return value.HasValue ? $"{value.Value:0.#}{suffix}" : "UNKNOWN";
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "UNKNOWN";
    }

    private static string FormatNullableBool(bool? value)
    {
        return value.HasValue ? value.Value.ToString() : "UNKNOWN";
    }

    private static string FormatResaleAction(ResaleAction action)
    {
        return action switch
        {
            ResaleAction.SellNow => "sell now",
            ResaleAction.PartsOnly => "parts only",
            _ => "upgrade first"
        };
    }

    private static string BuildUsbSummary(UsbTargetInfo? target, bool redact)
    {
        if (target is null)
        {
            return "USB target: none selected.";
        }

        return CopilotRedactor.Redact(
            $"USB target: {target.RootPath} {target.LabelDisplay}; {target.DisplayTotalBytes}; {target.FileSystem}; {target.SelectionStatusText}; benchmark {target.BenchmarkStatusDisplay}; write {target.WriteSpeedDisplayNormalized}; read {target.ReadSpeedDisplayNormalized}; warning {target.SelectionWarningDisplay}",
            redact);
    }

    private static string BuildToolkitSummary(string reportPath, bool redact)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return "Toolkit health: no latest toolkit-health report found.";
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            var lines = new List<string>
            {
                $"Toolkit health verdict: {GetJsonString(root, "healthVerdict", "Unknown")}"
            };

            if (root.TryGetProperty("summary", out var summary))
            {
                lines.Add($"Toolkit summary: installed {GetJsonString(summary, "installed", "0")}; missing {GetJsonString(summary, "missingRequired", "0")}; failed {GetJsonString(summary, "failed", "0")}; manual {GetJsonString(summary, "manual", "0")}");
            }

            return CopilotRedactor.Redact(string.Join(Environment.NewLine, lines), redact);
        }
        catch (Exception exception)
        {
            return $"Toolkit health: report could not be parsed ({exception.Message}).";
        }
    }

    private static string BuildLogSummary(IReadOnlyList<string> logs, bool redact)
    {
        var safeLines = logs
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(12)
            .Select(line => CopilotRedactor.Redact(line, redact))
            .ToArray();

        return safeLines.Length == 0
            ? "Recent logs: none supplied."
            : "Recent safe log snippets:" + Environment.NewLine + string.Join(Environment.NewLine, safeLines);
    }

    private static string GetJsonString(JsonElement element, string propertyName, string fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => fallback
        };
    }
}

public static class CopilotRedactor
{
    public static string Redact(string value, bool enabled = true)
    {
        if (!enabled || string.IsNullOrEmpty(value))
        {
            return value;
        }

        var redacted = Regex.Replace(value, @"(?i)(api[_-]?key|token|secret|password)\s*[:=]\s*['""]?[^'""\s;]+", "$1=[redacted]");
        redacted = Regex.Replace(redacted, @"[A-Za-z]:\\Users\\([^\\\s]+)", @"C:\Users\[redacted]");
        redacted = Regex.Replace(redacted, @"[A-Za-z]:\\[^\r\n\t ]+", "[local path]");
        redacted = Regex.Replace(redacted, @"(?i)\b(service tag|serial|s/n)\s*[:#]?\s*[A-Z0-9-]{5,}\b", "$1 [redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\bsk-[A-Za-z0-9_-]{12,}\b", "[api key redacted]");
        return redacted;
    }
}

public sealed class CopilotSettingsStore : ICopilotSettingsStore
{
    private readonly string _path;
    private readonly ICopilotProviderRegistry _registry;

    public CopilotSettingsStore(string path, ICopilotProviderRegistry registry)
    {
        _path = path;
        _registry = registry;
    }

    public CopilotSettings Load()
    {
        CopilotSettings settings;
        try
        {
            settings = File.Exists(_path)
                ? JsonSerializer.Deserialize<CopilotSettings>(File.ReadAllText(_path)) ?? new CopilotSettings()
                : new CopilotSettings();
        }
        catch
        {
            settings = new CopilotSettings();
        }

        ApplyDefaults(settings);
        return settings;
    }

    public void Save(CopilotSettings settings)
    {
        ApplyDefaults(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ApplyDefaults(CopilotSettings settings)
    {
        settings.TimeoutSeconds = settings.TimeoutSeconds <= 0 ? 12 : settings.TimeoutSeconds;
        settings.MaxContextCharacters = settings.MaxContextCharacters <= 0 ? 6000 : settings.MaxContextCharacters;
        settings.ModelName = string.IsNullOrWhiteSpace(settings.ModelName) ? "local-rules" : settings.ModelName;

        foreach (var provider in _registry.Providers)
        {
            if (!settings.Providers.TryGetValue(provider.Id, out var providerConfig))
            {
                providerConfig = new CopilotProviderConfiguration();
                settings.Providers[provider.Id] = providerConfig;
            }

            providerConfig.IsEnabled = providerConfig.IsEnabled || provider.EnabledByDefault;
            providerConfig.BaseUrl = string.IsNullOrWhiteSpace(providerConfig.BaseUrl) ? provider.DefaultBaseUrl : providerConfig.BaseUrl;
            providerConfig.ModelName = string.IsNullOrWhiteSpace(providerConfig.ModelName) ? provider.DefaultModelName : providerConfig.ModelName;
            providerConfig.ApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(providerConfig.ApiKeyEnvironmentVariable)
                ? provider.DefaultApiKeyEnvironmentVariable
                : providerConfig.ApiKeyEnvironmentVariable;
            providerConfig.TimeoutSeconds = providerConfig.TimeoutSeconds <= 0 ? settings.TimeoutSeconds : providerConfig.TimeoutSeconds;
            providerConfig.MaxRequestsPerMinute = providerConfig.MaxRequestsPerMinute <= 0 ? 12 : providerConfig.MaxRequestsPerMinute;
        }
    }
}

public static class PromptTemplates
{
    public static string GetSystemPrompt(CopilotPromptMode mode)
    {
        const string shared = "Answer like a careful technician: clear short answer first, then steps. Do not fake certainty. Ask for confirmation before destructive actions.";
        return mode switch
        {
            CopilotPromptMode.Troubleshooting => shared + " Troubleshooting mode: isolate likely causes for slow PCs, USB visibility, missing downloads, and OS choices.",
            CopilotPromptMode.FlipResale => shared + " Flip/resale mode: explain that real price estimates need online marketplace data; use local specs for upgrade and listing recommendations.",
            CopilotPromptMode.Technician => shared + " Technician mode: give safe repair guidance; avoid destructive commands unless the user explicitly confirms.",
            CopilotPromptMode.ToolkitBuilder => shared + " Toolkit Builder mode: recommend tools and ISOs based on task, licensing, and recovery/diagnostics constraints.",
            _ => shared
        };
    }
}

public sealed class LocalOfflineCopilotProvider : ICopilotProvider
{
    private readonly LocalRulesCopilotEngine _engine = new();

    public string Id => "local-offline";
    public string DisplayName => "Local Offline Rules";
    public CopilotProviderType ProviderType => CopilotProviderType.LocalOffline;
    public string Category => "Offline fallback";
    public bool IsOnlineProvider => false;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => true;
    public string DefaultBaseUrl => string.Empty;
    public string DefaultModelName => "local-rules";
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText => "Always available. Uses local rules and local scan JSON only.";

    public bool IsConfigured(CopilotProviderConfiguration configuration) => true;

    public bool CanHandle(CopilotProviderRequest request) => true;

    public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answer = _engine.GenerateReply(request.Prompt, request.Context);
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = true,
            UsedOnlineData = false,
            UserMessage = answer,
            DiagnosticMessage = "Local offline answer."
        });
    }
}

public sealed class OpenAICompatibleCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "openai-compatible";
    public string DisplayName => "OpenAI-Compatible";
    public CopilotProviderType ProviderType => CopilotProviderType.OpenAICompatible;
    public string Category => "Online/local AI";
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "https://api.openai.com/v1";
    public string DefaultModelName => "gpt-4.1-mini";
    public string DefaultApiKeyEnvironmentVariable => "OPENAI_API_KEY";
    public string StatusText => "Configurable OpenAI-compatible provider. API key is read from environment variable only.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.BaseUrl) &&
               !string.IsNullOrWhiteSpace(configuration.ModelName) &&
               !string.IsNullOrWhiteSpace(configuration.ApiKeyEnvironmentVariable) &&
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(configuration.ApiKeyEnvironmentVariable));
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var apiKey = Environment.GetEnvironmentVariable(request.ProviderConfiguration.ApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return NotConfigured("OpenAI-compatible API key environment variable is not set.");
        }

        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        var payload = new
        {
            model = request.ProviderConfiguration.ModelName,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = PromptTemplates.GetSystemPrompt(request.Context.PromptMode)
                },
                new
                {
                    role = "user",
                    content = request.Context.ContextText
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/responses");
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = (int)response.StatusCode is 408 or 429 or >= 500,
                UserMessage = "OpenAI-compatible provider returned an error. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        var text = TryExtractOpenAIResponseText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = "OpenAI-compatible provider returned an empty response. Offline fallback is available.",
                DiagnosticMessage = "Empty response text."
            }
            : new CopilotProviderResult
            {
                Succeeded = true,
                UsedOnlineData = true,
                UserMessage = text,
                DiagnosticMessage = "OpenAI-compatible response."
            };
    }

    private static CopilotProviderResult NotConfigured(string detail)
    {
        return new CopilotProviderResult
        {
            Succeeded = false,
            UserMessage = "OpenAI-compatible provider is not configured. Offline fallback is available.",
            DiagnosticMessage = detail
        };
    }

    private static string TryExtractOpenAIResponseText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("output_text", out var outputText))
            {
                return outputText.GetString() ?? string.Empty;
            }

            if (document.RootElement.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                var chunks = new List<string>();
                foreach (var item in output.EnumerateArray())
                {
                    if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    chunks.AddRange(content.EnumerateArray()
                        .Where(part => part.TryGetProperty("text", out _))
                        .Select(part => part.GetProperty("text").GetString())
                        .Where(text => !string.IsNullOrWhiteSpace(text))!);
                }

                return string.Join(Environment.NewLine, chunks);
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}

public sealed class AnthropicClaudeCopilotProvider : ICopilotProvider
{
    public string Id => "anthropic-claude";
    public string DisplayName => "Anthropic / Claude";
    public CopilotProviderType ProviderType => CopilotProviderType.AnthropicClaude;
    public string Category => "Online AI";
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "https://api.anthropic.com/v1";
    public string DefaultModelName => "claude-3-5-haiku-latest";
    public string DefaultApiKeyEnvironmentVariable => "ANTHROPIC_API_KEY";
    public string StatusText => "Adapter shell ready. Full Messages API implementation is intentionally deferred.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.ApiKeyEnvironmentVariable) &&
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(configuration.ApiKeyEnvironmentVariable));
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = false,
            UserMessage = "Claude provider shell is present but full API calls are not enabled yet. Offline fallback is available.",
            DiagnosticMessage = "Anthropic Messages adapter pending."
        });
    }
}

public sealed class OllamaCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "ollama-local";
    public string DisplayName => "Ollama Local Model";
    public CopilotProviderType ProviderType => CopilotProviderType.OllamaLocal;
    public string Category => "Offline/local AI";
    public bool IsOnlineProvider => false;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "http://localhost:11434";
    public string DefaultModelName => "llama3.2";
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText => "Local Ollama provider. Requires Ollama running on localhost.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.BaseUrl) && !string.IsNullOrWhiteSpace(configuration.ModelName);
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        try
        {
            using var ping = await HttpClient.GetAsync($"{baseUrl}/api/tags", cancellationToken).ConfigureAwait(false);
            if (!ping.IsSuccessStatusCode)
            {
                return NotReachable();
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return NotReachable();
        }

        var payload = new
        {
            model = request.ProviderConfiguration.ModelName,
            stream = false,
            prompt = request.Context.ContextText
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/generate")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = true,
                UserMessage = "Ollama returned an error. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var text = document.RootElement.TryGetProperty("response", out var responseText)
                ? responseText.GetString()
                : string.Empty;
            return string.IsNullOrWhiteSpace(text)
                ? new CopilotProviderResult
                {
                    Succeeded = false,
                    UserMessage = "Ollama returned an empty response. Offline fallback is available.",
                    DiagnosticMessage = "Empty response."
                }
                : new CopilotProviderResult
                {
                    Succeeded = true,
                    UsedOnlineData = false,
                    UserMessage = text,
                    DiagnosticMessage = "Ollama local response."
                };
        }
        catch (JsonException)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = "Ollama returned an unreadable response. Offline fallback is available.",
                DiagnosticMessage = "Invalid JSON."
            };
        }
    }

    private static CopilotProviderResult NotReachable()
    {
        return new CopilotProviderResult
        {
            Succeeded = false,
            IsTransientFailure = true,
            UserMessage = "Ollama is not reachable at the configured endpoint. Offline fallback is available.",
            DiagnosticMessage = "Ollama not reachable."
        };
    }
}

public sealed class StubCopilotProvider : ICopilotProvider
{
    public StubCopilotProvider(CopilotProviderType providerType, string id, string displayName, string category, string statusText)
    {
        ProviderType = providerType;
        Id = id;
        DisplayName = displayName;
        Category = category;
        StatusText = statusText;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public CopilotProviderType ProviderType { get; }
    public string Category { get; }
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => string.Empty;
    public string DefaultModelName => string.Empty;
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText { get; }

    public bool IsConfigured(CopilotProviderConfiguration configuration) => false;

    public bool CanHandle(CopilotProviderRequest request)
    {
        var prompt = request.Prompt.ToLowerInvariant();
        return ProviderType switch
        {
            CopilotProviderType.EbayPricing => prompt.Contains("worth") || prompt.Contains("price") || prompt.Contains("sell") || prompt.Contains("value"),
            CopilotProviderType.GitHubReleases => prompt.Contains("toolkit") || prompt.Contains("update") || prompt.Contains("release"),
            CopilotProviderType.ManufacturerSupport => prompt.Contains("driver") || prompt.Contains("bios") || prompt.Contains("manufacturer"),
            CopilotProviderType.MicrosoftDocs => prompt.Contains("windows") || prompt.Contains("tpm") || prompt.Contains("secure boot"),
            CopilotProviderType.LinuxReleaseInfo => prompt.Contains("ubuntu") || prompt.Contains("mint") || prompt.Contains("xubuntu") || prompt.Contains("linux"),
            _ => true
        };
    }

    public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = false,
            UserMessage = $"{DisplayName} is a provider shell and is not configured yet. Offline fallback is available.",
            DiagnosticMessage = StatusText
        });
    }
}

public sealed class LocalRulesCopilotEngine
{
    public string GenerateReply(string prompt, CopilotContext context)
    {
        var normalizedPrompt = prompt.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return "Ask Kyra about resale value, upgrades, lag, OS choice, USB toolkit selection, or a warning from the current scan.";
        }

        if (normalizedPrompt.Contains("upgrade", StringComparison.OrdinalIgnoreCase))
        {
            return BuildUpgradeAnswer(context);
        }

        return context.PromptMode switch
        {
            CopilotPromptMode.FlipResale => BuildValueAnswer(context),
            CopilotPromptMode.ToolkitBuilder => BuildToolkitAnswer(context.ContextText),
            CopilotPromptMode.Technician => BuildTechnicianAnswer(context.ContextText),
            CopilotPromptMode.Troubleshooting => BuildTroubleshootingAnswer(normalizedPrompt, context),
            _ => BuildGeneralAnswer(context)
        };
    }

    private static string BuildValueAnswer(CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return "Short answer: run System Intelligence first, then Kyra can judge sell/repair value from local specs. Real resale pricing still needs marketplace sold-listing data, and no fake comps are used offline.";
        }

        var health = context.HealthEvaluation?.HealthScore ?? 0;
        var salePosture = health < 55
            ? "repair-first or parts/repair until the scan issues are fixed"
            : "worth preparing for resale if condition/photos/charger/activation check out";
        var pricing = context.PricingEstimate;
        if (pricing is not null)
        {
            return $"Short answer: this looks {FormatResaleAction(pricing.RecommendedAction)}. Pricing Engine v0 local estimate only: ${pricing.LowEstimate:0} - ${pricing.HighEstimate:0}. Confidence: {pricing.ConfidenceScore:0.##}. Health score: {health}/100. Assumptions: {JoinOrFallback(pricing.Assumptions.Take(5), "local hardware facts only")}. Next moves: {JoinOrFallback(context.Recommendations.Take(4), "clean, update, verify drivers, and photograph condition")}. No marketplace comps, scraping, or API prices were used.";
        }

        return $"Short answer: this looks {salePosture}. Local estimate only: {profile.FlipValue.EstimatedResaleRange}; list around {profile.FlipValue.RecommendedListPrice}; quick-sale around {profile.FlipValue.QuickSalePrice}; parts/repair around {profile.FlipValue.PartsRepairPrice}. Confidence: {FormatConfidence(profile.FlipValue.ConfidenceScore)}. Health score: {health}/100. Pricing provider status: {profile.FlipValue.ProviderStatus}. Top reducers: {JoinOrFallback(profile.FlipValue.ValueReducers, "none from the local scan")}. Next moves: {JoinOrFallback(context.Recommendations.Take(4), "clean, update, verify drivers, and photograph condition")}.";
    }

    private static string BuildUpgradeAnswer(CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return "Short answer: run System Intelligence first so Kyra can rank upgrades from actual RAM, storage, battery, and security data.";
        }

        return $"Short answer: prioritize the upgrades that remove resale friction first. Health score: {context.HealthEvaluation?.HealthScore ?? 0}/100. Recommended order: {JoinOrFallback(context.Recommendations.Take(6), "no urgent hardware upgrade found locally")}. RAM: {profile.RamTotal}, upgrade path: {profile.RamUpgradePath}. Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.Name} {disk.MediaType} health {disk.Health}"), "storage health unknown")}. Battery: {JoinOrFallback(profile.Batteries.Select(battery => $"wear {FormatNullable(battery.WearPercent, "%")}, cycles {FormatNullable(battery.CycleCount)}"), "no battery detected")}."; 
    }

    private static string BuildToolkitAnswer(string contextText)
    {
        var usb = FindLine(contextText, "USB target:");
        var toolkit = FindLine(contextText, "Toolkit health");
        return $"Short answer: use the largest safe USB data partition and avoid EFI/system partitions. {usb} {toolkit} For diagnostics/recovery, keep Ventoy plus Windows installer media, Linux Mint/Ubuntu, Rescuezilla/Clonezilla, MemTest, and storage tools where licensing permits.";
    }

    private static string BuildTechnicianAnswer(string contextText)
    {
        var problems = FindLine(contextText, "Problems:");
        return $"Short answer: start with non-destructive checks. {problems} Steps: 1. Confirm power, storage health, RAM pressure, network state, and driver status. 2. Reproduce the issue. 3. Back up customer data before repairs. 4. Do not run destructive commands, format drives, or reinstall an OS until the user explicitly confirms.";
    }

    private static string BuildTroubleshootingAnswer(string prompt, CopilotContext context)
    {
        if (prompt.Contains("slow", StringComparison.OrdinalIgnoreCase) || prompt.Contains("lag", StringComparison.OrdinalIgnoreCase))
        {
            var profile = context.SystemProfile;
            var facts = profile is null
                ? "System Intelligence is not loaded yet, so Kyra is using general offline rules."
                : $"Health score: {context.HealthEvaluation?.HealthScore ?? 0}/100. RAM: {profile.RamTotal}. Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.MediaType} health {disk.Health} status {disk.Status}"), "storage health unknown")}. Battery: {profile.BatteryStatus}.";
            return $"Short answer: the usual lag suspects are RAM pressure, slow/weak storage, heat, startup load, power mode, or drivers. {facts} Detected issues: {JoinOrFallback(context.HealthEvaluation?.DetectedIssues.Take(5) ?? Array.Empty<string>(), "no obvious blocking issue found locally")}. Try this order: {JoinOrFallback(context.Recommendations.Take(5), "check Task Manager, SMART health, Windows Update activity, and driver status")}.";
        }

        if (prompt.Contains("usb", StringComparison.OrdinalIgnoreCase))
        {
            return "Short answer: check whether Windows mounted the main data partition, not the small VTOYEFI partition. " + FindLine(context.ContextText, "USB target:") + " Replug the USB, wait for mount, confirm Disk Management, then refresh only if auto-detect does not update.";
        }

        if (prompt.Contains("os", StringComparison.OrdinalIgnoreCase))
        {
            return "Short answer: Windows 11 Pro is best for resale/business when CPU, TPM, Secure Boot, RAM, and SSD are solid. For older/lower-spec systems, Linux Mint XFCE, Xubuntu, or Ubuntu can be better. Do not sell an unsupported OS install as the primary setup.";
        }

        return "Short answer: start with the latest local System Intelligence scan, then isolate one symptom at a time. Detected issues: " + JoinOrFallback(context.HealthEvaluation?.DetectedIssues.Take(5) ?? Array.Empty<string>(), "none loaded");
    }

    private static string BuildGeneralAnswer(CopilotContext context)
    {
        return $"Short answer: Kyra can work offline from the local System Intelligence scan, USB target, toolkit health, and recent logs. Health score: {context.HealthEvaluation?.HealthScore ?? 0}/100. Detected issues: {JoinOrFallback(context.HealthEvaluation?.DetectedIssues.Take(5) ?? Array.Empty<string>(), "none loaded")}.";
    }

    private static string FindLine(string text, string prefix)
    {
        return text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string JoinOrFallback(IEnumerable<string> values, string fallback)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToArray();
        return items.Length == 0 ? fallback : string.Join("; ", items);
    }

    private static string FormatNullable(double? value, string suffix = "")
    {
        return value.HasValue ? $"{value.Value:0.#}{suffix}" : "UNKNOWN";
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString() : "UNKNOWN";
    }

    private static string FormatConfidence(double? value)
    {
        return value.HasValue ? $"{value.Value:0.##}" : "UNKNOWN";
    }

    private static string FormatResaleAction(ResaleAction action)
    {
        return action switch
        {
            ResaleAction.SellNow => "sell now",
            ResaleAction.PartsOnly => "parts only",
            _ => "upgrade first"
        };
    }
}
