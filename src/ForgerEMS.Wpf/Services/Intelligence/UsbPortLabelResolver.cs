using System;
using System.Collections.Generic;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class UsbPortLabelStatus
{
    public UsbPortLabelValidity Validity { get; init; }

    public string? CurrentLabel { get; init; }

    public string? LastKnownLabel { get; init; }

    public UsbKnownPortRecord? CurrentRecord { get; init; }

    public UsbKnownPortRecord? LastKnownRecord { get; init; }

    public bool CanUseCurrentLabel =>
        Validity is UsbPortLabelValidity.VerifiedCurrent or UsbPortLabelValidity.CurrentSessionManual;

    public bool CanAttachBenchmarkToVerifiedPort => CanUseCurrentLabel && CurrentRecord is not null;

    public string StatusLine { get; init; } = string.Empty;

    public string ReasonLine { get; init; } = string.Empty;

    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CandidateLabels { get; init; } = Array.Empty<string>();
}

public static class UsbPortLabelResolver
{
    private static readonly string CurrentSessionId = Guid.NewGuid().ToString("N");
    private static readonly HashSet<string> RemovedDriveLetters = new(StringComparer.OrdinalIgnoreCase);

    private const int VerifiedScoreThreshold = 75;
    private const int AmbiguousScoreGap = 12;

    public static void MarkDriveRemoved(string? rootOrDriveLetter)
    {
        var letter = NormalizeDriveLetter(rootOrDriveLetter);
        if (!string.IsNullOrWhiteSpace(letter))
        {
            lock (RemovedDriveLetters)
            {
                RemovedDriveLetters.Add(letter);
            }
        }
    }

    public static UsbPortLabelStatus Resolve(UsbDeviceInfo? current, UsbMachineProfile? profile)
    {
        if (current is null || profile?.KnownPorts is not { Count: > 0 })
        {
            return NoLabel();
        }

        var currentDeviceKey = BuildDeviceIdentityKey(current);
        var currentTopologyKey = BuildPortTopologyKey(current);
        var currentHasStrongTopology = HasStrongPortTopologyEvidence(current);
        var removedObserved = WasDriveRemovalObserved(current.DriveLetter);

        var candidates = profile.KnownPorts
            .Where(p => !string.IsNullOrWhiteSpace(p.UserLabel))
            .Select(p => ScoreCandidate(p, current, currentDeviceKey, currentTopologyKey))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenByDescending(c => c.Record.UpdatedUtc ?? c.Record.LastSeenUtc ?? DateTimeOffset.MinValue)
            .ToList();

        if (candidates.Count == 0)
        {
            return NoLabel();
        }

        var labels = candidates.Select(c => c.Label).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var session = candidates.FirstOrDefault(c => IsCurrentSessionManual(c.Record, current, currentTopologyKey, removedObserved));
        if (session.Record is not null)
        {
            return new UsbPortLabelStatus
            {
                Validity = UsbPortLabelValidity.CurrentSessionManual,
                CurrentLabel = session.Label,
                LastKnownLabel = session.Label,
                CurrentRecord = session.Record,
                LastKnownRecord = session.Record,
                StatusLine = $"Current port: {session.Label} manually confirmed this session",
                ReasonLine = session.Record.HasStrongPortTopologyEvidence
                    ? "Manual label is bound to this current connection and has topology evidence."
                    : "Reconnect verification: weak — Windows may not expose enough topology to auto-detect this port later.",
                ReasonCodes = session.ReasonCodes.Append("manual-label-current-session").ToArray(),
                CandidateLabels = labels
            };
        }

        var best = candidates[0];
        var close = candidates
            .Where(c => c.Record != best.Record && best.Score - c.Score <= AmbiguousScoreGap)
            .ToList();

        if (best.Score >= VerifiedScoreThreshold &&
            best.StrongTopologyMatched &&
            !close.Any(c => c.Score >= VerifiedScoreThreshold && c.StrongTopologyMatched))
        {
            return new UsbPortLabelStatus
            {
                Validity = UsbPortLabelValidity.VerifiedCurrent,
                CurrentLabel = best.Label,
                LastKnownLabel = best.Label,
                CurrentRecord = best.Record,
                LastKnownRecord = best.Record,
                StatusLine = $"Current port: {best.Label} verified",
                ReasonLine = "Saved topology matches the current USB connection.",
                ReasonCodes = best.ReasonCodes.Append("topology-match").ToArray(),
                CandidateLabels = labels
            };
        }

        if (close.Count > 0)
        {
            return new UsbPortLabelStatus
            {
                Validity = UsbPortLabelValidity.Ambiguous,
                LastKnownLabel = best.Label,
                LastKnownRecord = best.Record,
                StatusLine = "Current port: Ambiguous match",
                ReasonLine = $"Possible labels: {string.Join(", ", labels.Take(4))}. Recommended: Open USB Mapping Wizard.",
                ReasonCodes = best.ReasonCodes.Append("ambiguous-weak-matches").ToArray(),
                CandidateLabels = labels
            };
        }

        if (currentHasStrongTopology && candidates.Any(c => c.Record.HasStrongPortTopologyEvidence))
        {
            return new UsbPortLabelStatus
            {
                Validity = UsbPortLabelValidity.PortChangedSuspected,
                LastKnownLabel = best.Label,
                LastKnownRecord = best.Record,
                StatusLine = "Current port: Port change suspected",
                ReasonLine = $"Last known label: {best.Label}. Open USB Mapping Wizard or update the manual label.",
                ReasonCodes = best.ReasonCodes.Append("topology-changed").Append("stale-manual-label-not-reused").ToArray(),
                CandidateLabels = labels
            };
        }

        return new UsbPortLabelStatus
        {
            Validity = currentHasStrongTopology || candidates.Any(c => c.Record.HasStrongPortTopologyEvidence)
                ? UsbPortLabelValidity.NeedsVerification
                : UsbPortLabelValidity.TopologyUnavailable,
            LastKnownLabel = best.Label,
            LastKnownRecord = best.Record,
            StatusLine = removedObserved ? "Current port: Unknown / needs mapping" : "Current port: Needs verification",
            ReasonLine = $"Last known label: {best.Label}. Save/update a manual label to attach this connection to a physical port.",
            ReasonCodes = best.ReasonCodes
                .Append(currentHasStrongTopology ? "topology-unmatched" : "topology-unavailable")
                .Append("stale-manual-label-not-reused")
                .ToArray(),
            CandidateLabels = labels
        };
    }

