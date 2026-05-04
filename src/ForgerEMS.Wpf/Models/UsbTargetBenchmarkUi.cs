using System;

namespace VentoyToolkitSetup.Wpf.Models;

/// <summary>UI-only helpers for USB benchmark onboarding (no persistence).</summary>
public static class UsbTargetBenchmarkUi
{
    /// <summary>True when the selected target has a completed file benchmark suitable for Intelligence.</summary>
    public static bool HasSuccessfulMeasuredBenchmark(UsbTargetInfo? target)
    {
        if (target is null || !target.IsSelectable)
        {
            return false;
        }

        var status = target.BenchmarkStatus ?? string.Empty;
        if (string.Equals(status, "Testing", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Queued", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(status, "Complete", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(status, "Failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var read = target.ReadSpeedDisplay ?? string.Empty;
        var write = target.WriteSpeedDisplay ?? string.Empty;
        if (read.Contains("MB/s", StringComparison.OrdinalIgnoreCase) &&
            write.Contains("MB/s", StringComparison.OrdinalIgnoreCase) &&
            !read.Contains("Skipped", StringComparison.OrdinalIgnoreCase) &&
            !write.Contains("Skipped", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}
