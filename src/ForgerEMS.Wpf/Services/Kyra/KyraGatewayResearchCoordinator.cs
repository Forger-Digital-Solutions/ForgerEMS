using System.Net.Http;
using VentoyToolkitSetup.Wpf.Configuration;

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

        if (!KyraRealtimeResearchClassifier.ShouldUseRealtimeGateway(userPrompt, settings, out var intent))
        {
            return null;
        }

        if (!ForgerEmsEnvironmentConfiguration.KyraGatewayEnabled)
        {
            return null;
        }

        var cfg = KyraGatewayProviderConfig.FromEnvironment();
        if (!cfg.IsConfigured)
        {
            return LiveUnavailableResponse(intent, "Gateway is not configured.");
        }

        var token = cfg.BetaToken;
        var endpoint = KyraGatewayResearchClient.BuildResearchEndpoint(cfg.GatewayUrl);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return LiveUnavailableResponse(intent, "Gateway URL is missing.");
        }

        var sanitizedPrompt = KyraGatewayResearchSanitizer.SanitizePrompt(userPrompt);
        if (string.IsNullOrWhiteSpace(sanitizedPrompt))
        {
            return LiveUnavailableResponse(intent, "Prompt was empty after sanitization.");
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
            return LiveUnavailableResponse(intent, "Realtime gateway timed out.");
        }
        catch (HttpRequestException)
        {
            return LiveUnavailableResponse(intent, "Realtime gateway network error.");
        }

        if (resp.Ok && !string.IsNullOrWhiteSpace(resp.Answer))
        {
            var stale = ContainsStaleKnowledgeWording(resp.Answer);
            if (intent is "crypto" or "finance" && stale)
            {
                return LiveUnavailableResponse(intent,
                    "Live market data could not be verified. Try again or check gateway tool configuration.");
            }

            return new CopilotResponse
            {
                Text = resp.Answer.Trim(),
                UsedOnlineData = true,
                OnlineStatus = "Kyra realtime gateway",
                ProviderType = CopilotProviderType.ForgerEmsGateway,
                ProviderNotes =
                [
                    "Kyra routing: realtime gateway research",
                    "Kyra gateway: sanitized machine context only (no raw scan dump)",
                    $"gatewayIntent={intent}",
                    $"gatewayTool={resp.Tool ?? "none"}",
                    $"gatewayProvider={resp.Provider ?? "unknown"}"
                ],
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

    private static CopilotResponse LiveUnavailableResponse(string intent, string note) =>
        new()
        {
            Text = intent == "crypto"
                ? CryptoUnavailableText
                : "I couldn’t load verified live data from the ForgerEMS realtime gateway right now. " +
                  "The live research path may be unavailable, rate-limited, or not configured on the gateway. " +
                  "Try again in a minute, or use local Kyra / Kyra Advanced live tools if your operator enabled them.",
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
            ActionSuggestions = []
        };

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