    public static void StampManualLabel(
        UsbKnownPortRecord record,
        UsbDeviceInfo device,
        string label,
        int mappingConfidence,
        DateTimeOffset confirmedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(record.MappingId))
        {
            record.MappingId = Guid.NewGuid().ToString("N");
        }

        record.UserLabel = label.Trim();
        record.StablePortKey = device.StablePortKey;
        record.DeviceIdentityKey = BuildDeviceIdentityKey(device);
        record.PortTopologyKey = BuildPortTopologyKey(device);
        record.HasStrongPortTopologyEvidence = HasStrongPortTopologyEvidence(device);
        record.LocationPathHash = device.LocationPathHash;
        record.LocationPathsHash = device.LocationPathsHash;
        record.LocationInformationHash = device.LocationInformationHash;
        record.ControllerKey = device.ControllerKey;
        record.HubKey = device.HubKey;
        record.ParentDeviceIdHash = device.ParentDeviceIdHash;
        record.ParentIdPrefixHash = device.ParentIdPrefixHash;
        record.UsbControllerAssociationHash = device.UsbControllerAssociationHash;
        record.UsbHubPathHash = device.UsbHubPathHash;
        record.UsbHubNameHash = device.UsbHubNameHash;
        record.ContainerIdHash = device.ContainerIdHash;
        record.BusReportedSpeed = device.BusReportedSpeed;
        record.InferredSpeed = device.InferredSpeed;
        record.MappingSource = record.HasStrongPortTopologyEvidence ? "manual-with-topology" : "manual-weak-topology";
        record.CreatedUtc ??= confirmedAtUtc;
        record.UpdatedUtc = confirmedAtUtc;
        record.LastManualLabelSessionId = CurrentSessionId;
        record.LabelConfirmedAtUtc = confirmedAtUtc;
        record.LabelConfirmedDeviceSeenCount = device.SeenCount;
        record.LabelConfirmedDriveLetter = NormalizeDriveLetter(device.DriveLetter);
        record.MappingConfidenceScore = mappingConfidence;

