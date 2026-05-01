using System;
using System.Collections.Generic;
using System.Globalization;
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
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public enum CopilotMode
{
    OfflineOnly,
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
    ForgerEmsCloud
}

public enum KyraProviderMode
{
    OfflineLocal,
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
    ForgerEMSCloud
}

public enum KyraProviderStatus
{
    NotConfigured,
    Configured,
    Ok,
    RateLimited,
    CoolingDown,
    Failed,
    Disabled
}

public enum KyraProviderHealth
{
    Healthy,
    Degraded,
    Unavailable
}

public enum KyraProviderFailureReason
{
    None,
    NotConfigured,
    AuthFailed,
    RateLimited,
    Timeout,
    ModelUnavailable,
    ServiceUnavailable,
    NetworkError,
    PrivacyBlocked,
    SafetyBlocked,
    Unknown
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

    /// <summary>Optional on-disk preferences (non-sensitive); user toggle.</summary>
    public bool KyraPersistentMemoryEnabled { get; set; }

    /// <summary>Kyra live weather/news/market tools (beta; API keys stored locally—never log them).</summary>
    public KyraLiveToolsSettings LiveTools { get; set; } = new();

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

    public int DailyRequestCap { get; set; } = 60;

    public int MaxInputCharacters { get; set; } = 4000;

    public int MaxOutputTokens { get; set; } = 700;
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
}

public sealed class CopilotResponse
{
    public string Text { get; init; } = string.Empty;

    public bool UsedOnlineData { get; init; }

    public string OnlineStatus { get; init; } = "Offline fallback";

    public CopilotProviderType ProviderType { get; init; } = CopilotProviderType.LocalOffline;

    public IReadOnlyList<string> ProviderNotes { get; init; } = Array.Empty<string>();

    public KyraResponseSource ResponseSource { get; init; } = KyraResponseSource.LocalKyra;

    public string SourceLabel { get; init; } = "Answered by Local Kyra";

    public bool FallbackUsed { get; init; }

    public IReadOnlyList<KyraActionSuggestion> ActionSuggestions { get; init; } = Array.Empty<KyraActionSuggestion>();
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

    public KyraProviderFailureReason FailureReason { get; init; } = KyraProviderFailureReason.None;
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
    LmStudio,
    Ollama
}

[Flags]
public enum KyraModelCapability
{
    None = 0,
    FastChat = 1,
    DeepReasoning = 2,
    CodeHelp = 4,
    WritingPolish = 8
}

public sealed class KyraProviderScore
{
    public ICopilotProvider Provider { get; init; } = null!;
    public int Score { get; init; }
}

public sealed class KyraToolCallPlan
{
    public bool ShouldUseLocalToolAnswer { get; init; }
    public bool ShouldPolishWithProvider { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public KyraStayLocalReason StayLocalReason { get; init; }
}

public enum KyraStayLocalReason
{
    None = 0,
    MachineContextPrivacy = 1,
    DeviceToolkitRouting = 2
}

public sealed class KyraConversationState
{
    public KyraIntent LastIntent { get; init; } = KyraIntent.Unknown;
    public string LastKyraSummary { get; init; } = string.Empty;
    public string UnresolvedIssue { get; init; } = string.Empty;
}

public sealed class KyraResponseEnvelope
{
    public CopilotResponse Response { get; init; } = new();
    public ICopilotProvider? Provider { get; init; }
}

public sealed class KyraProviderException : Exception
{
    public KyraProviderException(string message, KyraProviderFailureReason reason)
        : base(message)
    {
        Reason = reason;
    }

    public KyraProviderFailureReason Reason { get; }
}

public sealed class KyraProviderQuotaState
{
    public bool IsConfigured { get; set; }

    public bool IsEnabled { get; set; }

    public DateTimeOffset? LastSuccessUtc { get; set; }

    public DateTimeOffset? LastFailureUtc { get; set; }

    public KyraProviderFailureReason LastFailureReason { get; set; } = KyraProviderFailureReason.None;

    public int DailyRequestCount { get; set; }

    public int EstimatedTokenUsage { get; set; }

    public int TimeoutCount { get; set; }

    public int ErrorCount { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset? CooldownUntilUtc { get; set; }
}

public static class KyraIntentRouter
{
    public static KyraIntent DetectIntent(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return KyraIntent.Unknown;
        }

        var text = prompt.ToLowerInvariant();
        if (KyraCodeSnippetDetector.LooksLikeCodeSnippet(prompt))
        {
            return KyraIntent.CodeAssist;
        }

        if (ContainsAny(text, "weather", "forecast", "humidity", "precipitation", "celsius", "fahrenheit") &&
            !ContainsAny(text, "ssd", "storage health", "forecast upgrade"))
        {
            return KyraIntent.Weather;
        }

        if (ContainsAny(text, "headline", "breaking news", "in the news", "news today", "current events") ||
            (text.Contains("news", StringComparison.OrdinalIgnoreCase) &&
             !ContainsAny(text, "usb", "ventoy", "toolkit", "driver")))
        {
            return KyraIntent.News;
        }

        if (ContainsAny(text, "bitcoin", "ethereum", "btc", "eth", "dogecoin", "solana", "crypto price", "altcoin"))
        {
            return KyraIntent.CryptoPrice;
        }

        if (ContainsAny(text, "stock", "ticker", "nasdaq", "nyse", "s&p", "share price", "equity") &&
            !ContainsAny(text, "usb stick", "thumb drive", "ventoy"))
        {
            return KyraIntent.StockPrice;
        }

        if (ContainsAny(text, "nfl", "nba", "mlb", "nhl", "soccer score", "super bowl", "world cup", "final score", "playoff"))
        {
            return KyraIntent.Sports;
        }

        if (ContainsAny(text, "right now", "live price", "at this moment") &&
            ContainsAny(text, "price", "market", "exchange rate"))
        {
            return KyraIntent.LiveOnlineQuestion;
        }

        if (ContainsAny(text, "prime video", "netflix", "youtube", "browser", "edge", "chrome") &&
            ContainsAny(text, "lag", "slow", "freezing", "freeze", "stutter", "hang", "crash"))
        {
            return KyraIntent.AppFreezing;
        }

        if (ContainsAny(text, "freezing", "freeze", "hang", "not responding", "crash", "stuck"))
        {
            return KyraIntent.AppFreezing;
        }

        if (ContainsAny(text, "slow boot", "boot slow", "startup slow", "takes forever to start", "start up slow", "login slow"))
        {
            return KyraIntent.SlowBoot;
        }

        if (ContainsAny(text, "lag", "slow", "stutter", "sluggish", "choppy", "bottleneck"))
        {
            return KyraIntent.PerformanceLag;
        }

        if (ContainsAny(text, "usb builder", "ventoy", "usb", "flash drive", "drive not showing", "vtoyefi"))
        {
            return KyraIntent.USBBuilderHelp;
        }

        if (ContainsAny(text, "toolkit", "download", "missing tools", "tool missing", "iso", "rescuezilla", "clonezilla"))
        {
            return KyraIntent.ToolkitManagerHelp;
        }

        if (ContainsAny(text, "gpu", "graphics", "nvidia", "radeon", "intel uhd", "display driver", "vram"))
        {
            return KyraIntent.GPUQuestion;
        }

        if (ContainsAny(text, "driver", "bios", "chipset", "device manager", "missing driver"))
        {
            return KyraIntent.DriverIssue;
        }

        if (ContainsAny(text, "storage", "ssd", "nvme", "hard drive", "disk", "smart", "wear", "bad sectors"))
        {
            return KyraIntent.StorageIssue;
        }

        if (ContainsAny(text, "memory", "ram", "16gb", "32gb", "ddr4", "ddr5"))
        {
            return KyraIntent.MemoryIssue;
        }

        if (ContainsAny(text, "upgrade", "better", "improve", "faster", "what should i upgrade", "upgrade first"))
        {
            return KyraIntent.UpgradeAdvice;
        }

        if (ContainsAny(text, "worth", "sell", "selling", "price", "value", "resale", "flip", "listing", "profit", "comps", "ebay"))
        {
            return KyraIntent.ResaleValue;
        }

        if (ContainsAny(text, "windows 11", "windows 10", "linux", "ubuntu", "mint", "xubuntu", "what os", "which os", "best os", "reinstall"))
        {
            return KyraIntent.OSRecommendation;
        }

        if (IsKyraInAppHelpQuestion(text))
        {
            return KyraIntent.ForgerEMSQuestion;
        }

        if (LooksLikeCasualGreetingOrGeneralAssistantChat(text))
        {
            return KyraIntent.GeneralTechQuestion;
        }

        if (ContainsAny(text, "forgerems", "how do i use", "what does this app", "system intelligence", "settings tab"))
        {
            return KyraIntent.ForgerEMSQuestion;
        }

        if (ContainsAny(text, "scan", "system", "spec", "health", "diagnose this pc", "device report"))
        {
            return KyraIntent.SystemHealthSummary;
        }

        return text.Length < 4 ? KyraIntent.Unknown : KyraIntent.GeneralTechQuestion;
    }

