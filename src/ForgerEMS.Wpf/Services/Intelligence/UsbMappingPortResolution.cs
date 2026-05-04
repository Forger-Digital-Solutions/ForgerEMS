using System;
using System.Collections.Generic;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum UsbPortMappingMatchKind
{
    None = 0,
    StableCorrelationPortChange = 1,
    SameDriveLetterPortChange = 2,
    VolumeIdentityPortChange = 3,

    /// <summary>Volume identity matches but WMI correlation key drifted between snapshots.</summary>
    ReEnumeratedSameVolume = 4
}

public sealed class UsbPortMappingResolution
{
    public bool Success { get; init; }

    public UsbPortMappingMatchKind MatchKind { get; init; }

    public UsbDeviceInfo? BeforeDevice { get; init; }

    public UsbDeviceInfo? AfterDevice { get; init; }

    /// <summary>Short safe reference for UI (hashed), not raw topology IDs.</summary>
    public string OldPortKeyShort { get; init; } = string.Empty;

    public string NewPortKeyShort { get; init; } = string.Empty;

    public string ConfidenceTier { get; init; } = string.Empty;

    public string UserHint { get; init; } = string.Empty;

    public bool UsedLimitedConfidenceFallback =>
        MatchKind is UsbPortMappingMatchKind.SameDriveLetterPortChange
            or UsbPortMappingMatchKind.VolumeIdentityPortChange
            or UsbPortMappingMatchKind.ReEnumeratedSameVolume;
}

public static class UsbMappingPortResolution
{
    public static UsbPortMappingResolution Resolve(
        UsbTopologySnapshot before,
        UsbTopologySnapshot after,
        UsbTargetInfo? selectedTarget)
    {
        var beforeByKey = before.Devices.ToDictionary(UsbTopologyDiffService.DeviceCorrelationKey, d => d, StringComparer.Ordinal);
        foreach (var d in after.Devices)
        {
            var k = UsbTopologyDiffService.DeviceCorrelationKey(d);
            if (beforeByKey.TryGetValue(k, out var b) &&
                !string.IsNullOrWhiteSpace(b.StablePortKey) &&
                !string.IsNullOrWhiteSpace(d.StablePortKey) &&
                !string.Equals(b.StablePortKey, d.StablePortKey, StringComparison.Ordinal))
            {
                return BuildSuccess(
                    UsbPortMappingMatchKind.StableCorrelationPortChange,
                    b,
                    d,
                    "High",
                    "Stable device match with a different USB port heuristic.");
            }
        }

        if (selectedTarget is not null &&
            TryMatchByVolumeIdentity(before.Devices, after.Devices, selectedTarget, out var b3, out var a3))
        {
            return BuildSuccess(
                UsbPortMappingMatchKind.VolumeIdentityPortChange,
                b3,
                a3,
                "Medium",
                "Possible match found, but confidence is limited (volume identity hash aligned across snapshots).");
        }

        if (selectedTarget is not null &&
            TryMatchReEnumeratedVolume(before.Devices, after.Devices, selectedTarget, out var b5, out var a5))
        {
            return BuildSuccess(
                UsbPortMappingMatchKind.ReEnumeratedSameVolume,
                b5,
                a5,
                "Medium",
                "Possible match found, but confidence is limited (same volume identity after replug; device fingerprint shifted).");
        }

        if (selectedTarget is not null &&
            TryMatchByDriveLetterPortChange(before.Devices, after.Devices, selectedTarget, out var b2, out var a2))
        {
            return BuildSuccess(
                UsbPortMappingMatchKind.SameDriveLetterPortChange,
                b2,
                a2,
                "Medium",
                "Possible match found, but confidence is limited (same drive letter, port heuristic changed).");
        }

        return new UsbPortMappingResolution
        {
            Success = false,
            UserHint =
                "ForgerEMS could not confidently detect a stable port change. You can try again, use the current port, or save a manual label."
        };
    }

