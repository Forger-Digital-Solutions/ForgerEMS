using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Compact, provider-safe snapshot of recent Kyra chat for routing and prompts.
/// </summary>
public sealed class KyraConversationContext
{
    public string CurrentUserMessage { get; init; } = string.Empty;

    public string LastKyraResponseExcerpt { get; init; } = string.Empty;

    public KyraIntent CurrentIntent { get; init; }

    public KyraIntent PreviousIntent { get; init; }

    public IReadOnlyList<CopilotChatMessage> RecentTurns { get; init; } = Array.Empty<CopilotChatMessage>();

    public bool LastKyraResponseListedIssues { get; init; }

    public static KyraConversationContext Capture(KyraConversationMemory memory, string currentUserMessage, KyraIntent resolvedIntent)
    {
        var snap = memory.Snapshot();
        KyraConversationTurn? lastTurn = snap.Count > 0 ? snap[^1] : null;
        return new KyraConversationContext
        {
            CurrentUserMessage = currentUserMessage,
            LastKyraResponseExcerpt = lastTurn?.KyraResponseSummary ?? string.Empty,
            CurrentIntent = resolvedIntent,
            PreviousIntent = memory.PreviousIntent,
            RecentTurns = memory.ToChatMessages(),
            LastKyraResponseListedIssues = lastTurn?.GaveDiagnosticBreakdown == true
        };
    }
}