    /// <summary>
    /// User question appears to be about the machine Kyra is running on (not a generic tech essay).
    /// </summary>
    public static bool PromptReferencesThisMachine(string textLower)
    {
        return textLower.Contains("this laptop", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("this pc", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("this computer", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("this machine", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("from this one", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("my laptop", StringComparison.OrdinalIgnoreCase) ||
               textLower.Contains("on this machine", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// In-app Kyra configuration / behavior (stay local) — not casual "Hi Kyra" chat.
    /// </summary>
    private static bool IsKyraInAppHelpQuestion(string text)
    {
        if (!text.Contains("kyra", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ContainsAny(
                text,
                "why is kyra offline",
                "kyra offline",
                "configure kyra",
                "configuring kyra",
                "kyra settings",
                "kyra provider",
                "kyra providers",
                "kyra advanced",
                "enable kyra",
                "disable kyra",
                "turn on kyra",
                "turn off kyra",
                "kyra api",
                "refresh provider",
                "how do i use kyra",
                "how does kyra work in",
                "where is kyra",
                "kyra in forgerems",
                "forgerems kyra"))
        {
            return true;
        }

        if (text.Contains("kyra", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("session", StringComparison.OrdinalIgnoreCase) &&
            ContainsAny(text, "key", "api", "token"))
        {
            return true;
        }

        if (text.Contains("configure", StringComparison.OrdinalIgnoreCase) &&
            text.Contains("kyra", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Greetings and assistant-style chat should route API-first (free pool / BYOK), not in-app help.
    /// </summary>
    private static bool LooksLikeCasualGreetingOrGeneralAssistantChat(string text)
    {
        var t = text.Trim();
        if (t.Length == 0)
        {
            return false;
        }

        if (Regex.IsMatch(t, @"^(hi|hey|hello|yo|hiya|howdy|greetings|sup)\b([\s,!.?]+(there|again|kyra|friend)){0,4}\s*[!.?]*$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (Regex.IsMatch(t, @"^(good morning|good afternoon|good evening)\b([\s,!.?]+(there|kyra|friend)){0,4}\s*[!.?]*$", RegexOptions.IgnoreCase))
        {
            return true;
        }

        if (ContainsAny(
                text,
                "can you help me",
                "what can you do",
                "help me think",
                "explain this better",
                "write this cleaner",
                "rewrite this professionally",
                "rewrite professionally",
                "brainstorm",
                "make this listing sound better",
                "compare windows",
                "compare ubuntu",
                "windows vs",
                "ubuntu vs",
                "linux vs"))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class KyraConversationTurn
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string UserMessage { get; init; } = string.Empty;

    public string KyraResponseSummary { get; init; } = string.Empty;

    public KyraIntent Intent { get; init; } = KyraIntent.Unknown;

    public string SystemSnapshot { get; init; } = string.Empty;

    public string UnresolvedIssue { get; init; } = string.Empty;

    public string LastRecommendation { get; init; } = string.Empty;

    public bool GaveDiagnosticBreakdown { get; init; }
}

public sealed class KyraConversationMemory
{
    private readonly object _gate = new();
    private readonly List<KyraConversationTurn> _turns = [];
    private readonly int _maxTurns;

    public KyraConversationMemory(int maxTurns = 50)
    {
        _maxTurns = Math.Clamp(maxTurns, 20, 50);
    }

    public KyraIntent PreviousIntent
    {
        get
        {
            lock (_gate)
            {
                return _turns.LastOrDefault()?.Intent ?? KyraIntent.Unknown;
            }
        }
    }

    public bool AlreadyGaveDiagnosticBreakdown
    {
        get
        {
            lock (_gate)
            {
                return _turns.TakeLast(4).Any(turn => turn.GaveDiagnosticBreakdown);
            }
        }
    }

    public IReadOnlyList<KyraConversationTurn> Snapshot()
    {
        lock (_gate)
        {
            return _turns.ToArray();
        }
    }

    public CopilotChatMessage[] ToChatMessages()
    {
        lock (_gate)
        {
            return _turns
                .TakeLast(20)
                .SelectMany(turn => new[]
                {
                    new CopilotChatMessage { Role = "You", Text = turn.UserMessage, Timestamp = turn.Timestamp },
                    new CopilotChatMessage { Role = "Kyra", Text = turn.KyraResponseSummary, Timestamp = turn.Timestamp }
                })
                .ToArray();
        }
    }

    public KyraIntent ResolveIntent(string prompt, KyraIntent detectedIntent)
    {
        var text = prompt.ToLowerInvariant();
        if (detectedIntent != KyraIntent.GeneralTechQuestion && detectedIntent != KyraIntent.Unknown)
        {
            return detectedIntent;
        }

        if (ContainsAny(text, "what did you just say", "repeat that", "summarize that", "explain that simpler", "simpler", "what would you do", "give me the commands"))
        {
            return PreviousIntent == KyraIntent.Unknown ? KyraIntent.GeneralTechQuestion : PreviousIntent;
        }

        if (ContainsAny(text, "what about the gpu", "gpu?", "graphics?"))
        {
            return KyraIntent.GPUQuestion;
        }

        if (ContainsAny(text, "is that good for flipping", "good to flip", "worth flipping"))
        {
            return KyraIntent.ResaleValue;
        }

        if (ContainsAny(text, "upgrade first", "do first", "first?"))
        {
            return KyraIntent.UpgradeAdvice;
        }

        return detectedIntent;
    }

    public void AddTurn(string prompt, string response, KyraIntent intent, SystemContext context)
    {
        lock (_gate)
        {
            _turns.Add(new KyraConversationTurn
            {
                UserMessage = CopilotRedactor.Redact(prompt),
                KyraResponseSummary = Summarize(response),
                Intent = intent,
                SystemSnapshot = $"{context.Device}; {context.CPU}; {context.RAM} GB RAM; {context.GPU}",
                UnresolvedIssue = ExtractUnresolvedIssue(prompt, intent),
                LastRecommendation = ExtractLastRecommendation(response),
                GaveDiagnosticBreakdown = response.Contains("What I found", StringComparison.OrdinalIgnoreCase) ||
                                          response.Contains("What I’m seeing", StringComparison.OrdinalIgnoreCase)
            });

            if (_turns.Count > _maxTurns)
            {
                _turns.RemoveRange(0, _turns.Count - _maxTurns);
            }
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _turns.Clear();
        }
    }

    public KyraConversationState GetState()
    {
        lock (_gate)
        {
            var last = _turns.LastOrDefault();
            return new KyraConversationState
            {
                LastIntent = last?.Intent ?? KyraIntent.Unknown,
                LastKyraSummary = last?.KyraResponseSummary ?? string.Empty,
                UnresolvedIssue = _turns.Select(turn => turn.UnresolvedIssue).LastOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty
            };
        }
    }

    private static string Summarize(string value)
    {
        var clean = value.ReplaceLineEndings(" ").Trim();
        return clean.Length <= 260 ? clean : clean[..260] + "...";
    }

    private static string ExtractUnresolvedIssue(string prompt, KyraIntent intent)
    {
        return intent is KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot
            ? prompt
            : string.Empty;
    }

    private static string ExtractLastRecommendation(string response)
    {
        var line = response
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(item => item.TrimStart().StartsWith("1.", StringComparison.OrdinalIgnoreCase));
        return line ?? string.Empty;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public static class KyraSafetyGuard
{
    public static bool TryBuildRefusal(string prompt, out string response)
    {
        var text = prompt.ToLowerInvariant();
        if (ContainsAny(text, "steal password", "credential theft", "dump passwords", "bypass login", "bypass password", "bypass a password", "evade detection", "make malware", "write malware", "keylogger", "ransomware"))
        {
            response = """
                I can’t help with stealing credentials, bypassing someone else’s security, malware, or evasion.

                If this is your device, I can still help safely with account recovery, backing up data, malware removal, Windows repair, reinstall prep, or owner-authorized diagnostics.
                """;
            return true;
        }

        if (ContainsAny(text, "format c:", "delete system32", "wipe drive", "diskpart clean", "destroy data") &&
            !ContainsAny(text, "backup", "reinstall", "my device", "owned"))
        {
            response = """
                That could destroy data, so I won’t give destructive steps casually.

                If this is an owner-authorized repair, back up important files first and tell me the goal: clean reinstall, malware recovery, or drive prep.
                """;
            return true;
        }

        response = string.Empty;
        return false;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}

public static class KyraProviderRouter
{
    public static bool ShouldUseOnline(CopilotContext context, CopilotSettings settings)
    {
        if (settings.Mode is CopilotMode.OfflineOnly or CopilotMode.AskFirst)
        {
            return false;
        }

        if (KyraToolRouter.ShouldStayLocal(context.Intent, context.UserQuestion, settings))
        {
            return false;
        }

        return context.Intent is KyraIntent.LiveOnlineQuestion
                or KyraIntent.Weather
                or KyraIntent.News
                or KyraIntent.CryptoPrice
                or KyraIntent.StockPrice
                or KyraIntent.Sports
                or KyraIntent.CodeAssist
                or KyraIntent.ResaleValue
                or KyraIntent.UpgradeAdvice
                or KyraIntent.PerformanceLag
                or KyraIntent.AppFreezing
                or KyraIntent.SlowBoot
                or KyraIntent.GPUQuestion
                or KyraIntent.StorageIssue
                or KyraIntent.MemoryIssue
                or KyraIntent.DriverIssue
                or KyraIntent.OSRecommendation
                or KyraIntent.GeneralTechQuestion
                or KyraIntent.Unknown
            || context.UserQuestion.Contains("research", StringComparison.OrdinalIgnoreCase)
            || context.UserQuestion.Contains("lookup", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<KyraProviderScore> ScoreProviders(
        IReadOnlyList<ICopilotProvider> providers,
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        Func<ICopilotProvider, CopilotProviderConfiguration> configResolver)
    {
        var scores = new List<KyraProviderScore>();
        foreach (var provider in providers)
        {
            if (provider.ProviderType == CopilotProviderType.LocalOffline)
            {
                continue;
            }

            var config = configResolver(provider);
            if (!config.IsEnabled)
            {
                continue;
            }

            if (!settings.EnableByokProviders && provider.IsPaidProvider)
            {
                continue;
            }

            if (!settings.EnableFreeProviderPool && !provider.IsPaidProvider)
            {
                continue;
            }

            if (provider.Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase) &&
                ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source == KyraCredentialSource.None)
            {
                continue;
            }

            if (!provider.CanHandle(new CopilotProviderRequest
                {
                    Prompt = request.Prompt,
                    Context = context,
                    Settings = settings,
                    ProviderConfiguration = config
                }))
            {
                continue;
            }

            var deprioritizeLocalAi = !KyraToolRouter.ShouldStayLocal(context.Intent, request.Prompt, settings);
            var score = ScoreProvider(provider, context.Intent, deprioritizeLocalAi);
            scores.Add(new KyraProviderScore { Provider = provider, Score = score });
        }

        return scores
            .OrderByDescending(item => item.Score)
            .Take(Math.Max(1, settings.MaxProviderFallbacksPerMessage))
            .ToArray();
    }

    private static int ScoreProvider(ICopilotProvider provider, KyraIntent intent, bool deprioritizeLocalAi)
    {
        var capability = GetCapability(provider, intent);
        var baseScore = capability switch
        {
            KyraModelCapability.FastChat => 100,
            KyraModelCapability.DeepReasoning => 90,
            KyraModelCapability.CodeHelp => 88,
            KyraModelCapability.WritingPolish => 92,
            _ => 70
        };

        var bonus = provider.Id switch
        {
            "gemini-free" => 7,
            "openrouter-free" => 6,
            "github-models" => 5,
            "groq-free" => 4,
            "cerebras-free" => 3,
            _ => 0
        };

        var score = baseScore + bonus;

        if (deprioritizeLocalAi &&
            provider.ProviderType is CopilotProviderType.OllamaLocal or CopilotProviderType.LmStudioLocal)
        {
            score -= 28;
        }

        return score;
    }

    private static KyraModelCapability GetCapability(ICopilotProvider provider, KyraIntent intent)
    {
        if (intent is KyraIntent.GeneralTechQuestion or KyraIntent.LiveOnlineQuestion or KyraIntent.Weather
            or KyraIntent.News or KyraIntent.CryptoPrice or KyraIntent.StockPrice or KyraIntent.Sports)
        {
            return provider.Id switch
            {
                "gemini-free" or "groq-free" or "cerebras-free" or "openrouter-free" => KyraModelCapability.FastChat,
                "github-models" or "mistral-free" => KyraModelCapability.DeepReasoning,
                _ => KyraModelCapability.FastChat
            };
        }

        if (intent is KyraIntent.CodeAssist or KyraIntent.ForgerEMSQuestion or KyraIntent.DriverIssue)
        {
            return KyraModelCapability.CodeHelp;
        }

        if (intent is KyraIntent.ResaleValue or KyraIntent.UpgradeAdvice)
        {
            return KyraModelCapability.DeepReasoning;
        }

        return KyraModelCapability.WritingPolish;
    }
}

public static class KyraProviderPriority
{
    public static readonly string[] DefaultOrder =
    [
        "local-offline",
        "gemini-free",
        "groq-free",
        "cerebras-free",
        "openrouter-free",
        "mistral-free",
        "github-models",
        "cloudflare-workers-ai",
        "openai-compatible",
        "anthropic-claude",
        "forgerems-cloud"
    ];
}

public static class KyraPromptBuilder
{
    public static string BuildOnlinePrompt(CopilotContext context, bool includeSystemContext)
    {
        var basePrompt = includeSystemContext
            ? context.ContextText
            : context.UserQuestion;
        return basePrompt.Length <= 8000 ? basePrompt : basePrompt[..8000];
    }
}

/// <summary>
/// Routes machine-specific questions to Local Kyra when online providers must not receive system context.
/// </summary>
public static class KyraMachineContextRouter
{
    public static bool IsMachineAnchoredIntent(KyraIntent intent, string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        if (intent is KyraIntent.UpgradeAdvice or KyraIntent.ResaleValue or KyraIntent.PerformanceLag
            or KyraIntent.AppFreezing or KyraIntent.SlowBoot or KyraIntent.GPUQuestion
            or KyraIntent.StorageIssue or KyraIntent.MemoryIssue or KyraIntent.SystemHealthSummary
            or KyraIntent.DriverIssue)
        {
            return true;
        }

        if (intent == KyraIntent.OSRecommendation && KyraIntentRouter.PromptReferencesThisMachine(lower))
        {
            return true;
        }

        if (intent == KyraIntent.GeneralTechQuestion && KyraIntentRouter.PromptReferencesThisMachine(lower) &&
            (lower.Contains("upgrade", StringComparison.OrdinalIgnoreCase) ||
             lower.Contains("replace", StringComparison.OrdinalIgnoreCase) ||
             lower.Contains("laptop", StringComparison.OrdinalIgnoreCase) ||
             lower.Contains("worth", StringComparison.OrdinalIgnoreCase) ||
             lower.Contains("sell", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    public static bool RequiresLocalWhenContextSharingDisabled(KyraIntent intent, string prompt, CopilotSettings settings)
    {
        return !settings.AllowOnlineSystemContextSharing && IsMachineAnchoredIntent(intent, prompt);
    }
}

public static class KyraMessagePlanner
{
    public static KyraToolCallPlan BuildPlan(CopilotRequest request, CopilotContext context, CopilotSettings settings)
    {
        var lower = request.Prompt.ToLowerInvariant();
        var isListing = lower.Contains("listing", StringComparison.OrdinalIgnoreCase) || lower.Contains("make this sound better", StringComparison.OrdinalIgnoreCase);
        var stayReason = KyraToolRouter.GetStayLocalReason(context.Intent, request.Prompt, settings);
        var shouldStayLocal = stayReason != KyraStayLocalReason.None;
        var canUseOnline = KyraProviderRouter.ShouldUseOnline(context, settings);
        return new KyraToolCallPlan
        {
            ShouldUseLocalToolAnswer = shouldStayLocal || !canUseOnline,
            ShouldPolishWithProvider = isListing && canUseOnline && settings.EnableFreeProviderPool,
            ToolName = shouldStayLocal ? "Local Kyra Diagnostics" : (isListing ? "Listing Draft" : "Conversation"),
            StayLocalReason = shouldStayLocal ? stayReason : KyraStayLocalReason.None
        };
    }
}

public static class KyraToolRouter
{
    public static KyraStayLocalReason GetStayLocalReason(KyraIntent intent, string prompt, CopilotSettings settings)
    {
        if (intent == KyraIntent.ForgerEMSQuestion)
        {
            return KyraStayLocalReason.DeviceToolkitRouting;
        }

        if (intent is KyraIntent.USBBuilderHelp or KyraIntent.ToolkitManagerHelp)
        {
            return KyraStayLocalReason.DeviceToolkitRouting;
        }

        if (intent == KyraIntent.SystemHealthSummary &&
            (!settings.AllowOnlineSystemContextSharing || settings.PreferLocalForDiagnostics))
        {
            return KyraStayLocalReason.DeviceToolkitRouting;
        }

        var lower = prompt.ToLowerInvariant();
        if (lower.Contains("what provider", StringComparison.OrdinalIgnoreCase) &&
            lower.Contains("configured", StringComparison.OrdinalIgnoreCase))
        {
            return KyraStayLocalReason.DeviceToolkitRouting;
        }

        if (lower.Contains("scan my pc", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("usb not showing", StringComparison.OrdinalIgnoreCase))
        {
            return KyraStayLocalReason.DeviceToolkitRouting;
        }

        if (KyraMachineContextRouter.RequiresLocalWhenContextSharingDisabled(intent, prompt, settings))
        {
            return KyraStayLocalReason.MachineContextPrivacy;
        }

        return KyraStayLocalReason.None;
    }

    public static bool ShouldStayLocal(KyraIntent intent, string prompt, CopilotSettings settings) =>
        GetStayLocalReason(intent, prompt, settings) != KyraStayLocalReason.None;
}

public sealed class KyraOrchestrator
{
    public static (KyraToolCallPlan ToolPlan, IReadOnlyList<ICopilotProvider> Providers) BuildExecutionPlan(
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        IReadOnlyList<ICopilotProvider> providers,
        Func<ICopilotProvider, CopilotProviderConfiguration> configResolver)
    {
        var toolPlan = KyraMessagePlanner.BuildPlan(request, context, settings);
        if (toolPlan.ShouldUseLocalToolAnswer)
        {
            return (toolPlan, Array.Empty<ICopilotProvider>());
        }

        var scored = KyraProviderRouter.ScoreProviders(providers, request, settings, context, configResolver);
        return (toolPlan, scored.Select(item => item.Provider).ToArray());
    }
}

public static class KyraOnlineSafetyGate
{
    public static bool IsAllowedToCallOnline(string prompt, out string reason)
    {
        if (KyraSafetyGuard.TryBuildRefusal(prompt, out _))
        {
            reason = "unsafe request";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

public static class KyraPrivacyGate
{
    public static CopilotContext BuildProviderContext(CopilotContext context, bool allowSystemContextSharing)
    {
        var aug = BuildRealtimeAugmentationSection(context.ProviderRealtimeAugmentation);
        if (allowSystemContextSharing)
        {
            var sanitizedBlock = BuildSanitizedProviderSummary(context);
            var body = string.IsNullOrEmpty(aug)
                ? $"{sanitizedBlock}{Environment.NewLine}{Environment.NewLine}{context.UserQuestion}"
                : $"{sanitizedBlock}{Environment.NewLine}{Environment.NewLine}{aug}{Environment.NewLine}{Environment.NewLine}{context.UserQuestion}";
            return new CopilotContext
            {
                UserQuestion = context.UserQuestion,
                PromptMode = context.PromptMode,
                Intent = context.Intent,
                PreviousIntent = context.PreviousIntent,
                SystemContext = context.SystemContext,
                ContextText = body,
                ConversationHistory = context.ConversationHistory
            };
        }

        var privacyBody = string.IsNullOrEmpty(aug)
            ? context.UserQuestion
            : $"{aug}{Environment.NewLine}{Environment.NewLine}{context.UserQuestion}";
        return new CopilotContext
        {
            UserQuestion = context.UserQuestion,
            PromptMode = context.PromptMode,
            Intent = context.Intent,
            PreviousIntent = context.PreviousIntent,
            SystemContext = new SystemContext(),
            ContextText = privacyBody,
            ConversationHistory = context.ConversationHistory
        };
    }

    private static string BuildRealtimeAugmentationSection(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var safe = KyraSystemContextSanitizer.SanitizeForExternalProviders(raw.Trim());
        return "Real-time tool context (informational; verify figures and sources):" + Environment.NewLine + safe;
    }

    /// <summary>
    /// Provider-safe block derived from System Intelligence. Redacted; excludes serials, service tags, paths, and raw logs.
    /// </summary>
    public static string BuildSanitizedProviderSummary(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            var lightweight =
                "Sanitized system context: no System Intelligence profile is loaded. " +
                "I need a System Intelligence scan before I can give machine-specific advice." + Environment.NewLine +
                $"Lightweight hints only (not a full scan): {context.SystemContext.CPU}; {context.SystemContext.GPU}; {context.SystemContext.RAM} GB RAM; {context.SystemContext.Storage}; {context.SystemContext.OS}; device {context.SystemContext.Device}.";
            return CopilotRedactor.Redact(lightweight, enabled: true);
        }

        var profile = context.SystemProfile;
        var gpuLine = profile.Gpus.Count == 0
            ? "GPU unknown"
            : string.Join("; ", profile.Gpus.Select(gpu => gpu.Name).Take(3));
        var storageLine = profile.Disks.Count == 0
            ? "Storage unknown"
            : string.Join("; ",
                profile.Disks.Select(disk => $"{disk.MediaType} {disk.Size} health {disk.Health} status {disk.Status}").Take(4));
        var batteryLine = profile.Batteries.Count == 0
            ? "No battery detected"
            : string.Join("; ",
                profile.Batteries.Select(b =>
                        $"wear {(b.WearPercent.HasValue ? $"{b.WearPercent.Value:0.#}%" : "UNKNOWN")} cycles {(b.CycleCount.HasValue ? b.CycleCount.Value.ToString(CultureInfo.InvariantCulture) : "UNKNOWN")} status {b.Status}")
                    .Take(3));

        var healthScore = context.HealthEvaluation?.HealthScore;
        var issues = context.HealthEvaluation?.DetectedIssues.Take(5) ?? Enumerable.Empty<string>();
        var recs = context.Recommendations.Take(5);
        var problems = profile.ObviousProblems.Take(5);

        var block =
            "Sanitized System Intelligence summary (no serials, service tags, usernames, paths, or raw logs):" + Environment.NewLine +
            $"Device: {profile.Manufacturer} {profile.Model}" + Environment.NewLine +
            $"OS: {profile.OperatingSystem} build {profile.OsBuild}" + Environment.NewLine +
            $"CPU: {profile.Cpu}" + Environment.NewLine +
            $"RAM: {profile.RamTotal}; upgrade path: {profile.RamUpgradePath}" + Environment.NewLine +
            $"GPU: {gpuLine}" + Environment.NewLine +
            $"Storage: {storageLine}" + Environment.NewLine +
            $"Battery: {batteryLine}" + Environment.NewLine +
            $"Security: TPM present {FormatNullableBool(profile.TpmPresent)}, TPM ready {FormatNullableBool(profile.TpmReady)}, Secure Boot {FormatNullableBool(profile.SecureBoot)}" + Environment.NewLine +
            $"Network: {profile.NetworkStatus}; APIPA adapters {profile.ApipaAdapterCount}; missing gateway adapters {profile.MissingGatewayAdapterCount}" + Environment.NewLine +
            $"Overall: {profile.OverallStatus}; disk status {profile.DiskStatus}; battery status {profile.BatteryStatus}" + Environment.NewLine +
            (healthScore.HasValue ? $"Health score: {healthScore.Value}/100" + Environment.NewLine : string.Empty) +
            $"Notable issues: {string.Join("; ", issues)}" + Environment.NewLine +
            $"Recommendations: {string.Join("; ", recs)}" + Environment.NewLine +
            $"Warnings: {string.Join("; ", problems)}";

        return KyraSystemContextSanitizer.SanitizeForExternalProviders(CopilotRedactor.Redact(block, enabled: true));
    }

    private static string FormatNullableBool(bool? value) => value.HasValue ? value.Value.ToString() : "UNKNOWN";
}

public sealed class KyraResponseCache
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string key, out string value) => _cache.TryGetValue(key, out value!);

    public void Store(string key, string value)
    {
        _cache[key] = value;
    }

    public static bool IsCacheablePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var text = prompt.ToLowerInvariant();
        return !(text.Contains("current ") ||
                 text.Contains("latest ") ||
                 text.Contains("today") ||
                 text.Contains("right now") ||
                 text.Contains("password") ||
                 text.Contains("serial") ||
                 text.Contains("license"));
    }
}

public sealed class KyraProviderUsageTracker
{
    private readonly Dictionary<string, KyraProviderQuotaState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public KyraProviderQuotaState GetOrCreate(string providerId)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(providerId, out var state))
            {
                state = new KyraProviderQuotaState();
                _states[providerId] = state;
            }

            return state;
        }
    }
}

public static class KyraApiKeyStore
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> SessionKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void SetSessionKey(string providerId, string apiKey)
    {
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SessionKeys.Remove(providerId);
                return;
            }

            SessionKeys[providerId] = apiKey.Trim();
        }
    }

    public static void ClearSessionKey(string providerId)
    {
        lock (Sync)
        {
            SessionKeys.Remove(providerId);
        }
    }

    public static string GetSessionKey(string providerId)
    {
        lock (Sync)
        {
            return SessionKeys.TryGetValue(providerId, out var value) ? value : string.Empty;
        }
    }

    public static string ResolveApiKey(string providerId, CopilotProviderConfiguration configuration)
    {
        return ProviderEnvironmentResolver
            .ResolveApiCredential(providerId, configuration.ApiKeyEnvironmentVariable)
            .Value ?? string.Empty;
    }

    public static string Mask(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return string.Empty;
        }

        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 8)
        {
            return "****";
        }

        return $"{trimmed[..4]}...{trimmed[^4..]}";
    }
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

public interface IKyraProvider : ICopilotProvider
{
    KyraProviderKind Kind { get; }
}

public sealed class KyraProviderPool
{
    private readonly IReadOnlyList<ICopilotProvider> _providers;

    public KyraProviderPool(IReadOnlyList<ICopilotProvider> providers)
    {
        _providers = providers;
    }

    public IReadOnlyList<ICopilotProvider> GetPrioritizedProviders(CopilotSettings settings)
    {
        var index = KyraProviderPriority.DefaultOrder
            .Select((id, order) => new { id, order })
            .ToDictionary(item => item.id, item => item.order, StringComparer.OrdinalIgnoreCase);
        return _providers
            .OrderBy(provider => index.TryGetValue(provider.Id, out var order) ? order : int.MaxValue)
            .ThenBy(provider => provider.IsPaidProvider ? 1 : 0)
            .ToArray();
    }
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

    void ClearMemory();
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

    private static SystemGpuProfile[] MapGpus(JsonElement summary)
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

    private static string[] GetStringArray(JsonElement element, string propertyName)
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
    public static SystemHealthEvaluation Evaluate(SystemProfile? profile)
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
    public static IReadOnlyList<string> Generate(SystemProfile? profile, SystemHealthEvaluation evaluation)
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
            new GeminiCopilotProvider(),
            new OpenAiStyleCopilotProvider("groq-free", "Groq (Free Tier)", CopilotProviderType.GroqApi, "Free API pool", false, "https://api.groq.com/openai/v1", "llama-3.1-8b-instant", "GROQ_API_KEY", "Groq free-tier via OpenAI-compatible API."),
            new OpenAiStyleCopilotProvider("cerebras-free", "Cerebras (Free Tier)", CopilotProviderType.CerebrasApi, "Free API pool", false, "https://api.cerebras.ai/v1", "llama3.1-8b", "CEREBRAS_API_KEY", "Cerebras free inference via OpenAI-compatible API."),
            new OpenAiStyleCopilotProvider("openrouter-free", "OpenRouter Free", CopilotProviderType.OpenRouterFree, "Free API pool", false, "https://openrouter.ai/api/v1", "openrouter/auto", "OPENROUTER_API_KEY", "OpenRouter free model routing."),
            new OpenAiStyleCopilotProvider("mistral-free", "Mistral (Eval/BYOK)", CopilotProviderType.MistralApi, "Free API pool", false, "https://api.mistral.ai/v1", "mistral-small-latest", "MISTRAL_API_KEY", "Mistral API provider (free/eval depends on account plan)."),
            new OpenAiStyleCopilotProvider("github-models", "GitHub Models", CopilotProviderType.GitHubModels, "Free API pool", false, "https://models.inference.ai.azure.com", "gpt-4o-mini", "GITHUB_MODELS_TOKEN", "GitHub Models endpoint provider."),
            new OpenAiStyleCopilotProvider("cloudflare-workers-ai", "Cloudflare Workers AI", CopilotProviderType.CloudflareWorkersAi, "Free API pool", false, "https://api.cloudflare.com/client/v4/accounts", "@cf/meta/llama-3.1-8b-instruct", "CLOUDFLARE_API_KEY", "Cloudflare Workers AI (endpoint shape may require account-specific route)."),
            new StubCopilotProvider(CopilotProviderType.HuggingFaceInference, "huggingface-inference", "Hugging Face Inference Providers", "Free API pool", "Placeholder provider: endpoint/model compatibility varies by provider route."),
            new OpenAICompatibleCopilotProvider(),
            new AnthropicClaudeCopilotProvider(),
            new OllamaCopilotProvider(),
            new LmStudioCopilotProvider(),
            new StubCopilotProvider(CopilotProviderType.ForgerEmsCloud, "forgerems-cloud", "ForgerEMS Cloud (Future)", "Future", "Future ForgerEMS-hosted provider pool. Billing and broker routing intentionally not implemented in desktop app."),
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
    private readonly KyraToolRegistry _toolRegistry = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _providerRequests = new(StringComparer.OrdinalIgnoreCase);
    private readonly KyraProviderUsageTracker _usageTracker = new();
    private readonly KyraResponseCache _responseCache = new();
    private readonly KyraConversationMemory _memory = new();
    private SystemContext _lastSystemContext = new();

    public bool UseOnlineAI { get; set; }

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
            UseOnlineAI = settings.Mode is CopilotMode.OnlineAssisted or CopilotMode.HybridAuto or CopilotMode.OnlineWhenAvailable;
            string? lastReported = null;
            void Report(string message)
            {
                if (string.Equals(lastReported, message, StringComparison.Ordinal))
                {
                    return;
                }

                lastReported = message;
                try
                {
                    request.KyraActivityStatusCallback?.Invoke(message);
                }
                catch
                {
                }
            }

            Report("Checking system context…");
            var built = _contextBuilder.Build(request);
            Report("Sanitizing context…");
            Report("Checking configured tool…");
            var hostFacts = KyraToolRegistry.BuildHostFacts(request);
            var toolAugmentation = await _toolRegistry.BuildAugmentationAsync(
                new KyraToolExecutionRequest
                {
                    Intent = built.Intent,
                    Prompt = request.Prompt,
                    Context = built,
                    Settings = settings,
                    HostFacts = hostFacts
                },
                cancellationToken).ConfigureAwait(false);
            var context = AttachConversationMemory(AttachToolAugmentation(built, toolAugmentation, settings));
            var memoryState = _memory.GetState();
            _lastSystemContext = context.SystemContext;
            var notes = new List<string> { $"Intent detected: {context.Intent}", $"Previous intent: {memoryState.LastIntent}" };
            var localProvider = _providerRegistry.FindByType(CopilotProviderType.LocalOffline) ?? new LocalOfflineCopilotProvider();
            var executionPlan = KyraOrchestrator.BuildExecutionPlan(
                request,
                settings,
                context,
                _providerRegistry.Providers,
                provider => GetProviderConfig(settings, provider));
            var plan = executionPlan.ToolPlan;
            notes.Add($"Tool plan: {plan.ToolName}");
            if (request.VerboseDiagnosticNotes)
            {
                if (plan.StayLocalReason == KyraStayLocalReason.MachineContextPrivacy &&
                    settings.Mode is not CopilotMode.OfflineOnly and not CopilotMode.AskFirst)
                {
                    notes.Add("Kyra routing: machine-specific with online context sharing OFF -> Local Kyra (System Intelligence)");
                }
                else if (plan.StayLocalReason == KyraStayLocalReason.DeviceToolkitRouting &&
                         plan.ShouldUseLocalToolAnswer &&
                         settings.Mode is not CopilotMode.OfflineOnly and not CopilotMode.AskFirst)
                {
                    notes.Add("Kyra routing: local tool intent -> Local Kyra");
                }
            }

            if (_responseCache.TryGet(BuildCacheKey(request.Prompt), out var cached))
            {
                Report("Formatting Kyra response…");
                var hit = CompleteResponse(request, context, new CopilotResponse
                {
                    Text = cached,
                    UsedOnlineData = false,
                    ProviderType = CopilotProviderType.LocalOffline,
                    OnlineStatus = "Kyra Mode: Local cache hit - no provider call needed.",
                    ProviderNotes = notes,
                    ResponseSource = KyraResponseSource.LocalKyra,
                    SourceLabel = "Answered by Local Kyra"
                });
                Report("Done.");
                return hit;
            }

            if (settings.Mode is CopilotMode.OfflineOnly or CopilotMode.AskFirst || plan.ShouldUseLocalToolAnswer)
            {
                Report("Using local fallback…");
                var localResultEarly = await RunProviderSafeAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                var offlineStatus = settings.Mode == CopilotMode.AskFirst
                    ? "Kyra Mode: Hybrid (Ask First) - currently local/offline until you explicitly enable an online lookup."
                    : "Kyra Mode: Offline Local - no data leaves this machine.";
                var localResponse = ApplyLocalKyraSourceLabel(
                    BuildResponse(localResultEarly, localProvider, notes, offlineStatus),
                    plan,
                    context,
                    request.Prompt,
                    settings);

                Report("Formatting Kyra response…");
                var earlyDone = CompleteResponse(request, context, localResponse);
                Report("Done.");
                return earlyDone;
            }

            var candidates = executionPlan.Providers;
            if (candidates.Count == 0)
            {
                notes.Add("Kyra routing: no API providers selected; answering with Local Kyra.");
                Report("Using local fallback…");
                var localResultEmpty = await RunProviderSafeAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                var status = settings.Mode is CopilotMode.OnlineAssisted or CopilotMode.OnlineWhenAvailable
                    ? "No online provider is configured yet. Local Kyra is still available."
                    : "Kyra Mode: Free API Pool unavailable. Fallback: Local Kyra active.";
                Report("Formatting Kyra response…");
                var emptyProviders = CompleteResponse(
                    request,
                    context,
                    ApplyLocalKyraSourceLabel(BuildResponse(localResultEmpty, localProvider, notes, status), plan, context, request.Prompt, settings));
                Report("Done.");
                return emptyProviders;
            }

            var apiFirst = settings.ApiFirstRouting && !plan.ShouldPolishWithProvider;

            if (apiFirst)
            {
                var freePoolAttemptFailed = false;
                for (var i = 0; i < candidates.Count; i++)
                {
                    var provider = candidates[i];
                    Report(provider.IsOnlineProvider ? "Asking API provider…" : "Thinking locally…");
                    var result = await RunProviderSafeAsync(provider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        if (KyraResponseCache.IsCacheablePrompt(request.Prompt))
                        {
                            _responseCache.Store(BuildCacheKey(request.Prompt), result.UserMessage);
                        }

                        var status = result.UsedOnlineData
                            ? $"Kyra Mode: Free API Pool | Provider: {provider.DisplayName}"
                            : $"Kyra Mode: Hybrid | Provider: {provider.DisplayName} (no internet data sent)";
                        if (freePoolAttemptFailed &&
                            provider.ProviderType is CopilotProviderType.OllamaLocal or CopilotProviderType.LmStudioLocal)
                        {
                            notes.Add("Kyra routing: API exhausted -> Local AI");
                        }

                        notes.Add($"Kyra routing: normal chat -> {provider.DisplayName}");
                        var onlineResponse = BuildResponse(result, provider, notes, status);
                        if (provider.IsOnlineProvider &&
                            settings.AllowOnlineSystemContextSharing &&
                            KyraMachineContextRouter.IsMachineAnchoredIntent(context.Intent, request.Prompt))
                        {
                            onlineResponse = WithSourceLabel(onlineResponse,
                                $"Answered by {provider.DisplayName} (API) using sanitized System Intelligence context");
                        }
                        else if (provider.IsOnlineProvider && !string.IsNullOrWhiteSpace(toolAugmentation))
                        {
                            onlineResponse = WithSourceLabel(onlineResponse,
                                $"Answered by {provider.DisplayName} (API) with real-time/tool context where available");
                        }

                        Report("Formatting Kyra response…");
                        var apiOk = CompleteResponse(request, context, onlineResponse);
                        Report("Done.");
                        return apiOk;
                    }

                    if (provider.IsOnlineProvider)
                    {
                        freePoolAttemptFailed = true;
                    }

                    if (i < candidates.Count - 1)
                    {
                        notes.Add($"Kyra routing: provider failed ({provider.DisplayName}) -> trying next provider");
                    }
                }

                if (settings.OfflineFallbackEnabled)
                {
                    notes.Add("Kyra routing: all AI unavailable -> Local Kyra");
                    Report("Using local fallback…");
                    var localFallback = await RunProviderSafeAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                    var fb = localFallback.UserMessage?.Trim() ?? string.Empty;
                    if (!fb.StartsWith("I couldn’t reach", StringComparison.OrdinalIgnoreCase) &&
                        !fb.StartsWith("I couldn't reach", StringComparison.OrdinalIgnoreCase))
                    {
                        fb = "I couldn’t reach the online assistants right now, so I’m answering offline with Local Kyra.\n\n" + fb;
                    }

                    var wrappedLocal = new CopilotProviderResult
                    {
                        Succeeded = true,
                        UsedOnlineData = false,
                        UserMessage = fb
                    };
                    Report("Formatting Kyra response…");
                    var fbResp = CompleteResponse(
                        request,
                        context,
                        ApplyLocalKyraSourceLabel(
                            BuildResponse(wrappedLocal, localProvider, notes, "Local Kyra fallback — online providers were unavailable."),
                            plan,
                            context,
                            request.Prompt,
                            settings));
                    Report("Done.");
                    return fbResp;
                }

                Report("Formatting Kyra response…");
                var noFb = CompleteResponse(request, context, new CopilotResponse
                {
                    Text = "Kyra could not get a provider response and offline fallback is disabled. Re-enable offline fallback or check provider settings.",
                    OnlineStatus = "Error state - no fallback available.",
                    ProviderNotes = notes
                });
                Report("Done.");
                return noFb;
            }

            Report("Thinking locally…");
            var localResult = await RunProviderSafeAsync(localProvider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
            var freePoolAttemptFailedLegacy = false;
            for (var i = 0; i < candidates.Count; i++)
            {
                var provider = candidates[i];
                Report(provider.IsOnlineProvider ? "Asking API provider…" : "Thinking locally…");
                var result = await RunProviderSafeAsync(provider, request, settings, context, notes, cancellationToken).ConfigureAwait(false);
                if (result.Succeeded)
                {
                    if (plan.ShouldPolishWithProvider)
                    {
                        result = new CopilotProviderResult
                        {
                            Succeeded = true,
                            UsedOnlineData = result.UsedOnlineData,
                            UserMessage = $"Quick draft (local):{Environment.NewLine}{localResult.UserMessage}{Environment.NewLine}{Environment.NewLine}Polished version ({provider.DisplayName}):{Environment.NewLine}{result.UserMessage}"
                        };
                    }

                    if (KyraResponseCache.IsCacheablePrompt(request.Prompt))
                    {
                        _responseCache.Store(BuildCacheKey(request.Prompt), result.UserMessage);
                    }

                    var status = result.UsedOnlineData
                        ? $"Kyra Mode: Free API Pool | Provider: {provider.DisplayName}"
                        : $"Kyra Mode: Hybrid | Provider: {provider.DisplayName} (no internet data sent)";
                    if (freePoolAttemptFailedLegacy &&
                        provider.ProviderType is CopilotProviderType.OllamaLocal or CopilotProviderType.LmStudioLocal)
                    {
                        notes.Add("Kyra routing: API exhausted -> Local AI");
                    }

                    notes.Add($"Kyra routing: normal chat -> {provider.DisplayName}");
                    var onlineResponse = BuildResponse(result, provider, notes, status);
                    if (provider.IsOnlineProvider &&
                        settings.AllowOnlineSystemContextSharing &&
                        KyraMachineContextRouter.IsMachineAnchoredIntent(context.Intent, request.Prompt))
                    {
                        onlineResponse = WithSourceLabel(onlineResponse,
                            $"Answered by {provider.DisplayName} using sanitized System Intelligence context");
                    }

                    Report("Formatting Kyra response…");
                    var legacyOk = CompleteResponse(request, context, onlineResponse);
                    Report("Done.");
                    return legacyOk;
                }

                if (provider.IsOnlineProvider)
                {
                    freePoolAttemptFailedLegacy = true;
                }

                if (i < candidates.Count - 1)
                {
                    notes.Add($"Kyra routing: provider failed ({provider.DisplayName}) -> trying next provider");
                }
            }

            if (settings.OfflineFallbackEnabled)
            {
                notes.Add("Kyra routing: all AI unavailable -> Local Kyra");
                Report("Using local fallback…");
                Report("Formatting Kyra response…");
                var allFail = CompleteResponse(
                    request,
                    context,
                    ApplyLocalKyraSourceLabel(
                        BuildResponse(localResult, localProvider, notes, "All configured AI providers failed, so I answered with Local Kyra."),
                        plan,
                        context,
                        request.Prompt,
                        settings));
                Report("Done.");
                return allFail;
            }

            Report("Formatting Kyra response…");
            var errEnd = CompleteResponse(request, context, new CopilotResponse
            {
                Text = "Kyra could not get a provider response and offline fallback is disabled. Re-enable offline fallback or check provider settings.",
                OnlineStatus = "Error state - no fallback available.",
                ProviderNotes = notes
            });
            Report("Done.");
            return errEnd;
        }
        catch (OperationCanceledException)
        {
            return new CopilotResponse
            {
                Text = "Kyra generation was stopped.",
                OnlineStatus = "Stopped",
                ProviderNotes = ["Request cancelled by operator."]
            };
        }
        catch (Exception exception)
        {
            return new CopilotResponse
            {
                Text = "Kyra hit an internal error and fell back safely. Try again after refreshing the System Intelligence scan.",
                OnlineStatus = "Error state - safe fallback",
                ProviderNotes = [$"Internal Kyra error: {exception.Message}"]
            };
        }
    }

    public KyraIntent DetectIntent(string prompt) => _memory.ResolveIntent(prompt, KyraIntentRouter.DetectIntent(prompt));

    public SystemContext GetSystemContext() => _lastSystemContext;

    public async Task<string> GenerateResponse(string prompt)
    {
        var intent = DetectIntent(prompt);
        if (UseOnlineAI && intent == KyraIntent.GeneralTechQuestion)
        {
            return await CallExternalAPI(prompt).ConfigureAwait(false);
        }

        return intent switch
        {
            KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot => HandlePerformance(prompt),
            KyraIntent.UpgradeAdvice => HandleUpgrade(prompt),
            KyraIntent.ResaleValue => HandleResale(prompt),
            KyraIntent.SystemHealthSummary => HandleSystem(prompt),
            KyraIntent.GeneralTechQuestion => await HandleGeneral(prompt).ConfigureAwait(false),
            _ => LocalResponse(prompt)
        };
    }

    public void ClearMemory()
    {
        _memory.Clear();
    }

    private string HandlePerformance(string prompt) => LocalResponse(prompt);

    private string HandleUpgrade(string prompt) => LocalResponse(prompt);

    private string HandleResale(string prompt) => LocalResponse(prompt);

    private string HandleSystem(string prompt) => LocalResponse(prompt);

    private Task<string> HandleGeneral(string prompt) => Task.FromResult(LocalResponse(prompt));

    private string LocalResponse(string prompt)
    {
        var context = new CopilotContext
        {
            UserQuestion = prompt,
            Intent = DetectIntent(prompt),
            PreviousIntent = _memory.PreviousIntent,
            SystemContext = GetSystemContext(),
            ConversationHistory = GetHistorySnapshot()
        };
        return LocalRulesCopilotEngine.GenerateReply(prompt, context);
    }

    private static Task<string> CallExternalAPI(string prompt)
    {
        return Task.FromResult("Online Kyra is ready for provider wiring, but no external provider is configured in this build. I can still help offline with this PC, USB builds, resale prep, OS choices, and troubleshooting.");
    }

    private CopilotContext AttachConversationMemory(CopilotContext context)
    {
        var resolvedIntent = _memory.ResolveIntent(context.UserQuestion, context.Intent);
        return new CopilotContext
        {
            UserQuestion = context.UserQuestion,
            ContextText = context.ContextText,
            PromptMode = context.PromptMode,
            Intent = resolvedIntent,
            PreviousIntent = _memory.PreviousIntent,
            SystemContext = context.SystemContext,
            ConversationHistory = GetHistorySnapshot(),
            SystemProfile = context.SystemProfile,
            HealthEvaluation = context.HealthEvaluation,
            Recommendations = context.Recommendations,
            PricingEstimate = context.PricingEstimate,
            ProviderRealtimeAugmentation = context.ProviderRealtimeAugmentation
        };
    }

    private static CopilotContext AttachToolAugmentation(CopilotContext context, string? augmentation, CopilotSettings settings)
    {
        if (string.IsNullOrWhiteSpace(augmentation))
        {
            return context;
        }

        var safe = KyraSystemContextSanitizer.SanitizeForExternalProviders(augmentation.Trim());
        var block = Environment.NewLine + Environment.NewLine + "Real-time tool context (informational; verify figures):" + Environment.NewLine + safe;
        var newText = context.ContextText + block;
        if (settings.MaxContextCharacters > 0 && newText.Length > settings.MaxContextCharacters)
        {
            newText = newText[..settings.MaxContextCharacters] + Environment.NewLine + "[context trimmed]";
        }

        return new CopilotContext
        {
            UserQuestion = context.UserQuestion,
            ContextText = newText,
            PromptMode = context.PromptMode,
            Intent = context.Intent,
            PreviousIntent = context.PreviousIntent,
            SystemContext = context.SystemContext,
            ConversationHistory = context.ConversationHistory,
            SystemProfile = context.SystemProfile,
            HealthEvaluation = context.HealthEvaluation,
            Recommendations = context.Recommendations,
            PricingEstimate = context.PricingEstimate,
            ProviderRealtimeAugmentation = safe
        };
    }

    private CopilotResponse CompleteResponse(CopilotRequest request, CopilotContext context, CopilotResponse response)
    {
        RecordConversationTurn(request.Prompt, response.Text, context.Intent);
        var filteredNotes = FilterProviderNotesForDisplay(response.ProviderNotes, request.VerboseDiagnosticNotes);
        if (filteredNotes.Count == response.ProviderNotes.Count)
        {
            return response;
        }

        return new CopilotResponse
        {
            Text = response.Text,
            UsedOnlineData = response.UsedOnlineData,
            OnlineStatus = response.OnlineStatus,
            ProviderType = response.ProviderType,
            ProviderNotes = filteredNotes,
            ResponseSource = response.ResponseSource,
            SourceLabel = response.SourceLabel,
            FallbackUsed = response.FallbackUsed,
            ActionSuggestions = response.ActionSuggestions
        };
    }

    private static IReadOnlyList<string> FilterProviderNotesForDisplay(IReadOnlyList<string> notes, bool verbose)
    {
        if (verbose || notes.Count == 0)
        {
            return notes;
        }

        return notes
            .Where(static note =>
                note.StartsWith("Intent detected:", StringComparison.OrdinalIgnoreCase) ||
                note.StartsWith("Previous intent:", StringComparison.OrdinalIgnoreCase) ||
                note.StartsWith("Tool plan:", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private CopilotChatMessage[] GetHistorySnapshot() => _memory.ToChatMessages();

    private void RecordConversationTurn(string prompt, string response, KyraIntent intent)
    {
        _memory.AddTurn(prompt, response, intent, GetSystemContext());
    }

    private async Task<CopilotProviderResult> RunProviderSafeAsync(
        ICopilotProvider provider,
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        if (provider.IsOnlineProvider && !KyraOnlineSafetyGate.IsAllowedToCallOnline(request.Prompt, out _))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.SafetyBlocked,
                UserMessage = "Kyra blocked this request before contacting any online provider."
            };
        }

        var providerConfig = GetProviderConfig(settings, provider);
        var quotaState = _usageTracker.GetOrCreate(provider.Id);
        quotaState.IsConfigured = provider.IsConfigured(providerConfig);
        quotaState.IsEnabled = providerConfig.IsEnabled;
        if (quotaState.CooldownUntilUtc is not null && quotaState.CooldownUntilUtc > DateTimeOffset.UtcNow)
        {
            notes.Add($"{provider.DisplayName}: cooldown active");
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.RateLimited,
                IsTransientFailure = true,
                UserMessage = $"{provider.DisplayName} appears rate-limited right now. I’m trying the next configured provider."
            };
        }

        if (!provider.IsConfigured(providerConfig))
        {
            notes.Add($"{provider.DisplayName}: not configured");
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = $"{provider.DisplayName} is not configured.",
                DiagnosticMessage = provider.StatusText
            };
        }

        if (!TryEnterRateLimit(provider, providerConfig, notes))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.RateLimited,
                UserMessage = $"{provider.DisplayName} appears rate-limited right now. I’m trying the next configured provider.",
                DiagnosticMessage = "Rate limit reached."
            };
        }

        if (provider.IsOnlineProvider && quotaState.DailyRequestCount >= Math.Max(1, providerConfig.DailyRequestCap))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.RateLimited,
                UserMessage = $"{provider.DisplayName} local daily cap reached."
            };
        }

        var providerContext = provider.IsOnlineProvider
            ? KyraPrivacyGate.BuildProviderContext(context, settings.AllowOnlineSystemContextSharing)
            : context;

        var providerRequest = new CopilotProviderRequest
        {
            Prompt = request.Prompt,
            Context = providerContext,
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
                    if (lastResult.Succeeded)
                    {
                        quotaState.LastSuccessUtc = DateTimeOffset.UtcNow;
                        quotaState.ConsecutiveFailures = 0;
                        quotaState.DailyRequestCount++;
                    }
                    return lastResult;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    FailureReason = KyraProviderFailureReason.Timeout,
                    IsTransientFailure = true,
                    UserMessage = $"{provider.DisplayName} timed out.",
                    DiagnosticMessage = "Provider timeout."
                };
                quotaState.TimeoutCount++;
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
                    FailureReason = KyraProviderFailureReason.NetworkError,
                    IsTransientFailure = true,
                    UserMessage = $"{provider.DisplayName} network request failed.",
                    DiagnosticMessage = exception.Message
                };
                quotaState.ErrorCount++;
                notes.Add($"{provider.DisplayName}: network failure on attempt {attempt}");
            }
            catch (Exception exception)
            {
                lastResult = new CopilotProviderResult
                {
                    Succeeded = false,
                    FailureReason = KyraProviderFailureReason.Unknown,
                    UserMessage = $"{provider.DisplayName} failed safely.",
                    DiagnosticMessage = exception.Message
                };
                quotaState.ErrorCount++;
                notes.Add($"{provider.DisplayName}: failed safely ({exception.Message})");
                return lastResult;
            }

            if (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        quotaState.LastFailureUtc = DateTimeOffset.UtcNow;
        quotaState.LastFailureReason = lastResult.FailureReason;
        quotaState.ConsecutiveFailures++;
        if (lastResult.FailureReason == KyraProviderFailureReason.RateLimited)
        {
            quotaState.CooldownUntilUtc = DateTimeOffset.UtcNow.AddMinutes(10);
        }

        return lastResult;
    }

    private IEnumerable<ICopilotProvider> SelectOnlineProviders(CopilotRequest request, CopilotSettings settings, CopilotContext context)
    {
        if (!KyraProviderRouter.ShouldUseOnline(context, settings))
        {
            return Array.Empty<ICopilotProvider>();
        }

        var scored = KyraProviderRouter.ScoreProviders(
            _providerRegistry.Providers,
            request,
            settings,
            context,
            provider => GetProviderConfig(settings, provider));

        return scored.Select(item => item.Provider);
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
                MaxRetries = provider.IsOnlineProvider ? 1 : 0,
                DailyRequestCap = provider.IsOnlineProvider ? 60 : int.MaxValue,
                MaxInputCharacters = settings.MaxInputCharactersOnline,
                MaxOutputTokens = settings.MaxOutputTokensOnline
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
        var source = MapSource(provider);
        var usedFallback = provider.ProviderType == CopilotProviderType.LocalOffline &&
                           notes.Any(note => note.Contains("failed", StringComparison.OrdinalIgnoreCase) || note.Contains("timeout", StringComparison.OrdinalIgnoreCase) || note.Contains("rate", StringComparison.OrdinalIgnoreCase));
        return new CopilotResponse
        {
            Text = string.IsNullOrWhiteSpace(result.UserMessage)
                ? "Kyra could not produce a response."
                : result.UserMessage,
            UsedOnlineData = result.UsedOnlineData,
            OnlineStatus = status,
            ProviderType = provider.ProviderType,
            ProviderNotes = notes,
            ResponseSource = source,
            SourceLabel = usedFallback ? "Fallback: Local Kyra" : $"Answered by {provider.DisplayName}",
            FallbackUsed = usedFallback,
            ActionSuggestions = []
        };
    }

    private static CopilotResponse WithSourceLabel(CopilotResponse response, string sourceLabel) =>
        new()
        {
            Text = response.Text,
            UsedOnlineData = response.UsedOnlineData,
            OnlineStatus = response.OnlineStatus,
            ProviderType = response.ProviderType,
            ProviderNotes = response.ProviderNotes,
            ResponseSource = response.ResponseSource,
            SourceLabel = sourceLabel,
            FallbackUsed = response.FallbackUsed,
            ActionSuggestions = response.ActionSuggestions
        };

    private static CopilotResponse ApplyLocalKyraSourceLabel(
        CopilotResponse response,
        KyraToolCallPlan plan,
        CopilotContext context,
        string prompt,
        CopilotSettings settings)
    {
        if (response.FallbackUsed)
        {
            return response;
        }

        if (plan.ShouldUseLocalToolAnswer &&
            settings.Mode is not CopilotMode.OfflineOnly and not CopilotMode.AskFirst &&
            plan.StayLocalReason != KyraStayLocalReason.None)
        {
            var label = plan.StayLocalReason switch
            {
                KyraStayLocalReason.MachineContextPrivacy => "Answered by Local Kyra using System Intelligence",
                KyraStayLocalReason.DeviceToolkitRouting => "Answered by Local Kyra because this was a device/tool task.",
                _ => response.SourceLabel
            };
            return WithSourceLabel(response, label);
        }

        if (response.ProviderType == CopilotProviderType.LocalOffline &&
            context.SystemProfile is not null &&
            KyraMachineContextRouter.IsMachineAnchoredIntent(context.Intent, prompt))
        {
            return WithSourceLabel(response, "Answered by Local Kyra using System Intelligence");
        }

        return response;
    }

    private static KyraResponseSource MapSource(ICopilotProvider provider)
    {
        return provider.ProviderType switch
        {
            CopilotProviderType.GeminiApi => KyraResponseSource.Gemini,
            CopilotProviderType.OpenRouterFree => KyraResponseSource.OpenRouter,
            CopilotProviderType.GroqApi => KyraResponseSource.Groq,
            CopilotProviderType.CerebrasApi => KyraResponseSource.Cerebras,
            CopilotProviderType.GitHubModels => KyraResponseSource.GitHubModels,
            CopilotProviderType.MistralApi => KyraResponseSource.Mistral,
            CopilotProviderType.CloudflareWorkersAi => KyraResponseSource.CloudflareWorkersAi,
            CopilotProviderType.OpenAICompatible => KyraResponseSource.OpenAi,
            CopilotProviderType.AnthropicClaude => KyraResponseSource.Anthropic,
            CopilotProviderType.LmStudioLocal => KyraResponseSource.LmStudio,
            CopilotProviderType.OllamaLocal => KyraResponseSource.Ollama,
            _ => KyraResponseSource.LocalKyra
        };
    }

    private static string BuildCacheKey(string prompt)
    {
        return prompt.Trim().ToLowerInvariant();
    }

    /// <summary>Loads System Intelligence JSON for slash commands and host snapshots (same mapping as Kyra context).</summary>
    public static SystemProfile? TryLoadSystemProfileFromReport(string? reportPath)
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
}

public sealed class CopilotContextBuilder : ICopilotContextBuilder
{
    private readonly PricingEngine _pricingEngine = new();

    public CopilotContext Build(CopilotRequest request)
    {
        var settings = request.Settings ?? new CopilotSettings();
        var intent = KyraIntentRouter.DetectIntent(request.Prompt);
        var promptMode = DetectPromptMode(request.Prompt, intent);
        var profile = settings.UseLatestSystemScanContext
            ? LoadSystemProfile(request.SystemIntelligenceReportPath)
            : null;
        var systemContext = SystemContext.FromProfile(profile);
        var health = SystemHealthEvaluator.Evaluate(profile);
        var recommendations = RecommendationEngine.Generate(profile, health);
        var pricingEstimate = _pricingEngine.Estimate(profile, health);
        var parts = new List<string>
        {
            PromptTemplates.GetSystemPrompt(promptMode),
            $"User question: {CopilotRedactor.Redact(request.Prompt, settings.RedactContextEnabled)}",
            $"App version: {CopilotRedactor.Redact(request.AppVersion, settings.RedactContextEnabled)}"
        };

        if (settings.KyraPersistentMemoryEnabled &&
            !string.IsNullOrWhiteSpace(request.KyraMemorySummaryForPrompt))
        {
            parts.Add(KyraSystemContextSanitizer.SanitizeForExternalProviders(request.KyraMemorySummaryForPrompt.Trim()));
        }

        if (settings.UseLatestSystemScanContext)
        {
            parts.Add(BuildSystemSummary(request.SystemIntelligenceReportPath, profile, health, recommendations, pricingEstimate, settings.RedactContextEnabled));
            if (profile is not null)
            {
                var insight = KyraSystemAnalyzer.Analyze(profile, health, recommendations, pricingEstimate);
                parts.Add(CopilotRedactor.Redact(insight.ToPromptBlock(), settings.RedactContextEnabled));
            }
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
            Intent = intent,
            SystemContext = systemContext,
            SystemProfile = profile,
            HealthEvaluation = health,
            Recommendations = recommendations,
            PricingEstimate = pricingEstimate
        };
    }

    private static CopilotPromptMode DetectPromptMode(string prompt, KyraIntent intent)
    {
        var text = prompt.ToLowerInvariant();
        if (intent == KyraIntent.CodeAssist)
        {
            return CopilotPromptMode.Technician;
        }

        if (intent is KyraIntent.LiveOnlineQuestion or KyraIntent.Weather or KyraIntent.News or KyraIntent.CryptoPrice
            or KyraIntent.StockPrice or KyraIntent.Sports)
        {
            return CopilotPromptMode.CurrentLiveData;
        }

        if (intent == KyraIntent.ResaleValue)
        {
            return CopilotPromptMode.FlipResale;
        }

        if (intent == KyraIntent.UpgradeAdvice)
        {
            return CopilotPromptMode.Technician;
        }

        if (text.Contains("usb") || text.Contains("toolkit") || text.Contains("iso") || text.Contains("ventoy"))
        {
            return CopilotPromptMode.ToolkitBuilder;
        }

        if (text.Contains("repair") || text.Contains("fix") || text.Contains("diagnose") || text.Contains("step"))
        {
            return CopilotPromptMode.Technician;
        }

        if (intent is KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot or KyraIntent.OSRecommendation ||
            text.Contains("not showing") ||
            text.Contains("missing") ||
            text.Contains("os"))
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
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "UNKNOWN";
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

        var redacted = Regex.Replace(value, @"(?i)(api[_-]?key|token|secret|password)\s*[:=]\s*['""]?[^'""\s;]+", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(bearer)\s+[A-Za-z0-9._-]{12,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\b(ghp|gho|github_pat)_[A-Za-z0-9_]{20,}\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"(?i)\bsk-[A-Za-z0-9_-]{12,}\b", "[REDACTED_API_KEY]");
        redacted = Regex.Replace(redacted, @"(?i)\bxox[baprs]-[A-Za-z0-9-]+\b", "[REDACTED_TOKEN]");
        redacted = Regex.Replace(redacted, @"[A-Za-z]:\\Users\\([^\\\s]+)", @"[REDACTED_PRIVATE_PATH]");
        redacted = Regex.Replace(redacted, @"[A-Za-z]:\\[^\r\n\t ]+", "[REDACTED_PRIVATE_PATH]");
        redacted = Regex.Replace(redacted, @"(?i)\b(service tag|serial|s/n)\s*[:#]?\s*[A-Z0-9-]{5,}\b", "[REDACTED_SERIAL]");
        redacted = Regex.Replace(redacted, @"(?i)\b(bitlocker|recovery)\s*key\s*[:=]?\s*[^\s\r\n]{8,}", "[REDACTED_RECOVERY_KEY]");
        redacted = Regex.Replace(redacted, @"(?i)\b(windows|product)\s*key\s*[:=]?\s*[A-Z0-9-]{10,}", "[REDACTED_LICENSE_KEY]");
        redacted = Regex.Replace(redacted, @"\b(10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(1[6-9]|2\d|3[0-1])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b", "[private ip redacted]");
        redacted = Regex.Replace(redacted, @"(?i)\b(username|user|owner)\s*[:=]\s*[^;\r\n\t ]+", "[REDACTED_USERNAME]");
        return redacted;
    }
}

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
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, SaveJsonOptions));
    }

    private void ApplyDefaults(CopilotSettings settings)
    {
        settings.TimeoutSeconds = settings.TimeoutSeconds <= 0 ? 12 : settings.TimeoutSeconds;
        settings.MaxContextCharacters = settings.MaxContextCharacters <= 0 ? 6000 : settings.MaxContextCharacters;
        settings.ModelName = string.IsNullOrWhiteSpace(settings.ModelName) ? "local-rules" : settings.ModelName;
        settings.MaxProviderFallbacksPerMessage = settings.MaxProviderFallbacksPerMessage <= 0 ? 4 : settings.MaxProviderFallbacksPerMessage;
        settings.FreeApiDailyRequestCap = settings.FreeApiDailyRequestCap <= 0 ? 120 : settings.FreeApiDailyRequestCap;
        settings.MaxInputCharactersOnline = settings.MaxInputCharactersOnline <= 0 ? 4000 : settings.MaxInputCharactersOnline;
        settings.MaxOutputTokensOnline = settings.MaxOutputTokensOnline <= 0 ? 700 : settings.MaxOutputTokensOnline;
        settings.ConsecutiveFailureThreshold = settings.ConsecutiveFailureThreshold <= 0 ? 4 : settings.ConsecutiveFailureThreshold;

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
            providerConfig.DailyRequestCap = providerConfig.DailyRequestCap <= 0 ? (provider.IsOnlineProvider ? 60 : int.MaxValue) : providerConfig.DailyRequestCap;
            providerConfig.MaxInputCharacters = providerConfig.MaxInputCharacters <= 0 ? settings.MaxInputCharactersOnline : providerConfig.MaxInputCharacters;
            providerConfig.MaxOutputTokens = providerConfig.MaxOutputTokens <= 0 ? settings.MaxOutputTokensOnline : providerConfig.MaxOutputTokens;
        }

        settings.LiveTools ??= new KyraLiveToolsSettings();
        if (settings.LiveTools.CacheMinutes <= 0)
        {
            settings.LiveTools.CacheMinutes = 10;
        }
    }
}

public static class PromptTemplates
{
    public static string GetSystemPrompt(CopilotPromptMode mode)
    {
        const string shared =
            "You are Kyra, ForgerEMS’s friendly technician copilot—cute and upbeat in small doses, loving and supportive, but always practical. " +
            "You’re techy without being condescending: short personality, light humor, no walls of fluff. Be confident; when you’re unsure, say so honestly. " +
            "For troubleshooting and system questions, prefer this shape: **Short answer** → **What I noticed** → **Most likely cause** → **What to do next** → **Risk/caution** (if any). " +
            "Never invent live market/weather/news/sports data—if real-time APIs aren’t configured, say the feature needs setup and stick to safe general guidance. " +
            "Use Kyra device insight + System Intelligence naturally in plain language; don’t paste huge raw diagnostics unless the user asks. " +
            "For resale or pricing, stress estimates are informational, not guarantees; say when live marketplace comparison is not configured. " +
            "Do not ask for or repeat API keys, passwords, serials, recovery keys, or private paths. " +
            "Refuse malware, credential theft, bypassing security on devices the user doesn’t own, or illegal use—then offer legitimate repair paths. " +
            "Prefer short numbered steps for repairs. Ask at most one useful follow-up when it helps.";
        return mode switch
        {
            CopilotPromptMode.Troubleshooting => shared + " Troubleshooting mode: isolate likely causes for slow PCs, USB visibility, missing downloads, and OS choices.",
            CopilotPromptMode.FlipResale => shared + " Flip/resale mode: estimates are rough; call out upgrade and prep steps before listing.",
            CopilotPromptMode.Technician => shared + " Technician mode: safe repair guidance; avoid destructive commands unless the user clearly confirms and owns the machine.",
            CopilotPromptMode.ToolkitBuilder => shared + " Toolkit Builder mode: Ventoy USB repair sticks, licensing limits, manual downloads, and recovery/diagnostics constraints.",
            CopilotPromptMode.CurrentLiveData => shared + " Live data mode: cite that answers need configured APIs; never fake timestamps or sources.",
            _ => shared
        };
    }
}

public sealed class LocalOfflineCopilotProvider : ICopilotProvider
{
    public string Id => "local-offline";
    public string DisplayName => "Local Offline Rules";
    public CopilotProviderType ProviderType => CopilotProviderType.LocalOffline;
    public string Category => "Offline fallback";
    public bool IsOnlineProvider => false;
    public bool IsPaidProvider => true;
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
        var answer = LocalRulesCopilotEngine.GenerateReply(request.Prompt, request.Context);
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = true,
            UsedOnlineData = false,
            UserMessage = answer,
            DiagnosticMessage = "Local offline answer."
        });
    }
}

public sealed class OpenAiStyleCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();
    private readonly string _id;
    private readonly string _displayName;
    private readonly CopilotProviderType _providerType;
    private readonly string _category;
    private readonly bool _isPaidProvider;
    private readonly string _defaultBaseUrl;
    private readonly string _defaultModelName;
    private readonly string _defaultApiKeyEnvironmentVariable;
    private readonly string _statusText;

    public OpenAiStyleCopilotProvider(
        string id,
        string displayName,
        CopilotProviderType providerType,
        string category,
        bool isPaidProvider,
        string defaultBaseUrl,
        string defaultModelName,
        string defaultApiKeyEnvironmentVariable,
        string statusText)
    {
        _id = id;
        _displayName = displayName;
        _providerType = providerType;
        _category = category;
        _isPaidProvider = isPaidProvider;
        _defaultBaseUrl = defaultBaseUrl;
        _defaultModelName = defaultModelName;
        _defaultApiKeyEnvironmentVariable = defaultApiKeyEnvironmentVariable;
        _statusText = statusText;
    }

    public string Id => _id;
    public string DisplayName => _displayName;
    public CopilotProviderType ProviderType => _providerType;
    public string Category => _category;
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => _isPaidProvider;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => _defaultBaseUrl;
    public string DefaultModelName => _defaultModelName;
    public string DefaultApiKeyEnvironmentVariable => _defaultApiKeyEnvironmentVariable;
    public string StatusText => _statusText;

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        if (Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase) &&
            ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source == KyraCredentialSource.None)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(configuration.BaseUrl) &&
               !string.IsNullOrWhiteSpace(configuration.ModelName) &&
               (!string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration)));
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var apiKey = KyraApiKeyStore.ResolveApiKey(Id, request.ProviderConfiguration);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = $"{DisplayName} is not configured.",
                DiagnosticMessage = "API key environment variable is missing."
            };
        }

        if (Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase))
        {
            if (ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source == KyraCredentialSource.None)
            {
                return new CopilotProviderResult
                {
                    Succeeded = false,
                    FailureReason = KyraProviderFailureReason.NotConfigured,
                    UserMessage = $"{DisplayName} requires CLOUDFLARE_ACCOUNT_ID.",
                    DiagnosticMessage = "Missing CLOUDFLARE_ACCOUNT_ID."
                };
            }
        }

        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        var payload = new
        {
            model = request.ProviderConfiguration.ModelName,
            messages = new object[]
            {
                new { role = "system", content = PromptTemplates.GetSystemPrompt(request.Context.PromptMode) },
                new { role = "user", content = KyraPromptBuilder.BuildOnlinePrompt(request.Context, includeSystemContext: true) }
            },
            max_tokens = Math.Clamp(request.ProviderConfiguration.MaxOutputTokens, 128, 2048),
            temperature = 0.3
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
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
                FailureReason = ClassifyFailureReason(response.StatusCode, body),
                UserMessage = $"{DisplayName} returned an error. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        var text = ExtractChatCompletionText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.Unknown,
                UserMessage = $"{DisplayName} returned an empty response. Offline fallback is available."
            }
            : new CopilotProviderResult
            {
                Succeeded = true,
                UsedOnlineData = true,
                UserMessage = text,
                DiagnosticMessage = $"{DisplayName} response."
            };
    }

    private static KyraProviderFailureReason ClassifyFailureReason(System.Net.HttpStatusCode statusCode, string body)
    {
        return (int)statusCode switch
        {
            401 or 403 => KyraProviderFailureReason.AuthFailed,
            408 => KyraProviderFailureReason.Timeout,
            429 => KyraProviderFailureReason.RateLimited,
            >= 500 => KyraProviderFailureReason.ServiceUnavailable,
            _ when body.Contains("model", StringComparison.OrdinalIgnoreCase) && body.Contains("not", StringComparison.OrdinalIgnoreCase) => KyraProviderFailureReason.ModelUnavailable,
            _ => KyraProviderFailureReason.Unknown
        };
    }

    private static string ExtractChatCompletionText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
        }

        return string.Empty;
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
    public bool IsPaidProvider => true;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "https://api.openai.com/v1";
    public string DefaultModelName => "gpt-4.1-mini";
    public string DefaultApiKeyEnvironmentVariable => "OPENAI_API_KEY";
    public string StatusText => "Configurable OpenAI-compatible provider. API key is read from environment variable only.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(configuration.BaseUrl) &&
               !string.IsNullOrWhiteSpace(configuration.ModelName) &&
               !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var apiKey = KyraApiKeyStore.ResolveApiKey(Id, request.ProviderConfiguration);
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
        return !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
    }

    public bool CanHandle(CopilotProviderRequest request) => false;

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

