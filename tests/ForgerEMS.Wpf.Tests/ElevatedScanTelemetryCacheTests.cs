using System;
using System.Globalization;
using System.IO;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ElevatedScanTelemetryCacheTests
{
    [Fact]
    public void GetLatest_ReadsFreshElevatedPortPowerTelemetryFromSystemReport()
    {
        var now = At("2026-05-31T12:00:00Z");
        var reports = CreateReportsDirectory();
        try
        {
            WriteElevatedMarker(reports, now.AddMinutes(-5));
            File.WriteAllText(
                Path.Combine(reports, "system-intelligence-latest.json"),
                """
                {
                  "generatedUtc": "2026-05-31T11:55:00Z",
                  "scanMode": "Elevated",
                  "portPowerTelemetry": {
                    "collectedAtUtc": "2026-05-31T11:55:00Z",
                    "source": "System Intelligence Elevated Scan",
                    "confidence": "High",
                    "effectiveChargeRateWatts": 42.5,
                    "voltageVolts": 20.0,
                    "currentAmps": 2.1,
                    "sourceHints": ["USB4 UCSI Type-C controller"],
                    "evidence": ["root\\wmi BatteryStatus exposed read-only battery charge telemetry."],
                    "missingTelemetryReason": ""
                  }
                }
                """);
            var cache = new ElevatedScanTelemetryCache(() => now, TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.Fresh, snapshot.State);
            Assert.Equal(ElevatedScanParseQuality.Complete, snapshot.ParseQuality);
            Assert.Equal("Elevated scan complete", snapshot.UserMessage);
            Assert.Equal(PortPowerTelemetryConfidence.High, snapshot.Confidence);
            Assert.NotNull(snapshot.PortPower);
            Assert.Equal(42.5, snapshot.PortPower!.EffectiveChargeRateWatts);
            Assert.Equal(20.0, snapshot.PortPower.VoltageVolts);
            Assert.Contains("USB4", snapshot.UsbThunderboltDockSummary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    [Fact]
    public void GetLatest_MarksFreshElevatedReportPartialWhenDeepTelemetryUnavailable()
    {
        var now = At("2026-05-31T12:00:00Z");
        var reports = CreateReportsDirectory();
        try
        {
            WriteElevatedMarker(reports, now.AddMinutes(-2));
            File.WriteAllText(
                Path.Combine(reports, "system-intelligence-latest.json"),
                """
                {
                  "generatedUtc": "2026-05-31T11:58:00Z",
                  "scanMode": "Elevated",
                  "portPowerTelemetry": {
                    "collectedAtUtc": "2026-05-31T11:58:00Z",
                    "source": "System Intelligence Elevated Scan",
                    "confidence": "Unavailable",
                    "missingTelemetryReason": "Elevated Scan completed, but this device did not expose deeper port or charging telemetry."
                  }
                }
                """);
            var cache = new ElevatedScanTelemetryCache(() => now, TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.CompletePartial, snapshot.State);
            Assert.Equal(ElevatedScanParseQuality.Partial, snapshot.ParseQuality);
            Assert.Equal("Elevated scan complete — some permission-limited detail was unavailable on this device.", snapshot.UserMessage);
            Assert.Equal(ElevatedScanSeverity.Success, snapshot.Severity);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    [Fact]
    public void GetLatest_MarksElevatedTelemetryStaleWhenExpired()
    {
        var now = At("2026-05-31T12:00:00Z");
        var reports = CreateReportsDirectory();
        try
        {
            WriteElevatedMarker(reports, now.AddHours(-3));
            File.WriteAllText(
                Path.Combine(reports, "system-intelligence-latest.json"),
                """
                {
                  "generatedUtc": "2026-05-31T09:00:00Z",
                  "scanMode": "Elevated",
                  "portPowerTelemetry": {
                    "collectedAtUtc": "2026-05-31T09:00:00Z",
                    "source": "System Intelligence Elevated Scan",
                    "confidence": "High",
                    "voltageVolts": 20.0
                  }
                }
                """);
            var cache = new ElevatedScanTelemetryCache(() => now, TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.Stale, snapshot.State);
            Assert.Contains("stale/expired", snapshot.MissingTelemetryReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    [Fact]
    public void GetLatest_DoesNotMarkCompleteWhenReportIsNotElevated()
    {
        var now = At("2026-05-31T12:00:00Z");
        var reports = CreateReportsDirectory();
        try
        {
            WriteElevatedMarker(reports, now.AddMinutes(-1));
            File.WriteAllText(
                Path.Combine(reports, "system-intelligence-latest.json"),
                """
                {
                  "generatedUtc": "2026-05-31T11:59:00Z",
                  "scanMode": "Standard"
                }
                """);
            var cache = new ElevatedScanTelemetryCache(() => now, TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.Failed, snapshot.State);
            Assert.Equal(ElevatedScanParseQuality.Failed, snapshot.ParseQuality);
            Assert.Equal("Elevated scan failed", snapshot.UserMessage);
            Assert.NotEqual(ElevatedScanSeverity.Success, snapshot.Severity);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    [Fact]
    public void GetLatest_ParseFailureDoesNotFallBackToGreen()
    {
        var now = At("2026-05-31T12:00:00Z");
        var reports = CreateReportsDirectory();
        try
        {
            WriteElevatedMarker(reports, now.AddMinutes(-1));
            File.WriteAllText(Path.Combine(reports, "system-intelligence-latest.json"), "{ bad json");
            var cache = new ElevatedScanTelemetryCache(() => now, TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.Failed, snapshot.State);
            Assert.Equal(ElevatedScanParseQuality.Failed, snapshot.ParseQuality);
            Assert.Contains("parsing failed", snapshot.MissingTelemetryReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    [Fact]
    public void GetLatest_NewerErrorMarkerSupersedesOldSuccess()
    {
        var now = At("2026-05-31T12:00:00Z");
        var reports = CreateReportsDirectory();
        try
        {
            WriteElevatedMarker(reports, now.AddMinutes(-20));
            File.WriteAllText(
                Path.Combine(reports, "system-intelligence-latest.json"),
                """
                {
                  "generatedUtc": "2026-05-31T11:40:00Z",
                  "scanMode": "Elevated",
                  "portPowerTelemetry": {
                    "collectedAtUtc": "2026-05-31T11:40:00Z",
                    "source": "System Intelligence Elevated Scan",
                    "confidence": "High",
                    "voltageVolts": 20.0
                  }
                }
                """);
            WriteElevatedError(reports, now.AddMinutes(-1), "UacCancelled");
            var cache = new ElevatedScanTelemetryCache(() => now, TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.Cancelled, snapshot.State);
            Assert.Equal(ElevatedScanSeverity.Warning, snapshot.Severity);
            Assert.Equal("Elevated scan cancelled", snapshot.UserMessage);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    [Fact]
    public void GetLatest_NewerPendingMarkerSupersedesOldSuccess()
    {
        var now = At("2026-05-31T12:00:00Z");
        var reports = CreateReportsDirectory();
        try
        {
            WriteElevatedMarker(reports, now.AddMinutes(-20));
            File.WriteAllText(
                Path.Combine(reports, "system-intelligence-latest.json"),
                """
                {
                  "generatedUtc": "2026-05-31T11:40:00Z",
                  "scanMode": "Elevated",
                  "portPowerTelemetry": {
                    "collectedAtUtc": "2026-05-31T11:40:00Z",
                    "source": "System Intelligence Elevated Scan",
                    "confidence": "High",
                    "voltageVolts": 20.0
                  }
                }
                """);
            WriteElevatedHeartbeat(reports, now.AddSeconds(-10));
            var cache = new ElevatedScanTelemetryCache(() => now, TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.Running, snapshot.State);
            Assert.Equal("Elevated scan running", snapshot.UserMessage);
            Assert.NotEqual(ElevatedScanSeverity.Success, snapshot.Severity);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    [Fact]
    public void GetLatest_ReturnsUnlockPromptWhenNoElevatedScanHasRun()
    {
        var reports = CreateReportsDirectory();
        try
        {
            var cache = new ElevatedScanTelemetryCache(() => At("2026-05-31T12:00:00Z"), TimeSpan.FromHours(1));

            var snapshot = cache.GetLatest(reports);

            Assert.Equal(ElevatedScanTelemetryState.Missing, snapshot.State);
            Assert.Equal(ElevatedScanTelemetrySnapshot.RunElevatedScanPrompt, snapshot.MissingTelemetryReason);
        }
        finally
        {
            Directory.Delete(reports, true);
        }
    }

    private static string CreateReportsDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forgerems-elevated-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteElevatedMarker(string reports, DateTimeOffset utc) =>
        File.WriteAllText(
            Path.Combine(reports, "elevated-scan-result.json"),
            $$"""
            {
              "kind": "elevated-scan-result",
              "utc": "{{utc.ToString("o", CultureInfo.InvariantCulture)}}",
              "ok": true,
              "json": "system-intelligence-latest.json"
            }
            """);

    private static void WriteElevatedHeartbeat(string reports, DateTimeOffset utc) =>
        File.WriteAllText(
            Path.Combine(reports, "elevated-scan-heartbeat.json"),
            $$"""
            {
              "kind": "elevated-scan-heartbeat",
              "utc": "{{utc.ToString("o", CultureInfo.InvariantCulture)}}"
            }
            """);

    private static void WriteElevatedError(string reports, DateTimeOffset utc, string failureKind) =>
        File.WriteAllText(
            Path.Combine(reports, "elevated-scan-error.json"),
            $$"""
            {
              "kind": "elevated-scan-error",
              "utc": "{{utc.ToString("o", CultureInfo.InvariantCulture)}}",
              "failureKind": "{{failureKind}}",
              "advanced": "test"
            }
            """);

    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
