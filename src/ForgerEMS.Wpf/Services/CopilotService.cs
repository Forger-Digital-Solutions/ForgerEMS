using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public enum CopilotMode
{
    OfflineOnly,
    OnlineAssisted,
    HybridAuto
}

public sealed class CopilotConfiguration
{
    public CopilotMode Mode { get; set; } = CopilotMode.OfflineOnly;

    public bool AllowSensitiveOnlineData { get; set; }

    public Dictionary<string, CopilotProviderConfiguration> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CopilotProviderConfiguration
{
    public bool IsEnabled { get; set; }

    public int TimeoutSeconds { get; set; } = 8;

    public int MaxRequestsPerMinute { get; set; } = 12;
}

public sealed class CopilotRequest
{
    public string Prompt { get; init; } = string.Empty;

    public string SystemIntelligenceReportPath { get; init; } = string.Empty;

    public UsbTargetInfo? SelectedUsbTarget { get; init; }

    public CopilotConfiguration Configuration { get; init; } = new();
}

public sealed class CopilotResponse
{
    public string Text { get; init; } = string.Empty;

    public bool UsedOnlineData { get; init; }

    public string OnlineStatus { get; init; } = "Offline only";

    public IReadOnlyList<string> ProviderNotes { get; init; } = Array.Empty<string>();
}

public sealed class CopilotProviderRequest
{
    public string Prompt { get; init; } = string.Empty;

    public JsonElement? SanitizedSystemSummary { get; init; }

    public string SanitizedUsbSummary { get; init; } = string.Empty;

    public CopilotMode Mode { get; init; }
}

public sealed class CopilotProviderResult
{
    public bool Succeeded { get; init; }

    public bool UsedOnlineData { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Detail { get; init; } = string.Empty;
}

public interface ICopilotProvider
{
    string Id { get; }

    string DisplayName { get; }

    string Category { get; }

    bool IsOnlineProvider { get; }

    bool IsPaidProvider { get; }

    bool IsConfigured { get; }

    bool EnabledByDefault { get; }

    string StatusText { get; }

    bool CanHandle(CopilotProviderRequest request);

    Task<CopilotProviderResult> TryAnswerAsync(CopilotProviderRequest request, CancellationToken cancellationToken);
}

public interface ICopilotProviderRegistry
{
    IReadOnlyList<ICopilotProvider> Providers { get; }
}

public interface ICopilotService
{
    Task<CopilotResponse> GenerateReplyAsync(CopilotRequest request, CancellationToken cancellationToken = default);
}

public sealed class CopilotProviderRegistry : ICopilotProviderRegistry
{
    public CopilotProviderRegistry()
    {
        Providers =
        [
            new PlaceholderCopilotProvider("ebay-sold-listings", "eBay Sold Listings", "Pricing", "Looks up real sold-listing comps when API access is configured."),
            new PlaceholderCopilotProvider("github-releases", "GitHub Releases", "Toolkit updates", "Checks app/toolkit release feeds when a public release source is configured."),
            new PlaceholderCopilotProvider("manufacturer-support", "Manufacturer Support Lookup", "Drivers/BIOS", "Finds Dell, HP, Lenovo, and similar support pages from sanitized model data."),
            new PlaceholderCopilotProvider("microsoft-support-docs", "Microsoft/Windows Support Docs", "Windows docs", "Looks up Windows support docs for troubleshooting and OS recommendations."),
            new PlaceholderCopilotProvider("linux-release-info", "Ubuntu/Mint/Xubuntu Release Info", "Linux support", "Checks public distro release/support windows."),
            new PlaceholderCopilotProvider("openai-future", "OpenAI Provider", "Future AI", "Future paid LLM hook. Disabled by default.", isPaidProvider: true),
            new PlaceholderCopilotProvider("ollama-local", "Ollama Local Model", "Future offline AI", "Future local model hook for fully offline AI responses.", isOnlineProvider: false)
        ];
    }

    public IReadOnlyList<ICopilotProvider> Providers { get; }
}

public sealed class CopilotService : ICopilotService
{
    private readonly ICopilotProviderRegistry _providerRegistry;
    private readonly LocalRulesCopilotEngine _localEngine = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _providerRequests = new(StringComparer.OrdinalIgnoreCase);

