using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>High-level labels surfaced in UI / readiness scoring.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ToolkitLinkVerificationStatus
{
    NotChecked,
    VerifiedMetadata,
    Reachable,
    Warning,
    Broken,
    UnknownOffline
}

public sealed record ToolkitLinkVerificationInput(
    string ToolName,
    string OfficialUrl,
    long? LocalFileSizeBytes,
    string ChecksumStatusHint);

/// <summary>Aggregate counts derived from a completed verification pass.</summary>
public sealed class ToolkitLinkVerificationSummaryForReadiness
{
    public bool HasRun { get; init; }

    public int UrlsChecked { get; init; }

    public int BrokenCount { get; init; }

    public int WarningCount { get; init; }

    public int VerifiedMetadataCount { get; init; }

    public int ReachableCount { get; init; }

    public int UnknownOfflineCount { get; init; }

    public int SkippedNoUrlCount { get; init; }

    /// <summary>
    /// Loads persisted verification results when they align with the toolkit-health report scope.
    /// When <paramref name="selectedUsbRootPath"/> is empty, alignment uses only toolkit-health targetRoot vs snapshot target.
    /// </summary>
    public static ToolkitLinkVerificationSummaryForReadiness? TryLoadAligned(
        string toolkitHealthReportPath,
        string toolkitHealthReportTargetRoot,
        string? selectedUsbRootPath)
    {
        if (string.IsNullOrWhiteSpace(toolkitHealthReportPath))
        {
            return null;
        }

        var path = ToolkitLinkVerificationPersistence.VerificationPathForToolkitHealthReport(toolkitHealthReportPath);
        if (!ToolkitLinkVerificationPersistence.TryLoad(path, out var snapshot))
        {
            return null;
        }

        return FromSnapshotAligned(snapshot, toolkitHealthReportTargetRoot, selectedUsbRootPath);
    }

    /// <summary>Maps persisted snapshot into readiness inputs when verification aligns with the current toolkit-health report scope.</summary>
    public static ToolkitLinkVerificationSummaryForReadiness? FromSnapshotAligned(
        ToolkitLinkVerificationSnapshot? snapshot,
        string toolkitHealthReportTargetRoot,
        string? selectedUsbRootPath)
    {
        if (snapshot is null || !snapshot.CompletedSuccessfully || snapshot.Entries.Count == 0 || snapshot.Cancelled)
        {
            return null;
        }

        var aligned =
            ToolkitLinkVerificationPersistence.UsbRootsAligned(snapshot.TargetRoot, toolkitHealthReportTargetRoot) &&
            (string.IsNullOrWhiteSpace(selectedUsbRootPath) ||
             ToolkitLinkVerificationPersistence.UsbRootsAligned(snapshot.TargetRoot, selectedUsbRootPath));
        return FromSnapshot(snapshot, aligned);
    }

    private static ToolkitLinkVerificationSummaryForReadiness FromSnapshot(ToolkitLinkVerificationSnapshot snapshot, bool targetMatches)
    {
        if (!targetMatches)
        {
            return new ToolkitLinkVerificationSummaryForReadiness { HasRun = false };
        }

        static bool Countable(ToolkitLinkVerificationEntryDto e) =>
            e.Status is not ToolkitLinkVerificationStatus.NotChecked;

        var countable = snapshot.Entries.Where(Countable).ToArray();
        return new ToolkitLinkVerificationSummaryForReadiness
        {
            HasRun = targetMatches && countable.Length > 0,
            UrlsChecked = countable.Length,
            BrokenCount = countable.Count(e => e.Status == ToolkitLinkVerificationStatus.Broken),
            WarningCount = countable.Count(e => e.Status == ToolkitLinkVerificationStatus.Warning),
            VerifiedMetadataCount = countable.Count(e => e.Status == ToolkitLinkVerificationStatus.VerifiedMetadata),
            ReachableCount = countable.Count(e => e.Status == ToolkitLinkVerificationStatus.Reachable),
            UnknownOfflineCount = countable.Count(e => e.Status == ToolkitLinkVerificationStatus.UnknownOffline),
            SkippedNoUrlCount = snapshot.Entries.Count(e => e.Status == ToolkitLinkVerificationStatus.NotChecked)
        };
    }

    public static string StatusLabel(ToolkitLinkVerificationStatus status) => status switch
    {
        ToolkitLinkVerificationStatus.VerifiedMetadata => "Verified Metadata",
        ToolkitLinkVerificationStatus.Reachable => "Reachable",
        ToolkitLinkVerificationStatus.Warning => "Warning",
        ToolkitLinkVerificationStatus.Broken => "Broken",
        ToolkitLinkVerificationStatus.UnknownOffline => "Unknown / Offline",
        _ => "Not Checked"
    };
}

public sealed class ToolkitLinkVerificationSnapshot
{
    public DateTimeOffset GeneratedUtc { get; set; }

    public string TargetRoot { get; set; } = string.Empty;

    public bool CompletedSuccessfully { get; set; }

    public bool Cancelled { get; set; }

    public string SummaryNote { get; set; } = string.Empty;

    public List<ToolkitLinkVerificationEntryDto> Entries { get; set; } = [];
}

public sealed class ToolkitLinkVerificationEntryDto
{
    public string ToolName { get; set; } = string.Empty;

    /// <summary>Redacted URL (scheme/host/path only); query stripped for privacy.</summary>
    public string UrlFingerprint { get; set; } = string.Empty;

    public ToolkitLinkVerificationStatus Status { get; set; }

    public int? HttpStatus { get; set; }

    public string OriginalHost { get; set; } = string.Empty;

    public string FinalHost { get; set; } = string.Empty;

    public bool OfficialDomainAligned { get; set; }

    public string ContentLengthNote { get; set; } = string.Empty;

    public string ChecksumMetadataNote { get; set; } = string.Empty;

    public DateTimeOffset CheckedUtc { get; set; }

    public string DetailReason { get; set; } = string.Empty;
}

public sealed class ToolkitLinkVerificationRunOptions
{
    public TimeSpan PerRequestTimeout { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>Overall ceiling so offline stalls cannot hang indefinitely.</summary>
    public TimeSpan OverallDeadline { get; init; } = TimeSpan.FromMinutes(4);
}

public static class ToolkitLinkVerificationPersistence
{
    public const string VerificationReportFileName = "toolkit-link-verification-latest.json";

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string VerificationPathForToolkitHealthReport(string toolkitHealthReportPath)
    {
        var directory = Path.GetDirectoryName(toolkitHealthReportPath);
        return string.IsNullOrWhiteSpace(directory)
            ? VerificationReportFileName
            : Path.Combine(directory, VerificationReportFileName);
    }

    public static bool TryLoad(string path, out ToolkitLinkVerificationSnapshot snapshot)
    {
        snapshot = new ToolkitLinkVerificationSnapshot();
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<ToolkitLinkVerificationSnapshot>(File.ReadAllText(path), JsonOptions);
            if (loaded is null)
            {
                return false;
            }

            snapshot = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(string path, ToolkitLinkVerificationSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, JsonOptions));
    }

    public static bool UsbRootsAligned(string? a, string? b) =>
        string.Equals(
            string.IsNullOrWhiteSpace(a) ? string.Empty : a.TrimEnd('\\'),
            string.IsNullOrWhiteSpace(b) ? string.Empty : b.TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase);
}
