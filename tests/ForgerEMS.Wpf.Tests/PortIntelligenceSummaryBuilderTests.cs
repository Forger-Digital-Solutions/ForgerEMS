using System;
using System.Globalization;
using System.IO;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class PortIntelligenceSummaryBuilderTests
{
    [Fact]
    public void ChargingRateWithConfidence_AppearsWhenAvailable()
    {
        var summary = PortIntelligenceSummaryBuilder.Build(
            TypicalUsbState(),
            ChargingPower(percentPerHour: 18, confidence: PortPowerTelemetryConfidence.Medium),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("Estimated charge rate", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("+18%/hour", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Medium confidence", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Estimated full", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LowConfidenceSample_DoesNotOverpromiseFullTime()
    {
        var summary = PortIntelligenceSummaryBuilder.Build(
            TypicalUsbState(),
            ChargingPower(percentPerHour: 24, confidence: PortPowerTelemetryConfidence.Low),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("warming up", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Low confidence", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Estimated full", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownAdapterWattage_StaysUnknownWithoutDirectTelemetry()
    {
        var summary = PortIntelligenceSummaryBuilder.Build(
            TypicalUsbState(),
            ChargingPower(percentPerHour: 18, confidence: PortPowerTelemetryConfidence.Medium),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("Adapter wattage: Unknown", summary.PowerSourceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("65W", summary.PowerSourceSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbCDockSourceClassification_SaysHintBasedWhenInferredFromPnpHints()
    {
        var power = ChargingPower(
            percentPerHour: 18,
            confidence: PortPowerTelemetryConfidence.Medium,
            sourceKind: PortPowerSourceKind.Dock);

        var summary = PortIntelligenceSummaryBuilder.Build(TypicalUsbState(), power, ElevatedScanTelemetrySnapshot.Missing(), Target());

        Assert.Contains("hint-based/inferred", summary.PowerSourceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not USB-C PD negotiation", summary.PowerSourceSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingVoltageCurrent_CopyIsUnavailableAndTruthful()
    {
        var summary = PortIntelligenceSummaryBuilder.Build(
            TypicalUsbState(),
            ChargingPower(percentPerHour: 18, confidence: PortPowerTelemetryConfidence.Medium),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("Exact USB-C/PD voltage/current is unavailable", summary.PowerSourceSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Exact voltage/current unavailable", summary.BottlenecksSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("inline USB-C power meter", summary.RecommendedFixesSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortIntelligenceSummary_IncludesUsbMappingAndPortPowerData()
    {
        var summary = PortIntelligenceSummaryBuilder.Build(
            TypicalUsbState() with { MappingLabelDisplay = "Current port: Left USB-C", BenchmarkReadWriteDisplay = "420 MB/s read | 410 MB/s write" },
            ChargingPower(percentPerHour: 18, confidence: PortPowerTelemetryConfidence.Medium),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("Current port: Left USB-C", summary.PortMapSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("420 MB/s", summary.PortMapSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("+18%/hour", summary.ChargingSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HubPathWarning_ProducesRecommendedDirectPortFix()
    {
        var usb = TypicalUsbState() with
        {
            ConfidenceReasonDisplay = "Device is behind a hub path."
        };

        var summary = PortIntelligenceSummaryBuilder.Build(
            usb,
            ChargingPower(percentPerHour: 18, confidence: PortPowerTelemetryConfidence.Medium),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("behind a hub", summary.BottlenecksSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Plug directly into the laptop", summary.RecommendedFixesSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("powered hub", summary.RecommendedFixesSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SlowerThanExpectedMapping_ProducesCableHubGuidance()
    {
        var usb = TypicalUsbState() with
        {
            DetectedClassDisplay = "Bottleneck",
            BenchmarkReadWriteDisplay = "USB 2 speed fallback detected; slower than expected."
        };

        var summary = PortIntelligenceSummaryBuilder.Build(
            usb,
            ChargingPower(percentPerHour: 18, confidence: PortPowerTelemetryConfidence.Medium),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("cable, hub, port, or device limited", summary.BottlenecksSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Try a different cable", summary.RecommendedFixesSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USB-C/Thunderbolt", summary.RecommendedFixesSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CachedElevatedScanTimestamp_AppearsWhenAvailable()
    {
        var now = At("2026-05-31T12:03:00Z");
        var elevated = new ElevatedScanTelemetrySnapshot
        {
            State = ElevatedScanTelemetryState.Fresh,
            CollectedAtUtc = At("2026-05-31T12:00:00Z"),
            Source = "System Intelligence Elevated Scan",
            Confidence = PortPowerTelemetryConfidence.High
        };

        var summary = PortIntelligenceSummaryBuilder.Build(TypicalUsbState(), null, elevated, Target(), now);

        Assert.Contains("Deep scan data collected 3 minutes ago", summary.DeepScanSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cached elevated inventory", summary.DeepScanSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("System Intelligence Elevated Scan", summary.DeepScanSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingElevatedScanState_IsHonest()
    {
        var summary = PortIntelligenceSummaryBuilder.Build(TypicalUsbState(), null, ElevatedScanTelemetrySnapshot.Missing(), Target());

        Assert.Contains("Run Elevated Scan to include permission-limited controller, hub, and driver details", summary.DeepScanSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletePartialElevatedScan_RemovesRunPromptButKeepsLimitationHonest()
    {
        var elevated = new ElevatedScanTelemetrySnapshot
        {
            State = ElevatedScanTelemetryState.CompletePartial,
            CollectedAtUtc = At("2026-05-31T12:00:00Z"),
            Source = "System Intelligence Elevated Scan",
            ParseQuality = ElevatedScanParseQuality.Partial,
            UserMessage = "Elevated scan complete — some deep telemetry was unavailable on this device.",
            Confidence = PortPowerTelemetryConfidence.Unavailable
        };

        var summary = PortIntelligenceSummaryBuilder.Build(TypicalUsbState(), null, elevated, Target(), At("2026-05-31T12:01:00Z"));

        Assert.Equal("Elevated scan complete — some deep telemetry was unavailable on this device.", summary.DeepScanSummary);
        Assert.DoesNotContain("Run Elevated Scan", summary.DeepScanSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailedElevatedScan_UsesActionableFailureCopyNotGreen()
    {
        var elevated = new ElevatedScanTelemetrySnapshot
        {
            State = ElevatedScanTelemetryState.Failed,
            UserMessage = "Elevated scan failed",
            MissingTelemetryReason = "ForgerEMS stayed open. Check logs or retry as administrator.",
            ParseQuality = ElevatedScanParseQuality.Failed,
            Severity = ElevatedScanSeverity.Error
        };

        var summary = PortIntelligenceSummaryBuilder.Build(TypicalUsbState(), null, elevated, Target());

        Assert.Contains("Elevated scan failed", summary.DeepScanSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerEMS stayed open", summary.DeepScanSummary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cached elevated inventory", summary.DeepScanSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LimitationsCopy_CoversTruthAndWorkloadCaveats()
    {
        var summary = PortIntelligenceSummaryBuilder.Build(
            TypicalUsbState(),
            ChargingPower(percentPerHour: 18, confidence: PortPowerTelemetryConfidence.Medium),
            ElevatedScanTelemetrySnapshot.Missing(),
            Target());

        Assert.Contains("Exact USB-C/PD voltage/current is shown only when Windows or vendor telemetry exposes it", summary.TelemetryLimitationsSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("phone/accessory battery percentage", summary.TelemetryLimitationsSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Percent/hour and time-to-full are session estimates", summary.TelemetryLimitationsSummary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workload", summary.TelemetryLimitationsSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainWindow_UsesUnifiedPortIntelligenceCardWithoutDisconnectedChargingHeader()
    {
        var xaml = File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("Header=\"Port Intelligence\"", xaml, StringComparison.Ordinal);
        Assert.Contains("PortIntelligencePortMapSummaryText", xaml, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceChargingSummaryText", xaml, StringComparison.Ordinal);
        Assert.Contains("PortIntelligencePowerSourceSummaryText", xaml, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceBottlenecksText", xaml, StringComparison.Ordinal);
        Assert.Contains("PortIntelligenceRecommendedFixesText", xaml, StringComparison.Ordinal);
        Assert.Contains("UsbBuilderPortPowerDetailsCard", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Charging Intelligence\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("UsbBuilderChargingIntelligenceCard", xaml, StringComparison.Ordinal);
    }

    private static UsbIntelligencePanelUiState TypicalUsbState() =>
        new()
        {
            BuilderSummaryLine = "[Good] Quality: Good Risk: Low | Speed class: USB 3 | Ready - Device looks usable.",
            DetectedClassDisplay = "USB 3",
            BenchmarkReadWriteDisplay = "No successful benchmark yet.",
            RecommendationQualityDisplay = "Good",
            ConfidenceScoreDisplay = "72%",
            ConfidenceReasonDisplay = "safe Windows topology",
            MappingLabelDisplay = "Current port: Needs verification",
            BestKnownPortSummary = "Left USB-C"
        };

    private static PortPowerSnapshot ChargingPower(
        double percentPerHour,
        PortPowerTelemetryConfidence confidence,
        PortPowerSourceKind sourceKind = PortPowerSourceKind.UsbCPd) =>
        new()
        {
            CollectedAtUtc = At("2026-05-31T12:00:00Z"),
            BatteryPercent = 72,
            BatteryStatus = "Charging",
            IsCharging = true,
            IsPluggedIn = true,
            HasBattery = true,
            PowerSourceKind = sourceKind,
            PercentPerHour = percentPerHour,
            EstimatedTimeToFull = TimeSpan.FromMinutes(93),
            TelemetryConfidence = confidence,
            RateIsEstimatedFromBatterySamples = true,
            MissingTelemetryReason =
                "Exact USB-C voltage/current is not exposed by this device. Estimate is based on battery percentage change during this app session.",
            Warnings = ["Workload may affect charge rate."]
        };

    private static UsbTargetInfo Target() =>
        new()
        {
            DriveLetter = "E:",
            RootPath = "E:\\",
            Label = "FORGER",
            BusType = "USB",
            IsLikelyUsb = true,
            IsRemovableMedia = true
        };

    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static string FindRepoFile(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(dir))
        {
            var candidate = Path.Combine(new[] { dir }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
        }

        throw new InvalidOperationException("Could not locate repo file: " + string.Join(Path.DirectorySeparatorChar, parts));
    }
}
