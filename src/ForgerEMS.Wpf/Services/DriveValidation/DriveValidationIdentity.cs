#pragma warning disable CA1838 // P/Invokes use StringBuilder for GetVolumeInformationW; acceptable for a one-shot best-effort call.
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

/// <summary>
/// Composite identity used by the Drive Validator cache so a different physical drive mounted on the
/// same letter is not treated as already validated. Identity strength is reported back to the caller
/// so callers can avoid over-trusting a weak match.
/// </summary>
public static class DriveValidationIdentity
{
    public enum Confidence
    {
        /// <summary>No identifiers — fall back to "validation history unavailable", never auto-pass.</summary>
        None = 0,

        /// <summary>Root path only — treat any cached result as advisory and require re-run.</summary>
        Weak = 1,

        /// <summary>Drive model + size + label, no volume serial. Match likely correct but not guaranteed.</summary>
        Partial = 2,

        /// <summary>Volume serial present and matches.</summary>
        Strong = 3
    }

    public sealed record Fingerprint(string Hash, string VolumeSerial, Confidence Strength)
    {
        public string ConfidenceText => Strength switch
        {
            Confidence.Strong => "Strong (volume serial + drive)",
            Confidence.Partial => "Partial (drive model + size + label)",
            Confidence.Weak => "Weak (root path only)",
            _ => "Unknown"
        };
    }

    public static Fingerprint Compute(UsbTargetInfo? target)
    {
        if (target is null || string.IsNullOrWhiteSpace(target.RootPath))
        {
            return new Fingerprint(string.Empty, string.Empty, Confidence.None);
        }

        var volumeSerial = TryGetVolumeSerial(target.RootPath);
        var hasSerial = !string.IsNullOrWhiteSpace(volumeSerial);
        var hasIdentifiers = !string.IsNullOrWhiteSpace(target.DeviceModel)
                             || !string.IsNullOrWhiteSpace(target.DeviceBrand)
                             || target.TotalBytes > 0;

        var confidence = hasSerial
            ? Confidence.Strong
            : hasIdentifiers
                ? Confidence.Partial
                : Confidence.Weak;

        var payload = string.Join(
            "|",
            NormalizeRoot(target.RootPath),
            volumeSerial ?? string.Empty,
            (target.DeviceBrand ?? string.Empty).Trim(),
            (target.DeviceModel ?? string.Empty).Trim(),
            (target.TotalBytes > 0 ? target.TotalBytes.ToString(CultureInfo.InvariantCulture) : string.Empty),
            (target.Label ?? string.Empty).Trim(),
            (target.FileSystem ?? string.Empty).Trim(),
            (target.PartitionType ?? string.Empty).Trim());

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        var hex = Convert.ToHexString(hash, 0, 12);
        return new Fingerprint(hex, volumeSerial ?? string.Empty, confidence);
    }

    public static bool Matches(Fingerprint current, DriveValidationEvidence cached)
    {
        if (current.Strength == Confidence.None || cached is null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(cached.IdentityFingerprint) &&
            string.Equals(cached.IdentityFingerprint, current.Hash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Legacy cache (no fingerprint stored): allow only weak match by root path.
        return false;
    }

    public static string NormalizeRoot(string rootPath) =>
        string.IsNullOrWhiteSpace(rootPath)
            ? string.Empty
            : rootPath.Trim().TrimEnd('\\').ToUpperInvariant();

    /// <summary>
    /// Win32 GetVolumeInformationW returns a 32-bit volume serial. Best-effort; returns empty when
    /// running off Windows, when the path is not a real drive (e.g. tests under %TEMP%), or on any
    /// error. Identity tolerates an empty serial — confidence drops to Partial in that case.
    /// </summary>
    private static string? TryGetVolumeSerial(string rootPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var path = rootPath;
        if (!path.EndsWith('\\'))
        {
            path += "\\";
        }

        try
        {
            var volumeName = new StringBuilder(261);
            var fileSystemName = new StringBuilder(261);
            if (GetVolumeInformationW(
                    path,
                    volumeName,
                    volumeName.Capacity,
                    out var serial,
                    out _,
                    out _,
                    fileSystemName,
                    fileSystemName.Capacity))
            {
                return serial.ToString("X8", CultureInfo.InvariantCulture);
            }
        }
        catch
        {
            // Best-effort; identity falls back to weaker match below.
        }

        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}
