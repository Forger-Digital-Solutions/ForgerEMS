using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum ElevatedScanTelemetryState
{
    Missing = 0,
    Fresh = 1,
    Stale = 2,
    Running = 3,
    CompletePartial = 4,
    Failed = 5,
    NeedsAdmin = 6,
    Cancelled = 7
}

public enum ElevatedScanParseQuality
{
    None = 0,
    Complete = 1,
    Partial = 2,
    Failed = 3
}

public enum ElevatedScanSeverity
{
    Info = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public sealed record ElevatedPortPowerTelemetry
{
    public DateTimeOffset CollectedAtUtc { get; init; }

    public string Source { get; init; } = "Elevated Scan";

    public PortPowerTelemetryConfidence Confidence { get; init; } = PortPowerTelemetryConfidence.Unavailable;

    public double? EffectiveChargeRateWatts { get; init; }

    public double? AdapterWattageWatts { get; init; }

    public int? AdapterWattageClassWatts { get; init; }

    public double? VoltageVolts { get; init; }

    public double? CurrentAmps { get; init; }

    public IReadOnlyList<string> SourceHints { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public string MissingTelemetryReason { get; init; } = string.Empty;

    public bool HasDirectElectricalTelemetry =>
        EffectiveChargeRateWatts.HasValue ||
        AdapterWattageWatts.HasValue ||
        AdapterWattageClassWatts.HasValue ||
        VoltageVolts.HasValue ||
        CurrentAmps.HasValue;
}

public sealed record ElevatedScanTelemetrySnapshot
{
    public const string RunElevatedScanPrompt =
        "Run Elevated Scan to unlock deeper port and charging telemetry.";

    public ElevatedScanTelemetryState State { get; init; }

    public DateTimeOffset? CollectedAtUtc { get; init; }

    public DateTimeOffset? LastRunLocal => CollectedAtUtc?.ToLocalTime();

    public string ReportPath { get; init; } = string.Empty;

    public string Freshness { get; init; } = "Unknown";

    public ElevatedScanParseQuality ParseQuality { get; init; }

    public string UnavailableReason { get; init; } = string.Empty;

    public string UserMessage { get; init; } = RunElevatedScanPrompt;

    public ElevatedScanSeverity Severity { get; init; } = ElevatedScanSeverity.Warning;

    public string Source { get; init; } = "Elevated Scan";

    public PortPowerTelemetryConfidence Confidence { get; init; } = PortPowerTelemetryConfidence.Unavailable;

    public ElevatedPortPowerTelemetry? PortPower { get; init; }

    public string UsbThunderboltDockSummary { get; init; } = string.Empty;

    public string MissingTelemetryReason { get; init; } = RunElevatedScanPrompt;

    public bool IsFresh => State is ElevatedScanTelemetryState.Fresh or ElevatedScanTelemetryState.CompletePartial;

    public bool IsStale => State == ElevatedScanTelemetryState.Stale;

    public bool IsMissing => State == ElevatedScanTelemetryState.Missing;

    public bool IsFailure =>
        State is ElevatedScanTelemetryState.Failed or
            ElevatedScanTelemetryState.NeedsAdmin or
            ElevatedScanTelemetryState.Cancelled;

    public string StatusLine =>
        State switch
        {
            ElevatedScanTelemetryState.Fresh =>
                $"Elevated scan complete. Cached telemetry from {FormatLocalTime(CollectedAtUtc)} ({Source}; confidence {Confidence}).",
            ElevatedScanTelemetryState.CompletePartial =>
                "Elevated scan complete — some permission-limited detail was unavailable on this device.",
            ElevatedScanTelemetryState.Stale =>
                $"Elevated scan recommended. Admin inventory data is stale/expired; last scan {FormatLocalTime(CollectedAtUtc)}.",
            ElevatedScanTelemetryState.Running =>
                "Elevated scan running. Waiting for the elevated report to finish.",
            ElevatedScanTelemetryState.Failed =>
                "Elevated scan failed. ForgerEMS stayed open. Check logs or retry as administrator.",
            ElevatedScanTelemetryState.NeedsAdmin =>
                "Elevated scan needs administrator approval. Retry and approve the Windows UAC prompt.",
            ElevatedScanTelemetryState.Cancelled =>
                "Elevated scan cancelled. Standard Scan results are still available.",
            _ => RunElevatedScanPrompt
        };

    public static ElevatedScanTelemetrySnapshot Missing() =>
        new()
        {
            State = ElevatedScanTelemetryState.Missing,
            Freshness = "Missing",
            ParseQuality = ElevatedScanParseQuality.None,
            Severity = ElevatedScanSeverity.Warning,
            UserMessage = "Elevated scan recommended",
            MissingTelemetryReason = RunElevatedScanPrompt,
            UnavailableReason = RunElevatedScanPrompt
        };

    private static string FormatLocalTime(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "unknown time";
}

public interface IElevatedScanTelemetryCache
{
    ElevatedScanTelemetrySnapshot GetLatest(string reportsDirectory);

    Task<ElevatedScanTelemetrySnapshot> GetLatestAsync(string reportsDirectory, CancellationToken cancellationToken = default);
}

public sealed class ElevatedScanTelemetryCache : IElevatedScanTelemetryCache
{
    public static readonly TimeSpan DefaultFreshnessWindow = TimeSpan.FromHours(1);

    private const string PartialCompletionMessage =
        "Elevated scan complete — some permission-limited detail was unavailable on this device.";

    private readonly Func<DateTimeOffset> _utcNow;
    private readonly TimeSpan _freshnessWindow;
    private readonly object _gate = new();
    private ElevatedScanTelemetrySnapshot? _lastSnapshot;

    public ElevatedScanTelemetryCache()
        : this(() => DateTimeOffset.UtcNow, DefaultFreshnessWindow)
    {
    }

    internal ElevatedScanTelemetryCache(Func<DateTimeOffset> utcNow, TimeSpan freshnessWindow)
    {
        _utcNow = utcNow;
        _freshnessWindow = freshnessWindow;
    }

    public ElevatedScanTelemetrySnapshot GetLatest(string reportsDirectory)
    {
        var now = _utcNow();
        var snapshot = TryReadLatestFromDisk(reportsDirectory, now);
        lock (_gate)
        {
            if (snapshot is not null)
            {
                var current = ApplyFreshness(snapshot, now);
                _lastSnapshot = current;
                return current;
            }

            if (_lastSnapshot is not null)
            {
                return ApplyFreshness(_lastSnapshot, now);
            }
        }

        return ElevatedScanTelemetrySnapshot.Missing();
    }

    public Task<ElevatedScanTelemetrySnapshot> GetLatestAsync(
        string reportsDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => GetLatest(reportsDirectory), cancellationToken);

    private ElevatedScanTelemetrySnapshot ApplyFreshness(ElevatedScanTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        if (!snapshot.IsFresh && !snapshot.IsStale)
        {
            return snapshot;
        }

        if (!snapshot.CollectedAtUtc.HasValue)
        {
            return snapshot with
            {
                State = ElevatedScanTelemetryState.Stale,
                Freshness = "Stale",
                Severity = ElevatedScanSeverity.Warning,
                UserMessage = "Elevated scan recommended",
                MissingTelemetryReason =
                    "Elevated Scan telemetry is stale/expired because no collection timestamp was recorded. Run Elevated Scan to refresh deeper port and charging telemetry.",
                UnavailableReason =
                    "No elevated scan collection timestamp was recorded."
            };
        }

        var age = now - snapshot.CollectedAtUtc.Value;
        if (age <= _freshnessWindow)
        {
            var state = snapshot.ParseQuality == ElevatedScanParseQuality.Partial
                ? ElevatedScanTelemetryState.CompletePartial
                : ElevatedScanTelemetryState.Fresh;
            return snapshot with
            {
                State = state,
                Freshness = "Fresh",
                Severity = ElevatedScanSeverity.Success,
                UserMessage = state == ElevatedScanTelemetryState.CompletePartial
                    ? PartialCompletionMessage
                    : "Elevated scan complete",
                MissingTelemetryReason = BuildFreshMissingReason(snapshot),
                UnavailableReason = BuildFreshUnavailableReason(snapshot)
            };
        }

        var staleMessage =
            $"Elevated Scan telemetry is stale/expired; last scan {snapshot.CollectedAtUtc.Value.ToLocalTime():g}. Run Elevated Scan to refresh deeper port and charging telemetry.";
        return snapshot with
        {
            State = ElevatedScanTelemetryState.Stale,
            Freshness = "Stale",
            Severity = ElevatedScanSeverity.Warning,
            UserMessage = "Elevated scan recommended",
            MissingTelemetryReason = staleMessage,
            UnavailableReason = staleMessage
        };
    }

    private static string BuildFreshMissingReason(ElevatedScanTelemetrySnapshot snapshot)
    {
        if (snapshot.PortPower?.HasDirectElectricalTelemetry == true)
        {
            return string.Empty;
        }

        return string.IsNullOrWhiteSpace(snapshot.PortPower?.MissingTelemetryReason)
            ? "Elevated Scan completed, but this device did not expose deeper port or charging telemetry."
            : snapshot.PortPower!.MissingTelemetryReason;
    }

    private static string BuildFreshUnavailableReason(ElevatedScanTelemetrySnapshot snapshot)
    {
        if (snapshot.ParseQuality == ElevatedScanParseQuality.Partial)
        {
            return PartialCompletionMessage;
        }

        return BuildFreshMissingReason(snapshot);
    }

    private static ElevatedScanTelemetrySnapshot? TryReadLatestFromDisk(string reportsDirectory, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reportsDirectory) || !Directory.Exists(reportsDirectory))
        {
            return null;
        }

        var resultPath = Path.Combine(reportsDirectory, "elevated-scan-result.json");
        var errorPath = Path.Combine(reportsDirectory, "elevated-scan-error.json");
        var resultTimestamp = GetMarkerUtc(resultPath);
        var errorTimestamp = GetMarkerUtc(errorPath);
        var pendingTimestamp = GetPendingMarkerUtc(reportsDirectory);

        if (errorTimestamp.HasValue &&
            (!resultTimestamp.HasValue || errorTimestamp.Value >= resultTimestamp.Value) &&
            (!pendingTimestamp.HasValue || errorTimestamp.Value >= pendingTimestamp.Value))
        {
            return ReadErrorMarker(errorPath);
        }

        if (pendingTimestamp.HasValue &&
            (!resultTimestamp.HasValue || pendingTimestamp.Value > resultTimestamp.Value) &&
            (!errorTimestamp.HasValue || pendingTimestamp.Value > errorTimestamp.Value))
        {
            return ReadPendingMarkerIfAny(reportsDirectory, now);
        }

        if (File.Exists(resultPath))
        {
            return ReadResultMarkerAndReport(reportsDirectory, resultPath);
        }

        if (File.Exists(errorPath))
        {
            return ReadErrorMarker(errorPath);
        }

        return ReadPendingMarkerIfAny(reportsDirectory, now);
    }

    private static ElevatedScanTelemetrySnapshot ReadResultMarkerAndReport(string reportsDirectory, string resultPath)
    {
        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(resultPath));
            var markerRoot = marker.RootElement;
            if (ReadBool(markerRoot, "ok") != true)
            {
                return Failure(
                    "Elevated scan failed",
                    "Elevated Scan result marker did not report success. ForgerEMS stayed open. Check logs or retry as administrator.",
                    ElevatedScanTelemetryState.Failed,
                    GetMarkerUtc(resultPath) ?? GetFileTimestamp(resultPath),
                    resultPath);
            }

            var markerUtc = ReadDateTimeOffset(markerRoot, "utc");
            var reportFileName = GetString(markerRoot, "json");
            if (string.IsNullOrWhiteSpace(reportFileName))
            {
                reportFileName = "system-intelligence-latest.json";
            }

            var reportPath = Path.Combine(reportsDirectory, reportFileName);
            if (!File.Exists(reportPath))
            {
                return Failure(
                    "Elevated scan failed",
                    "Elevated Scan completed marker exists, but the System Intelligence report was not found. ForgerEMS stayed open. Check logs or retry as administrator.",
                    ElevatedScanTelemetryState.Failed,
                    markerUtc ?? GetFileTimestamp(resultPath),
                    resultPath);
            }

            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = report.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure(
                    "Elevated scan failed",
                    "Elevated Scan report parsing failed. ForgerEMS stayed open. Check logs or retry as administrator.",
                    ElevatedScanTelemetryState.Failed,
                    markerUtc ?? GetFileTimestamp(reportPath),
                    reportPath);
            }

