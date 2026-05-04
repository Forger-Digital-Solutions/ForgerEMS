using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Routing outcome for Kyra orchestration (diagnostics / tests).</summary>
public sealed class KyraProviderDecision
{
    public bool ApiFirst { get; init; }

    public KyraToolCallPlan ToolPlan { get; init; } = new();

    public IReadOnlyList<ICopilotProvider> OrderedProviders { get; init; } = Array.Empty<ICopilotProvider>();

    public KyraProviderCapabilities EffectiveCapabilities { get; init; }

    public static KyraProviderDecision Build(
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        IReadOnlyList<ICopilotProvider> providers,
        Func<ICopilotProvider, CopilotProviderConfiguration> configResolver,
        KyraConversationState memoryState,
        KyraToolRegistry toolRegistry,
        KyraToolHostFacts hostFacts)
    {
        var (plan, ordered) = KyraOrchestrator.BuildExecutionPlan(
            request,
            settings,
            context,
            providers,
            configResolver,
            memoryState,
            toolRegistry,
            hostFacts);

        var apiFirst = settings.ApiFirstRouting &&
                       !plan.ShouldPolishWithProvider &&
                       !KyraMachineContextRouter.IsMachineAnchoredIntent(context.Intent, request.Prompt);

        return new KyraProviderDecision
        {
            ToolPlan = plan,
            OrderedProviders = ordered,
            ApiFirst = apiFirst,
            EffectiveCapabilities = KyraProviderCapabilityCatalog.AggregateForProviders(ordered)
        };
    }
}
