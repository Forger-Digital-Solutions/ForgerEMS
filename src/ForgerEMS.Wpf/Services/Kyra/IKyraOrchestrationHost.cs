using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Host-side execution hooks for <see cref="KyraOrchestrator"/> (provider I/O stays in CopilotService).</summary>
public interface IKyraOrchestrationHost
{
    KyraConversationMemory Memory { get; }

    void SetLastSystemContext(SystemContext context);

    CopilotProviderConfiguration ResolveProviderConfig(CopilotSettings settings, ICopilotProvider provider);

    Task<CopilotProviderResult> RunProviderSafeAsync(
        ICopilotProvider provider,
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        List<string> notes,
        CancellationToken cancellationToken);

    CopilotResponse BuildResponse(
        CopilotProviderResult result,
        ICopilotProvider provider,
        List<string> notes,
        string onlineStatus,
        bool onlineEnhancementApplied = false);

    CopilotResponse CompleteResponse(CopilotRequest request, CopilotContext context, CopilotResponse response);

    CopilotResponse ApplyLocalKyraSourceLabel(
        CopilotResponse response,
        KyraToolCallPlan plan,
        CopilotContext context,
        string prompt,
        CopilotSettings settings);

    CopilotContext AttachConversationMemory(CopilotContext context);

    CopilotContext AttachToolAugmentation(CopilotContext context, string? augmentation, CopilotSettings settings);

    bool TryGetResponseCache(string key, out string value);

    void StoreResponseCache(string key, string value);
}
