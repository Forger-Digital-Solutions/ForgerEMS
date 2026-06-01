using System;
using System.Collections.Generic;
using System.Globalization;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class PortPowerTelemetryServiceTests
{
    [Fact]
    public void PercentPerHour_CalculatesFromBatterySamples()
    {
        var first = new PortPowerSample { CollectedAtUtc = At("2026-05-31T12:00:00Z"), BatteryPercent = 50 };
        var last = new PortPowerSample { CollectedAtUtc = first.CollectedAtUtc.AddMinutes(30), BatteryPercent = 60 };

        var rate = PortPowerEstimator.CalculatePercentPerHour(first, last);

        Assert.Equal(20d, rate);
    }

    [Fact]
    public void TimeToFull_CalculatesWhenChargingRateIsPositive()
    {
        var estimate = PortPowerEstimator.CalculateTimeToFull(75, 25);

        Assert.Equal(TimeSpan.FromHours(1), estimate);
    }

    [Fact]
    public void ConfidenceSelection_UsesUnavailableWhenNoPowerDataExists()
    {
        var service = new PortPowerTelemetryService(new SequenceSource(
            new PortPowerRawTelemetry { CollectedAtUtc = At("2026-05-31T12:00:00Z") }));

        var snapshot = service.CollectSnapshot();

        Assert.Equal(PortPowerTelemetryConfidence.Unavailable, snapshot.TelemetryConfidence);
        Assert.Contains("did not expose", snapshot.MissingTelemetryReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingVoltageCurrent_DoesNotProduceFakeNumbers()
    {
        var service = new PortPowerTelemetryService(new SequenceSource(
            RawBattery(At("2026-05-31T12:00:00Z"), 72, isCharging: true)));

        var snapshot = service.CollectSnapshot();

        Assert.Null(snapshot.VoltageVolts);
        Assert.Null(snapshot.CurrentAmps);
        Assert.Contains("Unavailable", PortPowerTelemetryFormatter.FormatVoltageCurrent(snapshot), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not exposed", PortPowerTelemetryFormatter.FormatVoltageCurrent(snapshot), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DesktopNoBatteryState_IsLimitedAndUnavailable()
    {
        var service = new PortPowerTelemetryService(new SequenceSource(
            new PortPowerRawTelemetry
            {
                CollectedAtUtc = At("2026-05-31T12:00:00Z"),
                HasBattery = false,
                IsPluggedIn = true,
                Evidence = ["Windows GetSystemPowerStatus exposed AC state."]
            }));

        var snapshot = service.CollectSnapshot();

        Assert.False(snapshot.HasBattery);
        Assert.Equal(PortPowerTelemetryConfidence.Unavailable, snapshot.TelemetryConfidence);
        Assert.Equal(PortPowerSourceKind.AcAdapter, snapshot.PowerSourceKind);
        Assert.Contains("No internal battery detected", snapshot.MissingTelemetryReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("limited on desktops", PortPowerTelemetryFormatter.FormatSummary(snapshot), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1, false, false, 50, "Discharging")]
    [InlineData(2, false, true, 80, "Not charging")]
    [InlineData(3, false, true, 100, "Full")]
    [InlineData(6, true, true, 40, "Charging")]
    [InlineData(11, false, false, 60, "Discharging")]
    public void ChargingStatusMapping_MapsWmiAndWindowsValues(
        int statusCode,
        bool isCharging,
        bool isPluggedIn,
        double percent,
        string expected)
    {
        var status = PortPowerEstimator.MapBatteryStatus(
            statusCode,
            string.Empty,
            isCharging,
            isPluggedIn,
            percent,
            hasBattery: true);

        Assert.Equal(expected, status);
    }

    [Fact]
    public void ShortSampleWindow_StaysLowConfidence()
    {
        var t0 = At("2026-05-31T12:00:00Z");
        var service = new PortPowerTelemetryService(new SequenceSource(
            RawBattery(t0, 50, isCharging: true),
            RawBattery(t0.AddMinutes(5), 52, isCharging: true)));

        service.CollectSnapshot();
        var snapshot = service.CollectSnapshot();

        Assert.NotNull(snapshot.PercentPerHour);
        Assert.Equal(PortPowerTelemetryConfidence.Low, snapshot.TelemetryConfidence);
        Assert.Contains("Short sample window", string.Join("\n", snapshot.Warnings), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleStableSamples_ReachMediumConfidence()
    {
        var t0 = At("2026-05-31T12:00:00Z");
        var service = new PortPowerTelemetryService(new SequenceSource(
            RawBattery(t0, 50, isCharging: true),
            RawBattery(t0.AddMinutes(10), 54, isCharging: true),
            RawBattery(t0.AddMinutes(25), 60, isCharging: true)));

        service.CollectSnapshot();
        service.CollectSnapshot();
        var snapshot = service.CollectSnapshot();

        Assert.Equal(PortPowerTelemetryConfidence.Medium, snapshot.TelemetryConfidence);
        Assert.True(snapshot.PercentPerHour > 0);
        Assert.NotNull(snapshot.EstimatedTimeToFull);
    }

    [Fact]
    public void DirectWattVoltageCurrentData_GivesHighConfidence()
    {
        var service = new PortPowerTelemetryService(new SequenceSource(
            RawBattery(
                At("2026-05-31T12:00:00Z"),
                66,
                isCharging: true,
                directRateWatts: 42,
                adapterWatts: 64,
                voltageVolts: 20,
                currentAmps: 2.1)));

        var snapshot = service.CollectSnapshot();

        Assert.Equal(PortPowerTelemetryConfidence.High, snapshot.TelemetryConfidence);
        Assert.Equal(65, snapshot.AdapterWattageClassWatts);
        Assert.Equal(42, snapshot.EffectiveChargeRateWatts);
        Assert.Equal(20, snapshot.VoltageVolts);
        Assert.Equal(2.1, snapshot.CurrentAmps);
    }

    [Fact]
    public void Formatter_UsesEstimatedCopyForInferredRates()
    {
        var t0 = At("2026-05-31T12:00:00Z");
        var service = new PortPowerTelemetryService(new SequenceSource(
            RawBattery(t0, 50, isCharging: true),
            RawBattery(t0.AddMinutes(30), 61, isCharging: true)));

        service.CollectSnapshot();
        var snapshot = service.CollectSnapshot();

        Assert.True(snapshot.RateIsEstimatedFromBatterySamples);
        Assert.Contains("Estimated", PortPowerTelemetryFormatter.FormatChargeRate(snapshot), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Estimated full", PortPowerTelemetryFormatter.FormatSummary(snapshot), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatter_SaysUnavailableWhenPerPortTelemetryIsMissing()
    {
        var service = new PortPowerTelemetryService(new SequenceSource(
            RawBattery(At("2026-05-31T12:00:00Z"), 88, isCharging: false)));

        var snapshot = service.CollectSnapshot();

        Assert.Contains("per-port USB-C power telemetry", snapshot.MissingTelemetryReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unavailable", PortPowerTelemetryFormatter.FormatVoltageCurrent(snapshot), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PowerSource_UsesDockAndUsbCPdHintsWithoutClaimingExactTelemetry()
    {
        Assert.Equal(
            PortPowerSourceKind.Dock,
            PortPowerEstimator.DetermineSourceKind(true, true, ["Thunderbolt Dock"]));
        Assert.Equal(
            PortPowerSourceKind.UsbCPd,
            PortPowerEstimator.DetermineSourceKind(true, true, ["USB4 UCSI Type-C controller"]));
    }

    private static PortPowerRawTelemetry RawBattery(
        DateTimeOffset at,
        double percent,
        bool isCharging,
        double? directRateWatts = null,
        double? adapterWatts = null,
        double? voltageVolts = null,
        double? currentAmps = null) =>
        new()
        {
            CollectedAtUtc = at,
            HasBattery = true,
            BatteryPercent = percent,
            BatteryStatusCode = isCharging ? 6 : 1,
            IsCharging = isCharging,
            IsPluggedIn = isCharging,
            DirectEffectiveChargeRateWatts = directRateWatts,
            DirectAdapterWattageWatts = adapterWatts,
            VoltageVolts = voltageVolts,
            CurrentAmps = currentAmps,
            Evidence = ["Fake test source exposed battery percent."]
        };

    private static DateTimeOffset At(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private sealed class SequenceSource : IPortPowerTelemetrySource
    {
        private readonly Queue<PortPowerRawTelemetry> _items;

        public SequenceSource(params PortPowerRawTelemetry[] items)
        {
            _items = new Queue<PortPowerRawTelemetry>(items);
        }

        public PortPowerRawTelemetry Read()
        {
            if (_items.Count > 1)
            {
                return _items.Dequeue();
            }

            return _items.Peek();
        }
    }
}
