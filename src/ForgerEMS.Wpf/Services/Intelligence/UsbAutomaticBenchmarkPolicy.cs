using System;
using System.Collections.Concurrent;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Guards automatic USB benchmarks from rapid repeat starts for the same mount path.</summary>
public sealed class UsbAutomaticBenchmarkPolicy
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastAutomaticStartUtcByRoot =
        new(StringComparer.OrdinalIgnoreCase);

    public static TimeSpan MinimumRepeatInterval { get; } = TimeSpan.FromSeconds(30);

    /// <summary>Returns false when an automatic benchmark should not start yet for this root path.</summary>
    public bool TryRegisterAutomaticStart(string rootPath, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return false;
        }

        var normalized = rootPath.TrimEnd('\\');
        if (_lastAutomaticStartUtcByRoot.TryGetValue(normalized, out var last) &&
            now - last < MinimumRepeatInterval)
        {
            return false;
        }

        _lastAutomaticStartUtcByRoot[normalized] = now;
        return true;
    }

    /// <summary>Manual runs should not consume the automatic cooldown slot.</summary>
    public void ClearEntry(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        _lastAutomaticStartUtcByRoot.TryRemove(rootPath.TrimEnd('\\'), out _);
    }

    /// <summary>Prevents an immediate automatic follow-up benchmark after a manual run on the same mount path.</summary>
    public void TouchCooldown(string rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return;
        }

        _lastAutomaticStartUtcByRoot[rootPath.TrimEnd('\\')] = DateTimeOffset.UtcNow;
    }
}
