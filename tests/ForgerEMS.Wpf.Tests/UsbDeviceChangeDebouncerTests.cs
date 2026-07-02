using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Unit tests for the USB plug/unplug debouncer (Part B of the v1.2.3-preview.1
/// follow-up pass). The debouncer is the testable core of the event-driven USB
/// detection; the Win32 hook is a thin marshalling layer on top.
/// </summary>
public sealed class UsbDeviceChangeDebouncerTests
{
    [Fact]
    public void DeviceArrival_FlushesExactlyOnce()
    {
        var schedule = new ManualSchedule();
        var flushes = new List<UsbDeviceChangeReason>();
        using var debouncer = new UsbDeviceChangeDebouncer(
            reason => flushes.Add(reason),
            TimeSpan.FromMilliseconds(1200),
            schedule.CreateTimer);

        debouncer.Notify(UsbDeviceChangeReason.Arrival);
        Assert.Empty(flushes);

        schedule.FireNext();
        Assert.Single(flushes);
        Assert.Equal(UsbDeviceChangeReason.Arrival, flushes[0]);
    }

    [Fact]
    public void DeviceRemoval_FlushesExactlyOnce()
    {
        var schedule = new ManualSchedule();
        var flushes = new List<UsbDeviceChangeReason>();
        using var debouncer = new UsbDeviceChangeDebouncer(
            reason => flushes.Add(reason),
            TimeSpan.FromMilliseconds(1200),
            schedule.CreateTimer);

        debouncer.Notify(UsbDeviceChangeReason.Removal);
        schedule.FireNext();

        Assert.Single(flushes);
        Assert.Equal(UsbDeviceChangeReason.Removal, flushes[0]);
    }

    [Fact]
    public void EventBurst_CollapsesToSingleFlush()
    {
        var schedule = new ManualSchedule();
        var flushes = new List<UsbDeviceChangeReason>();
        using var debouncer = new UsbDeviceChangeDebouncer(
            reason => flushes.Add(reason),
            TimeSpan.FromMilliseconds(1200),
            schedule.CreateTimer);

        // Simulate Windows broadcasting six device events for one physical USB
        // arrival (this happens in practice on multi-partition drives).
        for (var i = 0; i < 6; i++)
        {
            debouncer.Notify(UsbDeviceChangeReason.Arrival);
        }

        // Only the latest timer should still be live.
        Assert.Equal(1, schedule.LivePendingCount);
        schedule.FireNext();

        Assert.Single(flushes);
    }

    [Fact]
    public void MixedBurst_PrefersArrivalReason()
    {
        var schedule = new ManualSchedule();
        var flushes = new List<UsbDeviceChangeReason>();
        using var debouncer = new UsbDeviceChangeDebouncer(
            reason => flushes.Add(reason),
            TimeSpan.FromMilliseconds(1200),
            schedule.CreateTimer);

        debouncer.Notify(UsbDeviceChangeReason.Removal);
        debouncer.Notify(UsbDeviceChangeReason.Arrival);
        schedule.FireNext();

        Assert.Single(flushes);
        Assert.Equal(UsbDeviceChangeReason.Arrival, flushes[0]);
    }

    [Fact]
    public void Dispose_CancelsPendingFlush()
    {
        var schedule = new ManualSchedule();
        var flushes = new List<UsbDeviceChangeReason>();
        var debouncer = new UsbDeviceChangeDebouncer(
            reason => flushes.Add(reason),
            TimeSpan.FromMilliseconds(1200),
            schedule.CreateTimer);

        debouncer.Notify(UsbDeviceChangeReason.Arrival);
        debouncer.Dispose();
        schedule.FireNext();

        Assert.Empty(flushes);
    }

    [Fact]
    public void DebounceInterval_TooShort_Rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new UsbDeviceChangeDebouncer(
                _ => { },
                TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void ShellDebounceInterval_KeepsHotplugNearInstant()
    {
        // Spec (v1.2.3-preview.1 hotplug pass): the DBT_DEVTYP_VOLUME arrival
        // broadcast only fires after Windows has mounted the volume, so the
        // shell debounce just needs to collapse multi-partition bursts. It must
        // stay >= the 50ms debouncer floor and <= 400ms so a plugged USB shows
        // up in the app within roughly one second including enumeration.
        var interval = VentoyToolkitSetup.Wpf.ViewModels.MainViewModel.UsbDeviceChangeDebounceInterval;

        Assert.True(interval >= TimeSpan.FromMilliseconds(50), "debounce below the burst-collapse floor");
        Assert.True(interval <= TimeSpan.FromMilliseconds(400), "debounce too slow for near-instant hotplug UX");

        using var debouncer = new UsbDeviceChangeDebouncer(_ => { }, interval);
        Assert.Equal(interval, debouncer.DebounceInterval);
    }

    [Fact]
    public void MainViewModelSource_ReusesSignatureScanForEventDrivenRefresh()
    {
        // The debounced handler enumerates once for the change-signature check and
        // must hand that result to RefreshUsbTargetsAsync instead of scanning twice.
        var source = System.IO.File.ReadAllText(FindRepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("await RefreshUsbTargetsAsync(detectionResult)", source, StringComparison.Ordinal);
        Assert.Contains("UsbDetectionResult? prefetchedDetectionResult = null", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var current = new System.IO.DirectoryInfo(System.IO.Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = System.IO.Path.Combine(new[] { current.FullName }.Concat(segments).ToArray());
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        throw new System.IO.FileNotFoundException("Could not locate repo file.", System.IO.Path.Combine(segments));
    }

    /// <summary>
    /// Test-only scheduler that captures the latest pending action without
    /// actually waiting on a timer. The debouncer always disposes the previous
    /// timer before scheduling the next, so only one entry is "live" at a
    /// time; older entries flip to cancelled and are skipped when FireNext
    /// runs.
    /// </summary>
    private sealed class ManualSchedule
    {
        private readonly List<PendingItem> _pending = new();

        public int LivePendingCount
        {
            get
            {
                var live = 0;
                foreach (var item in _pending)
                {
                    if (!item.Cancelled)
                    {
                        live++;
                    }
                }

                return live;
            }
        }

        public IDisposable CreateTimer(Action action)
        {
            var item = new PendingItem(action);
            _pending.Add(item);
            return item;
        }

        public void FireNext()
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                var item = _pending[i];
                if (item.Cancelled)
                {
                    continue;
                }

                item.Cancel();
                item.Action();
                return;
            }
        }

        private sealed class PendingItem : IDisposable
        {
            public PendingItem(Action action)
            {
                Action = action;
            }

            public Action Action { get; }

            public bool Cancelled { get; private set; }

            public void Cancel()
            {
                Cancelled = true;
            }

            public void Dispose()
            {
                Cancel();
            }
        }
    }
}
