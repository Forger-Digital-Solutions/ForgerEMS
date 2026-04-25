namespace VentoyToolkitSetup.Wpf.Models;

public sealed class BundledBackendMetadata
{
    public int SchemaVersion { get; init; }

    public string FrontendVersion { get; init; } = string.Empty;

    public string BackendVersion { get; init; } = string.Empty;

    public string BundleSourceRoot { get; init; } = string.Empty;

    public string GeneratedUtc { get; init; } = string.Empty;
}
