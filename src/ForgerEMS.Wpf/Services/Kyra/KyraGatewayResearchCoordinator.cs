#pragma warning disable CA1305 // Locale-sensitive calls; text is diagnostic/UI output
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.IO;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public static class KyraGatewayResearchCoordinator
{
    private static readonly IKyraGatewayResearchClient DefaultClient = new KyraGatewayResearchClient();
    private const string CryptoUnavailableText =
        "I couldn’t load live BTC pricing right now. The crypto live tool may be unavailable or rate-limited. Try again in a minute or check provider settings.";

    public static async Task<CopilotResponse?> TryRealtimeResearchAsync(
        string userPrompt,
        CopilotSettings settings,
        string? systemIntelligenceReportPath,
        string? toolkitReportPath,
        string appVersion,
        IKyraGatewayResearchClient? client,
        CancellationToken cancellationToken)
    {
        if (KyraDestructiveRequestGuard.TryBuildSafeResponse(userPrompt, out var safety))
        {
            return safety;
        }

        if (!KyraRealtimeResearchClassifier.TryClassifyRealtimeNeed(userPrompt, out var intent))
        {
            return null;
        }

        if (!KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway(userPrompt, settings, out _))
        {
            return LiveUnavailableResponse(intent, "Realtime gateway research is disabled, not configured, or awaiting consent.", systemIntelligenceReportPath);
        }

        if (!ForgerEmsEnvironmentConfiguration.KyraGatewayEnabled)
        {
            return LiveUnavailableResponse(intent, "Gateway is disabled.", systemIntelligenceReportPath);
        }

        var cfg = KyraGatewayProviderConfig.FromEnvironment();
        if (!cfg.IsConfigured)
        {
            return LiveUnavailableResponse(intent, "Gateway is not configured.", systemIntelligenceReportPath);
        }

        var token = cfg.BetaToken;
        var endpoint = KyraGatewayResearchClient.BuildResearchEndpoint(cfg.GatewayUrl);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return LiveUnavailableResponse(intent, "Gateway URL is missing.", systemIntelligenceReportPath);
        }

        var sanitizedPrompt = KyraGatewayResearchSanitizer.SanitizePrompt(userPrompt);
        if (string.IsNullOrWhiteSpace(sanitizedPrompt))
        {
            return LiveUnavailableResponse(intent, "Prompt was empty after sanitization.", systemIntelligenceReportPath);
        }

        var body = new KyraGatewayResearchRequestDto
        {
            RequestId = Guid.NewGuid().ToString("N"),
            Intent = intent,
            Prompt = sanitizedPrompt,
            Context = KyraGatewayResearchContextBuilder.Build(
                settings,
                systemIntelligenceReportPath,
                toolkitReportPath,
                intent,
                sanitizedPrompt),
            Consent = new KyraGatewayResearchConsentDto
            {
                GatewayResearch = true,
                CommunitySharing = settings.KyraCommunitySharingEnabled
            }
        };

        client ??= DefaultClient;
        KyraGatewayResearchResponseDto resp;
        try
        {
            resp = await client.SendResearchAsync(
                    endpoint,
                    token,
                    body,
                    appVersion,
                    ForgerEmsEnvironmentConfiguration.ReleaseChannel,
                    cfg.TimeoutSeconds,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LiveUnavailableResponse(intent, "Realtime gateway timed out.", systemIntelligenceReportPath);
        }
        catch (HttpRequestException)
        {
            return LiveUnavailableResponse(intent, "Realtime gateway network error.", systemIntelligenceReportPath);
        }

        if (resp.Ok && !string.IsNullOrWhiteSpace(resp.Answer))
        {
            var stale = ContainsStaleKnowledgeWording(resp.Answer);
            if (intent is "crypto" or "finance" && stale)
            {
                return LiveUnavailableResponse(intent,
                    "Live market data could not be verified. Try again or check gateway tool configuration.",
                    systemIntelligenceReportPath);
            }

            var sourceSummary = KyraResearchResultFormatter.FormatEvidenceSummary(resp.Results);
            var providerNotes = new List<string>
            {
                "Kyra routing: realtime gateway research",
                "Kyra gateway: sanitized machine context only (no raw scan dump)",
                $"gatewayIntent={intent}",
                $"gatewayTool={resp.Tool ?? "none"}",
                $"gatewayProvider={resp.Provider ?? "unknown"}"
            };
            if (!string.IsNullOrWhiteSpace(sourceSummary))
            {
                providerNotes.Add("gatewaySources=" + sourceSummary.Replace(Environment.NewLine, " | "));
            }

            return new CopilotResponse
            {
                Text = resp.Answer.Trim(),
                UsedOnlineData = true,
                OnlineStatus = "Kyra realtime gateway",
                ProviderType = CopilotProviderType.ForgerEmsGateway,
                ProviderNotes = providerNotes,
                ResponseSource = KyraResponseSource.ForgerEmsGateway,
                SourceLabel = "Live research",
                OnlineEnhancementApplied = true,
                GroundedInSystemIntelligence = false,
                ActionSuggestions = [],
                KyraTransparencySummary = intent == "hardware_part_lookup"
                    ? "Live part research via gateway; context uses sanitized manufacturer/model family and capability bands only (no service tag/serial upload)."
                    : null
            };
        }

        var msg = !string.IsNullOrWhiteSpace(resp.SafeMessage)
            ? resp.SafeMessage.Trim()
            : "I couldn’t load live data from the realtime gateway. Local Kyra is still available.";
        if (intent == "crypto")
        {
            msg = CryptoUnavailableText;
        }

        return new CopilotResponse
        {
            Text = msg,
            UsedOnlineData = false,
            OnlineStatus = "Realtime gateway unavailable",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes =
            [
                "Kyra routing: realtime gateway research failed",
                $"gatewayIntent={intent}",
                $"errorCode={resp.ErrorCode ?? "unknown"}"
            ],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = "Live research unavailable",
            ActionSuggestions = []
        };
    }

    private static CopilotResponse LiveUnavailableResponse(string intent, string note, string? systemIntelligenceReportPath = null)
    {
        var hardwarePartLookup = string.Equals(intent, "hardware_part_lookup", StringComparison.OrdinalIgnoreCase);
        var text = intent == "crypto"
            ? CryptoUnavailableText
            : hardwarePartLookup
                ? BuildHardwarePartLookupUnavailableText(systemIntelligenceReportPath)
                : "I couldn’t load verified live data from the ForgerEMS realtime gateway right now. " +
                  "The live research path may be unavailable, rate-limited, or not configured on the gateway. " +
                  "Try again in a minute, or use local Kyra / Kyra Advanced live tools if your operator enabled them.";

        return new CopilotResponse
        {
            Text = text,
            UsedOnlineData = false,
            OnlineStatus = "Realtime gateway unavailable",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes =
            [
                "Kyra routing: realtime gateway unavailable",
                $"gatewayIntent={intent}",
                note
            ],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = "Live research unavailable",
            KyraTransparencySummary = hardwarePartLookup
                ? "Live part research was required but unavailable. Kyra used only sanitized local scan facts and did not verify exact SKUs, prices, or compatibility."
                : null,
            ActionSuggestions = []
        };
    }

    private static string BuildHardwarePartLookupUnavailableText(string? systemIntelligenceReportPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("I can’t verify the exact part from live sources right now. Based on your scan, these are the battery specs I can confirm, and here is what to check on the battery label/service manual.");
        sb.AppendLine();

        var profile = TryLoadProfile(systemIntelligenceReportPath);
        if (profile is not null)
        {
            sb.AppendLine("Confirmed from local scan:");
            sb.AppendLine($"- Machine: {SafeLabel($"{profile.Manufacturer} {profile.Model}".Trim(), "unknown model")}");
            if (profile.Batteries.Count > 0)
            {
                var battery = profile.Batteries[0];
                sb.AppendLine($"- Battery reported name: {SafeLabel(battery.Name, "not exposed")}");
                if (!string.IsNullOrWhiteSpace(battery.DesignCapacityDisplay))
                {
                    sb.AppendLine($"- Design capacity: {battery.DesignCapacityDisplay}");
                }

                if (!string.IsNullOrWhiteSpace(battery.FullChargeCapacityDisplay))
                {
                    sb.AppendLine($"- Full-charge capacity: {battery.FullChargeCapacityDisplay}");
                }

                sb.AppendLine(battery.WearPercent is { } wear
                    ? $"- Wear estimate: {wear:0.#}%"
                    : "- Wear estimate: not exposed");
                if (battery.CycleCount is { } cycles)
                {
                    sb.AppendLine($"- Cycle count: {cycles}");
                }
            }
            else
            {
                sb.AppendLine("- Battery details: not exposed in this scan");
            }
        }
        else
        {
            sb.AppendLine("Confirmed from local scan:");
            sb.AppendLine("- No current System Intelligence scan was available to read battery facts.");
        }

        sb.AppendLine();
        sb.AppendLine("Not verified externally:");
        sb.AppendLine("- Exact OEM part number / SKU");
        sb.AppendLine("- Current price, seller stock, or marketplace compatibility claims");
        sb.AppendLine("- Official Dell Precision 5540 battery compatibility");
        sb.AppendLine();
        sb.AppendLine("Verify before buying:");
        sb.AppendLine("- Match voltage, watt-hour capacity, connector, screw tabs, and physical shape.");
        sb.AppendLine("- Prefer Dell support/service manual/parts pages first, then use reputable sellers only as availability references.");
        sb.AppendLine("- Compare against the physical battery label or Dell service manual; do not rely only on marketplace titles.");

        return sb.ToString().Trim();
    }

    private static SystemProfile? TryLoadProfile(string? systemIntelligenceReportPath)
    {
        if (string.IsNullOrWhiteSpace(systemIntelligenceReportPath) || !File.Exists(systemIntelligenceReportPath))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(systemIntelligenceReportPath));
            return SystemProfileMapper.FromJson(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string SafeLabel(string? value, string fallback)
    {
        var v = KyraGatewayResearchSanitizer.SanitizeOptionalLabel(value ?? string.Empty, 120);
        return string.IsNullOrWhiteSpace(v) ? fallback : v;
    }

    internal static bool ContainsStaleKnowledgeWording(string text)
    {
        var l = text.ToLowerInvariant();
        return l.Contains("knowledge cutoff", StringComparison.Ordinal) ||
               l.Contains("as of my last update", StringComparison.Ordinal) ||
               l.Contains("i don't have real-time", StringComparison.Ordinal) ||
               l.Contains("i do not have real-time", StringComparison.Ordinal) ||
               l.Contains("i don't have access to real-time", StringComparison.Ordinal);
    }
}