        var letter = NormalizeDriveLetter(device.DriveLetter);
        if (!string.IsNullOrWhiteSpace(letter))
        {
            lock (RemovedDriveLetters)
            {
                RemovedDriveLetters.Remove(letter);
            }
        }
    }

    public static UsbIntelligenceBenchmarkResult WithPortAttachment(
        UsbIntelligenceBenchmarkResult source,
        bool attachedToVerifiedPort,
        string? label,
        UsbPortLabelValidity validity)
    {
        return new UsbIntelligenceBenchmarkResult
        {
            Succeeded = source.Succeeded,
            EndKind = source.EndKind,
            WriteSpeedMBps = source.WriteSpeedMBps,
            ReadSpeedMBps = source.ReadSpeedMBps,
            DurationMs = source.DurationMs,
            TestSizeMb = source.TestSizeMb,
            Classification = source.Classification,
            ConfidenceScore = source.ConfidenceScore,
            Timestamp = source.Timestamp,
            SummaryLine = source.SummaryLine,
            DetailReason = source.DetailReason,
            ActualBytesWritten = source.ActualBytesWritten,
            ActualBytesRead = source.ActualBytesRead,
            WriteElapsedMs = source.WriteElapsedMs,
            ReadElapsedMs = source.ReadElapsedMs,
            ReadLikelyCached = source.ReadLikelyCached,
            ReadIsEstimate = source.ReadIsEstimate,
            BenchmarkConfidence = source.BenchmarkConfidence,
            AccuracyWarning = source.AccuracyWarning,
            AttachedToVerifiedPort = attachedToVerifiedPort,
            AttachedPortLabel = label?.Trim() ?? string.Empty,
            PortLabelValidity = validity
        };
    }

    public static string NormalizeDriveLetter(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return string.Empty;
        }

        return driveLetter.Trim().TrimEnd('\\').TrimEnd(':').ToUpperInvariant();
    }

    public static string BuildDeviceIdentityKey(UsbDeviceInfo device)
    {
        var parts = new[]
        {
            device.SerialHash,
            device.VolumeIdentityHash,
            device.DeviceInstanceIdHash,
            device.PnpDeviceIdHash,
            device.WmiDeviceIdHash,
            device.StableDeviceKey
        }.Where(v => !string.IsNullOrWhiteSpace(v));

        var joined = string.Join("|", parts);
        return string.IsNullOrWhiteSpace(joined) ? string.Empty : UsbIdentityHasher.Sha256Hex(joined);
    }

    public static string BuildPortTopologyKey(UsbDeviceInfo device)
    {
        var parts = new[]
        {
            device.LocationPathHash,
            device.LocationPathsHash,
            device.LocationInformationHash,
            device.ParentDeviceIdHash,
            device.ParentIdPrefixHash,
            device.UsbControllerAssociationHash,
            device.UsbHubPathHash,
            device.UsbHubNameHash,
            device.HubKey,
            device.ContainerIdHash
        }.Where(v => !string.IsNullOrWhiteSpace(v));

        var joined = string.Join("|", parts);
        return string.IsNullOrWhiteSpace(joined) ? string.Empty : UsbIdentityHasher.Sha256Hex(joined);
    }

    public static bool HasStrongPortTopologyEvidence(UsbDeviceInfo device) =>
        !string.IsNullOrWhiteSpace(device.LocationPathHash) ||
        !string.IsNullOrWhiteSpace(device.LocationPathsHash) ||
        !string.IsNullOrWhiteSpace(device.UsbControllerAssociationHash) ||
        !string.IsNullOrWhiteSpace(device.UsbHubPathHash) ||
        !string.IsNullOrWhiteSpace(device.ParentDeviceIdHash) ||
        !string.IsNullOrWhiteSpace(device.ParentIdPrefixHash);

    private static UsbPortLabelStatus NoLabel() =>
        new()
        {
            Validity = UsbPortLabelValidity.None,
            StatusLine = "Current port: Not mapped",
            ReasonLine = "No saved port label for the selected USB.",
            ReasonCodes = ["no-label"]
        };

    private static bool SameDevice(UsbKnownPortRecord record, string currentDeviceKey) =>
        !string.IsNullOrWhiteSpace(record.DeviceIdentityKey) &&
        !string.IsNullOrWhiteSpace(currentDeviceKey) &&
        string.Equals(record.DeviceIdentityKey, currentDeviceKey, StringComparison.Ordinal);

    private static bool IsCurrentSessionManual(
        UsbKnownPortRecord record,
        UsbDeviceInfo current,
        string currentTopologyKey,
        bool removedObserved)
    {
        if (removedObserved ||
            !string.Equals(record.LastManualLabelSessionId, CurrentSessionId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(record.StablePortKey, current.StablePortKey, StringComparison.Ordinal))
        {
            return false;
        }

        if (record.HasStrongPortTopologyEvidence)
        {
            return string.Equals(record.PortTopologyKey, currentTopologyKey, StringComparison.Ordinal);
        }

        return current.SeenCount <= Math.Max(1, record.LabelConfirmedDeviceSeenCount) + 1;
    }

    private static UsbPortCandidateScore ScoreCandidate(
        UsbKnownPortRecord record,
        UsbDeviceInfo current,
        string currentDeviceKey,
        string currentTopologyKey)
    {
        var score = 0;
        var reasons = new List<string>();
        var sameDevice = SameDevice(record, currentDeviceKey);
        if (sameDevice)
        {
            score += 10;
            reasons.Add("same-device-identity");
        }

        if (!string.IsNullOrWhiteSpace(record.StablePortKey) &&
            string.Equals(record.StablePortKey, current.StablePortKey, StringComparison.Ordinal))
        {
            score += record.HasStrongPortTopologyEvidence ? 20 : 8;
            reasons.Add("stable-port-key-match");
        }

        var strongTopologyMatched = false;
        if (record.HasStrongPortTopologyEvidence &&
            HasStrongPortTopologyEvidence(current) &&
            !string.IsNullOrWhiteSpace(record.PortTopologyKey) &&
            string.Equals(record.PortTopologyKey, currentTopologyKey, StringComparison.Ordinal))
        {
            score += 85;
            strongTopologyMatched = true;
            reasons.Add("strong-topology-fingerprint-match");
        }
        else
        {
            AddFieldScore(record.LocationPathHash, current.LocationPathHash, 42, "location-path-match", ref score, reasons);
            AddFieldScore(record.LocationPathsHash, current.LocationPathsHash, 42, "location-paths-match", ref score, reasons);
            AddFieldScore(record.UsbControllerAssociationHash, current.UsbControllerAssociationHash, 28, "controller-association-match", ref score, reasons);
            AddFieldScore(record.UsbHubPathHash, current.UsbHubPathHash, 26, "hub-path-match", ref score, reasons);
            AddFieldScore(record.ParentDeviceIdHash, current.ParentDeviceIdHash, 22, "parent-device-match", ref score, reasons);
            AddFieldScore(record.ParentIdPrefixHash, current.ParentIdPrefixHash, 18, "parent-prefix-match", ref score, reasons);
            AddFieldScore(record.ControllerKey, current.ControllerKey, 12, "controller-match", ref score, reasons);
            AddFieldScore(record.HubKey, current.HubKey, 10, "hub-match", ref score, reasons);
            AddFieldScore(record.ContainerIdHash, current.ContainerIdHash, 8, "container-match", ref score, reasons);
        }

        if (!string.IsNullOrWhiteSpace(record.BusReportedSpeed) &&
            string.Equals(record.BusReportedSpeed, current.BusReportedSpeed, StringComparison.OrdinalIgnoreCase))
        {
            score += 3;
            reasons.Add("bus-speed-match");
        }

        if (record.InferredSpeed != UsbSpeedClassification.Unknown &&
            record.InferredSpeed == current.InferredSpeed)
        {
            score += 3;
            reasons.Add("speed-class-match");
        }

        if (!sameDevice && !strongTopologyMatched)
        {
            score = Math.Min(score, 20);
        }

        return new UsbPortCandidateScore(
            record,
            record.UserLabel?.Trim() ?? string.Empty,
            score,
            strongTopologyMatched,
            reasons);
    }

    private static void AddFieldScore(
        string? saved,
        string? current,
        int points,
        string reason,
        ref int score,
        List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(saved) ||
            string.IsNullOrWhiteSpace(current) ||
            !string.Equals(saved, current, StringComparison.Ordinal))
        {
            return;
        }

        score += points;
        reasons.Add(reason);
    }

    private static bool WasDriveRemovalObserved(string? driveLetter)
    {
        var letter = NormalizeDriveLetter(driveLetter);
        if (string.IsNullOrWhiteSpace(letter))
        {
            return false;
        }

        lock (RemovedDriveLetters)
        {
            return RemovedDriveLetters.Contains(letter);
        }
    }

    private readonly record struct UsbPortCandidateScore(
        UsbKnownPortRecord Record,
        string Label,
        int Score,
        bool StrongTopologyMatched,
        IReadOnlyList<string> ReasonCodes);
}
