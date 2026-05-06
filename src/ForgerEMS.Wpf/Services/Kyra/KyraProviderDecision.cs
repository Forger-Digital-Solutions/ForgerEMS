using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Routing outcome for Kyra orchestration (diagnostics / tests).</summary>
public sealed class KyraProviderDecision
{
    public bool ApiFirst { get; init; }

    public KyraToolCallPlan ToolPlan { get; init; } = new();

    public IReadOnlyList<ICopilotProvider> OrderedProviders { get; init; } = Array.Empty<ICopilotProvider>();

    public IReadOnlyList<string> SkippedProviders { get; init; } = Array.Empty<string>();

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

        var machineAnchored = KyraMachineContextRouter.IsMachineAnchoredIntent(context.Intent, request.Prompt);
        var apiFirst = settings.ApiFirstRouting &&
                       !plan.ShouldPolishWithProvider &&
                       (!machineAnchored || settings.AllowOnlineSystemContextSharing);

        return new KyraProviderDecision
        {
            ToolPlan = plan,
            OrderedProviders = ordered,
            SkippedProviders = KyraProviderRouter.ExplainSkippedProviders(providers, request, settings, context, configResolver),
            ApiFirst = apiFirst,
            EffectiveCapabilities = KyraProviderCapabilityCatalog.AggregateForProviders(ordered)
        };
    }
}
