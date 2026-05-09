#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: CopilotRedactor (generic-ish), CopilotSettingsStore (reads ForgerEMS env vars).
// KYRA_UI_SHELL: KyraProviderStatusPresenter — formats provider status for ForgerEMS UI display.
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

public static class KyraProviderStatusPresenter
{
    public static string GetProviderBadge(CopilotMode mode, bool localOllamaEnabled, bool localLmStudioEnabled, bool openAiConfigured, bool anyOnlineConfigured)
    {
        var parts = new List<string>();
        var showApiReady = anyOnlineConfigured && mode is not CopilotMode.OfflineOnly and not CopilotMode.AskFirst;
        if (showApiReady)
        {
            parts.Add("API providers ready");
        }

        if (localOllamaEnabled || localLmStudioEnabled)
        {
            parts.Add("Local AI available");
        }

        if (parts.Count > 0)
        {
            return string.Join(" · ", parts);
        }

        if (openAiConfigured)
        {
            return "OpenAI-Compatible Ready";
        }

        if (mode != CopilotMode.OfflineOnly && !anyOnlineConfigured)
        {
            return "Online Not Configured";
        }

        return anyOnlineConfigured ? "Online Ready" : "Offline Ready";
    }

    public static string GetOnlineSummary(
        bool localOllamaEnabled,
        bool localLmStudioEnabled,
        bool openAiConfigured,
        bool anyPricingConfigured,
        bool anyOnlinePoolConfigured)
    {
        var onlineAi = openAiConfigured ? "Online AI: configured" : "Online AI: Not configured";
        var localAi = (localOllamaEnabled, localLmStudioEnabled) switch
        {
            (true, true) => "Local AI: Ollama and LM Studio selected",
            (true, false) => "Local AI: Ollama selected",
            (false, true) => "Local AI: LM Studio selected",
            _ => "Local AI: Not configured"
        };
        var pricing = anyPricingConfigured ? "Pricing Lookup: configured" : "Pricing Lookup: Not configured";
        var optional = !anyOnlinePoolConfigured && !openAiConfigured
            ? $"{Environment.NewLine}Local Kyra is active. Online providers are optional."
            : string.Empty;
        return $"Local Offline Rules: Ready{Environment.NewLine}{onlineAi}{Environment.NewLine}{localAi}{Environment.NewLine}{pricing}{optional}";
    }

    public static string GetPrivacyBadge(CopilotMode mode)
    {
        return mode switch
        {
            CopilotMode.OfflineOnly => "Local Only",
            CopilotMode.AskFirst => "Ask Before Sending",
            _ => "Sanitized Context"
        };
    }
}

public sealed class CopilotSettingsStore : ICopilotSettingsStore
{
    private static readonly JsonSerializerOptions SaveJsonOptions = new() { WriteIndented = true };

