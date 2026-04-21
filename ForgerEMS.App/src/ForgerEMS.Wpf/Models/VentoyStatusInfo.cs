namespace VentoyToolkitSetup.Wpf.Models;

public sealed class VentoyStatusInfo
{
    public bool PackageAvailable { get; init; }

    public bool HasTarget { get; init; }

    public bool IsInstalled { get; init; }

    public string InstalledVersion { get; init; } = string.Empty;

    public string StatusText { get; init; } = string.Empty;

    public string DetailText { get; init; } = string.Empty;

    public string PackageText { get; init; } = string.Empty;

    public string PackageVersion { get; init; } = string.Empty;

    public string OfficialDownloadUrl { get; init; } = string.Empty;

    public string ManualNotePath { get; init; } = string.Empty;
}

public sealed class VentoyLaunchResult
{
    public bool Succeeded { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;
}
