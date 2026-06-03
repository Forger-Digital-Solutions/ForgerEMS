using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum PortPowerTelemetryConfidence
{
    Unavailable = 0,
    Low = 1,
    Medium = 2,
    High = 3
}

public enum PortPowerSourceKind
{
    Unknown = 0,
    Battery = 1,
    AcAdapter = 2,
    UsbCPd = 3,
    Dock = 4
}

public sealed class PortPowerSample
{
    public DateTimeOffset CollectedAtUtc { get; init; }

    public double BatteryPercent { get; init; }
}

public sealed class PortPowerEstimate
{
    public double? PercentPerHour { get; init; }

    public TimeSpan? EstimatedTimeToFull { get; init; }

    public bool IsBasedOnShortWindow { get; init; }

    public TimeSpan SampleWindow { get; init; }

    public int SampleCount { get; init; }
}

public sealed class PortPowerSnapshot
{
    public DateTimeOffset CollectedAtUtc { get; init; }

    public double? BatteryPercent { get; init; }

    public string BatteryStatus { get; init; } = "Unknown";

    public bool IsCharging { get; init; }

    public bool IsPluggedIn { get; init; }

    public bool HasBattery { get; init; }

    public PortPowerSourceKind PowerSourceKind { get; init; }

    public int? AdapterWattageClassWatts { get; init; }

    public double? EffectiveChargeRateWatts { get; init; }

    public double? PercentPerHour { get; init; }

    public TimeSpan? EstimatedTimeToFull { get; init; }

    public double? VoltageVolts { get; init; }

    public double? CurrentAmps { get; init; }

    public PortPowerTelemetryConfidence TelemetryConfidence { get; init; }

    public string EvidenceSummary { get; init; } = string.Empty;

    public string MissingTelemetryReason { get; init; } = string.Empty;

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool RateIsEstimatedFromBatterySamples { get; init; }

    public bool HasDirectElectricalTelemetry =>
        EffectiveChargeRateWatts.HasValue ||
        VoltageVolts.HasValue ||
        CurrentAmps.HasValue ||
        AdapterWattageClassWatts.HasValue;
}

internal sealed class PortPowerRawTelemetry
{
    public DateTimeOffset CollectedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public bool HasBattery { get; init; }

    public double? BatteryPercent { get; init; }

    public int? BatteryStatusCode { get; init; }

    public string BatteryStatusText { get; init; } = string.Empty;

    public bool? IsCharging { get; init; }

    public bool? IsPluggedIn { get; init; }

    public double? DirectEffectiveChargeRateWatts { get; init; }

    public double? DirectAdapterWattageWatts { get; init; }

    public double? VoltageVolts { get; init; }