    public static UsbDeviceInfo? FindAfterDeviceForTarget(UsbTopologySnapshot after, UsbTargetInfo target)
    {
        var letter = NormalizeLetter(target.DriveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            return null;
        }

        return after.Devices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(NormalizeLetter(d.DriveLetter), letter, StringComparison.OrdinalIgnoreCase));
    }

    private static UsbPortMappingResolution BuildSuccess(
        UsbPortMappingMatchKind kind,
        UsbDeviceInfo before,
        UsbDeviceInfo after,
        string tier,
        string hint) =>
        new()
        {
            Success = true,
            MatchKind = kind,
            BeforeDevice = before,
            AfterDevice = after,
            OldPortKeyShort = UsbIdentityHasher.ShortKey(before.StablePortKey),
            NewPortKeyShort = UsbIdentityHasher.ShortKey(after.StablePortKey),
            ConfidenceTier = tier,
            UserHint = hint
        };

    private static bool TryMatchByDriveLetterPortChange(
        IReadOnlyList<UsbDeviceInfo> beforeDevices,
        IReadOnlyList<UsbDeviceInfo> afterDevices,
        UsbTargetInfo target,
        out UsbDeviceInfo before,
        out UsbDeviceInfo after)
    {
        before = null!;
        after = null!;
        var letter = NormalizeLetter(target.DriveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            return false;
        }

        var b = beforeDevices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(NormalizeLetter(d.DriveLetter), letter, StringComparison.OrdinalIgnoreCase));
        var a = afterDevices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(NormalizeLetter(d.DriveLetter), letter, StringComparison.OrdinalIgnoreCase));
        if (b is null || a is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(b.StablePortKey) ||
            string.IsNullOrWhiteSpace(a.StablePortKey) ||
            string.Equals(b.StablePortKey, a.StablePortKey, StringComparison.Ordinal))
        {
            return false;
        }

        before = b;
        after = a;
        return true;
    }

    private static bool TryMatchReEnumeratedVolume(
        IReadOnlyList<UsbDeviceInfo> beforeDevices,
        IReadOnlyList<UsbDeviceInfo> afterDevices,
        UsbTargetInfo target,
        out UsbDeviceInfo before,
        out UsbDeviceInfo after)
    {
        before = null!;
        after = null!;
        var letter = NormalizeLetter(target.DriveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            return false;
        }

        var a = afterDevices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(NormalizeLetter(d.DriveLetter), letter, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(d.VolumeIdentityHash));
        if (a is null)
        {
            return false;
        }

        var b = beforeDevices.FirstOrDefault(d =>
            string.Equals(d.VolumeIdentityHash, a.VolumeIdentityHash, StringComparison.Ordinal));
        if (b is null)
        {
            return false;
        }

        if (string.Equals(UsbTopologyDiffService.DeviceCorrelationKey(b), UsbTopologyDiffService.DeviceCorrelationKey(a), StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(b.StablePortKey) ||
            string.IsNullOrWhiteSpace(a.StablePortKey) ||
            string.Equals(b.StablePortKey, a.StablePortKey, StringComparison.Ordinal))
        {
            return false;
        }

        before = b;
        after = a;
        return true;
    }

    private static bool TryMatchByVolumeIdentity(
        IReadOnlyList<UsbDeviceInfo> beforeDevices,
        IReadOnlyList<UsbDeviceInfo> afterDevices,
        UsbTargetInfo target,
        out UsbDeviceInfo before,
        out UsbDeviceInfo after)
    {
        before = null!;
        after = null!;
        var letter = NormalizeLetter(target.DriveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            return false;
        }

        static string? VolHash(UsbDeviceInfo d) =>
            string.IsNullOrWhiteSpace(d.VolumeIdentityHash) ? null : d.VolumeIdentityHash;

        var b = beforeDevices.FirstOrDefault(d =>
            VolHash(d) is { } h &&
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(NormalizeLetter(d.DriveLetter), letter, StringComparison.OrdinalIgnoreCase));
        var hash = b is null ? null : VolHash(b);
        if (string.IsNullOrEmpty(hash))
        {
            return false;
        }

        var a = afterDevices.FirstOrDefault(d =>
            string.Equals(VolHash(d), hash, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(d.StablePortKey));
        if (a is null || b is null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(b.StablePortKey) ||
            string.IsNullOrWhiteSpace(a.StablePortKey) ||
            string.Equals(b.StablePortKey, a.StablePortKey, StringComparison.Ordinal))
        {
            return false;
        }

        before = b;
        after = a;
        return true;
    }

    private static string NormalizeLetter(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return string.Empty;
        }

        return driveLetter.TrimEnd('\\').TrimEnd(':');
    }
}
