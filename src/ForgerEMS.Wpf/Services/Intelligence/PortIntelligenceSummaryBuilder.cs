using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed record PortIntelligenceUiSummary
{
    public string Overview { get; init; } =
        "Select a USB target and refresh telemetry to build Port Intelligence.";

    public string PortMapSummary { get; init; } =
        "Port map unavailable until USB Intelligence has a saved report.";

    public string ChargingSummary { get; init; } =
        "Charging telemetry has not been refreshed yet.";

    public string PowerSourceSummary { get; init; } =
        "Power source unknown. Adapter wattage: Unknown.";

    public string BottlenecksSummary { get; init; } =
        "No obvious cable, hub, port, power, or device bottleneck has been identified yet.";

    public string RecommendedFixesSummary { get; init; } =
        "Map the current port, run a USB benchmark, and refresh charging telemetry before making hardware changes.";

    public string DeepScanSummary { get; init; } = ElevatedScanTelemetrySnapshot.RunElevatedScanPrompt;

    public string TelemetryLimitationsSummary { get; init; } =
        PortIntelligenceSummaryBuilder.DefaultTelemetryLimitations;
}

public static class PortIntelligenceSummaryBuilder
{
    public const string DefaultTelemetryLimitations =
        "Exact USB-C/PD voltage/current is shown only when Windows or vendor telemetry exposes it. Most systems do not expose raw USB-C PD negotiation details or phone/accessory battery percentage to normal apps. Percent/hour and time-to-full are session estimates; short samples are Low confidence, and workload, brightness, CPU/GPU load, dock load, and battery health can change the rate.";

    public static PortIntelligenceUiSummary Build(
        UsbIntelligencePanelUiState? usb,
        PortPowerSnapshot? power,
        ElevatedScanTelemetrySnapshot? elevated,
        UsbTargetInfo? selectedTarget,
        DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var bottlenecks = BuildBottlenecks(usb, power);
        var fixes = BuildRecommendedFixes(usb, power, bottlenecks);

        return new PortIntelligenceUiSummary
        {
            Overview = BuildOverview(usb, power, selectedTarget),
            PortMapSummary = BuildPortMapSummary(usb, selectedTarget),
            ChargingSummary = BuildChargingSummary(power),
            PowerSourceSummary = BuildPowerSourceSummary(power),
            BottlenecksSummary = FormatLines(
                bottlenecks,
                "No obvious cable, hub, port, power, or device bottleneck has been identified yet."),
            RecommendedFixesSummary = FormatLines(
                fixes,
                "Map the current port, run a USB benchmark, and refresh charging telemetry before making hardware changes."),
            DeepScanSummary = BuildDeepScanSummary(elevated, now),
            TelemetryLimitationsSummary = DefaultTelemetryLimitations
        };
    }

    private static string BuildOverview(
        UsbIntelligencePanelUiState? usb,
        PortPowerSnapshot? power,
        UsbTargetInfo? selectedTarget)
    {
        var target = selectedTarget is null
            ? "No USB target selected"
            : FormatTarget(selectedTarget);
        var speed = SafeValue(usb?.DetectedClassDisplay, UsbIntelligencePanelUiCopy.NotMeasuredClass);
        var powerStatus = power is null
            ? "charging telemetry not refreshed"
            : $"{SafeValue(power.BatteryStatus, "Unknown")} | {PortPowerTelemetryFormatter.FormatConfidence(power.TelemetryConfidence)} confidence";
        return $"{target} | {speed} | {powerStatus}";
    }

    private static string BuildPortMapSummary(UsbIntelligencePanelUiState? usb, UsbTargetInfo? selectedTarget)
    {
        if (usb is null)
        {
            return selectedTarget is null
                ? "Select a USB target to map its port, hub, and controller path."
                : "USB Intelligence report unavailable. Open USB Mapping Wizard or refresh intelligence to map the selected target.";
        }

        var parts = new List<string>
        {
            $"Target: {SafeValue(usb.BuilderSummaryLine, SafeValue(selectedTarget?.LabelDisplay, "selected USB device"))}",
            $"Port: {SafeValue(usb.MappingLabelDisplay, UsbIntelligencePanelUiCopy.NoPortLabelYet)}",
            $"Speed: {SafeValue(usb.BenchmarkReadWriteDisplay, UsbIntelligencePanelUiCopy.NoBenchmarkYet)}"
        };

        if (!string.IsNullOrWhiteSpace(usb.BestKnownPortSummary))
        {
            parts.Add($"Best known: {usb.BestKnownPortSummary.Trim()}");
        }

        return string.Join(" | ", parts);
    }

