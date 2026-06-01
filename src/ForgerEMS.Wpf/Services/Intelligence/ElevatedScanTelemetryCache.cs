using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum ElevatedScanTelemetryState
{
    Missing = 0,
    Fresh = 1,
    Stale = 2
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

    public string Source { get; init; } = "Elevated Scan";

    public PortPowerTelemetryConfidence Confidence { get; init; } = PortPowerTelemetryConfidence.Unavailable;

    public ElevatedPortPowerTelemetry? PortPower { get; init; }

    public string UsbThunderboltDockSummary { get; init; } = string.Empty;

    public string MissingTelemetryReason { get; init; } = RunElevatedScanPrompt;

    public bool IsFresh => State == ElevatedScanTelemetryState.Fresh;

    public bool IsStale => State == ElevatedScanTelemetryState.Stale;

    public bool IsMissing => State == ElevatedScanTelemetryState.Missing;

    public string StatusLine =>
        State switch
        {
            ElevatedScanTelemetryState.Fresh =>
                $"Cached Elevated Scan telemetry from {FormatLocalTime(CollectedAtUtc)} ({Source}; confidence {Confidence}).",
            ElevatedScanTelemetryState.Stale =>
                $"Elevated Scan telemetry is stale/expired; last scan {FormatLocalTime(CollectedAtUtc)}. Run Elevated Scan to refresh deeper port and charging telemetry.",
            _ => RunElevatedScanPrompt
        };

    public static ElevatedScanTelemetrySnapshot Missing() =>
        new()
        {
            State = ElevatedScanTelemetryState.Missing,
            MissingTelemetryReason = RunElevatedScanPrompt
        };

    private static string FormatLocalTime(DateTimeOffset? value) =>
        value.HasValue
            ? value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            : "unknown time";
}

public interface IElevatedScanTelemetryCache
{
    ElevatedScanTelemetrySnapshot GetLatest(string reportsDirectory);
}

public sealed class ElevatedScanTelemetryCache : IElevatedScanTelemetryCache
{
    public static readonly TimeSpan DefaultFreshnessWindow = TimeSpan.FromHours(1);

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
        var snapshot = TryReadLatestFromDisk(reportsDirectory);
        lock (_gate)
        {
            if (snapshot is not null)
            {
                _lastSnapshot = snapshot;
                return ApplyFreshness(snapshot, now);
            }

            if (_lastSnapshot is not null)
            {
                return ApplyFreshness(_lastSnapshot, now);
            }
        }

        return ElevatedScanTelemetrySnapshot.Missing();
    }

    private ElevatedScanTelemetrySnapshot ApplyFreshness(ElevatedScanTelemetrySnapshot snapshot, DateTimeOffset now)
    {
        if (!snapshot.CollectedAtUtc.HasValue)
        {
            return snapshot with
            {
                State = ElevatedScanTelemetryState.Stale,
                MissingTelemetryReason =
                    "Elevated Scan telemetry is stale/expired because no collection timestamp was recorded. Run Elevated Scan to refresh deeper port and charging telemetry."
            };
        }

        var age = now - snapshot.CollectedAtUtc.Value;
        if (age <= _freshnessWindow)
        {
            return snapshot with
            {
                State = ElevatedScanTelemetryState.Fresh,
                MissingTelemetryReason = BuildFreshMissingReason(snapshot)
            };
        }

        return snapshot with
        {
            State = ElevatedScanTelemetryState.Stale,
            MissingTelemetryReason =
                $"Elevated Scan telemetry is stale/expired; last scan {snapshot.CollectedAtUtc.Value.ToLocalTime():g}. Run Elevated Scan to refresh deeper port and charging telemetry."
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

    private static ElevatedScanTelemetrySnapshot? TryReadLatestFromDisk(string reportsDirectory)
    {
        if (string.IsNullOrWhiteSpace(reportsDirectory))
        {
            return null;
        }

        var resultPath = Path.Combine(reportsDirectory, "elevated-scan-result.json");
        if (!File.Exists(resultPath))
        {
            return null;
        }

        try
        {
            using var marker = JsonDocument.Parse(File.ReadAllText(resultPath));
            var markerRoot = marker.RootElement;
            var markerUtc = ReadDateTimeOffset(markerRoot, "utc");
            var reportFileName = GetString(markerRoot, "json");
            if (string.IsNullOrWhiteSpace(reportFileName))
            {
                reportFileName = "system-intelligence-latest.json";
            }

            var reportPath = Path.Combine(reportsDirectory, reportFileName);
            if (!File.Exists(reportPath))
            {
                return new ElevatedScanTelemetrySnapshot
                {
                    State = ElevatedScanTelemetryState.Stale,
                    CollectedAtUtc = markerUtc ?? GetFileTimestamp(resultPath),
                    Source = "Elevated Scan marker",
                    MissingTelemetryReason =
                        "Elevated Scan marker exists, but the system report was not found. Run Elevated Scan to refresh deeper port and charging telemetry."
                };
            }

            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = report.RootElement;
            var collectedAt = ReadDateTimeOffset(root, "generatedUtc") ??
                              markerUtc ??
                              GetFileTimestamp(reportPath) ??
                              DateTimeOffset.UtcNow;
            var portPower = TryReadPortPowerTelemetry(root, collectedAt);
            var source = portPower?.Source ?? "System Intelligence Elevated Scan";
            var confidence = portPower?.Confidence ?? ParseConfidence(GetString(root, "sensorMatrix", "confidence"));
            var usbSummary = BuildUsbThunderboltDockSummary(portPower);

            return new ElevatedScanTelemetrySnapshot
            {
                State = ElevatedScanTelemetryState.Fresh,
                CollectedAtUtc = collectedAt,
                Source = source,
                Confidence = confidence,
                PortPower = portPower,
                UsbThunderboltDockSummary = usbSummary,
                MissingTelemetryReason = portPower?.MissingTelemetryReason ??
                                         "Elevated Scan completed, but this device did not expose deeper port or charging telemetry."
            };
        }
        catch
        {
            return null;
        }
    }

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

    private static DateTimeOffset? GetFileTimestamp(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
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
