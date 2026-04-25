using System;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class ManagedDownloadSummary
{
    public bool IsAvailable { get; init; }

    public string SummaryPath { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public DateTimeOffset? LastUpdatedUtc { get; init; }

    public static ManagedDownloadSummary Missing(string message)
    {
        return new ManagedDownloadSummary
        {
            IsAvailable = false,
            Text = message
        };
    }
}