    public CopilotService(ICopilotProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public async Task<CopilotResponse> GenerateReplyAsync(CopilotRequest request, CancellationToken cancellationToken = default)
    {
        var localAnswer = _localEngine.GenerateReply(request.Prompt, request.SystemIntelligenceReportPath, request.SelectedUsbTarget);
        if (request.Configuration.Mode == CopilotMode.OfflineOnly)
        {
            return new CopilotResponse
            {
                Text = localAnswer,
                UsedOnlineData = false,
                OnlineStatus = "Offline Only - no data left this machine."
            };
        }

        var providerRequest = BuildProviderRequest(request);
        var providerNotes = new List<string>();
        var providerDetails = new List<string>();
        var usedOnline = false;

        foreach (var provider in SelectProviders(providerRequest, request.Configuration))
        {
            var providerConfig = GetProviderConfig(request.Configuration, provider);
            if (!provider.IsConfigured)
            {
                providerNotes.Add($"{provider.DisplayName}: {provider.StatusText}");
                continue;
            }

            if (!TryEnterRateLimit(provider, providerConfig, providerNotes))
            {
                continue;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(providerConfig.TimeoutSeconds, 2, 30)));
                var result = await provider.TryAnswerAsync(providerRequest, timeout.Token).ConfigureAwait(false);
                providerNotes.Add($"{provider.DisplayName}: {(result.Succeeded ? "OK" : "No result")}");
                if (result.Succeeded && !string.IsNullOrWhiteSpace(result.Detail))
                {
                    providerDetails.Add(result.Detail);
                    usedOnline = usedOnline || result.UsedOnlineData;
                }
            }
            catch (OperationCanceledException)
            {
                providerNotes.Add($"{provider.DisplayName}: timed out, falling back to offline answer");
            }
            catch (Exception exception)
            {
                providerNotes.Add($"{provider.DisplayName}: failed safely ({exception.Message})");
            }
        }

        var onlineStatus = usedOnline
            ? "Online lookup used with sanitized context."
            : request.Configuration.Mode == CopilotMode.OnlineAssisted
                ? "Online lookup enabled, but no configured provider returned data. Offline answer shown."
                : "Hybrid Auto used offline answer; no online provider was needed or configured.";

        var extra = providerDetails.Count == 0 ? string.Empty : $"{Environment.NewLine}{Environment.NewLine}Online lookup notes: {string.Join(" ", providerDetails)}";
        return new CopilotResponse
        {
            Text = $"{localAnswer}{extra}",
            UsedOnlineData = usedOnline,
            OnlineStatus = onlineStatus,
            ProviderNotes = providerNotes
        };
    }

    private IEnumerable<ICopilotProvider> SelectProviders(CopilotProviderRequest request, CopilotConfiguration configuration)
    {
        var enabled = _providerRegistry.Providers
            .Where(provider => GetProviderConfig(configuration, provider).IsEnabled)
            .Where(provider => provider.CanHandle(request));

        if (configuration.Mode == CopilotMode.HybridAuto && !ShouldUseOnline(request.Prompt))
        {
            return Array.Empty<ICopilotProvider>();
        }

        return enabled;
    }

    private static bool ShouldUseOnline(string prompt)
    {
        var text = prompt.ToLowerInvariant();
        return text.Contains("worth") ||
               text.Contains("price") ||
               text.Contains("driver") ||
               text.Contains("bios") ||
               text.Contains("os") ||
               text.Contains("windows") ||
               text.Contains("linux") ||
               text.Contains("ubuntu") ||
               text.Contains("mint") ||
               text.Contains("research") ||
               text.Contains("lookup");
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
            notes.Add($"{provider.DisplayName}: rate limit reached, falling back to offline answer");
            return false;
        }

        queue.Enqueue(now);
        return true;
    }

    private static CopilotProviderConfiguration GetProviderConfig(CopilotConfiguration configuration, ICopilotProvider provider)
    {
        if (!configuration.Providers.TryGetValue(provider.Id, out var providerConfig))
        {
            providerConfig = new CopilotProviderConfiguration
            {
                IsEnabled = provider.EnabledByDefault
            };
            configuration.Providers[provider.Id] = providerConfig;
        }

        return providerConfig;
    }

    private static CopilotProviderRequest BuildProviderRequest(CopilotRequest request)
    {
        return new CopilotProviderRequest
        {
            Prompt = SanitizePrompt(request.Prompt),
            SanitizedSystemSummary = TryLoadSanitizedSummary(request.SystemIntelligenceReportPath),
            SanitizedUsbSummary = SanitizeUsbSummary(request.SelectedUsbTarget),
            Mode = request.Configuration.Mode
        };
    }

