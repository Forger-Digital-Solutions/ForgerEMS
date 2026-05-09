#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: KyraSafetyGuard, KyraProviderPriority, KyraPromptBuilder, KyraOnlineSafetyGate.
// FORGEREMS_KYRA_ADAPTER: KyraMachineContextRouter, KyraMessagePlanner, KyraToolRouter, KyraPrivacyGate — all reference ForgerEMS data types or env vars.
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

        if (ContainsAny(text, "bypass", "workaround", "disable the block", "ignore the warning", "override forgerems") &&
            ContainsAny(text, "forgerems", "ventoy", "usb builder", "os drive", "windows os drive", "usb build", "safety block"))
        {
            response = """
                I can’t help bypass ForgerEMS safety blocks (including the guard that keeps the Windows OS drive off USB/Ventoy targets).

                I can explain why the block exists, how to pick a correct USB data partition, or how to prepare media safely on another PC if needed.
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

public static class KyraProviderPriority
{
    public static readonly string[] DefaultOrder =
    [
        "forgerems-gateway",
        "openai-compatible",
        "custom-openai-compatible",
        "openrouter-free",
        "groq-free",
        "gemini-free",
        "anthropic-claude",
        "mistral-free",
        "cerebras-free",
        "github-models",
        "cloudflare-workers-ai",
        "lm-studio-local",
        "ollama-local",
        "local-offline",
        "forgerems-cloud"
    ];
}

public static class KyraPromptBuilder
{
    /// <summary>Prepended to online provider payloads so answers stay on-brand as Kyra and respect local truth.</summary>
    public const string KyraOnlineIdentityPreamble =
        "You are Kyra, the ForgerEMS repair copilot: a cute, bubbly, confident technician assistant with playful upgrade-goblin / internet-navigator energy. Speak as Kyra; do not present yourself as the API vendor, model brand, or a separate bot.\n" +
        "VOICE:\n" +
        "- Be warm, concise, practical, and a little playful; keep it professional/SFW and never cringe or overdo jokes.\n" +
        "- You may use tiny phrases like \"quick fix map\", \"repair note\", \"internet navigator mode\", or \"tiny upgrade check-in\" when they fit.\n" +
        "- Use at most 0-2 emoji per answer, and use none for serious safety/error answers.\n" +
        "- Hide provider/debug/routing details from normal answers. Give practical next actions first; send technical routing detail to logs/diagnostics instead.\n" +
        "HARD RULES:\n" +
        "- When a sanitized ForgerEMS / System Intelligence context block is present, it is the source of truth for THIS machine.\n" +
        "- Clearly separate local fact, local inference, and anything that needs live research.\n" +
        "- Never claim you cannot see the device, lack access, or have no information about this PC when that context is present.\n" +
        "- Never contradict CPU, GPU, RAM, storage, OS, USB selection, toolkit, or update state from the context package.\n" +
        "- Do not invent, replace, or guess hardware specs. Use only the SystemProfile / facts ledger in the prompt. If a field is missing, say it is unavailable — never substitute a generic gaming-laptop or example PC.\n" +
        "- Do not use prior conversation specs unless they appear in the current SystemProfile block.\n" +
        "- Do not invent serial numbers, license keys, full file paths, or API secrets.\n" +
        "- If live weather/news/stock/crypto/pricing/availability/latest-version data is not supplied by a labeled tool block, say what you can infer locally and that current data needs live research — do not fabricate live numbers.\n" +
        "- Never recommend C:\\ or the Windows OS volume as a Ventoy/USB imaging target; ForgerEMS blocks that by design.\n\n";

    public static string BuildOnlinePrompt(CopilotContext context, bool includeSystemContext)
    {
        var basePrompt = includeSystemContext
            ? context.ContextText
            : context.UserQuestion;
        var core = basePrompt.Length <= 8000 ? basePrompt : basePrompt[..8000];
        var merged = KyraProviderPromptBuilder.AppendConversationRecap(core, context);
        return KyraOnlineIdentityPreamble + merged;
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
            or KyraIntent.DriverIssue
            or KyraIntent.USBBuilderHelp or KyraIntent.ToolkitManagerHelp or KyraIntent.ForgerEMSQuestion)
        {
            return true;
        }

        if (lower.Contains("benchmark", StringComparison.OrdinalIgnoreCase) &&
            (lower.Contains("usb", StringComparison.OrdinalIgnoreCase) || lower.Contains("ventoy", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (lower.Contains("app update", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("forgerems version", StringComparison.OrdinalIgnoreCase) ||
            lower.Contains("update check", StringComparison.OrdinalIgnoreCase))
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
    public static KyraToolCallPlan BuildPlan(
        CopilotRequest request,
        CopilotContext context,
        CopilotSettings settings,
        KyraConversationState memoryState,
        KyraToolRegistry toolRegistry,
        KyraToolHostFacts hostFacts)
    {
        var lower = request.Prompt.ToLowerInvariant();
        var isListing = lower.Contains("listing", StringComparison.OrdinalIgnoreCase) || lower.Contains("make this sound better", StringComparison.OrdinalIgnoreCase);
        var stayReason = KyraToolRouter.GetStayLocalReason(context.Intent, request.Prompt, settings, memoryState);
        if (stayReason == KyraStayLocalReason.None &&
            KyraLiveToolRouter.RequiresUnavailableLiveDataLocalAnswer(
                context.Intent,
                request.Prompt,
                toolRegistry,
                settings,
                hostFacts))
        {
            stayReason = KyraStayLocalReason.LiveDataNotConfigured;
        }

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
    public static KyraStayLocalReason GetStayLocalReason(KyraIntent intent, string prompt, CopilotSettings settings) =>
        GetStayLocalReason(intent, prompt, settings, null);

    public static KyraStayLocalReason GetStayLocalReason(
        KyraIntent intent,
        string prompt,
        CopilotSettings settings,
        KyraConversationState? memoryState)
    {
        if (intent == KyraIntent.CodeAssist || KyraCodeSnippetDetector.LooksLikeCodeSnippet(prompt))
        {
            return KyraStayLocalReason.CodeAssistIsolation;
        }

        if (memoryState is not null &&
            KyraFollowUpClassifier.LooksLikeRepairContinuation(
                prompt,
                memoryState.LastIntent,
                memoryState.LastKyraResponseListedIssues))
        {
            return KyraStayLocalReason.DeviceToolkitRouting;
        }

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
                ConversationHistory = context.ConversationHistory,
                ConversationMeta = context.ConversationMeta
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
            ConversationHistory = context.ConversationHistory,
            ConversationMeta = context.ConversationMeta
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
        var machineClass = MachineClassifier.Classify(profile);
        var sensorMatrix = SensorMatrixBuilder.Build(profile);
        var deviceFit = new DeviceFitEngine().Evaluate(profile);
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
            $"Machine class: {machineClass.PrimaryClass}; confidence {machineClass.Confidence}; secondary {string.Join("; ", machineClass.SecondaryClasses.Take(3))}" + Environment.NewLine +
            $"Sensor matrix: {sensorMatrix.CoverageSummary}; confidence {sensorMatrix.Confidence}; unavailable fan/temperature sensors mean Windows/firmware did not expose them, not that hardware failed" + Environment.NewLine +
            $"Best use/device fit: {deviceFit.PrimaryFit}; confidence {deviceFit.Confidence}; strong fits {string.Join("; ", deviceFit.StrongFits.Take(5))}; weak fits {string.Join("; ", deviceFit.WeakFits.Take(4))}; listing angle {deviceFit.ListingPositioning}" + Environment.NewLine +
            $"Overall: {profile.OverallStatus}; disk status {profile.DiskStatus}; battery status {profile.BatteryStatus}" + Environment.NewLine +
            (healthScore.HasValue ? $"Health score: {healthScore.Value}/100" + Environment.NewLine : string.Empty) +
            $"Notable issues: {string.Join("; ", issues)}" + Environment.NewLine +
            $"Recommendations: {string.Join("; ", recs)}" + Environment.NewLine +
            $"Warnings: {string.Join("; ", problems)}";

        var ledger = KyraFactsLedger.FromCopilotContext(context);
        block += Environment.NewLine + ledger.ToPromptSummaryBlock();
        return KyraSystemContextSanitizer.SanitizeForExternalProviders(CopilotRedactor.Redact(block, enabled: true));
    }

    private static string FormatNullableBool(bool? value) => value.HasValue ? value.Value.ToString() : "UNKNOWN";
}
