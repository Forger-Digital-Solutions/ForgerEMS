#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// ForgerEMS scan result models; Kyra.Core would define a generic ISystemContext instead.
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

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

    /// <summary>SMBIOS-derived label from System Intelligence (e.g. DDR4, LPDDR5); may be generic "RAM" when type is unknown.</summary>
    public string MemoryTypeSummary { get; init; } = string.Empty;

    public IReadOnlyList<SystemGpuProfile> Gpus { get; init; } = Array.Empty<SystemGpuProfile>();

    public IReadOnlyList<SystemDiskProfile> Disks { get; init; } = Array.Empty<SystemDiskProfile>();

    public IReadOnlyList<SystemBatteryProfile> Batteries { get; init; } = Array.Empty<SystemBatteryProfile>();

    public bool? TpmPresent { get; init; }

    public bool? TpmReady { get; init; }

    public bool? SecureBoot { get; init; }

    public string TpmStatus { get; init; } = "UNKNOWN";

    public string TpmEvidence { get; init; } = string.Empty;

    public string SecureBootStatus { get; init; } = "UNKNOWN";

    public string SecureBootEvidence { get; init; } = string.Empty;

    public string OverallStatus { get; init; } = "UNKNOWN";

    public string DiskStatus { get; init; } = "UNKNOWN";

    public string BatteryStatus { get; init; } = "UNKNOWN";

    public string NetworkStatus { get; init; } = "UNKNOWN";

    public bool InternetCheck { get; init; }

    public int ApipaAdapterCount { get; init; }

    public int MissingGatewayAdapterCount { get; init; }

    public int PhysicalNetworkAdapterCount { get; init; }

    public int VirtualNetworkAdapterCount { get; init; }

    public bool HasActivePhysicalInternetAdapter { get; init; }

    public IReadOnlyList<string> ObviousProblems { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ReportRecommendations { get; init; } = Array.Empty<string>();

    public FlipValueProfile FlipValue { get; init; } = new();
}

public sealed class SystemGpuProfile
{
    public string Name { get; init; } = "Unknown GPU";

    public string DriverVersion { get; init; } = "UNKNOWN";

    /// <summary>Integrated vs dedicated classification from the System Intelligence scan JSON.</summary>
    public string GpuKind { get; init; } = "UNKNOWN";
}

public sealed class SystemDiskProfile
{
    public string Name { get; init; } = "Disk";

    /// <summary>Windows storage bus type (e.g. NVMe, SATA, USB) from the System Intelligence scan.</summary>
    public string InterfaceType { get; init; } = string.Empty;

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

    public string DesignCapacityDisplay { get; init; } = string.Empty;

    public string FullChargeCapacityDisplay { get; init; } = string.Empty;
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

    public int ConfidenceScore { get; init; } = 100;

    public IReadOnlyList<SystemHealthCategoryScore> Categories { get; init; } = Array.Empty<SystemHealthCategoryScore>();

    public IReadOnlyList<string> DetectedIssues { get; init; } = Array.Empty<string>();
}

public sealed class SystemHealthCategoryScore
{
    public string Category { get; init; } = string.Empty;

    public int Score { get; init; }

    public string Status { get; init; } = "UNKNOWN";

    public string Confidence { get; init; } = "Medium";

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public string RecommendedAction { get; init; } = string.Empty;
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
            MemoryTypeSummary = GetJsonString(summary, "memoryType", string.Empty),
            Gpus = MapGpus(summary),
            Disks = MapDisks(root),
            Batteries = MapBatteries(root),
            TpmPresent = GetJsonNullableBool(summary, "tpmPresent"),
            TpmReady = GetJsonNullableBool(summary, "tpmReady"),
            SecureBoot = GetJsonNullableBool(summary, "secureBoot"),
            TpmStatus = GetProviderStatus(summary, "tpmInfo"),
            TpmEvidence = GetProviderEvidence(summary, "tpmInfo"),
            SecureBootStatus = GetProviderStatus(summary, "secureBootInfo"),
            SecureBootEvidence = GetProviderEvidence(summary, "secureBootInfo"),
            OverallStatus = GetJsonString(root, "overallStatus", "UNKNOWN"),
            DiskStatus = GetJsonString(root, "diskStatus", "UNKNOWN"),
            BatteryStatus = GetJsonString(root, "batteryStatus", "UNKNOWN"),
            NetworkStatus = GetJsonString(network, "status", "UNKNOWN"),
            InternetCheck = GetJsonBool(network, "internetCheck"),
            ApipaAdapterCount = CountNetworkAdapters(network, "apipaDetected"),
            MissingGatewayAdapterCount = CountMissingGateways(network),
            PhysicalNetworkAdapterCount = CountAdapterKind(network, virtualAdapters: false),
            VirtualNetworkAdapterCount = CountAdapterKind(network, virtualAdapters: true),
            HasActivePhysicalInternetAdapter = HasActivePhysicalInternet(network),
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

