using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Hooks WM_DEVICECHANGE on a WPF window so we can react to USB plug/unplug
/// events without polling. Volume arrival/removal is forwarded to a
/// <see cref="UsbDeviceChangeDebouncer"/> which collapses event bursts into a
/// single refresh request.
///
/// We listen for DBT_DEVICEARRIVAL (0x8000) and DBT_DEVICEREMOVECOMPLETE
/// (0x8004) and filter on DBT_DEVTYP_VOLUME (0x0002) so we only fire for real
/// volume mounts — not battery/power/sound device noise.
/// </summary>
public sealed class UsbDeviceChangeWindowHook : IDisposable
{
    // ReSharper disable InconsistentNaming
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_VOLUME = 0x0002;
    // ReSharper restore InconsistentNaming

    private readonly UsbDeviceChangeDebouncer _debouncer;
    private HwndSource? _source;
    private HwndSourceHook? _hookDelegate;
    private Window? _deferredWindow;
    private bool _attached;
    private bool _disposed;

    public UsbDeviceChangeWindowHook(UsbDeviceChangeDebouncer debouncer)
    {
        _debouncer = debouncer ?? throw new ArgumentNullException(nameof(debouncer));
    }

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_disposed || _attached)
        {
            return;
        }

        var helper = new WindowInteropHelper(window);
        if (helper.Handle == IntPtr.Zero)
        {
            // The window hasn't been shown yet — defer hook installation until
            // its handle is realized. We mark _attached so a second Attach
            // call cannot stack a second SourceInitialized subscription.
            _attached = true;
            _deferredWindow = window;
            window.SourceInitialized += OnSourceInitialized;
            return;
        }

        _attached = true;
        InstallHook(helper.Handle);
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (sender is not Window window)
        {
            return;
        }

        window.SourceInitialized -= OnSourceInitialized;
        _deferredWindow = null;
        if (_disposed)
        {
            return;
        }

        var helper = new WindowInteropHelper(window);
        if (helper.Handle != IntPtr.Zero)
        {
            InstallHook(helper.Handle);
        }
    }

    private void InstallHook(IntPtr hwnd)
    {
        if (_disposed)
        {
            return;
        }

        _source = HwndSource.FromHwnd(hwnd);
        if (_source is null)
        {
            return;
        }

        _hookDelegate = WndProc;
        _source.AddHook(_hookDelegate);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_DEVICECHANGE)
        {
            return IntPtr.Zero;
        }

        var wParamCode = wParam.ToInt32();
        if (wParamCode != DBT_DEVICEARRIVAL && wParamCode != DBT_DEVICEREMOVECOMPLETE)
        {
            return IntPtr.Zero;
        }

        if (lParam == IntPtr.Zero || !IsVolumeBroadcast(lParam))
        {
            return IntPtr.Zero;
        }

        var reason = wParamCode == DBT_DEVICEARRIVAL
            ? UsbDeviceChangeReason.Arrival
            : UsbDeviceChangeReason.Removal;
        _debouncer.Notify(reason);
        return IntPtr.Zero;
    }

    private static bool IsVolumeBroadcast(IntPtr lParam)
    {
        try
        {
            var header = Marshal.PtrToStructure<DevBroadcastHdr>(lParam);
            return header.DeviceType == DBT_DEVTYP_VOLUME;
        }
        catch
        {
            // If we can't read the header for any reason, assume non-volume and
            // skip — false negatives are safe (the user can still hit the
            // manual Refresh USB Targets button).
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_deferredWindow is not null)
        {
            try
            {
                _deferredWindow.SourceInitialized -= OnSourceInitialized;
            }
            catch
            {
                // Window may already be torn down.
            }

            _deferredWindow = null;
        }

        if (_source is not null && _hookDelegate is not null)
        {
            try
            {
                _source.RemoveHook(_hookDelegate);
            }
            catch
            {
                // Defensive: HwndSource may already be torn down with the
                // window.
            }
        }

        _source = null;
        _hookDelegate = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevBroadcastHdr
    {
        public int Size;
        public int DeviceType;
        public int Reserved;
    }
}
