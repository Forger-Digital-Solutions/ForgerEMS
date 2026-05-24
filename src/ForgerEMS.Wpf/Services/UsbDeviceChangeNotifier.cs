using System;
using System.Threading;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Reason for a debounced USB device-change refresh notification.
/// </summary>
public enum UsbDeviceChangeReason
{
    Arrival,
    Removal,
}

/// <summary>
/// Pure debouncer for USB plug/unplug events. Separated from any Win32 plumbing
/// so it can be exercised in unit tests without a window handle.
///
/// Multiple events that fire within <see cref="DebounceInterval"/> collapse into
/// a single notification carrying the most "interesting" reason (arrival wins
/// over removal in mixed bursts because plug-in is what makes a USB selectable).
/// </summary>
public sealed class UsbDeviceChangeDebouncer : IDisposable
{
    private readonly object _gate = new();
    private readonly Action<UsbDeviceChangeReason> _onFlush;
    private readonly Func<Action, IDisposable> _scheduleFactory;
    private IDisposable? _pendingTimer;
    private bool _hasPending;
    private UsbDeviceChangeReason _pendingReason;
    private int _disposed;

    public UsbDeviceChangeDebouncer(
        Action<UsbDeviceChangeReason> onFlush,
        TimeSpan debounceInterval,
        Func<Action, IDisposable>? scheduleFactory = null)
    {
        if (debounceInterval < TimeSpan.FromMilliseconds(50))
        {
            throw new ArgumentOutOfRangeException(
                nameof(debounceInterval),
                "Debounce interval must be at least 50ms — anything tighter undermines burst collapsing.");
        }

        _onFlush = onFlush ?? throw new ArgumentNullException(nameof(onFlush));
        DebounceInterval = debounceInterval;
        _scheduleFactory = scheduleFactory ?? CreateDefaultThreadingTimerFactory(debounceInterval);
    }

    public TimeSpan DebounceInterval { get; }

    /// <summary>
    /// Called by the Win32 hook (or tests). The first event in a burst starts
    /// the debounce window; subsequent events extend it. On expiry the most
    /// "interesting" reason wins.
    /// </summary>
    public void Notify(UsbDeviceChangeReason reason)
    {
        if (_disposed != 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_hasPending)
            {
                // Arrival is the louder signal — once a USB is plugged in we
                // care about the target list refresh whether or not a removal
                // also fired in the same burst.
                if (reason == UsbDeviceChangeReason.Arrival)
                {
                    _pendingReason = UsbDeviceChangeReason.Arrival;
                }
            }
            else
            {
                _hasPending = true;
                _pendingReason = reason;
            }

            _pendingTimer?.Dispose();
            _pendingTimer = _scheduleFactory(Flush);
        }
    }

    private void Flush()
    {
        UsbDeviceChangeReason reasonToRaise;
        lock (_gate)
        {
            if (!_hasPending)
            {
                return;
            }

            reasonToRaise = _pendingReason;
            _hasPending = false;
            _pendingTimer?.Dispose();
            _pendingTimer = null;
        }

        try
        {
            _onFlush(reasonToRaise);
        }
        catch
        {
            // Notifier consumers are responsible for their own error handling;
            // we never let a downstream exception break future debouncing.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        lock (_gate)
        {
            _pendingTimer?.Dispose();
            _pendingTimer = null;
            _hasPending = false;
        }
    }

    private static Func<Action, IDisposable> CreateDefaultThreadingTimerFactory(TimeSpan delay)
    {
        return action =>
        {
            // System.Threading.Timer fires on a thread-pool thread; the
            // downstream onFlush is responsible for marshalling onto the UI
            // thread when it actually touches WPF state.
            var timer = new Timer(static state => ((Action)state!)(), action, delay, Timeout.InfiniteTimeSpan);
            return timer;
        };
    }
}
