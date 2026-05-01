namespace VentoyToolkitSetup.Wpf.Models;

/// <summary>One row in Kyra Advanced → Tools grid (bindable, no secrets).</summary>
public sealed class KyraToolStatusRowView
{
    public string ToolName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string LastChecked { get; init; } = "—";

    public string Notes { get; init; } = string.Empty;
}
