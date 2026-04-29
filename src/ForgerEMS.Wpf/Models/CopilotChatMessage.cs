using System;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class CopilotChatMessage
{
    public string Role { get; init; } = "Assistant";

    public string Text { get; init; } = string.Empty;

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string DisplayText => $"{Role}: {Text}";
}
