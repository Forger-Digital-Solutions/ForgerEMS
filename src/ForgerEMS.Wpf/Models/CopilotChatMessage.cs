using System;
using System.Windows;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class CopilotChatMessage
{
    public string Role { get; init; } = "Assistant";

    public string Text { get; init; } = string.Empty;

    public string SourceLabel { get; init; } = string.Empty;

    public bool OnlineEnhancementApplied { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string DisplayText => $"{Role}: {Text}";

    public Visibility CopyVisibility => Role.Equals("Kyra", StringComparison.OrdinalIgnoreCase)
        ? Visibility.Visible
        : Visibility.Collapsed;
}
