using System;
using System.Collections.Generic;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class UsbTopologyDiffService
{
    public static string DeviceCorrelationKey(UsbDeviceInfo d)
    {
        if (!string.IsNullOrWhiteSpace(d.StableDeviceKey))
        {
            return d.StableDeviceKey;
        }

        var letter = d.DriveLetter?.TrimEnd('\\', ':') ?? "";
        return UsbIdentityHasher.Sha256Hex($"{letter}|{d.FriendlyName}");
    }

    public static string SafeDeviceLabel(UsbDeviceInfo d)
    {
        var letter = string.IsNullOrWhiteSpace(d.DriveLetter) ? "no letter" : d.DriveLetter.TrimEnd('\\');
        var shortKey = UsbIdentityHasher.ShortKey(d.StableDeviceKey);
        return $"{d.FriendlyName} ({letter}, ref {shortKey})";
    }

    public static UsbTopologyDiffResult Compare(UsbTopologySnapshot? previous, UsbTopologySnapshot current)
    {
        previous ??= new UsbTopologySnapshot { GeneratedUtc = DateTimeOffset.MinValue, Devices = Array.Empty<UsbDeviceInfo>() };

        var hasBaseline = previous.Devices.Count > 0;
        if (!hasBaseline)
        {
            return new UsbTopologyDiffResult
            {
                SummaryLine = "USB topology: first snapshot on this machine (no prior diff).",
                RecommendationLine = "The next scan will compare against this snapshot for plug/unplug and port changes.",
                DiffConfidenceScore = 30,
                DiffConfidenceReason = "No prior snapshot to compare."
            };
        }

        var prevMap = previous.Devices.ToDictionary(DeviceCorrelationKey, d => d, StringComparer.Ordinal);
        var currMap = current.Devices.ToDictionary(DeviceCorrelationKey, d => d, StringComparer.Ordinal);

        var addedKeys = currMap.Keys.Except(prevMap.Keys, StringComparer.Ordinal).ToList();
        var removedKeys = prevMap.Keys.Except(currMap.Keys, StringComparer.Ordinal).ToList();

        var added = addedKeys.Select(k => SafeDeviceLabel(currMap[k])).ToList();
        var removed = removedKeys.Select(k => SafeDeviceLabel(prevMap[k])).ToList();

        var changes = new List<UsbTopologyDeviceChange>();
        foreach (var key in currMap.Keys.Intersect(prevMap.Keys, StringComparer.Ordinal))
        {
            var p = prevMap[key];
            var c = currMap[key];
            if (p.InferredSpeed == UsbSpeedClassification.Unknown &&
                c.InferredSpeed != UsbSpeedClassification.Unknown)
            {
                changes.Add(new UsbTopologyDeviceChange
                {
                    DeviceSignatureShort = UsbIdentityHasher.ShortKey(key),
                    ChangeKind = "SpeedClarified",
                    Description = $"Speed classification improved from unknown to {c.InferredSpeed}."
                });
            }
            else if (p.InferredSpeed != c.InferredSpeed && p.InferredSpeed != UsbSpeedClassification.Unknown)
            {
                changes.Add(new UsbTopologyDeviceChange
                {
                    DeviceSignatureShort = UsbIdentityHasher.ShortKey(key),
                    ChangeKind = "SpeedChanged",
                    Description = $"Inferred speed changed from {p.InferredSpeed} to {c.InferredSpeed}."
                });
            }

            if (!string.IsNullOrWhiteSpace(p.StablePortKey) &&
                !string.IsNullOrWhiteSpace(c.StablePortKey) &&
                !string.Equals(p.StablePortKey, c.StablePortKey, StringComparison.Ordinal))
            {
                changes.Add(new UsbTopologyDeviceChange
                {
                    DeviceSignatureShort = UsbIdentityHasher.ShortKey(key),
                    ChangeKind = "LikelyPortMove",
                    Description = "The same storage device now maps to a different USB path heuristic (likely a different physical port)."
                });
            }

            if (!string.IsNullOrWhiteSpace(p.ControllerKey) &&
                !string.IsNullOrWhiteSpace(c.ControllerKey) &&
                !string.Equals(p.ControllerKey, c.ControllerKey, StringComparison.Ordinal))
            {
                changes.Add(new UsbTopologyDeviceChange
                {
                    DeviceSignatureShort = UsbIdentityHasher.ShortKey(key),
                    ChangeKind = "ControllerPathChanged",
                    Description = "Device association moved to a different USB controller bucket."
                });
            }

            if (!string.IsNullOrWhiteSpace(p.HubKey) &&
                !string.IsNullOrWhiteSpace(c.HubKey) &&
                !string.Equals(p.HubKey, c.HubKey, StringComparison.Ordinal))
            {
                changes.Add(new UsbTopologyDeviceChange
                {
                    DeviceSignatureShort = UsbIdentityHasher.ShortKey(key),
                    ChangeKind = "HubChanged",
                    Description = "USB hub segment in the heuristic path changed."
                });
            }
        }

        var diffConfidence = 50;
        var diffReason = "Baseline diff heuristics.";
        if (added.Count + removed.Count + changes.Count == 0)
        {
            diffConfidence = 75;
            diffReason = "Topology stable versus last snapshot.";
        }
        else if (changes.Any(c => c.ChangeKind is "LikelyPortMove" or "ControllerPathChanged"))
        {
            diffConfidence = 65;
            diffReason = "Port or controller heuristic changed; physical replug likely.";
        }
        else
        {
            diffConfidence = 60;
            diffReason = "Changes detected between snapshots.";
        }

        var summary = BuildSummaryLine(added, removed, changes);
        var recommend = BuildRecommendationLine(added, removed, changes);

        return new UsbTopologyDiffResult
        {
            AddedDevices = added,
            RemovedDevices = removed,
            ChangedDevices = changes,
            SummaryLine = summary,
            RecommendationLine = recommend,
            DiffConfidenceScore = diffConfidence,
            DiffConfidenceReason = diffReason
        };
    }

    private static string BuildSummaryLine(
        List<string> added,
        List<string> removed,
        List<UsbTopologyDeviceChange> changes)
    {
        var parts = new List<string>();
        if (added.Count > 0)
        {
            parts.Add($"added {added.Count} device(s)");
        }

        if (removed.Count > 0)
        {
            parts.Add($"removed {removed.Count} device(s)");
        }

        if (changes.Count > 0)
        {
            parts.Add($"{changes.Count} heuristic change(s)");
        }

        return parts.Count == 0
            ? "USB topology: unchanged since last scan."
            : "USB changes since last scan: " + string.Join(", ", parts) + ".";
    }

    private static string BuildRecommendationLine(
        List<string> added,
        List<string> removed,
        List<UsbTopologyDeviceChange> changes)
    {
        if (changes.Any(c => c.ChangeKind is "LikelyPortMove" or "ControllerPathChanged" or "SpeedChanged"))
        {
            return "If you moved the USB stick, give Windows a few seconds to settle, then retry the build on the same port you intend to keep.";
        }

        if (added.Count > 0 || removed.Count > 0)
        {
            return "Reconnect only the USB you intend to image, wait for it to mount, then confirm the correct drive letter in USB Builder.";
        }

        return "No replug action required based on the last topology diff.";
    }
}