    private static JsonElement? TryLoadSanitizedSummary(string reportPath)
    {
        try
        {
            if (!File.Exists(reportPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            if (!document.RootElement.TryGetProperty("summary", out var summary))
            {
                return null;
            }

            var safeSummary = new Dictionary<string, object?>
            {
                ["manufacturer"] = GetJsonString(summary, "manufacturer", "Unknown"),
                ["model"] = GetJsonString(summary, "model", "Unknown"),
                ["os"] = GetJsonString(summary, "os", "Unknown"),
                ["osBuild"] = GetJsonString(summary, "osBuild", "Unknown"),
                ["cpu"] = GetJsonString(summary, "cpu", "Unknown"),
                ["cpuCores"] = GetJsonString(summary, "cpuCores", "Unknown"),
                ["cpuLogicalProcessors"] = GetJsonString(summary, "cpuLogicalProcessors", "Unknown"),
                ["ramTotal"] = GetJsonString(summary, "ramTotal", "Unknown"),
                ["ramSpeed"] = GetJsonString(summary, "ramSpeed", "Unknown"),
                ["gpuStatus"] = GetJsonString(summary, "gpuStatus", "Unknown")
            };

            return JsonSerializer.SerializeToElement(safeSummary);
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeUsbSummary(UsbTargetInfo? target)
    {
        if (target is null)
        {
            return "No USB target selected";
        }

        return $"USB target: {target.DisplayTotalBytes}, {target.FileSystem}, {target.BusTypeDisplay}, benchmark {target.BenchmarkStatusDisplay}, write {target.WriteSpeedDisplayNormalized}, read {target.ReadSpeedDisplayNormalized}";
    }

    private static string SanitizePrompt(string prompt)
    {
        var sanitized = Regex.Replace(prompt, @"[A-Za-z]:\\[^\s]+", "[local path]");
        sanitized = Regex.Replace(sanitized, @"(?i)\b(service tag|serial|s/n)\s*[:#]?\s*[A-Z0-9-]{5,}\b", "$1 [redacted]");
        sanitized = Regex.Replace(sanitized, @"(?i)\bC:\\Users\\[^\\\s]+", @"C:\Users\[redacted]");
        return sanitized.Trim();
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

public sealed class PlaceholderCopilotProvider : ICopilotProvider
{
    public PlaceholderCopilotProvider(
        string id,
        string displayName,
        string category,
        string description,
        bool isOnlineProvider = true,
        bool isPaidProvider = false)
    {
        Id = id;
        DisplayName = displayName;
        Category = category;
        StatusText = $"Provider hook ready; not configured. {description}";
        IsOnlineProvider = isOnlineProvider;
        IsPaidProvider = isPaidProvider;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Category { get; }

    public bool IsOnlineProvider { get; }

    public bool IsPaidProvider { get; }

    public bool IsConfigured => false;

    public bool EnabledByDefault => false;

    public string StatusText { get; }

    public bool CanHandle(CopilotProviderRequest request)
    {
        var prompt = request.Prompt.ToLowerInvariant();
        return Category switch
        {
            "Pricing" => prompt.Contains("worth") || prompt.Contains("price") || prompt.Contains("sell") || prompt.Contains("value"),
            "Toolkit updates" => prompt.Contains("toolkit") || prompt.Contains("update") || prompt.Contains("release"),
            "Drivers/BIOS" => prompt.Contains("driver") || prompt.Contains("bios") || prompt.Contains("manufacturer"),
            "Windows docs" => prompt.Contains("windows") || prompt.Contains("tpm") || prompt.Contains("secure boot"),
            "Linux support" => prompt.Contains("ubuntu") || prompt.Contains("mint") || prompt.Contains("xubuntu") || prompt.Contains("linux"),
            "Future AI" => true,
            "Future offline AI" => true,
            _ => true
        };
    }

    public Task<CopilotProviderResult> TryAnswerAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = false,
            UsedOnlineData = false,
            Summary = "Provider not configured",
            Detail = StatusText
        });
    }
}

public sealed class LocalRulesCopilotEngine
{
    public string GenerateReply(string prompt, string systemIntelligenceReportPath, UsbTargetInfo? selectedUsbTarget)
    {
        var normalizedPrompt = prompt.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return "Ask me about resale value, upgrades, lag, OS choice, USB toolkit selection, or a warning from the current scan.";
        }

        using var report = TryLoadReport(systemIntelligenceReportPath);
        if (report is null)
        {
            return "I do not have a System Intelligence report yet. Run System Scan first, then I can reason from the collected local hardware and USB context.";
        }

        var root = report.RootElement;
        var summary = root.TryGetProperty("summary", out var summaryElement) ? summaryElement : default;
        var lowerPrompt = normalizedPrompt.ToLowerInvariant();

        if (lowerPrompt.Contains("worth") || lowerPrompt.Contains("value") || lowerPrompt.Contains("sell") || lowerPrompt.Contains("price"))
        {
            return BuildValueAnswer(root);
        }

        if (lowerPrompt.Contains("upgrade"))
        {
            return BuildUpgradeAnswer(root);
        }

        if (lowerPrompt.Contains("lag") || lowerPrompt.Contains("slow") || lowerPrompt.Contains("performance"))
        {
            return BuildLagAnswer(root);
        }

        if (lowerPrompt.Contains("os") || lowerPrompt.Contains("windows") || lowerPrompt.Contains("linux") || lowerPrompt.Contains("ubuntu") || lowerPrompt.Contains("mint"))
        {
            return BuildOsAnswer(summary);
        }

        if (lowerPrompt.Contains("usb") || lowerPrompt.Contains("toolkit") || lowerPrompt.Contains("ventoy"))
        {
            return BuildUsbAnswer(selectedUsbTarget);
        }

        if (lowerPrompt.Contains("warning") || lowerPrompt.Contains("problem") || lowerPrompt.Contains("mean"))
        {
            return BuildWarningAnswer(root);
        }

        return BuildGeneralAnswer(root, selectedUsbTarget);
    }

    private static JsonDocument? TryLoadReport(string path)
    {
        try
        {
            return File.Exists(path) ? JsonDocument.Parse(File.ReadAllText(path)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildValueAnswer(JsonElement root)
    {
        if (!root.TryGetProperty("flipValue", out var flipValue))
        {
            return "The report does not include Flip Value yet. Run System Scan again so I can generate the local resale estimate.";
        }

        var range = GetJsonString(flipValue, "estimatedResaleRange", "unknown");
        var list = GetJsonString(flipValue, "recommendedListPrice", "unknown");
        var quick = GetJsonString(flipValue, "quickSalePrice", "unknown");
        var parts = GetJsonString(flipValue, "partsRepairPrice", "unknown");
        var confidence = GetJsonString(flipValue, "confidenceScore", "unknown");
        var drivers = GetJsonStringArray(flipValue, "valueDrivers").Take(3);
        var reducers = GetJsonStringArray(flipValue, "valueReducers").Take(3);

        return $"Local estimate only: resale range {range}, list around {list}, quick-sale around {quick}, parts/repair around {parts}. Confidence is {confidence}. Value drivers: {FormatList(drivers)}. Reducers: {FormatList(reducers)}. Marketplace pricing providers are not configured, so I am not treating these as real sold-comps.";
    }

    private static string BuildUpgradeAnswer(JsonElement root)
    {
        var suggestions = root.TryGetProperty("flipValue", out var flipValue)
            ? GetJsonStringArray(flipValue, "suggestedUpgradeRecommendations")
            : [];

        if (suggestions.Length > 0)
        {
            return "Before selling: " + FormatList(suggestions.Take(4));
        }

        var recs = GetJsonStringArray(root, "recommendations");
        return recs.Length > 0
            ? "I would start here: " + FormatList(recs.Take(4))
            : "No upgrade recommendation is obvious yet. A clean install, driver updates, SMART check, and battery check are the best baseline before listing.";
    }

    private static string BuildLagAnswer(JsonElement root)
    {
        var findings = new List<string>();
        if (root.TryGetProperty("summary", out var summary))
        {
            var ramText = GetJsonString(summary, "ramTotal", string.Empty);
            if (TryParseGigabytes(ramText, out var ramGb) && ramGb < 16)
            {
                findings.Add("RAM is under 16 GB, so multitasking and modern Windows workloads may feel tight.");
            }
        }

        if (root.TryGetProperty("disks", out var disks) && disks.ValueKind == JsonValueKind.Array)
        {
            foreach (var disk in disks.EnumerateArray())
            {
                var media = GetJsonString(disk, "mediaType", string.Empty);
                var health = GetJsonString(disk, "health", string.Empty);
                if (!media.Contains("SSD", StringComparison.OrdinalIgnoreCase) &&
                    !media.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add("Storage is not clearly SSD/NVMe, which is a common cause of lag.");
                }

                if (!string.IsNullOrWhiteSpace(health) &&
                    !health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) &&
                    !health.Equals("OK", StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add($"Storage health is reported as {health}.");
                }
            }
        }

        return findings.Count == 0
            ? "I do not see a single obvious lag cause in the current report. Check startup apps, thermals, Windows Update activity, storage SMART details, and whether apps are using the intended GPU."
            : string.Join(" ", findings.Distinct());
    }

    private static string BuildOsAnswer(JsonElement summary)
    {
        var cpu = GetJsonString(summary, "cpu", "this CPU");
        var ramText = GetJsonString(summary, "ramTotal", string.Empty);
        TryParseGigabytes(ramText, out var ramGb);
        var tpmReady = GetJsonBool(summary, "tpmReady");
        var secureBoot = GetJsonBool(summary, "secureBoot");

        if (ramGb >= 16 && tpmReady && secureBoot)
        {
            return $"For resale or business users, Windows 11 Pro is the best primary setup for {cpu}. Keep Linux as a toolkit option unless the buyer specifically wants it.";
        }

        if (ramGb > 0 && ramGb < 8)
        {
            return $"This looks better suited to Linux Mint XFCE, Xubuntu, or a diagnostics/recovery role. I would not present an unsupported Windows install as the primary resale setup.";
        }

        return "Windows 10/11 suitability depends on TPM, Secure Boot, CPU support, and buyer needs. For older or lower-spec systems, Linux Mint or Xubuntu is a cleaner primary experience; keep ForgerEMS/Ventoy for diagnostics and recovery.";
    }

    private static string BuildUsbAnswer(UsbTargetInfo? selectedUsbTarget)
    {
        if (selectedUsbTarget is null)
        {
            return "No USB target is selected. Insert a safe removable USB and I will use the detected target, Ventoy status, and benchmark results to recommend the toolkit path.";
        }

        return $"Selected USB: {selectedUsbTarget.RootPath} {selectedUsbTarget.LabelDisplay}, {selectedUsbTarget.DisplayTotalBytes}, {selectedUsbTarget.SelectionStatusText}. Benchmark status: {selectedUsbTarget.BenchmarkStatusDisplay}, write {selectedUsbTarget.WriteSpeedDisplayNormalized}, read {selectedUsbTarget.ReadSpeedDisplayNormalized}. For diagnostics/recovery, use the ForgerEMS Ventoy toolkit on the largest safe data partition and avoid EFI/system partitions.";
    }

    private static string BuildWarningAnswer(JsonElement root)
    {
        var problems = GetJsonStringArray(root, "obviousProblems");
        if (problems.Length == 0)
        {
            return "No obvious problem list is available yet. Run System Scan again to refresh APIPA, gateway, BIOS, TPM, battery, storage, and driver checks.";
        }

        return "Current warnings: " + FormatList(problems.Take(5));
    }

    private static string BuildGeneralAnswer(JsonElement root, UsbTargetInfo? selectedUsbTarget)
    {
        var problems = GetJsonStringArray(root, "obviousProblems").Take(3).ToArray();
        var usb = selectedUsbTarget is null
            ? "No USB target selected."
            : $"USB {selectedUsbTarget.RootPath}: {selectedUsbTarget.BenchmarkStatusDisplay}, write {selectedUsbTarget.WriteSpeedDisplayNormalized}, read {selectedUsbTarget.ReadSpeedDisplayNormalized}.";

        return problems.Length > 0
            ? $"I am using the latest local System Intelligence report. Top context: {FormatList(problems)} {usb}"
            : $"I am using the latest local System Intelligence report. I do not see a major local warning yet. {usb}";
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

    private static bool GetJsonBool(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               (property.ValueKind == JsonValueKind.True ||
                (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed) && parsed));
    }

    private static string[] GetJsonStringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Select(item => item.GetString())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .ToArray();
    }

    private static bool TryParseGigabytes(string text, out double value)
    {
        value = 0;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && double.TryParse(parts[0], out value);
    }

    private static string FormatList(IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return items.Length == 0 ? "none" : string.Join("; ", items);
    }
}
