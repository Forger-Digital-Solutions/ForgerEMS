using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Deterministic answers for privacy, memory, and data-flow questions (no LLM required).</summary>
public static class KyraPrivacyHelpAnswerBuilder
{
    private const int MaxLen = 360;

    public static bool TryBuild(string? prompt, CopilotSettings settings, out CopilotResponse response)
    {
        response = new CopilotResponse();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > MaxLen)
        {
            return false;
        }

        var t = prompt.Trim().ToLowerInvariant();

        if (!LooksLikePrivacyOrMemoryQuestion(t))
        {
            return false;
        }

        var gw = settings.KyraRealtimeGatewayEnabled && settings.KyraRealtimeGatewayResearchEnabled &&
                 settings.KyraRealtimeGatewayResearchConsent;
        var mem = settings.KyraLocalRepairMemoryEnabled;
        var share = settings.KyraCommunitySharingEnabled;
        var si = settings.KyraUseSanitizedSystemIntelligenceContext && settings.AllowOnlineSystemContextSharing;

        var body = $"""
            Privacy quick answers (local):

            • What can leave this PC: only what you enable. Realtime gateway research sends a sanitized prompt plus optional broad labels (machine class, health band, USB state) — never full logs, paths, serials, or raw scans unless you explicitly turn on broader sharing in settings.
            • Kyra machine memory: stores sanitized learning events on disk when local memory is on (confirmed fixes, scan summaries, etc.). It is not a full chat transcript.
            • Community intelligence: off by default. Opt-in boxes stay separate from gateway research.
            • Delete / reset: use Kyra Advanced → local memory delete/export. Chat history and machine memory are separate.
            • Gateway token: a shared beta gate for your worker — not your OpenAI/OpenRouter key. Provider keys stay only on the gateway as worker secrets.

            Your current toggles (non-secret): gateway research consent effective={gw}, local repair memory={mem}, community sharing={share}, sanitized SI context with online sharing={si}.
            """;

        response = new CopilotResponse
        {
            Text = body.Trim(),
            UsedOnlineData = false,
            OnlineStatus = "Kyra privacy helper (local).",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes =
            [
                "Kyra routing: local privacy / data-flow helper",
                "Open Kyra Advanced for export, delete, and sharing previews."
            ],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = "Privacy helper",
            GroundedInSystemIntelligence = false,
            ActionSuggestions =
            [
                new KyraActionSuggestion
                {
                    Title = "Open Kyra Advanced",
                    Description = "Memory, gateway, and sharing controls.",
                    Category = "Settings",
                    SafetyLevel = KyraActionSafetyLevel.Safe
                }
            ],
            KyraTransparencySummary =
                "Route: Local privacy helper. Context: your settings flags and this question only — no provider or gateway call."
        };

        return true;
    }

    private static bool LooksLikePrivacyOrMemoryQuestion(string t)
    {
        if (t.Contains("price of btc", StringComparison.Ordinal) ||
            t.Contains("bitcoin price", StringComparison.Ordinal))
        {
            return false;
        }

        return t.Contains("what did you send", StringComparison.Ordinal) ||
               (t.Contains("what data", StringComparison.Ordinal) && t.Contains("kyra", StringComparison.Ordinal)) ||
               t.Contains("what leaves", StringComparison.Ordinal) ||
               t.Contains("what is stored", StringComparison.Ordinal) ||
               t.Contains("what do you store", StringComparison.Ordinal) ||
               t.Contains("kyra memory", StringComparison.Ordinal) ||
               t.Contains("delete my kyra", StringComparison.Ordinal) ||
               t.Contains("reset kyra learning", StringComparison.Ordinal) ||
               t.Contains("clear kyra memory", StringComparison.Ordinal) ||
               (t.Contains("community sharing", StringComparison.Ordinal) && t.Contains("what", StringComparison.Ordinal)) ||
               t.Contains("what would be shared", StringComparison.Ordinal) ||
               t.Contains("privacy mode", StringComparison.Ordinal) ||
               (t.Contains("gateway token", StringComparison.Ordinal) && t.Contains("api", StringComparison.Ordinal)) ||
               t.Contains("is the gateway token", StringComparison.Ordinal);
    }
}
