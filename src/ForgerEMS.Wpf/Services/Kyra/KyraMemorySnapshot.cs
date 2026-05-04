using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Serializable Kyra memory (redacted fields only).</summary>
public sealed class KyraMemorySnapshot
{
    public List<KyraConversationTurnDto> Turns { get; set; } = [];

    public string RollingSummary { get; set; } = string.Empty;

    public string LastUserGoal { get; set; } = string.Empty;

    public string LastKyraAnswerExcerpt { get; set; } = string.Empty;

    public string LastIntent { get; set; } = nameof(KyraIntent.Unknown);

    public string LastUsbReference { get; set; } = string.Empty;

    public string LastRecommendedAction { get; set; } = string.Empty;

    public string LastKnownDeviceReference { get; set; } = string.Empty;

    public string LastKnownUsbReference { get; set; } = string.Empty;

    public string LastKnownToolkitIssue { get; set; } = string.Empty;

    public string LastKnownSystemIssue { get; set; } = string.Empty;

    public string LastRecommendationSummary { get; set; } = string.Empty;

    public string LastKyraAnswer { get; set; } = string.Empty;
}

public sealed class KyraConversationTurnDto
{
    public string UserMessage { get; set; } = string.Empty;

    public string KyraResponseSummary { get; set; } = string.Empty;

    public string Intent { get; set; } = nameof(KyraIntent.Unknown);

    public string SystemSnapshot { get; set; } = string.Empty;

    public string UnresolvedIssue { get; set; } = string.Empty;

    public string LastRecommendation { get; set; } = string.Empty;

    public bool GaveDiagnosticBreakdown { get; set; }

    public DateTimeOffset Timestamp { get; set; }
}
