using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public static class KyraContextBuilder
{
    public static KyraContextPackage BuildPackage(
        CopilotContext context,
        KyraFactsLedger ledger,
        KyraConversationMemory memory,
        KyraToolCallPlan plan,
        CopilotSettings settings)
    {
        var redact = settings.RedactContextEnabled;
        var sys = KyraRedactionService.RedactForProviders(context.ContextText, redact);
        var ledgerBlock = KyraRedactionService.RedactForProviders(ledger.ToPromptSummaryBlock(), redact);
        var lastKyra = memory.GetState().LastKyraSummary;
        var convo = string.Join(
            " ",
            memory.ToChatMessages().TakeLast(6).Select(m => $"{m.Role}:{KyraRedactionService.RedactForProviders(m.Text, redact)}"));

        var requiresLocalTruth = plan.ShouldUseLocalToolAnswer ||
                                 KyraMachineContextRouter.IsMachineAnchoredIntent(context.Intent, context.UserQuestion);

        return new KyraContextPackage
        {
            Intent = context.Intent,
            SafeSystemSummary = SummarizeSection(sys, "system"),
            SafeUsbSummary = SummarizeSection(ledger.UsbHeadline, "usb"),
            SafeToolkitSummary = SummarizeSection(ledger.ToolkitHeadline, "toolkit"),
            SafeUpdateSummary = SummarizeSection(context.UserQuestion, "app"),
            SafeDiagnosticsSummary = SummarizeSection(
                context.HealthEvaluation is null
                    ? string.Empty
                    : $"score={context.HealthEvaluation.HealthScore}; issues={string.Join("; ", context.HealthEvaluation.DetectedIssues.Take(5))}",
                "diag"),
            FactsLedgerSummary = ledgerBlock,
            RecentConversationSummary = SummarizeSection(convo, "convo"),
            LastKyraAnswer = KyraRedactionService.RedactForProviders(lastKyra, redact),
            LocalTruthAvailable = ledger.HasTrustedLocalHardwareFacts || context.SystemProfile is not null,
            RequiresLocalTruth = requiresLocalTruth,
            AllowsOnlineEnhancement = !plan.ShouldUseLocalToolAnswer && settings.Mode is not CopilotMode.OfflineOnly,
            AllowsGeneralProviderAnswer = !requiresLocalTruth,
            AllowsLiveToolLookup = false
        };
    }

    private static string SummarizeSection(string? raw, string label)
    {
        var t = KyraRedactionService.RedactForProviders(raw ?? string.Empty, enabled: true).Trim();
        if (t.Length <= 1_200)
        {
            return t;
        }

        return t[..1_200] + "… [" + label + " trimmed]";
    }
}