public sealed class GeminiCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();
    public string Id => "gemini-free";
    public string DisplayName => "Gemini (Free Tier)";
    public CopilotProviderType ProviderType => CopilotProviderType.GeminiApi;
    public string Category => "Free API pool";
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "https://generativelanguage.googleapis.com/v1beta";
    public string DefaultModelName => "gemini-1.5-flash";
    public string DefaultApiKeyEnvironmentVariable => "GEMINI_API_KEY";
    public string StatusText => "Google AI Studio/Gemini free-tier provider. Key is optional and BYOK.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
    }

    public bool CanHandle(CopilotProviderRequest request) => true;

    public async Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        var apiKey = KyraApiKeyStore.ResolveApiKey(Id, request.ProviderConfiguration);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                FailureReason = KyraProviderFailureReason.NotConfigured,
                UserMessage = "Gemini is not configured. Offline fallback is available."
            };
        }

        var model = string.IsNullOrWhiteSpace(request.ProviderConfiguration.ModelName) ? DefaultModelName : request.ProviderConfiguration.ModelName;
        var baseUrl = request.ProviderConfiguration.BaseUrl.TrimEnd('/');
        var prompt = KyraPromptBuilder.BuildOnlinePrompt(request.Context, includeSystemContext: true);
        var payload = new
        {
            contents = new object[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = $"{PromptTemplates.GetSystemPrompt(request.Context.PromptMode)}\n\n{prompt}" } }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(apiKey)}");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = (int)response.StatusCode is 408 or 429 or >= 500,
                FailureReason = (int)response.StatusCode switch
                {
                    401 or 403 => KyraProviderFailureReason.AuthFailed,
                    429 => KyraProviderFailureReason.RateLimited,
                    >= 500 => KyraProviderFailureReason.ServiceUnavailable,
                    _ => KyraProviderFailureReason.Unknown
                },
                UserMessage = "Gemini provider failed. Offline fallback is available.",
                DiagnosticMessage = $"HTTP {(int)response.StatusCode}"
            };
        }

        var text = ExtractGeminiText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult { Succeeded = false, FailureReason = KyraProviderFailureReason.Unknown, UserMessage = "Gemini returned no text." }
            : new CopilotProviderResult { Succeeded = true, UsedOnlineData = true, UserMessage = text };
    }

    private static string ExtractGeminiText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var candidate in candidates.EnumerateArray())
            {
                if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts))
                {
                    continue;
                }

                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text))
                    {
                        return text.GetString() ?? string.Empty;
                    }
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }
}

