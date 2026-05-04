using System;
using System.Collections.Generic;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum DiagnosticSeverityLevel
{
    Ok,
    Warning,
    Blocked,
    Unknown
}

public sealed class UnifiedDiagnosticItem
{
    public string Source { get; init; } = string.Empty;

    public string Code { get; init; } = string.Empty;

    public DiagnosticSeverityLevel Severity { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Non-destructive suggestion only.</summary>
    public string? SuggestedFix { get; init; }
}

public sealed class UnifiedDiagnosticsReport
{
    public DateTimeOffset GeneratedUtc { get; init; }

    public DiagnosticSeverityLevel OverallSeverity { get; init; }

    public string SummaryLine { get; init; } = string.Empty;

    public IReadOnlyList<UnifiedDiagnosticItem> Items { get; init; } = Array.Empty<UnifiedDiagnosticItem>();

    /// <summary>USB section mirrored from usb-intelligence-latest.json (safe fields only).</summary>
    public UsbDiagnosticsEmbeddedSection? Usb { get; init; }
}
