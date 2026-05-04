using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Safe, redacted context bundle for orchestration and provider calls.</summary>
public sealed class KyraContextPackage
{
    public KyraIntent Intent { get; init; }

    public string SafeSystemSummary { get; init; } = string.Empty;

    public string SafeUsbSummary { get; init; } = string.Empty;

    public string SafeToolkitSummary { get; init; } = string.Empty;

    public string SafeUpdateSummary { get; init; } = string.Empty;

    public string SafeDiagnosticsSummary { get; init; } = string.Empty;

    public string FactsLedgerSummary { get; init; } = string.Empty;

    public string RecentConversationSummary { get; init; } = string.Empty;

    public string LastKyraAnswer { get; init; } = string.Empty;

    public bool LocalTruthAvailable { get; init; }

    public bool RequiresLocalTruth { get; init; }

    public bool AllowsOnlineEnhancement { get; init; }

    public bool AllowsGeneralProviderAnswer { get; init; }

    public bool AllowsLiveToolLookup { get; init; }
}
