#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: most enums and request/response models are generic.
// FORGEREMS_KYRA_ADAPTER: KyraIntent ForgerEMS entries; SystemContext; CopilotSettings ForgerEMS fields; CopilotContext.SystemProfile/PricingEstimate.
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

public enum CopilotMode
{
    OfflineOnly,
    ForgerEmsBetaGateway,
    OnlineAssisted,
    HybridAuto,
    OnlineWhenAvailable,
    AskFirst,
    FreeApiPool,
    BringYourOwnKey,
    ForgerEmsCloudFuture
}

public enum CopilotProviderType
{
    LocalOffline,
    OpenAICompatible,
    CustomOpenAICompatible,
    AnthropicClaude,
    OllamaLocal,
    LmStudioLocal,
    EbayPricing,
    GitHubReleases,
    ManufacturerSupport,
    MicrosoftDocs,
    LinuxReleaseInfo,
    GeminiApi,
    GroqApi,
    CerebrasApi,
    OpenRouterFree,
    MistralApi,
    GitHubModels,
    CloudflareWorkersAi,
    HuggingFaceInference,
    ForgerEmsGateway,
    ForgerEmsCloud
}

public enum KyraProviderMode
{
    OfflineLocal,
    ForgerEMSBetaGateway,
    FreeApiPool,
    Hybrid,
    OnlineApi,
    BringYourOwnKey,
    ForgerEMSCloudFuture
}

public enum KyraProviderKind
{
    Local,
    Gemini,
    Groq,
    Cerebras,
    OpenRouterFree,
    Mistral,
    GitHubModels,
    CloudflareWorkersAI,
    HuggingFace,
    OpenAI,
    Anthropic,
    ForgerEMSGateway,
    ForgerEMSCloud
}

public enum CopilotPromptMode
{
    General,
    Troubleshooting,
    FlipResale,
    Technician,
    ToolkitBuilder,
    CurrentLiveData
}

public enum KyraIntent
{
    PerformanceLag,
    AppFreezing,
    SlowBoot,
    UpgradeAdvice,
    ResaleValue,
    USBBuilderHelp,
    ToolkitManagerHelp,
    SystemHealthSummary,
    DriverIssue,
    StorageIssue,
    MemoryIssue,
    GPUQuestion,
    OSRecommendation,
    GeneralTechQuestion,
    ForgerEMSQuestion,
    LiveOnlineQuestion,
    Weather,
    News,
    CryptoPrice,
    StockPrice,
    Sports,
    CodeAssist,
    Unknown
}

public sealed class SystemContext
{
    public string CPU { get; init; } = "Unknown CPU";

    public string GPU { get; init; } = "Unknown GPU";

    public int RAM { get; init; }

    public string Storage { get; init; } = "Storage unknown";

    public string OS { get; init; } = "Unknown OS";

    public bool HasDedicatedGpu { get; init; }

    public string Device { get; init; } = "Unknown device";

    public static SystemContext FromProfile(SystemProfile? profile)
    {
        if (profile is null)
        {
            return new SystemContext
            {
                OS = Environment.OSVersion.VersionString
            };
        }

        var gpuNames = profile.Gpus.Count == 0
            ? "Unknown GPU"
            : string.Join(" + ", profile.Gpus.Select(gpu => gpu.Name).Take(3));
        var storage = profile.Disks.Count == 0
            ? "Storage health unknown"
            : string.Join("; ", profile.Disks.Select(disk => $"{disk.Name} {disk.MediaType} {disk.Size} health {disk.Health}").Take(3));

        return new SystemContext
        {
            CPU = profile.Cpu,
            GPU = gpuNames,
            RAM = (int)Math.Round(profile.RamTotalGb ?? 0),
            Storage = storage,
            OS = string.IsNullOrWhiteSpace(profile.OperatingSystem) ? Environment.OSVersion.VersionString : profile.OperatingSystem,
            HasDedicatedGpu = profile.Gpus.Any(gpu =>
                gpu.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                gpu.Name.Contains("AMD Radeon", StringComparison.OrdinalIgnoreCase) ||
                gpu.Name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
                gpu.Name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
                gpu.Name.Contains("GTX", StringComparison.OrdinalIgnoreCase)),
            Device = $"{profile.Manufacturer} {profile.Model}".Trim()
        };
    }
}