public sealed class LmStudioCopilotProvider : ICopilotProvider
{
    private static readonly HttpClient HttpClient = new();

    public string Id => "lm-studio-local";
    public string DisplayName => "LM Studio Local Model";
    public CopilotProviderType ProviderType => CopilotProviderType.LmStudioLocal;
    public string Category => "Offline/local AI";
    public bool IsOnlineProvider => false;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "http://localhost:1234/v1";
    public string DefaultModelName => "local-model";
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText => "Local LM Studio provider. Requires the local server running on localhost.";

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
            using var ping = await HttpClient.GetAsync($"{baseUrl}/models", cancellationToken).ConfigureAwait(false);
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
            messages = new object[]
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
            },
            temperature = 0.3
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        httpRequest.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var response = await HttpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = (int)response.StatusCode is 408 or 429 or >= 500,
                UserMessage = "LM Studio returned an error. Offline fallback is available.",
                DiagnosticMessage = $"LM Studio HTTP {(int)response.StatusCode}"
            };
        }

        var text = TryExtractChatCompletionText(body);
        return string.IsNullOrWhiteSpace(text)
            ? new CopilotProviderResult
            {
                Succeeded = false,
                UserMessage = "LM Studio returned an empty response. Offline fallback is available.",
                DiagnosticMessage = "Empty LM Studio response text."
            }
            : new CopilotProviderResult
            {
                Succeeded = true,
                UsedOnlineData = false,
                UserMessage = text,
                DiagnosticMessage = "LM Studio local response."
            };
    }

    private static CopilotProviderResult NotReachable()
    {
        return new CopilotProviderResult
        {
            Succeeded = false,
            IsTransientFailure = true,
            UserMessage = "LM Studio is not reachable at the configured endpoint. Offline fallback is available.",
            DiagnosticMessage = "LM Studio not reachable."
        };
    }

    private static string TryExtractChatCompletionText(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var choice in choices.EnumerateArray())
            {
                if (choice.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? string.Empty;
                }
            }
        }
        catch
        {
        }

        return string.Empty;
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
    public static string GenerateReply(string prompt, CopilotContext context)
    {
        var normalizedPrompt = prompt.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return "I can help with resale value, upgrades, lag, OS choice, USB toolkit picks, or warnings from the latest scan. Ask it like you would in a normal chat and I will keep it practical.";
        }

        if (KyraSafetyGuard.TryBuildRefusal(normalizedPrompt, out var refusal))
        {
            return refusal;
        }

        if (ContainsAny(normalizedPrompt, "what did you just say", "repeat that", "summarize that"))
        {
            return BuildMemoryRecallAnswer(context);
        }

        if (ContainsAny(normalizedPrompt, "explain that simpler", "simpler", "plain english"))
        {
            return BuildSimplerAnswer(context);
        }

        if (ContainsAny(normalizedPrompt, "give me the commands", "commands", "powershell"))
        {
            return BuildSafeCommandsAnswer(context);
        }

        return context.Intent switch
        {
            KyraIntent.PerformanceLag => BuildTroubleshootingAnswer(normalizedPrompt, context),
            KyraIntent.AppFreezing => BuildAppFreezingAnswer(normalizedPrompt, context),
            KyraIntent.SlowBoot => BuildSlowBootAnswer(context),
            KyraIntent.UpgradeAdvice => BuildUpgradeAnswer(context),
            KyraIntent.ResaleValue => BuildValueAnswer(normalizedPrompt, context),
            KyraIntent.USBBuilderHelp => BuildUsbBuilderAnswer(context),
            KyraIntent.ToolkitManagerHelp => BuildToolkitManagerAnswer(context),
            KyraIntent.SystemHealthSummary => BuildSystemAnswer(context),
            KyraIntent.DriverIssue => BuildDriverAnswer(context),
            KyraIntent.StorageIssue => BuildStorageAnswer(context),
            KyraIntent.MemoryIssue => BuildMemoryAnswer(context),
            KyraIntent.GPUQuestion => BuildGpuAnswer(context),
            KyraIntent.OSRecommendation => BuildOsAnswer(context),
            KyraIntent.ForgerEMSQuestion => BuildForgerEmsAnswer(context),
            KyraIntent.LiveOnlineQuestion => BuildLiveDataAnswer(),
            KyraIntent.Weather or KyraIntent.News or KyraIntent.CryptoPrice or KyraIntent.StockPrice or KyraIntent.Sports => BuildLiveDataAnswer(),
            KyraIntent.CodeAssist => BuildCodeAssistAnswer(normalizedPrompt, context),
            _ => context.PromptMode switch
            {
                CopilotPromptMode.CurrentLiveData => BuildLiveDataAnswer(),
                CopilotPromptMode.FlipResale => BuildValueAnswer(normalizedPrompt, context),
                CopilotPromptMode.ToolkitBuilder => BuildToolkitAnswer(context.ContextText),
                CopilotPromptMode.Technician => BuildTechnicianAnswer(context.ContextText),
                CopilotPromptMode.Troubleshooting => BuildTroubleshootingAnswer(normalizedPrompt, context),
                _ => BuildGeneralAnswer(context)
            }
        };
    }

    private static string BuildLiveDataAnswer()
    {
        return """
            Short answer:
            Kyra is running local/offline right now, so I can help from your system context but I can’t verify live web results.

            What I can do:
            I can still help with this PC, USB builds, diagnostics, resale prep, and OS recommendations using local data.

            What to do next:
            Configure an online provider later if you want exact live prices, newest versions/download links, weather/news, or real-time web research.
            """;
    }

    private static string BuildCodeAssistAnswer(string prompt, CopilotContext context)
    {
        var hint = KyraCodeSnippetDetector.GuessLanguageHint(prompt);
        return $"""
            Yep—I’ve got you on that snippet ({hint}). I’m not executing anything here; this is read-only guidance.

            What often goes wrong:
            - Unbalanced braces/parentheses/brackets, missing semicolons where the language requires them, bad string escaping (especially PowerShell paths), JSON trailing commas, YAML indentation, or XAML namespace typos.

            Fixed snippet:
            I need an online provider (or paste a smaller chunk) to safely rewrite the whole thing offline. If you enable Hybrid/Free API Pool, I can return a cleaned version and call out the exact lines.

            Why it broke:
            Usually one missing delimiter or an escaped character—your editor’s error list + formatter will narrow it fast.

            Caution:
            Don’t run destructive disk/registry scripts unless you own the machine and have backups.
            """;
    }

    private static string BuildMachineSpecificScanRequiredResponse(SystemContext systemContext)
    {
        return $"""
            Short answer:
            I need a System Intelligence scan before I can give machine-specific advice.

            What I can see without a scan:
            {DescribeSystemContext(systemContext)}

            What to do next:
            Run System Intelligence from this app, then ask again.
            """;
    }

    private static string BuildValueAnswer(string prompt, CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var health = context.HealthEvaluation?.HealthScore ?? 0;
        var probe = new WindowsHardwareReader().Read(profile);
        var resaleProfile = new DeviceResaleProfile
        {
            Identity = probe.Identity,
            RawSystemProfile = profile,
            Condition = new ResaleConditionProfile
            {
                CosmeticCondition = "Unknown (ask seller/operator)",
                ScreenCondition = "Unknown (ask seller/operator)",
                KeyboardTrackpadCondition = "Unknown (ask seller/operator)",
                HingeCondition = "Unknown (ask seller/operator)",
                ChargerIncluded = true,
                BatteryHoldsCharge = true,
                WindowsActivated = true,
                FreshInstallCompleted = false,
                CleanedOrRepasted = false,
                MissingScrewsOrDamage = false
            }
        };
        var estimator = new OfflineResaleEstimator();
        var listingEstimate = estimator.Estimate(resaleProfile);
        var listingDraft = estimator.GenerateListingDraft(resaleProfile, listingEstimate);
        var wantsListing = prompt.Contains("listing", StringComparison.OrdinalIgnoreCase) || prompt.Contains("make me", StringComparison.OrdinalIgnoreCase);
        var asksEbay = prompt.Contains("ebay", StringComparison.OrdinalIgnoreCase) || prompt.Contains("comps", StringComparison.OrdinalIgnoreCase);
        var asksOfferUp = prompt.Contains("offerup", StringComparison.OrdinalIgnoreCase);
        var asksFacebook = prompt.Contains("facebook", StringComparison.OrdinalIgnoreCase) || prompt.Contains("marketplace", StringComparison.OrdinalIgnoreCase);
        var salePosture = health < 55
            ? "repair-first or parts/repair until the scan issues are fixed"
            : "worth preparing for resale if condition/photos/charger/activation check out";
        var pricing = context.PricingEstimate;
        var conversationalEstimate = EstimateDeviceValue(context.SystemContext);
        if (pricing is not null)
        {
            var pricedAnswer = $"""
                Short answer:
                This looks like a {FormatResaleAction(pricing.RecommendedAction)} situation. Pricing Engine v0 says ${pricing.LowEstimate:0} - ${pricing.HighEstimate:0}, local estimate only.

                What I found:
                {conversationalEstimate}
                Health score: {health}/100.
                Confidence: {pricing.ConfidenceScore:0.##}.
                No marketplace comps, scraping, or API prices were used.

                What to do next:
                {FormatNumbered(context.Recommendations.Take(5), "Clean it, update it, verify drivers, and photograph the condition.")}

                Technical details:
                {JoinOrFallback(pricing.Assumptions.Take(5), "Local hardware facts only.")}
                Confidence detail: {listingEstimate.ConfidenceReason}
                {GetMarketplaceStatusLine(asksEbay, asksOfferUp, asksFacebook)}
                """;
            if (!wantsListing)
            {
                return pricedAnswer;
            }

            return pricedAnswer + Environment.NewLine + $"""
                
                Listing draft:
                Title: {listingDraft.Title}
                Short description: {listingDraft.ShortDescription}
                Recommended list: ${listingEstimate.FairListingPrice:0}; quick-sale: ${listingEstimate.QuickSalePrice:0}; min acceptable: ${listingEstimate.MinimumAcceptablePrice:0}.
                Photo checklist: {string.Join("; ", listingDraft.PhotoChecklist)}
                """;
        }

        var baseAnswer = $"""
            Short answer:
            This is {salePosture}. Local estimate only: {profile.FlipValue.EstimatedResaleRange}.

            What I found:
            {conversationalEstimate}
            Recommended list: {profile.FlipValue.RecommendedListPrice}.
            Quick-sale: {profile.FlipValue.QuickSalePrice}.
            Parts/repair floor: {profile.FlipValue.PartsRepairPrice}.
            Confidence: {FormatConfidence(profile.FlipValue.ConfidenceScore)}.

            What to do next:
            {FormatNumbered(context.Recommendations.Take(5), "Clean it, update it, verify drivers, and photograph condition.")}

            Technical details:
            Pricing provider status: {profile.FlipValue.ProviderStatus}.
            Value reducers: {JoinOrFallback(profile.FlipValue.ValueReducers, "nothing obvious from the local scan")}.
            """;
        if (!wantsListing)
        {
            return baseAnswer + Environment.NewLine + GetMarketplaceStatusLine(asksEbay, asksOfferUp, asksFacebook);
        }

        return baseAnswer + Environment.NewLine + $"""
            
            Listing draft:
            Title: {listingDraft.Title}
            Short description: {listingDraft.ShortDescription}
            Recommended list: ${listingEstimate.FairListingPrice:0}; quick-sale: ${listingEstimate.QuickSalePrice:0}; min acceptable: ${listingEstimate.MinimumAcceptablePrice:0}.
            Photo checklist: {string.Join("; ", listingDraft.PhotoChecklist)}
            """;
    }

    private static string GetMarketplaceStatusLine(bool asksEbay, bool asksOfferUp, bool asksFacebook)
    {
        if (asksOfferUp || asksFacebook)
        {
            return "Marketplace status: OfferUp/Facebook are manual/future sources only in this beta. I can estimate offline or use manual comparables.";
        }

        if (asksEbay)
        {
            return "eBay comps status: Active eBay comps can be used only when official API config is present. Sold comps are not configured in this beta.";
        }

        return "Marketplace status: Offline estimate only by default. Facebook/OfferUp are manual/future sources only in this beta.";
    }

    private static string BuildUpgradeAnswer(CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Short answer:
            I’d fix buyer-confidence issues first, then upgrade only where it changes the feel or resale value.

            What I found:
            Device: {profile.Manufacturer} {profile.Model}.
            Health score: {context.HealthEvaluation?.HealthScore ?? 0}/100.
            RAM: {profile.RamTotal}; upgrade path: {profile.RamUpgradePath}.
            Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.Name} {disk.MediaType}, health {disk.Health}"), "storage health unknown")}.
            Battery: {JoinOrFallback(profile.Batteries.Select(battery => $"wear {FormatNullable(battery.WearPercent, "%")}, cycles {FormatNullable(battery.CycleCount)}"), "no battery detected")}.

            What to do next:
            {FormatNumbered(context.Recommendations.Take(6), "No urgent hardware upgrade found locally. Clean it, update it, and verify drivers before listing.")}

            Technical details:
            If this is for resale, don’t overspend. Prioritize required-for-sale fixes first, low-cost confidence upgrades second, and optional upgrades last.
            """;
    }

    private static string BuildSystemAnswer(CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Short answer:
            This machine looks like a {profile.Manufacturer} {profile.Model} with a local health score of {context.HealthEvaluation?.HealthScore ?? 0}/100.

            What I found:
            CPU: {profile.Cpu}.
            RAM: {profile.RamTotal}.
            GPU: {JoinOrFallback(profile.Gpus.Select(gpu => gpu.Name), "GPU unknown")}.
            Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.MediaType} health {disk.Health}"), "storage health unknown")}.

            What to do next:
            {FormatNumbered(context.Recommendations.Take(5), "Review any warnings, update drivers, and rerun the scan after fixes.")}
            """;
    }

    private static string BuildAppFreezingAnswer(string prompt, CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var appName = ResolveAppName(prompt);
        return $"""
            Quick read:
            Yeah, app-specific lag usually points to GPU acceleration, app cache, network, or storage/RAM pressure before I blame the whole computer.

            What I'm seeing:
            {DescribeSystemContext(context.SystemContext)}
            {SummarizeHealth(context)}

            What to try first:
            1. Restart {appName}, then test the same video/app again.
            2. Update the app and GPU driver.
            3. Turn hardware acceleration off/on for the app or browser and retest.
            4. Check Task Manager while it lags: CPU, memory, disk, and GPU video decode.
            5. If it is streaming, test another network or browser to separate app lag from internet lag.

            Next step:
            If you tell me whether it only happens in {appName} or everywhere, I can narrow it down fast.
            """;
    }

    private static string BuildSlowBootAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            Slow boot is usually startup apps, Windows update cleanup, storage health, or a driver/service hanging during login.

            What I'm seeing:
            {DescribeSystemContext(context.SystemContext)}
            {SummarizeHealth(context)}

            What to try first:
            1. Open Task Manager > Startup apps and disable anything nonessential.
            2. Check storage health before chasing Windows tweaks.
            3. Run Windows Update once, reboot twice, then retest boot time.
            4. If boot is still slow, check Event Viewer > Diagnostics-Performance > Operational.
            """;
    }

    private static string BuildUsbBuilderAnswer(CopilotContext context)
    {
        return $"""
            Quick read:
            For USB Builder, the big thing is selecting the real data partition, not the tiny EFI/VTOYEFI partition.

            What I'm seeing:
            {FindLine(context.ContextText, "USB target:")}

            What to try first:
            1. Unplug and replug the USB, then wait a few seconds for Windows to mount it.
            2. Pick the largest safe removable data partition.
            3. Avoid C:, fixed/system disks, and the tiny VTOYEFI EFI partition.
            4. If the drive does not appear, check Disk Management for a missing drive letter or an uninitialized disk.
            5. If speeds are still pending, leave the drive inserted while Kyra/ForgerEMS retests in the background.

            Notes:
            If the USB has Ventoy, target the large Ventoy/data partition for toolkit content. The small EFI/VTOYEFI partition is boot metadata and not where your toolkit files belong.
            """;
    }

    private static string BuildToolkitManagerAnswer(CopilotContext context)
    {
        return $"""
            Quick read:
            Toolkit issues are usually missing downloads, failed verification, licensed/manual tools, or a path mismatch.

            What I'm seeing:
            {FindLine(context.ContextText, "Toolkit health")}

            What to try first:
            1. Check whether the item is marked installed, missing, failed, manual, or placeholder.
            2. Manual items usually mean ForgerEMS cannot legally auto-download them because of licensing, EULA, or gated distribution.
            3. Missing can mean a failed download, checksum mismatch, moved file, incomplete setup, or manifest mismatch.
            4. Retry safely with the USB selected, then re-run the toolkit health scan.
            5. Check logs for the failing stage: `%LOCALAPPDATA%\ForgerEMS\Runtime\logs` and `%LOCALAPPDATA%\ForgerEMS\Runtime\reports`.
            """;
    }

    private static string BuildDriverAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            Driver problems usually show up as lag, missing devices, bad GPU switching, weak Wi-Fi, or sleep/display weirdness.

            What I'm seeing:
            GPU: {context.SystemContext.GPU}.
            OS: {context.SystemContext.OS}.

            What to try first:
            1. Install chipset/platform drivers first.
            2. Then install GPU, network, audio, and storage drivers.
            3. Prefer the manufacturer support page for laptops and OEM workstations.
            4. If Hybrid/Online Kyra is configured later, I can help look up the exact support page without sending serial numbers.
            """;
    }

    private static string BuildStorageAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            Storage is one of the first things I check because a weak SSD/HDD can make a good machine feel awful.

            What I'm seeing:
            {context.SystemContext.Storage}

            What to try first:
            1. Confirm SMART/health status.
            2. Check free space.
            3. Watch Disk usage in Task Manager during lag.
            4. If health or wear is unknown, verify with a vendor tool before selling.
            """;
    }

    private static string BuildMemoryAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var ram = context.SystemContext.RAM > 0 ? $"{context.SystemContext.RAM} GB" : "unknown";
        return $"""
            Quick read:
            RAM matters most when apps pile up, browsers are heavy, or Windows starts paging to disk.

            What I'm seeing:
            Installed RAM: {ram}.

            What to try first:
            1. If this is under 16 GB, upgrade RAM before judging the whole laptop.
            2. If it is already 16 GB, check actual memory pressure in Task Manager.
            3. If it is 32 GB or more, RAM probably is not the first bottleneck unless a specific app is leaking memory.
            """;
    }

    private static string BuildGpuAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            The GPU matters for video playback, external displays, CAD/light gaming, and apps that need acceleration. For normal resale, it helps, but battery/storage/RAM condition still matters too.

            What I'm seeing:
            GPU: {context.SystemContext.GPU}.
            Dedicated GPU detected: {(context.SystemContext.HasDedicatedGpu ? "yes" : "not obvious from the local scan")}.

            What to try first:
            1. Make sure the GPU driver is installed cleanly.
            2. For app lag, test hardware acceleration on and off.
            3. For NVIDIA/AMD systems, check Windows Graphics settings and the vendor control panel.
            """;
    }

    private static string BuildOsAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var ram = context.SystemContext.RAM;
        var recommendation = ram is > 0 and < 8
            ? "Linux Mint XFCE or Xubuntu will probably feel better than Windows 11 on low RAM."
            : "Windows 11 Pro is usually best for resale/business use if TPM, Secure Boot, drivers, and activation are clean.";
        var device = context.SystemContext.Device.Trim();
        var deviceLead = string.IsNullOrWhiteSpace(device) || device.Equals("Unknown device", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"Based on this {device}: ";

        return $"""
            Quick read:
            {deviceLead}{recommendation}

            What I'm seeing:
            {DescribeSystemContext(context.SystemContext)}

            What I would choose:
            1. Resale/business: Windows 11 Pro if supported and activated.
            2. Older/low-spec daily use: Linux Mint XFCE or Xubuntu.
            3. Diagnostics/recovery: ForgerEMS USB toolkit with Windows and Linux rescue tools.

            One note:
            I would not sell an unsupported OS install as the primary setup.
            """;
    }

    private static string BuildForgerEmsAnswer(CopilotContext context)
    {
        return """
            Quick read:
            ForgerEMS is meant to help you prep, inspect, and build repair USBs without digging through a pile of scripts.

            What Kyra can help with:
            1. Explain System Intelligence warnings.
            2. Recommend repair/resale steps.
            3. Help pick USB toolkit actions.
            4. Explain why a USB target is safe, blocked, or suspicious.
            5. Turn scan results into practical next steps.
            """;
    }

    private static string BuildMemoryRecallAnswer(CopilotContext context)
    {
        var last = context.ConversationHistory.LastOrDefault(message => message.Role.Equals("Kyra", StringComparison.OrdinalIgnoreCase));
        if (last is null)
        {
            return "I don’t have a previous answer in this chat yet. Ask me what you want to diagnose and I’ll keep it practical.";
        }

        return $"""
            Quick read:
            Here’s the short recap: {last.Text}

            Shorter version:
            I’m trying to narrow the issue to the most likely cause instead of dumping every scan detail at you.
            """;
    }

    private static string BuildSimplerAnswer(CopilotContext context)
    {
        return $"""
            Quick read:
            In plain English: I’m looking for the part of the machine that makes everything else wait.

            What that usually means:
            If apps open slowly, check RAM, storage, startup apps, drivers, and heat first. Those are the common “this feels laggy” causes.

            What I’d do:
            Start with Task Manager while the problem is happening. If CPU, memory, disk, or GPU spikes hard, that tells us where to look next.
            """;
    }

    private static string BuildSafeCommandsAnswer(CopilotContext context)
    {
        return """
            Quick read:
            I can give safe read-only checks. I’m avoiding wipe/repair commands unless you explicitly ask for an owner-authorized repair plan.

            Safe PowerShell checks:
            1. Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber
            2. Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, Size
            3. Get-CimInstance Win32_PhysicalMemory | Select-Object Capacity, Speed, Manufacturer, PartNumber
            4. Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion

            Next step:
            Paste the output here if you want me to interpret it.
            """;
    }

    private static string ResolveAppName(string prompt)
    {
        if (prompt.Contains("prime", StringComparison.OrdinalIgnoreCase))
        {
            return "Prime Video";
        }

        if (prompt.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "Chrome";
        }

        if (prompt.Contains("edge", StringComparison.OrdinalIgnoreCase))
        {
            return "Edge";
        }

        return "that app";
    }

    private static string BuildToolkitAnswer(string contextText)
    {
        var usb = FindLine(contextText, "USB target:");
        var toolkit = FindLine(contextText, "Toolkit health");
        return $"I would use the largest safe USB data partition and ignore EFI/system partitions. {usb} {toolkit} For a solid technician kit, keep Ventoy plus Windows installer media, Linux Mint or Ubuntu, Rescuezilla or Clonezilla, MemTest, and storage tools where licensing allows it.";
    }

    private static string BuildTechnicianAnswer(string contextText)
    {
        var problems = FindLine(contextText, "Problems:");
        return $"Start with the safe checks first. {problems} I would do this: 1. Check power, storage health, RAM pressure, network state, and drivers. 2. Reproduce the issue once. 3. Back up customer data before repairs. 4. Do not format, wipe, reinstall, or run destructive commands unless the user clearly confirms it.";
    }

    private static string BuildTroubleshootingAnswer(string prompt, CopilotContext context)
    {
        if (prompt.Contains("slow", StringComparison.OrdinalIgnoreCase) || prompt.Contains("lag", StringComparison.OrdinalIgnoreCase))
        {
            var profile = context.SystemProfile;
            var facts = profile is null
                ? "I need a System Intelligence scan before I can give machine-specific advice."
                : $"Health score: {context.HealthEvaluation?.HealthScore ?? 0}/100. RAM: {profile.RamTotal}. Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.MediaType} health {disk.Health} status {disk.Status}"), "storage health unknown")}. Battery: {profile.BatteryStatus}.";
            var memoryHint = context.PreviousIntent is KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot
                ? "Since we were already looking at lag, I’ll keep this focused instead of repeating the whole scan."
                : "Yeah, that kind of lag usually lines up with a bottleneck during app launch.";
            return $"""
                Short answer:
                {memoryHint}

                What I found:
                {facts}
                Detected issues: {JoinOrFallback(context.HealthEvaluation?.DetectedIssues.Take(5) ?? Array.Empty<string>(), "no obvious blocking issue found locally")}.

                What to do next:
                {FormatNumbered(context.Recommendations.Take(5), "Check Task Manager, SMART health, Windows Update activity, thermals, and driver status.")}

                Technical details:
                I’m using the local System Intelligence scan only, so I’m not sending your device details anywhere.
                """;
        }

        if (prompt.Contains("usb", StringComparison.OrdinalIgnoreCase))
        {
            return "First check whether Windows mounted the main data partition, not the small VTOYEFI partition. " + FindLine(context.ContextText, "USB target:") + " Replug the USB, wait a few seconds for mount, confirm it in Disk Management, then use refresh only if auto-detect does not update.";
        }

        if (prompt.Contains("os", StringComparison.OrdinalIgnoreCase))
        {
            return "For resale or business use, I would usually choose Windows 11 Pro when the CPU, TPM, Secure Boot, RAM, and SSD are all solid. For older or lower-spec systems, Linux Mint XFCE, Xubuntu, or Ubuntu can feel much better. I would not sell an unsupported OS install as the main setup.";
        }

        return """
            Short answer:
            I can help, but I need the symptom or goal first.

            What I can do:
            Device diagnostics, lag troubleshooting, USB builder help, resale prep, OS recommendations, and explaining local warnings.

            What to do next:
            Ask something like “why is this slow?”, “what should I upgrade?”, or “build me a repair USB.”
            """;
    }

    private static string BuildGeneralAnswer(CopilotContext context)
    {
        var q = context.UserQuestion.Trim();
        var lower = q.ToLowerInvariant();
        if (context.Intent == KyraIntent.GeneralTechQuestion &&
            context.SystemProfile is not null &&
            KyraIntentRouter.PromptReferencesThisMachine(lower) &&
            (lower.Contains("upgrade", StringComparison.OrdinalIgnoreCase) || lower.Contains("laptop", StringComparison.OrdinalIgnoreCase) || lower.Contains("replace", StringComparison.OrdinalIgnoreCase)))
        {
            var device = context.SystemContext.Device.Trim();
            return $"""
                Short answer:
                Based on this {device}, I can compare upgrade paths, but I need how you use it (gaming, business, school) and a rough budget.

                What I already know from System Intelligence:
                {DescribeSystemContext(context.SystemContext)}

                What to do next:
                Reply with budget + priority (battery vs performance vs quiet/fan noise), and whether you want new or used.
                """;
        }

        return """
            Short answer:
            I can help like a technician, a resale advisor, or a USB toolkit helper.

            What I can’t do offline:
            Live weather, current web research, marketplace comps, or driver page lookups need an online provider.

            What to do next:
            Tell me what you want to fix, build, sell, or understand. If it’s about this PC, I’ll use the local scan without dumping raw logs at you.
            """;
    }

    private static string EstimateDeviceValue(SystemContext context)
    {
        var low = 120;
        var high = 220;

        if (context.RAM >= 32)
        {
            low += 130;
            high += 180;
        }
        else if (context.RAM >= 16)
        {
            low += 70;
            high += 110;
        }
        else if (context.RAM > 0 && context.RAM < 8)
        {
            low -= 40;
            high -= 60;
        }

        if (context.CPU.Contains("i7", StringComparison.OrdinalIgnoreCase) ||
            context.CPU.Contains("Ryzen 7", StringComparison.OrdinalIgnoreCase))
        {
            low += 90;
            high += 140;
        }
        else if (context.CPU.Contains("i5", StringComparison.OrdinalIgnoreCase) ||
                 context.CPU.Contains("Ryzen 5", StringComparison.OrdinalIgnoreCase))
        {
            low += 45;
            high += 80;
        }

        if (context.HasDedicatedGpu)
        {
            low += 80;
            high += 160;
        }

        low = Math.Max(60, low);
        high = Math.Max(low + 80, high);
        return $"Based on the local specs I can see, a rough offline range is around ${low:0}-${high:0}. Treat that as a starting point, not a real marketplace comp.";
    }

    private static string DescribeSystemContext(SystemContext context)
    {
        var ram = context.RAM > 0 ? $"{context.RAM} GB RAM" : "RAM unknown";
        return $"{context.Device}; {context.CPU}; {ram}; {context.GPU}; {context.Storage}; {context.OS}";
    }

    private static string SummarizeHealth(CopilotContext context)
    {
        if (context.HealthEvaluation is null)
        {
            return "I do not have a full health score loaded yet.";
        }

        var issues = JoinOrFallback(context.HealthEvaluation.DetectedIssues.Take(3), "no major issue called out by the local scan");
        return $"Health score: {context.HealthEvaluation.HealthScore}/100. Main signals: {issues}.";
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

    private static string FormatNumbered(IEnumerable<string> values, string fallback)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToArray();
        if (items.Length == 0)
        {
            return "1. " + fallback;
        }

        return string.Join(Environment.NewLine, items.Select((item, index) => $"{index + 1}. {item}"));
    }

    private static string FormatNullable(double? value, string suffix = "")
    {
        return value.HasValue ? $"{value.Value:0.#}{suffix}" : "UNKNOWN";
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "UNKNOWN";
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

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