    private static readonly JsonSerializerOptions LoadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
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
                ? JsonSerializer.Deserialize<CopilotSettings>(File.ReadAllText(_path), LoadJsonOptions) ?? new CopilotSettings()
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
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, SaveJsonOptions));
    }

    private void ApplyDefaults(CopilotSettings settings)
    {
        settings.TimeoutSeconds = settings.TimeoutSeconds <= 0 ? ForgerEmsEnvironmentConfiguration.KyraProviderTimeoutSeconds : settings.TimeoutSeconds;
        settings.MaxContextCharacters = settings.MaxContextCharacters <= 0 ? ForgerEmsEnvironmentConfiguration.KyraContextMaxChars : settings.MaxContextCharacters;
        settings.ModelName = string.IsNullOrWhiteSpace(settings.ModelName) ? "local-rules" : settings.ModelName;
        settings.MaxProviderFallbacksPerMessage = settings.MaxProviderFallbacksPerMessage <= 0 ? 4 : settings.MaxProviderFallbacksPerMessage;
        settings.FreeApiDailyRequestCap = settings.FreeApiDailyRequestCap <= 0 ? 120 : settings.FreeApiDailyRequestCap;
        settings.MaxInputCharactersOnline = settings.MaxInputCharactersOnline <= 0 ? 4000 : settings.MaxInputCharactersOnline;
        settings.MaxOutputTokensOnline = settings.MaxOutputTokensOnline <= 0 ? 700 : settings.MaxOutputTokensOnline;
        settings.ConsecutiveFailureThreshold = settings.ConsecutiveFailureThreshold <= 0 ? 4 : settings.ConsecutiveFailureThreshold;
        settings.ApiFirstRouting = settings.ApiFirstRouting && ForgerEmsEnvironmentConfiguration.KyraApiFirst;
        settings.ProviderPriorityCsv = string.IsNullOrWhiteSpace(settings.ProviderPriorityCsv)
            ? ForgerEmsEnvironmentConfiguration.KyraProviderPriority
            : settings.ProviderPriorityCsv;
        settings.ConsensusMode = settings.ConsensusMode || ForgerEmsEnvironmentConfiguration.KyraConsensusMode;
        settings.MemoryMode = string.IsNullOrWhiteSpace(settings.MemoryMode) ? ForgerEmsEnvironmentConfiguration.KyraMemoryMode : settings.MemoryMode;
        settings.PersistMemory = settings.PersistMemory || ForgerEmsEnvironmentConfiguration.KyraPersistMemory;
        settings.KyraPersistentMemoryEnabled = settings.KyraPersistentMemoryEnabled || settings.PersistMemory;
        // Local repair memory is intentionally on by default for sanitized machine-scoped notes only.
        // Anonymous community learning remains explicit opt-in and has no upload client in this phase.
        if (!settings.KyraCommunitySharingEnabled &&
            (settings.KyraShareResolvedIssueFixPatterns ||
             settings.KyraShareHardwareCompatibilityPerformancePatterns ||
             settings.KyraShareCrashErrorDiagnostics))
        {
            settings.KyraCommunitySharingEnabled = true;
        }

        settings.KyraShareResolvedIssueFixPatterns = settings.KyraShareResolvedIssueFixPatterns && settings.KyraCommunitySharingEnabled;
        settings.KyraShareHardwareCompatibilityPerformancePatterns = settings.KyraShareHardwareCompatibilityPerformancePatterns && settings.KyraCommunitySharingEnabled;
        settings.KyraShareCrashErrorDiagnostics = settings.KyraShareCrashErrorDiagnostics && settings.KyraCommunitySharingEnabled;
        settings.MaxContextTurns = Math.Clamp(settings.MaxContextTurns <= 0 ? ForgerEmsEnvironmentConfiguration.KyraMaxContextTurns : settings.MaxContextTurns, 1, 200);
        settings.PersonalityProfile = string.IsNullOrWhiteSpace(settings.PersonalityProfile)
            ? ForgerEmsEnvironmentConfiguration.KyraPersonality
            : settings.PersonalityProfile;
        if (ForgerEmsEnvironmentConfiguration.KyraOnlineEnabled &&
            ForgerEmsEnvironmentConfiguration.KyraGatewayConfigured &&
            ForgerEmsEnvironmentConfiguration.KyraProvider.Equals("forgerems-gateway", StringComparison.OrdinalIgnoreCase) &&
            settings.Mode == CopilotMode.OfflineOnly)
        {
            settings.Mode = CopilotMode.ForgerEmsBetaGateway;
        }
        else if (ForgerEmsEnvironmentConfiguration.KyraOnlineEnabled && settings.Mode == CopilotMode.OfflineOnly)
        {
            settings.Mode = CopilotMode.HybridAuto;
        }

        settings.AllowOnlineSystemContextSharing |= ForgerEmsEnvironmentConfiguration.KyraShareSystemContext;
        settings.EnableFreeProviderPool = settings.EnableFreeProviderPool || ForgerEmsEnvironmentConfiguration.KyraOnlineEnabled;
        settings.EnableByokProviders = settings.EnableByokProviders || ForgerEmsEnvironmentConfiguration.KyraOnlineEnabled;

        foreach (var provider in _registry.Providers)
        {
            if (!settings.Providers.TryGetValue(provider.Id, out var providerConfig))
            {
                providerConfig = new CopilotProviderConfiguration();
                settings.Providers[provider.Id] = providerConfig;
            }

            providerConfig.BaseUrl = string.IsNullOrWhiteSpace(providerConfig.BaseUrl) ||
                                     KyraProviderConfigResolver.IsPlaceholderSecretOrValue(providerConfig.BaseUrl)
                ? provider.DefaultBaseUrl
                : providerConfig.BaseUrl;
            providerConfig.ModelName = string.IsNullOrWhiteSpace(providerConfig.ModelName) ||
                                       KyraProviderConfigResolver.IsPlaceholderSecretOrValue(providerConfig.ModelName)
                ? provider.DefaultModelName
                : providerConfig.ModelName;
            providerConfig.ApiKeyEnvironmentVariable = string.IsNullOrWhiteSpace(providerConfig.ApiKeyEnvironmentVariable)
                ? provider.DefaultApiKeyEnvironmentVariable
                : providerConfig.ApiKeyEnvironmentVariable;
            var resolvedProvider = KyraProviderConfigResolver.ResolveProvider(provider, providerConfig);
            providerConfig.IsEnabled = providerConfig.IsEnabled || provider.EnabledByDefault || (resolvedProvider.IsReady && provider.IsConfigured(providerConfig));
            providerConfig.TimeoutSeconds = providerConfig.TimeoutSeconds <= 0 ? settings.TimeoutSeconds : providerConfig.TimeoutSeconds;
            providerConfig.MaxRequestsPerMinute = providerConfig.MaxRequestsPerMinute <= 0 ? 12 : providerConfig.MaxRequestsPerMinute;
            providerConfig.DailyRequestCap = providerConfig.DailyRequestCap <= 0 ? (provider.IsOnlineProvider ? 60 : int.MaxValue) : providerConfig.DailyRequestCap;
            providerConfig.MaxInputCharacters = providerConfig.MaxInputCharacters <= 0 ? settings.MaxInputCharactersOnline : providerConfig.MaxInputCharacters;
            providerConfig.MaxOutputTokens = providerConfig.MaxOutputTokens <= 0 ? settings.MaxOutputTokensOnline : providerConfig.MaxOutputTokens;
        }

        settings.LiveTools ??= new KyraLiveToolsSettings();
        KyraProviderConfigResolver.ApplyLiveToolEnvironmentDefaults(settings.LiveTools);
        if (settings.LiveTools.CacheMinutes <= 0)
        {
            settings.LiveTools.CacheMinutes = 10;
        }

        settings.KyraRealtimeGatewayResearchEnabled =
            settings.KyraRealtimeGatewayResearchEnabled ||
            ForgerEmsEnvironmentConfiguration.KyraResearchEnabled ||
            (ForgerEmsEnvironmentConfiguration.KyraGatewayConfigured &&
             settings.Mode == CopilotMode.ForgerEmsBetaGateway);

        if (!ForgerEmsEnvironmentConfiguration.KyraGatewayRequireConsent)
        {
            settings.KyraRealtimeGatewayResearchConsent = true;
        }
    }
}