public sealed class CopilotSettings
{
    /// <summary>Beta default: operators configure providers via environment variables; testers are not prompted for BYOK.</summary>
    public KyraProviderConfigurationMode ProviderConfigurationMode { get; set; } = KyraProviderConfigurationMode.DeveloperManaged;

    public CopilotMode Mode { get; set; } = CopilotMode.OfflineOnly;

    public CopilotProviderType ProviderType { get; set; } = CopilotProviderType.LocalOffline;

    public string ModelName { get; set; } = "local-rules";

    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;

    public bool OfflineFallbackEnabled { get; set; } = true;

    public bool RedactContextEnabled { get; set; } = true;

    public int MaxContextCharacters { get; set; } = 12000;

    public bool UseLatestSystemScanContext { get; set; } = true;

    public bool AllowOnlineSystemContextSharing { get; set; }

    public bool EnableFreeProviderPool { get; set; } = true;

    public bool EnableByokProviders { get; set; }

    public bool DisableOnlineAfterConsecutiveFailures { get; set; } = true;

    public int ConsecutiveFailureThreshold { get; set; } = 4;

    public int MaxProviderFallbacksPerMessage { get; set; } = 4;

    public int FreeApiDailyRequestCap { get; set; } = 120;

    public int MaxInputCharactersOnline { get; set; } = 4000;

    public int MaxOutputTokensOnline { get; set; } = 700;

    public bool PreferLocalForDiagnostics { get; set; } = true;

    public bool PreferFreeProviderForGeneralChat { get; set; } = true;

    /// <summary>When true (default), online providers are tried before falling back to Local Kyra when mode allows.</summary>
    public bool ApiFirstRouting { get; set; } = true;

    public string ProviderPriorityCsv { get; set; } =
        "forgerems-gateway,openai-compatible,custom,openrouter,groq,gemini,anthropic,mistral,cerebras,github-models,cloudflare,lmstudio,ollama,offline";

    /// <summary>Future testing hook; off by default to avoid surprise token burn.</summary>
    public bool ConsensusMode { get; set; }

    public string MemoryMode { get; set; } = "session";

    public bool PersistMemory { get; set; }

    public int MaxContextTurns { get; set; } = 100;

    public string PersonalityProfile { get; set; } = "bubbly-tech";

    /// <summary>Optional on-disk preferences (non-sensitive); user toggle.</summary>
    public bool KyraPersistentMemoryEnabled { get; set; }

    /// <summary>Local-first sanitized machine repair memory. This is separate from chat transcript memory.</summary>
    public bool KyraLocalRepairMemoryEnabled { get; set; } = true;

    public bool KyraCommunitySharingEnabled { get; set; }

    public bool KyraShareResolvedIssueFixPatterns { get; set; }

    public bool KyraShareHardwareCompatibilityPerformancePatterns { get; set; }

    public bool KyraShareCrashErrorDiagnostics { get; set; }

    /// <summary>Kyra live weather/news/market tools (beta; API keys stored locally—never log them).</summary>
    public KyraLiveToolsSettings LiveTools { get; set; } = new();

    /// <summary>
    /// When false, the ForgerEMS Kyra gateway is not used for chat or realtime research, even if the environment allows it.
    /// </summary>
    public bool KyraRealtimeGatewayEnabled { get; set; } = true;

    /// <summary>
    /// When true with a configured gateway, current/realtime questions may use the server-side
    /// /v1/kyra/research path first (provider keys stay on the gateway).
    /// </summary>
    public bool KyraRealtimeGatewayResearchEnabled { get; set; }

    /// <summary>User consent for sanitized realtime gateway research (independent of local memory / community).</summary>
    public bool KyraRealtimeGatewayResearchConsent { get; set; } = true;

    /// <summary>
    /// When false, Kyra omits sanitized System Intelligence / cross-system summaries from LLM prompts and gateway research context.
    /// </summary>
    public bool KyraUseSanitizedSystemIntelligenceContext { get; set; } = true;

    /// <summary>When false, USB and Toolkit status summaries are omitted from Kyra context packages.</summary>
    public bool IncludeUsbToolkitStatusInKyraContext { get; set; } = true;

    public Dictionary<string, CopilotProviderConfiguration> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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

    /// <summary>When false, Kyra routing detail notes are omitted from the chat response (still used internally).</summary>
    public bool VerboseDiagnosticNotes { get; init; }