    private static string BuildChargingSummary(PortPowerSnapshot? power)
    {
        if (power is null)
        {
            return "Charging telemetry has not been refreshed yet.";
        }

        if (!power.HasBattery)
        {
            return "No internal battery detected. Port power telemetry is limited on desktops.";
        }

        var confidence = PortPowerTelemetryFormatter.FormatConfidence(power.TelemetryConfidence);
        if (power.IsCharging && power.EffectiveChargeRateWatts is > 0d)
        {
            return $"Direct charge rate: ~{power.EffectiveChargeRateWatts.Value.ToString("0.#", CultureInfo.InvariantCulture)} W | {confidence} confidence";
        }

        if (power.IsCharging &&
            power.PercentPerHour is > 0d &&
            power.TelemetryConfidence >= PortPowerTelemetryConfidence.Medium)
        {
            var full = power.EstimatedTimeToFull.HasValue
                ? $" | Estimated full in ~{PortPowerTelemetryService.FormatDuration(power.EstimatedTimeToFull.Value)}"
                : string.Empty;
            return $"Estimated charge rate: +{power.PercentPerHour.Value.ToString("0.#", CultureInfo.InvariantCulture)}%/hour{full} | {confidence} confidence";
        }

        if (power.IsCharging && power.PercentPerHour.HasValue)
        {
            return $"Charging estimate is still warming up. Current estimated rate is +{power.PercentPerHour.Value.ToString("0.#", CultureInfo.InvariantCulture)}%/hour with {confidence} confidence.";
        }

        if (power.IsCharging)
        {
            return $"Charging, but ForgerEMS needs a longer battery sample window before estimating time-to-full | {confidence} confidence";
        }

        return $"{power.BatteryStatus} | {confidence} confidence";
    }

    private static string BuildPowerSourceSummary(PortPowerSnapshot? power)
    {
        if (power is null)
        {
            return "Power source unknown. Adapter wattage: Unknown.";
        }

        var source = PortPowerTelemetryFormatter.FormatPowerSource(power.PowerSourceKind);
        var adapter = power.AdapterWattageClassWatts.HasValue
            ? $"{power.AdapterWattageClassWatts.Value.ToString(CultureInfo.InvariantCulture)}W-class based on direct OS/vendor telemetry"
            : "Unknown";
        var qualifier = power.PowerSourceKind is PortPowerSourceKind.Dock or PortPowerSourceKind.UsbCPd &&
                        !power.HasDirectElectricalTelemetry
            ? " Source classification is hint-based/inferred from safe Windows PnP data, not USB-C PD negotiation."
            : string.Empty;

        if (power.VoltageVolts.HasValue || power.CurrentAmps.HasValue)
        {
            return $"Power source: {source}. Adapter wattage: {adapter}. Port voltage/current: {PortPowerTelemetryFormatter.FormatVoltageCurrent(power)}.{qualifier}";
        }

        return $"Power source: {source}. Adapter wattage: {adapter}. Exact USB-C/PD voltage/current is unavailable unless Windows or vendor telemetry exposes it.{qualifier}";
    }

