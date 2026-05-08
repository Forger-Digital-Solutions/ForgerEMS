using System;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class MachineProfileSnapshot
{
    public string MachineIdentityHash { get; set; } = string.Empty;

    public string FriendlyMachineLabel { get; set; } = "Unknown machine";

    public DateTimeOffset LastScanUtc { get; set; }

    public int HealthScore { get; set; }

    public int? ToolkitReadinessScore { get; set; }

    public string ToolkitReadinessLabel { get; set; } = "Unknown / Limited Data";

    public string BestUse { get; set; } = "Unknown";

    public string FlipValueBand { get; set; } = "Unknown";

    public string MachineClass { get; set; } = "Unknown";

    public string UsbBenchmarkSummary { get; set; } = "Not available";

    public string ReportPath { get; set; } = string.Empty;

    public string NotesPlaceholder { get; set; } = "Notes placeholder (UI pending).";
}