    private static SystemGpuProfile[] MapGpus(JsonElement summary)
    {
        if (summary.ValueKind != JsonValueKind.Object ||
            !summary.TryGetProperty("gpus", out var gpus) ||
            gpus.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SystemGpuProfile>();
        }

        return gpus.EnumerateArray()
            .Select(gpu => new SystemGpuProfile
            {
                Name = GetJsonString(gpu, "name", "Unknown GPU"),
                DriverVersion = GetJsonString(gpu, "driverVersion", "UNKNOWN"),
                GpuKind = GetJsonString(gpu, "type", "UNKNOWN")
            })
            .ToArray();
    }

    private static SystemDiskProfile[] MapDisks(JsonElement root)
    {
        if (!root.TryGetProperty("disks", out var disks) || disks.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<SystemDiskProfile>();
        }

        return disks.EnumerateArray()
            .Select(disk => new SystemDiskProfile
            {
                Name = GetJsonString(disk, "name", "Disk"),
                InterfaceType = GetJsonString(disk, "interfaceType", string.Empty),
                MediaType = GetJsonString(disk, "mediaType", "UNKNOWN"),
                Size = GetJsonString(disk, "size", "UNKNOWN"),
                Health = GetJsonString(disk, "health", "Unknown"),
                Status = GetJsonString(disk, "status", "UNKNOWN"),
                TemperatureC = GetJsonDouble(disk, "temperatureC"),
                WearPercent = GetJsonDouble(disk, "wearPercent")
            })
            .ToArray();
    }

    private static SystemBatteryProfile[] MapBatteries(JsonElement root)
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
                Status = GetJsonString(battery, "status", "UNKNOWN"),
                DesignCapacityDisplay = GetJsonString(battery, "designCapacityDisplay", string.Empty),
                FullChargeCapacityDisplay = GetJsonString(battery, "fullChargeCapacityDisplay", string.Empty)
            })
            .ToArray();
    }

    private static int CountNetworkAdapters(JsonElement network, string propertyName)
    {
        if (network.ValueKind != JsonValueKind.Object ||
            !network.TryGetProperty("adapters", out var adapters) ||
            adapters.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return adapters.EnumerateArray().Count(adapter => !ShouldIgnoreAdapterForHealth(adapter) && GetJsonBool(adapter, propertyName));
    }

    private static int CountMissingGateways(JsonElement network)
    {
        if (network.ValueKind != JsonValueKind.Object ||
            !network.TryGetProperty("adapters", out var adapters) ||
            adapters.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return adapters.EnumerateArray().Count(adapter => !ShouldIgnoreAdapterForHealth(adapter) && !GetJsonBool(adapter, "gatewayPresent"));
    }

    private static int CountAdapterKind(JsonElement network, bool virtualAdapters)
    {
        if (network.ValueKind != JsonValueKind.Object)
        {
            return 0;
        }

        var propertyName = virtualAdapters ? "virtualAdapters" : "physicalAdapters";
        if (network.TryGetProperty(propertyName, out var explicitAdapters) && explicitAdapters.ValueKind == JsonValueKind.Array)
        {
            return explicitAdapters.GetArrayLength();
        }

        if (!network.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return adapters.EnumerateArray().Count(adapter => ShouldIgnoreAdapterForHealth(adapter) == virtualAdapters);
    }

    private static bool HasActivePhysicalInternet(JsonElement network)
    {
        if (network.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var internet = GetJsonBool(network, "internetCheck");
        if (!network.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array)
        {
            return internet;
        }

        return adapters.EnumerateArray().Any(adapter =>
            !ShouldIgnoreAdapterForHealth(adapter) &&
            (GetJsonBool(adapter, "isDefaultRoute") || GetJsonBool(adapter, "gatewayPresent")) &&
            (internet || GetJsonBool(adapter, "gatewayPresent")));
    }

    private static bool ShouldIgnoreAdapterForHealth(JsonElement adapter)
    {
        if (GetJsonBool(adapter, "isVirtual"))
        {
            return true;
        }

        var role = GetJsonString(adapter, "adapterRole", string.Empty);
        if (role.Contains("virtual", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("vpn", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("host-only", StringComparison.OrdinalIgnoreCase) ||
            role.Contains("loopback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return SystemIntelligenceFormatter.ShouldIgnoreAdapterForWarnings(
            GetJsonString(adapter, "name", string.Empty),
            GetJsonString(adapter, "description", string.Empty));
    }

    private static string GetProviderStatus(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return "UNKNOWN";
        }

        return property.ValueKind == JsonValueKind.Object
            ? GetJsonString(property, "status", "UNKNOWN")
            : "UNKNOWN";
    }

    private static string GetProviderEvidence(JsonElement element, string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Undefined || !element.TryGetProperty(propertyName, out var property))
        {
            return string.Empty;
        }

        if (property.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var source = GetJsonString(property, "source", string.Empty);
        var reason = GetJsonString(property, "reason", string.Empty);
        return string.Join("; ", new[] { source, reason }.Where(value => !string.IsNullOrWhiteSpace(value)));
    }

    private static string[] GetStringArray(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
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