    /// <summary>Sanitized memory hint from optional local Kyra memory store.</summary>
    public string? KyraMemorySummaryForPrompt { get; init; }

    /// <summary>Optional UI status line (non-sensitive). Caller should marshal to UI thread.</summary>
    public Action<string>? KyraActivityStatusCallback { get; init; }

    /// <summary>Compact cross-system summary (USB + diagnostics + toolkit headlines).</summary>
    public string KyraSafeCrossSystemSummary { get; init; } = string.Empty;

    /// <summary>On-disk Kyra machine memory JSON (sanitized learning events; used for scan deltas).</summary>
    public string KyraMachineMemoryStorePath { get; init; } = string.Empty;

    public string MachineProfilesPath { get; init; } = string.Empty;
}

public sealed class CopilotResponse
{
    public string Text { get; init; } = string.Empty;

    public bool UsedOnlineData { get; init; }

    public string OnlineStatus { get; init; } = "Offline fallback";

    public CopilotProviderType ProviderType { get; init; } = CopilotProviderType.LocalOffline;

    public IReadOnlyList<string> ProviderNotes { get; init; } = Array.Empty<string>();

    public KyraResponseSource ResponseSource { get; init; } = KyraResponseSource.LocalKyra;

    public string SourceLabel { get; init; } = "Kyra";

    public bool FallbackUsed { get; init; }

    /// <summary>True when an online engine improved wording and the answer was not discarded by the truth guard.</summary>
    public bool OnlineEnhancementApplied { get; init; }

    /// <summary>True when a System Intelligence profile was attached to the turn (Kyra should not invent hardware).</summary>
    public bool GroundedInSystemIntelligence { get; init; }

    public IReadOnlyList<KyraActionSuggestion> ActionSuggestions { get; init; } = Array.Empty<KyraActionSuggestion>();

    /// <summary>Non-sensitive “why this answer?” line for the Details panel only (never secrets or raw context).</summary>
    public string? KyraTransparencySummary { get; init; }
}

public sealed class CopilotContext
{
    public string UserQuestion { get; init; } = string.Empty;

    public string ContextText { get; init; } = string.Empty;

    public CopilotPromptMode PromptMode { get; init; }

    public KyraIntent Intent { get; init; } = KyraIntent.Unknown;

    public KyraIntent PreviousIntent { get; init; } = KyraIntent.Unknown;

    public SystemContext SystemContext { get; init; } = new();

    public IReadOnlyList<CopilotChatMessage> ConversationHistory { get; init; } = Array.Empty<CopilotChatMessage>();

    public SystemProfile? SystemProfile { get; init; }

    public SystemHealthEvaluation? HealthEvaluation { get; init; }

    public IReadOnlyList<string> Recommendations { get; init; } = Array.Empty<string>();

    public PricingEstimate? PricingEstimate { get; init; }

    /// <summary>Optional block from Kyra tool adapters; merged into online provider prompts via <see cref="KyraPrivacyGate"/>.</summary>
    public string? ProviderRealtimeAugmentation { get; init; }

    /// <summary>Unified recap for prompts and routing (built from <see cref="KyraConversationMemory"/>).</summary>
    public KyraConversationContext? ConversationMeta { get; init; }

    /// <summary>User preference: professional, balanced, bubbly-tech (drives Local Kyra phrasing).</summary>
    public string PersonalityProfile { get; init; } = "bubbly-tech";
}

public sealed class CopilotProviderRequest
{
    public string AppVersion { get; init; } = string.Empty;

    public string Prompt { get; init; } = string.Empty;

    public CopilotContext Context { get; init; } = new();

    public CopilotSettings Settings { get; init; } = new();

    public CopilotProviderConfiguration ProviderConfiguration { get; init; } = new();
}

public enum KyraResponseSource
{
    LocalKyra,
    Gemini,
    OpenRouter,
    Groq,
    Cerebras,
    GitHubModels,
    Mistral,
    CloudflareWorkersAi,
    OpenAi,
    Anthropic,
    ForgerEmsGateway,
    LmStudio,
    Ollama
}

public sealed class KyraProviderScore
{
    public ICopilotProvider Provider { get; init; } = null!;
    public int Score { get; init; }
}

public sealed class KyraResponseEnvelope
{
    public CopilotResponse Response { get; init; } = new();
    public ICopilotProvider? Provider { get; init; }
}

