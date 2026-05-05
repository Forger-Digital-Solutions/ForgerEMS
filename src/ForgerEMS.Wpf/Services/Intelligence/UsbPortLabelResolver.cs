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
}

public static class UsbPortLabelResolver
{
    private static readonly string CurrentSessionId = Guid.NewGuid().ToString("N");
    private static readonly HashSet<string> RemovedDriveLetters = new(StringComparer.OrdinalIgnoreCase);

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

        var exact = !string.IsNullOrWhiteSpace(current.StablePortKey)
            ? profile.KnownPorts.FirstOrDefault(p =>
                string.Equals(p.StablePortKey, current.StablePortKey, StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(p.UserLabel))
            : null;

        var sameDevice = profile.KnownPorts
            .Where(p => !string.IsNullOrWhiteSpace(p.UserLabel))
            .Where(p => SameDevice(p, currentDeviceKey))
            .OrderByDescending(p => p.LastSeenUtc ?? DateTimeOffset.MinValue)
            .ToList();

        var candidate = exact ?? sameDevice.FirstOrDefault();
        if (candidate is null)
        {
            return NoLabel();
        }

        var label = candidate.UserLabel?.Trim();
        var reasons = new List<string>();
        if (!string.IsNullOrWhiteSpace(currentDeviceKey) && SameDevice(candidate, currentDeviceKey))
        {
            reasons.Add("same-device-identity");
        }

        if (removedObserved)
        {
            reasons.Add("reconnect-observed");
        }

        var savedHasStrongTopology = candidate.HasStrongPortTopologyEvidence &&
                                     !string.IsNullOrWhiteSpace(candidate.PortTopologyKey);
        if (savedHasStrongTopology && currentHasStrongTopology)
        {
            if (string.Equals(candidate.PortTopologyKey, currentTopologyKey, StringComparison.Ordinal))
            {
                reasons.Add("topology-match");
                return new UsbPortLabelStatus
                {
                    Validity = UsbPortLabelValidity.VerifiedCurrent,
                    CurrentLabel = label,
                    LastKnownLabel = label,
                    CurrentRecord = candidate,
                    LastKnownRecord = candidate,
                    StatusLine = $"Current port: {label} verified",
                    ReasonLine = "Saved topology matches the current USB connection.",
                    ReasonCodes = reasons
                };
            }

            reasons.Add("topology-changed");
            return new UsbPortLabelStatus
            {
                Validity = UsbPortLabelValidity.PortChangedSuspected,
                LastKnownLabel = label,
                LastKnownRecord = candidate,
                StatusLine = "Current port: Port change suspected",
                ReasonLine = $"Last known label: {label}. Open USB Mapping Wizard or update the manual label.",
                ReasonCodes = reasons
            };
        }

        if (IsCurrentSessionManual(candidate, current, currentTopologyKey, removedObserved))
        {
            reasons.Add("manual-label-current-session");
            return new UsbPortLabelStatus
            {
                Validity = UsbPortLabelValidity.CurrentSessionManual,
                CurrentLabel = label,
                LastKnownLabel = label,
                CurrentRecord = candidate,
                LastKnownRecord = candidate,
                StatusLine = $"Current port: {label} manually labeled",
                ReasonLine = "Manual label was saved for this current app session and connection.",
                ReasonCodes = reasons
            };
        }

        reasons.Add(currentHasStrongTopology || savedHasStrongTopology
            ? "topology-unmatched"
            : "topology-unavailable");
        reasons.Add("stale-manual-label-not-reused");

        return new UsbPortLabelStatus
        {
            Validity = currentHasStrongTopology || savedHasStrongTopology
                ? UsbPortLabelValidity.NeedsVerification
                : UsbPortLabelValidity.TopologyUnavailable,
            LastKnownLabel = label,
            LastKnownRecord = candidate,
            StatusLine = removedObserved
                ? "Current port: Unverified after reconnect"
                : "Current port: Needs verification",
            ReasonLine = $"Last known label: {label}. Save/update a manual label to attach this connection to a physical port.",
            ReasonCodes = reasons
        };
    }

    public static void StampManualLabel(
        UsbKnownPortRecord record,
        UsbDeviceInfo device,
        string label,
        int mappingConfidence,
        DateTimeOffset confirmedAtUtc)
    {
        record.UserLabel = label.Trim();
        record.DeviceIdentityKey = BuildDeviceIdentityKey(device);
        record.PortTopologyKey = BuildPortTopologyKey(device);
        record.HasStrongPortTopologyEvidence = HasStrongPortTopologyEvidence(device);
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
}
