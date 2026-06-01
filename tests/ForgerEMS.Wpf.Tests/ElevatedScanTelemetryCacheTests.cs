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

    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
