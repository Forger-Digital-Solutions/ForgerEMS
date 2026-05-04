using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.Models;

/// <summary>Terminal state of a USB sequential benchmark run (UI + diagnostics).</summary>
public enum UsbBenchmarkResultKind
{
    NotStarted = 0,
    Running = 1,
    Completed = 2,
    CancelledByUser = 3,
    BlockedBySafety = 4,
    TargetChanged = 5,
    DeviceRemoved = 6,
    IoFailed = 7,
    ValidationFailed = 8,
    UnknownFailed = 9,
    CancelledByHost = 10
}

/// <summary>Why cooperative cancellation fired (distinct from <see cref="UsbBenchmarkResultKind"/>).</summary>
public enum UsbBenchmarkCancellationSource
{
    None = 0,
    UserRequested = 1,
    DuplicateStartIgnored = 2,
    DeviceRemoved = 3,
    TargetSelectionChanged = 4,
    AppShutdown = 5,
    SafetyRevalidationBlocked = 6,
    IoException = 7,
    Timeout = 8,
    OperationCanceledUnknown = 9
}

/// <summary>Locked USB volume identity for a single benchmark run.</summary>
public sealed class UsbTargetIdentitySnapshot
{
    public string DriveLetter { get; init; } = string.Empty;

    public string VolumeLabel { get; init; } = string.Empty;

    public long TotalBytes { get; init; }

    public long FreeBytes { get; init; }

    /// <summary>Stable hash of non-secret topology fields (not a serial number).</summary>
    public string TopologyFingerprint { get; init; } = string.Empty;

    public static UsbTargetIdentitySnapshot Capture(UsbTargetInfo target)
    {
        var letter = (target.DriveLetter ?? string.Empty).TrimEnd('\\').TrimEnd(':').ToUpperInvariant();
        var fp = ComputeFingerprint(target);
        return new UsbTargetIdentitySnapshot
        {
            DriveLetter = letter,
            VolumeLabel = target.Label ?? string.Empty,
            TotalBytes = target.TotalBytes,
            FreeBytes = target.FreeBytes,
            TopologyFingerprint = fp
        };
    }

    /// <summary>Returns false when the same drive letter points at different underlying media.</summary>
    public bool MatchesVolumeIdentity(UsbTargetInfo? live, out string reason)
    {
        reason = string.Empty;
        if (live is null)
        {
            reason = "Target volume no longer enumerated.";
            return false;
        }

        if (!string.Equals(
                (live.DriveLetter ?? string.Empty).TrimEnd('\\').TrimEnd(':').ToUpperInvariant(),
                DriveLetter,
                StringComparison.Ordinal))
        {
            reason = "Drive letter mapping changed.";
            return false;
        }

        if (live.TotalBytes != TotalBytes)
        {
            reason = "Reported capacity changed.";
            return false;
        }

        var liveFp = ComputeFingerprint(live);
        if (!string.Equals(liveFp, TopologyFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            reason = "Underlying disk/volume identity changed.";
            return false;
        }

        return true;
    }

    private static string ComputeFingerprint(UsbTargetInfo t)
    {
        var raw = string.Join(
            '|',
            t.TotalBytes.ToString(CultureInfo.InvariantCulture),
            t.FreeBytes.ToString(CultureInfo.InvariantCulture),
            t.DeviceModel ?? string.Empty,
            t.DeviceBrand ?? string.Empty,
            t.BusType ?? string.Empty,
            t.ClassificationDetails ?? string.Empty,
            t.FileSystem ?? string.Empty);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }
}

public static class UsbBenchmarkUiMessages
{
    public static string BuildUiSummary(UsbBenchmarkResultKind kind, double readMbps, double writeMbps, string? safeDetail = null)
    {
        return kind switch
        {
            UsbBenchmarkResultKind.Completed =>
                FormattableString.Invariant(
                    $"Benchmark completed: Read {readMbps:0.0} MB/s, Write {writeMbps:0.0} MB/s."),
            UsbBenchmarkResultKind.CancelledByUser => "Benchmark cancelled by user.",
            UsbBenchmarkResultKind.TargetChanged => "Benchmark stopped because the selected USB target changed.",
            UsbBenchmarkResultKind.DeviceRemoved => "Benchmark stopped because the USB drive was removed.",
            UsbBenchmarkResultKind.BlockedBySafety =>
                "Benchmark blocked: target is not safe for USB Builder operations.",
            UsbBenchmarkResultKind.ValidationFailed =>
                string.IsNullOrWhiteSpace(safeDetail)
                    ? "Benchmark failed: validation did not pass."
                    : $"Benchmark failed: {safeDetail}",
            UsbBenchmarkResultKind.IoFailed =>
                string.IsNullOrWhiteSpace(safeDetail)
                    ? "Benchmark failed: storage I/O error."
                    : $"Benchmark failed: {safeDetail}",
            UsbBenchmarkResultKind.UnknownFailed =>
                string.IsNullOrWhiteSpace(safeDetail)
                    ? "Benchmark failed: unknown error."
                    : $"Benchmark failed: {safeDetail}",
            UsbBenchmarkResultKind.CancelledByHost => "Benchmark stopped because the application closed.",
            UsbBenchmarkResultKind.Running => "Benchmark running…",
            UsbBenchmarkResultKind.NotStarted => "Benchmark not started.",
            _ => "Benchmark did not complete."
        };
    }

    public static UsbBenchmarkResultKind MapNativeEndKind(UsbNativeBenchmarkEndKind endKind, bool succeeded)
    {
        if (succeeded)
        {
            return UsbBenchmarkResultKind.Completed;
        }

        return endKind switch
        {
            UsbNativeBenchmarkEndKind.ValidationBlocked => UsbBenchmarkResultKind.BlockedBySafety,
            UsbNativeBenchmarkEndKind.OperationCanceled => UsbBenchmarkResultKind.CancelledByUser,
            UsbNativeBenchmarkEndKind.IoOrSystemError => UsbBenchmarkResultKind.IoFailed,
            _ => UsbBenchmarkResultKind.UnknownFailed
        };
    }
}