    public double? CurrentAmps { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> SourceHints { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public bool HasAnyPowerData =>
        HasBattery ||
        BatteryPercent.HasValue ||
        IsCharging.HasValue ||
        IsPluggedIn.HasValue ||
        DirectEffectiveChargeRateWatts.HasValue ||
        DirectAdapterWattageWatts.HasValue ||
        VoltageVolts.HasValue ||
        CurrentAmps.HasValue;
}

public interface IPortPowerTelemetryService
{
    PortPowerSnapshot CollectSnapshot();

    PortPowerSnapshot CollectSnapshot(ElevatedScanTelemetrySnapshot? elevatedTelemetry);

    void ResetSamples();
}

internal interface IPortPowerTelemetrySource
{
    PortPowerRawTelemetry Read();
}

public sealed class PortPowerTelemetryService : IPortPowerTelemetryService
{
    private static readonly TimeSpan SampleRetention = TimeSpan.FromHours(2);

    private readonly object _sampleGate = new();
    private readonly List<PortPowerSample> _samples = new();
    private readonly IPortPowerTelemetrySource _source;

    public PortPowerTelemetryService()
        : this(new WindowsPortPowerTelemetrySource())
    {
    }

    internal PortPowerTelemetryService(IPortPowerTelemetrySource source)
    {
        _source = source;
    }

    public PortPowerSnapshot CollectSnapshot()
    {
        return CollectSnapshot(null);
    }

    public PortPowerSnapshot CollectSnapshot(ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        var raw = _source.Read();
        raw = MergeElevatedTelemetry(raw, elevatedTelemetry);
        var collectedAt = raw.CollectedAtUtc == default ? DateTimeOffset.UtcNow : raw.CollectedAtUtc;

        if (!raw.HasAnyPowerData)
        {
            return new PortPowerSnapshot
            {
                CollectedAtUtc = collectedAt,
                BatteryStatus = "Unknown",
                TelemetryConfidence = PortPowerTelemetryConfidence.Unavailable,
                MissingTelemetryReason = BuildNoPowerDataReason(elevatedTelemetry),
                EvidenceSummary = BuildNoPowerDataEvidence(elevatedTelemetry),
                Warnings = BuildElevatedTelemetryWarnings(elevatedTelemetry).ToArray()
            };
        }

        if (raw.HasBattery && raw.BatteryPercent.HasValue)
        {
            RecordSample(new PortPowerSample
            {
                CollectedAtUtc = collectedAt,
                BatteryPercent = raw.BatteryPercent.Value
            });
        }

        var recentSamples = GetRecentSamples(collectedAt);
        var status = PortPowerEstimator.MapBatteryStatus(
            raw.BatteryStatusCode,
            raw.BatteryStatusText,
            raw.IsCharging,
            raw.IsPluggedIn,
            raw.BatteryPercent,
            raw.HasBattery);
        var isCharging = string.Equals(status, "Charging", StringComparison.OrdinalIgnoreCase);
        var isPluggedIn = raw.IsPluggedIn ?? isCharging;
        var sourceKind = PortPowerEstimator.DetermineSourceKind(isPluggedIn, raw.HasBattery, raw.SourceHints);
        var estimate = PortPowerEstimator.BuildEstimate(recentSamples, raw.BatteryPercent, isCharging);
        var adapterClass = PortPowerEstimator.ClassifyAdapterWattage(raw.DirectAdapterWattageWatts);
        var directRate = NormalizeDirectWatts(raw.DirectEffectiveChargeRateWatts);
        var warnings = BuildWarnings(raw, estimate, isCharging, directRate.HasValue, elevatedTelemetry).ToArray();
        var confidence = PortPowerEstimator.SelectConfidence(raw, estimate, directRate.HasValue, adapterClass.HasValue);
        var evidence = BuildEvidenceSummary(raw, estimate, directRate.HasValue, adapterClass.HasValue, elevatedTelemetry);

        return new PortPowerSnapshot
        {
            CollectedAtUtc = collectedAt,
            BatteryPercent = raw.BatteryPercent,
            BatteryStatus = status,
            IsCharging = isCharging,
            IsPluggedIn = isPluggedIn,
            HasBattery = raw.HasBattery,
            PowerSourceKind = sourceKind,
            AdapterWattageClassWatts = adapterClass,
            EffectiveChargeRateWatts = directRate,
            PercentPerHour = estimate.PercentPerHour,
            EstimatedTimeToFull = estimate.EstimatedTimeToFull,
            VoltageVolts = raw.VoltageVolts,
            CurrentAmps = raw.CurrentAmps,
            TelemetryConfidence = confidence,
            EvidenceSummary = evidence,
            MissingTelemetryReason = BuildMissingTelemetryReason(raw, adapterClass.HasValue, directRate.HasValue, elevatedTelemetry),
            Warnings = warnings,
            RateIsEstimatedFromBatterySamples = !directRate.HasValue && estimate.PercentPerHour.HasValue
        };
    }

    public void ResetSamples()
    {
        lock (_sampleGate)
        {
            _samples.Clear();
        }
    }

    private void RecordSample(PortPowerSample sample)
    {
        if (sample.BatteryPercent is < 0d or > 100d)
        {
            return;
        }

        lock (_sampleGate)
        {
            if (_samples.Count > 0)
            {
                var last = _samples[^1];
                if (last.CollectedAtUtc == sample.CollectedAtUtc &&
                    Math.Abs(last.BatteryPercent - sample.BatteryPercent) < 0.001d)
                {
                    return;
                }
            }

            _samples.Add(sample);
            var oldestAllowed = sample.CollectedAtUtc - SampleRetention;
            _samples.RemoveAll(item => item.CollectedAtUtc < oldestAllowed);
        }
    }

    private PortPowerSample[] GetRecentSamples(DateTimeOffset now)
    {
        lock (_sampleGate)
        {
            var oldestAllowed = now - SampleRetention;
            return _samples
                .Where(item => item.CollectedAtUtc >= oldestAllowed)
                .OrderBy(item => item.CollectedAtUtc)
                .ToArray();
        }
    }

    private static double? NormalizeDirectWatts(double? watts)
    {
        if (!watts.HasValue || watts.Value <= 0d || watts.Value > 1000d)
        {
            return null;
        }

        return watts.Value;
    }

    private static PortPowerRawTelemetry MergeElevatedTelemetry(
        PortPowerRawTelemetry raw,
        ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        if (elevatedTelemetry is not { IsFresh: true, PortPower: { } elevated })
        {
            return raw;
        }

        var evidence = raw.Evidence.Concat(elevated.Evidence).ToList();
        evidence.Add($"Elevated Scan telemetry cache used ({elevated.Source}; confidence {elevated.Confidence}).");
        var sourceHints = raw.SourceHints.Concat(elevated.SourceHints).ToArray();
        var warnings = raw.Warnings.ToList();
        if (!elevated.HasDirectElectricalTelemetry)
        {
            warnings.Add("Elevated Scan did not expose deeper port or charging electrical telemetry on this device.");
        }

        return new PortPowerRawTelemetry
        {
            CollectedAtUtc = raw.CollectedAtUtc,
            HasBattery = raw.HasBattery,
            BatteryPercent = raw.BatteryPercent,
            BatteryStatusCode = raw.BatteryStatusCode,
            BatteryStatusText = raw.BatteryStatusText,
            IsCharging = raw.IsCharging,
            IsPluggedIn = raw.IsPluggedIn,
            DirectEffectiveChargeRateWatts = raw.DirectEffectiveChargeRateWatts ?? elevated.EffectiveChargeRateWatts,
            DirectAdapterWattageWatts = raw.DirectAdapterWattageWatts ??
                                        elevated.AdapterWattageWatts ??
                                        elevated.AdapterWattageClassWatts,
            VoltageVolts = raw.VoltageVolts ?? elevated.VoltageVolts,
            CurrentAmps = raw.CurrentAmps ?? elevated.CurrentAmps,
            Evidence = evidence
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            SourceHints = sourceHints
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Warnings = warnings
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };
    }

    private static IEnumerable<string> BuildWarnings(
        PortPowerRawTelemetry raw,
        PortPowerEstimate estimate,
        bool isCharging,
        bool hasDirectRate,
        ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        foreach (var warning in raw.Warnings.Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            yield return warning;
        }

        foreach (var warning in BuildElevatedTelemetryWarnings(elevatedTelemetry))
        {
            yield return warning;
        }

        if (!hasDirectRate && estimate.PercentPerHour.HasValue)
        {
            yield return "Estimated rate is based on battery percentage change during this app session.";
        }

        if (estimate.IsBasedOnShortWindow)
        {
            yield return "Short sample window; confidence stays low until more battery samples are collected.";
        }

        if (isCharging)
        {
            yield return "Workload affects charging speed; the estimate may change while CPU/GPU load changes.";
        }
    }

    private static string BuildEvidenceSummary(
        PortPowerRawTelemetry raw,
        PortPowerEstimate estimate,
        bool hasDirectRate,
        bool hasAdapterClass,
        ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        var parts = new List<string>();
        parts.AddRange(raw.Evidence.Where(item => !string.IsNullOrWhiteSpace(item)));

        if (hasDirectRate)
        {
            parts.Add("Direct battery charge/discharge wattage exposed by Windows WMI.");
        }

        if (hasAdapterClass)
        {
            parts.Add("Adapter wattage class is based on direct OS/vendor telemetry.");
        }

        if (estimate.PercentPerHour.HasValue)
        {
            parts.Add($"Battery sample estimate from {estimate.SampleCount.ToString(CultureInfo.InvariantCulture)} samples over {FormatDuration(estimate.SampleWindow)}.");
        }

        var elevatedEvidence = BuildElevatedTelemetryEvidence(elevatedTelemetry);
        if (!string.IsNullOrWhiteSpace(elevatedEvidence))
        {
            parts.Add(elevatedEvidence);
        }

        return parts.Count == 0
            ? "Only basic Windows power status was available."
            : string.Join(" ", parts.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string BuildMissingTelemetryReason(
        PortPowerRawTelemetry raw,
        bool hasAdapterClass,
        bool hasDirectRate,
        ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        var elevatedReason = BuildElevatedAvailabilityReason(elevatedTelemetry);
        if (!raw.HasBattery)
        {
            return AppendReason(
                "No internal battery detected. Port power telemetry is limited on desktops.",
                elevatedReason);
        }

        if (!raw.VoltageVolts.HasValue && !raw.CurrentAmps.HasValue)
        {
            return AppendReason(
                elevatedReason,
                "This device does not expose per-port USB-C power telemetry. ForgerEMS can still estimate charging from battery behavior.");
        }

        if (!hasAdapterClass && !hasDirectRate)
        {
            return "Exact adapter wattage and effective charge wattage are not exposed by this device.";
        }

        if (!raw.VoltageVolts.HasValue || !raw.CurrentAmps.HasValue)
        {
            return "Voltage/current is only shown for fields directly exposed by Windows, firmware, USB-C/PD controllers, or vendor tooling.";
        }

        return string.Empty;
    }

    private static string BuildNoPowerDataReason(ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        var elevatedReason = BuildElevatedAvailabilityReason(elevatedTelemetry);
        return AppendReason(
            string.IsNullOrWhiteSpace(elevatedReason)
                ? "Windows did not expose battery or power status to user-mode APIs."
                : elevatedReason,
            "Windows did not expose battery or power status to user-mode APIs.");
    }

    private static string BuildNoPowerDataEvidence(ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        var elevatedEvidence = BuildElevatedTelemetryEvidence(elevatedTelemetry);
        return AppendReason(
            "No user-mode battery, AC, USB-C, or direct electrical telemetry was available.",
            elevatedEvidence);
    }

    private static string BuildElevatedAvailabilityReason(ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        if (elevatedTelemetry is null || elevatedTelemetry.IsMissing)
        {
            return ElevatedScanTelemetrySnapshot.RunElevatedScanPrompt;
        }

        if (elevatedTelemetry.State == ElevatedScanTelemetryState.Running)
        {
            return elevatedTelemetry.MissingTelemetryReason;
        }

        if (elevatedTelemetry.IsFailure)
        {
            return elevatedTelemetry.MissingTelemetryReason;
        }

        if (elevatedTelemetry.IsStale)
        {
            return elevatedTelemetry.MissingTelemetryReason;
        }

        if (elevatedTelemetry.PortPower?.HasDirectElectricalTelemetry != true)
        {
            if (!string.IsNullOrWhiteSpace(elevatedTelemetry.PortPower?.MissingTelemetryReason))
            {
                return elevatedTelemetry.PortPower!.MissingTelemetryReason;
            }

            return string.IsNullOrWhiteSpace(elevatedTelemetry.MissingTelemetryReason) ||
                   string.Equals(
                       elevatedTelemetry.MissingTelemetryReason,
                       ElevatedScanTelemetrySnapshot.RunElevatedScanPrompt,
                       StringComparison.Ordinal)
                ? "Elevated Scan completed, but this device did not expose deeper port or charging telemetry."
                : elevatedTelemetry.MissingTelemetryReason;
        }

        return string.Empty;
    }

    private static string BuildElevatedTelemetryEvidence(ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        if (elevatedTelemetry is null || elevatedTelemetry.IsMissing)
        {
            return "No cached Elevated Scan telemetry was available.";
        }

        if (elevatedTelemetry.State == ElevatedScanTelemetryState.Running)
        {
            return "Elevated Scan is running; cached elevated telemetry is not available yet.";
        }

        if (elevatedTelemetry.IsFailure)
        {
            return $"Cached Elevated Scan telemetry is unavailable ({elevatedTelemetry.UserMessage}).";
        }

        if (elevatedTelemetry.IsStale)
        {
            return $"Cached Elevated Scan telemetry is stale/expired (source: {elevatedTelemetry.Source}; confidence {elevatedTelemetry.Confidence}).";
        }

        return $"Cached Elevated Scan telemetry is available from {elevatedTelemetry.Source} with {elevatedTelemetry.Confidence} confidence.";
    }

    private static IEnumerable<string> BuildElevatedTelemetryWarnings(ElevatedScanTelemetrySnapshot? elevatedTelemetry)
    {
        if (elevatedTelemetry is null || elevatedTelemetry.IsMissing)
        {
            yield return ElevatedScanTelemetrySnapshot.RunElevatedScanPrompt;
            yield break;
        }

        if (elevatedTelemetry.State == ElevatedScanTelemetryState.Running || elevatedTelemetry.IsFailure)
        {
            yield return elevatedTelemetry.MissingTelemetryReason;
            yield break;
        }

        if (elevatedTelemetry.IsStale)
        {
            yield return elevatedTelemetry.MissingTelemetryReason;
        }
    }

    private static string AppendReason(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second.Trim();
        }

        if (string.IsNullOrWhiteSpace(second) ||
            first.Contains(second, StringComparison.OrdinalIgnoreCase))
        {
            return first.Trim();
        }

        return first.TrimEnd('.', ' ') + ". " + second.Trim();
    }

    internal static string FormatDuration(TimeSpan duration)
    {
        duration = duration.Duration();

        if (duration.TotalHours >= 1d)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        }

        return $"{Math.Max(1, (int)Math.Round(duration.TotalMinutes, MidpointRounding.AwayFromZero))}m";
    }
}

internal static class PortPowerEstimator
{
    private static readonly TimeSpan MinimumMediumConfidenceWindow = TimeSpan.FromMinutes(10);

    public static PortPowerEstimate BuildEstimate(
        IReadOnlyList<PortPowerSample> samples,
        double? currentPercent,
        bool isCharging)
    {
        if (samples.Count < 2)
        {
            return new PortPowerEstimate
            {
                SampleCount = samples.Count
            };
        }

        var ordered = samples.OrderBy(item => item.CollectedAtUtc).ToArray();
        var first = ordered[0];
        var last = ordered[^1];
        var percentPerHour = CalculatePercentPerHour(first, last);
        var window = last.CollectedAtUtc - first.CollectedAtUtc;
        var timeToFull = isCharging && currentPercent.HasValue && percentPerHour.HasValue && percentPerHour.Value > 0d
            ? CalculateTimeToFull(currentPercent.Value, percentPerHour.Value)
            : null;

        return new PortPowerEstimate
        {
            PercentPerHour = percentPerHour,
            EstimatedTimeToFull = timeToFull,
            IsBasedOnShortWindow = percentPerHour.HasValue && window < MinimumMediumConfidenceWindow,
            SampleWindow = window,
            SampleCount = ordered.Length
        };
    }

    public static double? CalculatePercentPerHour(PortPowerSample first, PortPowerSample last)
    {
        var elapsed = last.CollectedAtUtc - first.CollectedAtUtc;
        if (elapsed.TotalSeconds <= 0d)
        {
            return null;
        }

        var delta = last.BatteryPercent - first.BatteryPercent;
        return delta / elapsed.TotalHours;
    }

    public static TimeSpan? CalculateTimeToFull(double batteryPercent, double percentPerHour)
    {
        if (batteryPercent >= 100d || percentPerHour <= 0d)
        {
            return null;
        }

        var hours = (100d - Math.Max(0d, batteryPercent)) / percentPerHour;
        if (double.IsNaN(hours) || double.IsInfinity(hours) || hours <= 0d)
        {
            return null;
        }

        return TimeSpan.FromHours(hours);
    }

    public static PortPowerTelemetryConfidence SelectConfidence(
        PortPowerRawTelemetry raw,
        PortPowerEstimate estimate,
        bool hasDirectRate,
        bool hasAdapterClass)
    {
        if (!raw.HasAnyPowerData || (!raw.HasBattery && !raw.VoltageVolts.HasValue && !raw.CurrentAmps.HasValue))
        {
            return PortPowerTelemetryConfidence.Unavailable;
        }

        if (hasDirectRate || hasAdapterClass || raw.VoltageVolts.HasValue || raw.CurrentAmps.HasValue)
        {
            return PortPowerTelemetryConfidence.High;
        }

        if (estimate.PercentPerHour.HasValue &&
            estimate.SampleCount >= 3 &&
            estimate.SampleWindow >= MinimumMediumConfidenceWindow)
        {
            return PortPowerTelemetryConfidence.Medium;
        }

        return PortPowerTelemetryConfidence.Low;
    }

    public static string MapBatteryStatus(
        int? batteryStatusCode,
        string? batteryStatusText,
        bool? isCharging,
        bool? isPluggedIn,
        double? batteryPercent,
        bool hasBattery)
    {
        if (!hasBattery)
        {
            return "Unknown";
        }

        if (isCharging == true)
        {
            return "Charging";
        }

        var isFullPercent = batteryPercent.HasValue && batteryPercent.Value >= 100d;

        if (isFullPercent && isPluggedIn == true)
        {
            return "Full";
        }

        if (batteryStatusCode.HasValue)
        {
            return batteryStatusCode.Value switch
            {
                1 => "Discharging",
                2 => isFullPercent ? "Full" : "Not charging",
                3 => "Full",
                4 => "Discharging",
                5 => "Discharging",
                6 or 7 or 8 or 9 => "Charging",
                11 => isPluggedIn == true ? "Not charging" : "Discharging",
                _ => "Unknown"
            };
        }

        if (!string.IsNullOrWhiteSpace(batteryStatusText))
        {
            var text = batteryStatusText.Trim();
            if (text.Contains("charg", StringComparison.OrdinalIgnoreCase) &&
                !text.Contains("not", StringComparison.OrdinalIgnoreCase))
            {
                return "Charging";
            }

            if (text.Contains("discharg", StringComparison.OrdinalIgnoreCase))
            {
                return "Discharging";
            }

            if (text.Contains("full", StringComparison.OrdinalIgnoreCase))
            {
                return "Full";
            }
        }

        if (isPluggedIn == false)
        {
            return "Discharging";
        }

        if (isPluggedIn == true)
        {
            return isFullPercent ? "Full" : "Not charging";
        }

        return "Unknown";
    }

    public static PortPowerSourceKind DetermineSourceKind(
        bool isPluggedIn,
        bool hasBattery,
        IReadOnlyList<string> sourceHints)
    {
        if (!isPluggedIn && hasBattery)
        {
            return PortPowerSourceKind.Battery;
        }

        if (!isPluggedIn)
        {
            return PortPowerSourceKind.Unknown;
        }

        var joinedHints = string.Join(" ", sourceHints ?? Array.Empty<string>());
        if (ContainsAny(joinedHints, "dock", "docking station"))
        {
            return PortPowerSourceKind.Dock;
        }

        if (ContainsAny(joinedHints, "usb-c", "type-c", "usb4", "thunderbolt", "ucsi", "power delivery"))
        {
            return PortPowerSourceKind.UsbCPd;
        }

        return PortPowerSourceKind.AcAdapter;
    }

    public static int? ClassifyAdapterWattage(double? directAdapterWatts)
    {
        if (!directAdapterWatts.HasValue || directAdapterWatts.Value <= 0d)
        {
            return null;
        }

        var classes = new[] { 45, 65, 90, 100, 130 };
        var nearest = classes
            .Select(item => new { Watts = item, Delta = Math.Abs(item - directAdapterWatts.Value) })
            .OrderBy(item => item.Delta)
            .First();

        return nearest.Delta <= 15d ? nearest.Watts : null;
    }

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
}

public static class PortPowerTelemetryFormatter
{
    public static string FormatSummary(PortPowerSnapshot snapshot)
    {
        if (!snapshot.HasBattery)
        {
            return "No internal battery detected. Port power telemetry is limited on desktops.";
        }

        if (snapshot.IsCharging && snapshot.PercentPerHour is > 0d && snapshot.EstimatedTimeToFull.HasValue)
        {
            return $"Charging at estimated ~{snapshot.PercentPerHour.Value:0.#}%/hr. Estimated full in ~{PortPowerTelemetryService.FormatDuration(snapshot.EstimatedTimeToFull.Value)}.";
        }

        if (snapshot.IsCharging && snapshot.EffectiveChargeRateWatts is > 0d)
        {
            return $"Charging with direct battery telemetry at ~{snapshot.EffectiveChargeRateWatts.Value:0.#} W. Workload can still change the rate.";
        }

        if (snapshot.BatteryPercent.HasValue)
        {
            return $"{snapshot.BatteryStatus} at {snapshot.BatteryPercent.Value:0.#}%. {snapshot.MissingTelemetryReason}";
        }

        return snapshot.MissingTelemetryReason;
    }

    public static string FormatBatteryPercent(PortPowerSnapshot snapshot) =>
        snapshot.BatteryPercent.HasValue ? $"{snapshot.BatteryPercent.Value:0.#}%" : "Unknown";

    public static string FormatPowerSource(PortPowerSourceKind sourceKind) =>
        sourceKind switch
        {
            PortPowerSourceKind.AcAdapter => "AC adapter",
            PortPowerSourceKind.UsbCPd => "USB-C PD",
            PortPowerSourceKind.Dock => "Dock",
            PortPowerSourceKind.Battery => "Battery",
            _ => "Unknown"
        };

    public static string FormatAdapterClass(PortPowerSnapshot snapshot) =>
        snapshot.AdapterWattageClassWatts.HasValue
            ? $"{snapshot.AdapterWattageClassWatts.Value.ToString(CultureInfo.InvariantCulture)}W-class based on direct OS/vendor telemetry"
            : "Unknown";

    public static string FormatChargeRate(PortPowerSnapshot snapshot)
    {
        if (snapshot.EffectiveChargeRateWatts.HasValue)
        {
            var watts = Math.Abs(snapshot.EffectiveChargeRateWatts.Value);
            var direction = snapshot.EffectiveChargeRateWatts.Value < 0d ? "discharge" : "charge";
            return $"Direct {direction} rate ~{watts:0.#} W";
        }

        if (snapshot.PercentPerHour.HasValue)
        {
            return $"Estimated ~{snapshot.PercentPerHour.Value:0.#}%/hr";
        }

        return "Unavailable";
    }

    public static string FormatEstimatedFull(PortPowerSnapshot snapshot)
    {
        if (snapshot.EstimatedTimeToFull.HasValue)
        {
            return $"~{PortPowerTelemetryService.FormatDuration(snapshot.EstimatedTimeToFull.Value)}";
        }

        if (snapshot.IsCharging && snapshot.HasBattery)
        {
            return "Needs more charging samples";
        }

        return "Unavailable";
    }

    public static string FormatVoltageCurrent(PortPowerSnapshot snapshot)
    {
        if (snapshot.VoltageVolts.HasValue && snapshot.CurrentAmps.HasValue)
        {
            return $"{snapshot.VoltageVolts.Value:0.##} V / {snapshot.CurrentAmps.Value:0.##} A (direct telemetry)";
        }

        if (snapshot.VoltageVolts.HasValue)
        {
            return $"{snapshot.VoltageVolts.Value:0.##} V (direct telemetry; current not exposed)";
        }

        if (snapshot.CurrentAmps.HasValue)
        {
            return $"{snapshot.CurrentAmps.Value:0.##} A (direct telemetry; voltage not exposed)";
        }

        return "Unavailable - exact USB-C voltage/current is not exposed by this device.";
    }

    public static string FormatConfidence(PortPowerTelemetryConfidence confidence) =>
        confidence switch
        {
            PortPowerTelemetryConfidence.High => "High",
            PortPowerTelemetryConfidence.Medium => "Medium",
            PortPowerTelemetryConfidence.Low => "Low",
            _ => "Unavailable"
        };

    public static string FormatLastUpdated(DateTimeOffset collectedAtUtc) =>
        $"Updated {collectedAtUtc.ToLocalTime():g}";

    public static string FormatWarnings(PortPowerSnapshot snapshot) =>
        snapshot.Warnings.Count == 0 ? string.Empty : string.Join(Environment.NewLine, snapshot.Warnings);
}

internal sealed class WindowsPortPowerTelemetrySource : IPortPowerTelemetrySource
{
    // TODO: Future Deep Sensor Mode may use a signed, read-only driver/module for
    // supported EC/USB-C/PD controllers. This pass intentionally remains user-mode
    // and telemetry-only.
    public PortPowerRawTelemetry Read()
    {
        var builder = new RawPortPowerBuilder();
        builder.CollectedAtUtc = DateTimeOffset.UtcNow;

        TryReadSystemPowerStatus(builder);
        TryReadWin32Battery(builder);
        TryReadWin32PortableBattery(builder);
        TryReadPnpSourceHints(builder);

        return builder.Build();
    }

    private static void TryReadSystemPowerStatus(RawPortPowerBuilder builder)
    {
        try
        {
            if (!NativeMethods.GetSystemPowerStatus(out var status))
            {
                return;
            }

            builder.AddEvidence("Windows GetSystemPowerStatus exposed AC/battery state.");

            if (status.ACLineStatus == 0)
            {
                builder.IsPluggedIn = false;
            }
            else if (status.ACLineStatus == 1)
            {
                builder.IsPluggedIn = true;
            }

            if (status.BatteryLifePercent <= 100)
            {
                builder.BatteryPercent ??= status.BatteryLifePercent;
            }

            var noBattery = (status.BatteryFlag & 128) == 128;
            if (!noBattery)
            {
                builder.HasBattery = true;
            }

            if ((status.BatteryFlag & 8) == 8)
            {
                builder.IsCharging = true;
            }
            else if (!noBattery && status.ACLineStatus == 0)
            {
                builder.IsCharging = false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        {
            builder.AddWarning("Windows power status API was unavailable.");
        }
    }

    private static void TryReadWin32Battery(RawPortPowerBuilder builder)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery");
            foreach (ManagementObject battery in searcher.Get())
            {
                builder.HasBattery = true;
                builder.BatteryPercent ??= TryGetDouble(battery, "EstimatedChargeRemaining");
                builder.BatteryStatusCode ??= TryGetInt(battery, "BatteryStatus");
                builder.BatteryStatusText = FirstNonEmpty(builder.BatteryStatusText, TryGetString(battery, "Status"));
                builder.AddEvidence("Win32_Battery exposed battery status/percent.");
                break;
            }
        }
        catch (Exception ex) when (IsExpectedManagementFailure(ex))
        {
            builder.AddEvidence("Win32_Battery did not expose usable battery details.");
        }
    }

    private static void TryReadWin32PortableBattery(RawPortPowerBuilder builder)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PortableBattery");
            foreach (ManagementObject battery in searcher.Get())
            {
                builder.HasBattery = true;
                builder.BatteryPercent ??= TryGetDouble(battery, "EstimatedChargeRemaining");
                builder.BatteryStatusCode ??= TryGetInt(battery, "BatteryStatus");
                builder.BatteryStatusText = FirstNonEmpty(builder.BatteryStatusText, TryGetString(battery, "Status"));
                builder.AddEvidence("Win32_PortableBattery exposed battery status/percent.");
                break;
            }
        }
        catch (Exception ex) when (IsExpectedManagementFailure(ex))
        {
            builder.AddEvidence("Win32_PortableBattery did not expose usable battery details.");
        }
    }

