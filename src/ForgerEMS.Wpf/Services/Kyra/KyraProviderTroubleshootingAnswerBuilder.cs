using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Local answers for “why did providers fail / why local fallback” (no secrets, no token values).</summary>
public static class KyraProviderTroubleshootingAnswerBuilder
{
    private const int MaxLen = 420;

    public static bool TryBuild(string? prompt, CopilotSettings settings, out CopilotResponse response)
    {
        response = new CopilotResponse();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaxLen)
        {
            return false;
        }

        var t = prompt.Trim().ToLowerInvariant();
        if (!LooksLikeProviderOrFallbackQuestion(t))
        {
            return false;
        }

        var gwOn = settings.KyraRealtimeGatewayEnabled && ForgerEmsEnvironmentConfiguration.KyraGatewayEnabled;
        var research = settings.KyraRealtimeGatewayResearchEnabled && settings.KyraRealtimeGatewayResearchConsent;
        var apiFirst = settings.ApiFirstRouting;
        var freePool = settings.EnableFreeProviderPool;
        var byok = settings.EnableByokProviders;

        var body = $"""
            Tool-status style readout (nothing sensitive here):

            • **Why Local Kyra / offline sometimes wins:** truth guard may discard an online answer that disagrees with your System Intelligence scan, or every configured provider failed, timed out, hit a rate limit, or is cooling down after errors.
            • **ForgerEMS Gateway:** beta path for **live research tools** (crypto, weather, news, etc.) on the worker — not a general “chat for every hello” endpoint. Casual chat uses your normal LLM providers (Groq, OpenRouter, …) when configured.
            • **Cooldowns:** after repeated failures, a provider is skipped briefly so Kyra doesn’t hammer the same broken route every message. Check Kyra Advanced → provider status; fix URL/keys; use **Check gateway status** for coarse worker readiness.
            • **Secrets:** I never echo API keys, gateway tokens, or passwords. Safe to share in support: app version, channel, and **masked** provider status — not raw tokens.

            Your flags (non-secret): gateway toggles effective≈{gwOn}, realtime research consent≈{research}, API-first≈{apiFirst}, free pool≈{freePool}, BYOK≈{byok}.

            **Best next move:** open Kyra Advanced, confirm at least one chat provider is configured, gateway URL + beta token if you want live tool research, then retry. If everything’s red, stay on Local Kyra — it’s intentional fallback, not a personal slight 😄
            """;

        response = new CopilotResponse
        {
            Text = body.Trim(),
            UsedOnlineData = false,
            OnlineStatus = "Kyra provider troubleshooting (local).",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes =
            [
                "Kyra routing: local provider / fallback troubleshooting helper",
                "No provider API keys or gateway tokens are included in this answer."
            ],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = KyraResponseComposer.KyraIdentityLabel,
            GroundedInSystemIntelligence = false,
            KyraTransparencySummary =
                "Route: local troubleshooting helper. Uses settings flags and your question only — no live provider call."
        };

        return true;
    }

    private static bool LooksLikeProviderOrFallbackQuestion(string t)
    {
        if (t.Contains("api key", StringComparison.Ordinal) && t.Contains("show", StringComparison.Ordinal))
        {
            return false;
        }

        return t.Contains("why did you fall back", StringComparison.Ordinal) ||
               t.Contains("why fall back", StringComparison.Ordinal) ||
               t.Contains("why did kyra fall back", StringComparison.Ordinal) ||
               (t.Contains("why") && t.Contains("local") && t.Contains("provider", StringComparison.Ordinal)) ||
               (t.Contains("why") && t.Contains("providers") && t.Contains("fail", StringComparison.Ordinal)) ||
               t.Contains("provider troubleshooting", StringComparison.Ordinal) ||
               t.Contains("tool status doctor", StringComparison.Ordinal) ||
               (t.Contains("gateway", StringComparison.Ordinal) && t.Contains("failing", StringComparison.Ordinal)) ||
               (t.Contains("openrouter", StringComparison.Ordinal) && t.Contains("not working", StringComparison.Ordinal));
    }
}
