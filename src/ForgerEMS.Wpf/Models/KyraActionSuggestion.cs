namespace VentoyToolkitSetup.Wpf.Models;

public enum KyraActionSafetyLevel
{
    Safe,
    Caution,
    Destructive
}

/// <summary>Optional follow-ups Kyra may suggest; UI may render later; chat can format as text.</summary>
public sealed class KyraActionSuggestion
{
    public string Title { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public KyraActionSafetyLevel SafetyLevel { get; init; } = KyraActionSafetyLevel.Safe;

    public string? SuggestedPrompt { get; init; }

    public string? CommandId { get; init; }

    public bool RequiresConfirmation { get; init; }

    public bool RequiresAdmin { get; init; }

    public string? RelatedTab { get; init; }
}