    private static string[] BuildBottlenecks(UsbIntelligencePanelUiState? usb, PortPowerSnapshot? power)
    {
        var items = new List<string>();
        var usbText = CombineUsbText(usb);
        if (ContainsAny(usbText, "behind a hub", " hub ", "hub path", "through hub"))
        {
            items.Add("Device is behind a hub; transfer speed or charging behavior may be limited by the hub path.");
        }

        if (ContainsAny(usbText, "bottleneck", "usb 2", "slower", "speed fallback", "cache suspected", "unverified current port"))
        {
            items.Add("This device may be cable, hub, port, or device limited. Current connection appears slower or less certain than expected.");
        }

        if (power is not null)
        {
            if (power.IsCharging && power.TelemetryConfidence == PortPowerTelemetryConfidence.Low)
            {
                items.Add("Charging estimate is Low confidence because the battery sample window is still short.");
            }

            if (!power.VoltageVolts.HasValue && !power.CurrentAmps.HasValue)
            {
                items.Add("Exact voltage/current unavailable from safe OS or vendor telemetry.");
            }

            if (power.PowerSourceKind is PortPowerSourceKind.Dock or PortPowerSourceKind.UsbCPd &&
                !power.HasDirectElectricalTelemetry)
            {
                items.Add("Dock/USB-C source is inferred from safe Windows hints only, not direct PD negotiation telemetry.");
            }

            foreach (var warning in power.Warnings.Where(item => !string.IsNullOrWhiteSpace(item)))
            {
                items.Add(warning.Trim());
            }
        }

        return items.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] BuildRecommendedFixes(
        UsbIntelligencePanelUiState? usb,
        PortPowerSnapshot? power,
        IReadOnlyList<string> bottlenecks)
    {
        var fixes = new List<string>();
        var usbText = CombineUsbText(usb);
        var bottleneckText = string.Join(" ", bottlenecks);

        if (ContainsAny(usbText, "behind a hub", " hub ", "hub path", "through hub") ||
            ContainsAny(bottleneckText, "hub"))
        {
            fixes.Add("Plug directly into the laptop instead of a hub; use a powered hub for bus-powered drives.");
        }

        if (ContainsAny(usbText, "bottleneck", "usb 2", "slower", "speed fallback") ||
            ContainsAny(bottleneckText, "cable", "slower"))
        {
            fixes.Add("Try a different cable and use a USB-C/Thunderbolt port for high-speed storage.");
        }

        if (power is not null &&
            power.IsCharging &&
            power.TelemetryConfidence <= PortPowerTelemetryConfidence.Low)
        {
            fixes.Add("Leave ForgerEMS open longer before trusting estimated time-to-full.");
        }

        if (power is null || (!power.VoltageVolts.HasValue && !power.CurrentAmps.HasValue))
        {
            fixes.Add("Use an inline USB-C power meter for exact volts/amps.");
        }

        return fixes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string BuildDeepScanSummary(ElevatedScanTelemetrySnapshot? elevated, DateTimeOffset nowUtc)
    {
        if (elevated is null || elevated.IsMissing)
        {
            return "Run Elevated Scan to include permission-limited controller, hub, and driver details.";
        }

        if (elevated.IsFresh)
        {
            var age = FormatAge(elevated.CollectedAtUtc, nowUtc);
            return $"Deep scan data collected {age}. Port Intelligence is using cached elevated inventory where available. Source: {elevated.Source}; confidence {elevated.Confidence}.";
        }

        var staleAge = FormatAge(elevated.CollectedAtUtc, nowUtc);
        return $"Deep scan data is stale/expired; last collected {staleAge}. Run Elevated Scan to refresh permission-limited controller, hub, and driver details.";
    }

    private static string FormatAge(DateTimeOffset? collectedAtUtc, DateTimeOffset nowUtc)
    {
        if (!collectedAtUtc.HasValue)
        {
            return "at an unknown time";
        }

        var age = nowUtc - collectedAtUtc.Value;
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }

        if (age < TimeSpan.FromMinutes(1))
        {
            return "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            var minutes = Math.Max(1, (int)Math.Round(age.TotalMinutes));
            return $"{minutes.ToString(CultureInfo.InvariantCulture)} minute{(minutes == 1 ? string.Empty : "s")} ago";
        }

        if (age < TimeSpan.FromDays(1))
        {
            var hours = Math.Max(1, (int)Math.Round(age.TotalHours));
            return $"{hours.ToString(CultureInfo.InvariantCulture)} hour{(hours == 1 ? string.Empty : "s")} ago";
        }

        return collectedAtUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    private static string CombineUsbText(UsbIntelligencePanelUiState? usb)
    {
        if (usb is null)
        {
            return string.Empty;
        }

        return string.Join(
            " ",
            usb.BuilderSummaryLine,
            usb.DetectedClassDisplay,
            usb.BenchmarkReadWriteDisplay,
            usb.RecommendationQualityDisplay,
            usb.ConfidenceReasonDisplay,
            usb.MappingLabelDisplay,
            usb.BestKnownPortSummary,
            usb.RunBenchmarkRecommendedLine);
    }

    private static string FormatTarget(UsbTargetInfo target)
    {
        var drive = SafeTrim(target.DriveLetter).TrimEnd('\\');
        return string.IsNullOrWhiteSpace(drive)
            ? target.LabelDisplay
            : $"{drive} | {target.LabelDisplay}";
    }

    private static string SafeValue(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "\u2014", StringComparison.Ordinal)
            ? fallback
            : value.Trim();

    private static string SafeTrim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static bool ContainsAny(string value, params string[] tokens) =>
        tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string FormatLines(IReadOnlyList<string> lines, string fallback) =>
        lines.Count == 0 ? fallback : string.Join(Environment.NewLine, lines);
}
