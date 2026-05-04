using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Kyra-side response shaping (keeps CopilotService as a thin compatibility host).</summary>
public static class KyraCopilotResponseBuilder
{
    public static CopilotResponse Build(
        CopilotProviderResult result,
        ICopilotProvider provider,
        IReadOnlyList<string> notes,
        string status,
        bool onlineEnhancementApplied = false)
    {
        var source = MapSource(provider);
        var usedFallback = provider.ProviderType == CopilotProviderType.LocalOffline &&
                           notes.Any(note =>
                               note.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                               note.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                               note.Contains("rate", StringComparison.OrdinalIgnoreCase));
        var enhancement = onlineEnhancementApplied && !usedFallback && provider.IsOnlineProvider;
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
            SourceLabel = usedFallback
                ? KyraResponseComposer.KyraLocalModeLabel
                : KyraResponseComposer.BuildChatSourceLabel(provider, enhancement),
            FallbackUsed = usedFallback,
            OnlineEnhancementApplied = enhancement,
            ActionSuggestions = []
        };
    }

    public static CopilotResponse WithSourceLabel(CopilotResponse response, string sourceLabel) =>
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
            OnlineEnhancementApplied = response.OnlineEnhancementApplied,
            ActionSuggestions = response.ActionSuggestions
        };

    public static CopilotResponse ApplyLocalKyraSourceLabel(
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
                KyraStayLocalReason.MachineContextPrivacy => "Kyra · local mode (System Intelligence)",
                KyraStayLocalReason.DeviceToolkitRouting => "Kyra · local mode (device / USB / toolkit)",
                KyraStayLocalReason.LiveDataNotConfigured => "Kyra · local mode (live tools unavailable)",
                _ => response.SourceLabel
            };
            return WithSourceLabel(response, label);
        }

        if (response.ProviderType == CopilotProviderType.LocalOffline &&
            context.SystemProfile is not null &&
            KyraMachineContextRouter.IsMachineAnchoredIntent(context.Intent, prompt))
        {
            return WithSourceLabel(response, "Kyra · local mode (System Intelligence)");
        }

        return response;
    }

    private static KyraResponseSource MapSource(ICopilotProvider provider) =>
        provider.ProviderType switch
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