            var scanMode = GetString(root, "scanMode");
            if (!string.Equals(scanMode, "Elevated", StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    "Elevated scan failed",
                    "Elevated Scan result did not point to a fresh elevated report. ForgerEMS stayed open. Retry as administrator.",
                    ElevatedScanTelemetryState.Failed,
                    markerUtc ?? GetFileTimestamp(reportPath),
                    reportPath);
            }

            var collectedAt = ReadDateTimeOffset(root, "generatedUtc") ??
                              markerUtc ??
                              GetFileTimestamp(reportPath) ??
                              DateTimeOffset.UtcNow;
            var portPower = TryReadPortPowerTelemetry(root, collectedAt);
            var source = portPower?.Source ?? "System Intelligence Elevated Scan";
            var confidence = portPower?.Confidence ?? ParseConfidence(GetString(root, "sensorMatrix", "confidence"));
            var usbSummary = BuildUsbThunderboltDockSummary(portPower);
            var partial = portPower?.HasDirectElectricalTelemetry != true;
            var unavailableReason = partial
                ? PartialCompletionMessage
                : string.Empty;

            return new ElevatedScanTelemetrySnapshot
            {
                State = partial ? ElevatedScanTelemetryState.CompletePartial : ElevatedScanTelemetryState.Fresh,
                CollectedAtUtc = collectedAt,
                ReportPath = reportPath,
                Freshness = "Fresh",
                ParseQuality = partial ? ElevatedScanParseQuality.Partial : ElevatedScanParseQuality.Complete,
                Severity = ElevatedScanSeverity.Success,
                UserMessage = partial ? PartialCompletionMessage : "Elevated scan complete",
                Source = source,
                Confidence = confidence,
                PortPower = portPower,
                UsbThunderboltDockSummary = usbSummary,
                MissingTelemetryReason = portPower?.MissingTelemetryReason ??
                                         "Elevated Scan completed, but this device did not expose deeper port or charging telemetry.",
                UnavailableReason = unavailableReason
            };
        }
        catch (JsonException)
        {
            return Failure(
                "Elevated scan failed",
                "Elevated Scan report parsing failed. ForgerEMS stayed open. Check logs or retry as administrator.",
                ElevatedScanTelemetryState.Failed,
                GetMarkerUtc(resultPath) ?? GetFileTimestamp(resultPath),
                resultPath);
        }
        catch (IOException)
        {
            return Failure(
                "Elevated scan failed",
                "Elevated Scan report could not be read. ForgerEMS stayed open. Check logs or retry as administrator.",
                ElevatedScanTelemetryState.Failed,
                GetMarkerUtc(resultPath) ?? GetFileTimestamp(resultPath),
                resultPath);
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                "Elevated scan failed",
                "Elevated Scan report could not be read because Windows denied file access. ForgerEMS stayed open. Check logs or retry as administrator.",
                ElevatedScanTelemetryState.Failed,
                GetMarkerUtc(resultPath) ?? GetFileTimestamp(resultPath),
                resultPath);
        }
    }

    private static ElevatedScanTelemetrySnapshot ReadErrorMarker(string errorPath)
    {
        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(errorPath));
            var root = marker.RootElement;
            var failureKind = GetString(root, "failureKind");
            var utc = ReadDateTimeOffset(root, "utc") ?? GetFileTimestamp(errorPath);
            var state = failureKind switch
            {
                nameof(ElevatedScanFailureKind.UacCancelled) => ElevatedScanTelemetryState.Cancelled,
                nameof(ElevatedScanFailureKind.UacBlockedOrDenied) => ElevatedScanTelemetryState.NeedsAdmin,
                nameof(ElevatedScanFailureKind.ElevatedProcessDidNotStart) => ElevatedScanTelemetryState.NeedsAdmin,
                _ => ElevatedScanTelemetryState.Failed
            };
            var title = state switch
            {
                ElevatedScanTelemetryState.Cancelled => "Elevated scan cancelled",
                ElevatedScanTelemetryState.NeedsAdmin => "Elevated scan needs administrator approval",
                _ => "Elevated scan failed"
            };
            var detail = state switch
            {
                ElevatedScanTelemetryState.Cancelled =>
                    "Elevated Scan was cancelled before administrator permission was approved. Standard Scan results are still available.",
                ElevatedScanTelemetryState.NeedsAdmin =>
                    "Windows blocked or denied administrator elevation. Retry Elevated Scan and approve the UAC prompt, or start ForgerEMS as administrator.",
                _ =>
                    "ForgerEMS stayed open. Check logs or retry as administrator."
            };

            return Failure(title, detail, state, utc, errorPath);
        }
        catch
        {
            return Failure(
                "Elevated scan failed",
                "Elevated Scan error marker could not be parsed. ForgerEMS stayed open. Check logs or retry as administrator.",
                ElevatedScanTelemetryState.Failed,
                GetFileTimestamp(errorPath),
                errorPath);
        }
    }

    private static ElevatedScanTelemetrySnapshot? ReadPendingMarkerIfAny(string reportsDirectory, DateTimeOffset now)
    {
        var startedPath = Path.Combine(reportsDirectory, "elevated-scan-started.json");
        var heartbeatPath = Path.Combine(reportsDirectory, "elevated-scan-heartbeat.json");
        var lastUtc = GetPendingMarkerUtc(reportsDirectory);
        if (!lastUtc.HasValue)
        {
            return null;
        }

        if (now - lastUtc.Value <= ElevatedScanDiagnostics.ElevatedScanWaitTimeout)
        {
            return new ElevatedScanTelemetrySnapshot
            {
                State = ElevatedScanTelemetryState.Running,
                CollectedAtUtc = lastUtc.Value,
                ReportPath = File.Exists(heartbeatPath) ? heartbeatPath : startedPath,
                Freshness = "Running",
                ParseQuality = ElevatedScanParseQuality.None,
                Severity = ElevatedScanSeverity.Info,
                UserMessage = "Elevated scan running",
                MissingTelemetryReason = "Elevated Scan is still running. Waiting for the elevated report to finish.",
                UnavailableReason = "Elevated Scan is still running."
            };
        }

        return Failure(
            "Elevated scan failed",
            "Elevated Scan started but did not produce a completed report before the wait window expired. ForgerEMS stayed open. Check logs or retry as administrator.",
            ElevatedScanTelemetryState.Failed,
            lastUtc.Value,
            File.Exists(heartbeatPath) ? heartbeatPath : startedPath);
    }

    private static ElevatedScanTelemetrySnapshot Failure(
        string title,
        string detail,
        ElevatedScanTelemetryState state,
        DateTimeOffset? at,
        string path) =>
        new()
        {
            State = state,
            CollectedAtUtc = at,
            ReportPath = path,
            Freshness = "Unavailable",
            ParseQuality = ElevatedScanParseQuality.Failed,
            Severity = state is ElevatedScanTelemetryState.Cancelled or ElevatedScanTelemetryState.NeedsAdmin
                ? ElevatedScanSeverity.Warning
                : ElevatedScanSeverity.Error,
            UserMessage = title,
            Source = "Elevated Scan",
            Confidence = PortPowerTelemetryConfidence.Unavailable,
            MissingTelemetryReason = detail,
            UnavailableReason = detail
        };

    private static ElevatedPortPowerTelemetry? TryReadPortPowerTelemetry(JsonElement root, DateTimeOffset collectedAt)
    {
        if (!TryGetProperty(root, "portPowerTelemetry", out var portPower) ||
            portPower.ValueKind != JsonValueKind.Object)
        {
            return new ElevatedPortPowerTelemetry
            {
                CollectedAtUtc = collectedAt,
                Source = "System Intelligence Elevated Scan",
                Confidence = PortPowerTelemetryConfidence.Unavailable,
                MissingTelemetryReason =
                    "Elevated Scan completed, but this device did not expose deeper port or charging telemetry."
            };
        }

        var source = GetString(portPower, "source");
        if (string.IsNullOrWhiteSpace(source))
        {
            source = "System Intelligence Elevated Scan";
        }

        var classWatts = GetInt(portPower, "adapterWattageClassWatts");
        var adapterWatts = GetDouble(portPower, "adapterWattageWatts");
        if (!adapterWatts.HasValue && classWatts.HasValue)
        {
            adapterWatts = classWatts.Value;
        }

        return new ElevatedPortPowerTelemetry
        {
            CollectedAtUtc = ReadDateTimeOffset(portPower, "collectedAtUtc") ?? collectedAt,
            Source = source,
            Confidence = ParseConfidence(GetString(portPower, "confidence")),
            EffectiveChargeRateWatts = GetDouble(portPower, "effectiveChargeRateWatts"),
            AdapterWattageWatts = adapterWatts,
            AdapterWattageClassWatts = classWatts,
            VoltageVolts = GetDouble(portPower, "voltageVolts"),
            CurrentAmps = GetDouble(portPower, "currentAmps"),
            SourceHints = GetStringArray(portPower, "sourceHints"),
            Evidence = GetStringArray(portPower, "evidence"),
            MissingTelemetryReason = GetString(portPower, "missingTelemetryReason")
        };
    }

    private static string BuildUsbThunderboltDockSummary(ElevatedPortPowerTelemetry? portPower)
    {
        if (portPower is null)
        {
            return string.Empty;
        }

        var hints = portPower.SourceHints
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Take(3)
            .ToArray();
        if (hints.Length == 0)
        {
            return portPower.HasDirectElectricalTelemetry
                ? "Elevated Scan exposed charging telemetry; no separate USB/Thunderbolt/dock source hint was exposed."
                : string.Empty;
        }

        return "Elevated Scan source hints: " + string.Join("; ", hints);
    }

    private static DateTimeOffset? GetMarkerUtc(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(path));
            return ReadDateTimeOffset(marker.RootElement, "utc") ?? GetFileTimestamp(path);
        }
        catch
        {
            return GetFileTimestamp(path);
        }
    }

    private static DateTimeOffset? GetPendingMarkerUtc(string reportsDirectory)
    {
        var timestamps = new[]
            {
                GetMarkerUtc(Path.Combine(reportsDirectory, "elevated-scan-started.json")),
                GetMarkerUtc(Path.Combine(reportsDirectory, "elevated-scan-heartbeat.json"))
            }
            .Where(item => item.HasValue)
            .Select(item => item!.Value)
            .ToArray();

        return timestamps.Length == 0
            ? null
            : timestamps.Max();
    }

    private static DateTimeOffset? GetFileTimestamp(string path)
    {
        try
        {
            return File.Exists(path)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static PortPowerTelemetryConfidence ParseConfidence(string? value) =>
        Enum.TryParse<PortPowerTelemetryConfidence>(value, ignoreCase: true, out var parsed)
            ? parsed
            : PortPowerTelemetryConfidence.Unavailable;

    private static string GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string GetString(JsonElement element, string parent, string child)
    {
        if (TryGetProperty(element, parent, out var parentElement) &&
            parentElement.ValueKind == JsonValueKind.Object)
        {
            return GetString(parentElement, child);
        }

        return string.Empty;
    }

    private static bool? ReadBool(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static double? GetDouble(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static int? GetInt(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return number;
        }

        return null;
    }

    private static DateTimeOffset? ReadDateTimeOffset(JsonElement element, string name)
    {
        if (TryGetProperty(element, name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string[] GetStringArray(JsonElement element, string name)
    {
        if (!TryGetProperty(element, name, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
