namespace VentoyToolkitSetup.Wpf.Models;

public sealed class CopilotProviderSettingView
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsConfigured { get; init; }

    public bool IsPaidProvider { get; init; }

    public string DetailText =>
        $"{Category} | {(IsConfigured ? "Configured" : "Not configured")} | {(IsPaidProvider ? "Paid/future" : "Free/low-cost hook")} | {Status}";
}
