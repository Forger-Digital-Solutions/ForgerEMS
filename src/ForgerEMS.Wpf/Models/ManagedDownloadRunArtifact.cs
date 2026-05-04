using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class ManagedDownloadFailedItemRecord
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("safeReason")]
    public string SafeReason { get; init; } = string.Empty;

    [JsonPropertyName("fallbackRelativePath")]
    public string FallbackRelativePath { get; init; } = string.Empty;

    [JsonPropertyName("retryable")]
    public bool Retryable { get; init; } = true;

    [JsonPropertyName("destinationRelativePath")]
    public string DestinationRelativePath { get; init; } = string.Empty;
}

public sealed class ManagedDownloadRunArtifact
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [JsonPropertyName("readiness")]
    public string Readiness { get; init; } = string.Empty;

    [JsonPropertyName("failedItems")]
    public List<ManagedDownloadFailedItemRecord> FailedItems { get; init; } = new();

    public static ManagedDownloadRunArtifact? TryLoadFromUsbRoot(string? usbRootPath)
    {
        if (string.IsNullOrWhiteSpace(usbRootPath))
        {
            return null;
        }

        var path = Path.Combine(usbRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), "ForgerEMS-managed-download-result.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ManagedDownloadRunArtifact>(File.ReadAllText(path), ReadOptions);
        }
        catch
        {
            return null;
        }
    }

    public bool HasRetryableFailures =>
        FailedItems.Exists(static i => i.Retryable && !string.IsNullOrWhiteSpace(i.DestinationRelativePath));
}