    private static void TryReadPnpSourceHints(RawPortPowerBuilder builder)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name,Description FROM Win32_PnPEntity WHERE Name LIKE '%USB%' OR Name LIKE '%Thunderbolt%' OR Name LIKE '%UCSI%' OR Name LIKE '%Dock%'");
            foreach (ManagementObject device in searcher.Get())
            {
                var label = string.Join(" ", new[]
                {
                    TryGetString(device, "Name"),
                    TryGetString(device, "Description")
                }.Where(item => !string.IsNullOrWhiteSpace(item)));

                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (ContainsAny(label, "dock", "docking station", "usb-c", "type-c", "usb4", "thunderbolt", "ucsi", "power delivery"))
                {
                    builder.AddSourceHint(label);
                }

                if (builder.SourceHints.Count >= 8)
                {
                    break;
                }
            }

            if (builder.SourceHints.Count > 0)
            {
                builder.AddEvidence("PnP enumeration found USB-C/Thunderbolt/dock source hints.");
            }
        }
        catch (Exception ex) when (IsExpectedManagementFailure(ex))
        {
            builder.AddEvidence("PnP source hints were unavailable.");
        }
    }

    private static bool IsExpectedManagementFailure(Exception ex) =>
        ex is ManagementException or UnauthorizedAccessException or COMException or InvalidOperationException;

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string FirstNonEmpty(string current, string? candidate) =>
        string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(candidate) ? candidate.Trim() : current;

    private static int? TryGetInt(ManagementBaseObject obj, string propertyName)
    {
        var value = TryGetValue(obj, propertyName);
        if (value is null)
        {
            return null;
        }

        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static double? TryGetDouble(ManagementBaseObject obj, string propertyName)
    {
        var value = TryGetValue(obj, propertyName);
        if (value is null)
        {
            return null;
        }

        return double.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool? TryGetBool(ManagementBaseObject obj, string propertyName)
    {
        var value = TryGetValue(obj, propertyName);
        if (value is null)
        {
            return null;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
            ? parsed
            : null;
    }

    private static string TryGetString(ManagementBaseObject obj, string propertyName) =>
        Convert.ToString(TryGetValue(obj, propertyName), CultureInfo.InvariantCulture) ?? string.Empty;

    private static object? TryGetValue(ManagementBaseObject obj, string propertyName)
    {
        try
        {
            return obj.Properties[propertyName]?.Value;
        }
        catch (ManagementException)
        {
            return null;
        }
    }

    private sealed class RawPortPowerBuilder
    {
        public DateTimeOffset CollectedAtUtc { get; set; }

        public bool HasBattery { get; set; }

        public double? BatteryPercent { get; set; }

        public int? BatteryStatusCode { get; set; }

        public string BatteryStatusText { get; set; } = string.Empty;

        public bool? IsCharging { get; set; }

        public bool? IsPluggedIn { get; set; }

        public double? DirectEffectiveChargeRateWatts { get; set; }

        public double? DirectAdapterWattageWatts { get; set; }

        public double? VoltageVolts { get; set; }

        public double? CurrentAmps { get; set; }

        public List<string> Evidence { get; } = new();

        public List<string> SourceHints { get; } = new();

        public List<string> Warnings { get; } = new();

        public void AddEvidence(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Evidence.Add(value);
            }
        }

        public void AddSourceHint(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                SourceHints.Add(value);
            }
        }

        public void AddWarning(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                Warnings.Add(value);
            }
        }

        public PortPowerRawTelemetry Build() =>
            new()
            {
                CollectedAtUtc = CollectedAtUtc,
                HasBattery = HasBattery,
                BatteryPercent = BatteryPercent,
                BatteryStatusCode = BatteryStatusCode,
                BatteryStatusText = BatteryStatusText,
                IsCharging = IsCharging,
                IsPluggedIn = IsPluggedIn,
                DirectEffectiveChargeRateWatts = DirectEffectiveChargeRateWatts,
                DirectAdapterWattageWatts = DirectAdapterWattageWatts,
                VoltageVolts = VoltageVolts,
                CurrentAmps = CurrentAmps,
                Evidence = Evidence.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                SourceHints = SourceHints.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Warnings = Warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
    }

    private static class NativeMethods
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }
}
