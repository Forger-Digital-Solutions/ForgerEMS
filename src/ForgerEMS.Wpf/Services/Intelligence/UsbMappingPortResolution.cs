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
    ReEnumeratedSameVolume = 4,

    /// <summary>Same selected volume, but only weaker Windows topology evidence changed.</summary>
    WeakTopologyEvidencePortChange = 5,

    /// <summary>The same drive was seen again, but Windows exposed no reliable changed port evidence.</summary>
    ManualLabelRecommended = 6
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
            or UsbPortMappingMatchKind.ReEnumeratedSameVolume
            or UsbPortMappingMatchKind.WeakTopologyEvidencePortChange;

    public bool ManualLabelRecommended => MatchKind == UsbPortMappingMatchKind.ManualLabelRecommended;

    public int MatchedCandidateCount { get; init; }

    public IReadOnlyList<string> ReasonCodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> PresentTopologyFields { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> MissingTopologyFields { get; init; } = Array.Empty<string>();
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
                    "Stable device match with a different USB port heuristic.",
                    BuildEvidence(b, d).ReasonCodes);
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
                "Possible match found, but confidence is limited (volume identity hash aligned across snapshots).",
                BuildEvidence(b3, a3).ReasonCodes);
        }

        if (selectedTarget is not null &&
            TryMatchReEnumeratedVolume(before.Devices, after.Devices, selectedTarget, out var b5, out var a5))
        {
            return BuildSuccess(
                UsbPortMappingMatchKind.ReEnumeratedSameVolume,
                b5,
                a5,
                "Medium",
                "Possible match found, but confidence is limited (same volume identity after replug; device fingerprint shifted).",
                BuildEvidence(b5, a5).ReasonCodes);
        }

        if (selectedTarget is not null &&
            TryMatchByDriveLetterPortChange(before.Devices, after.Devices, selectedTarget, out var b2, out var a2))
        {
            return BuildSuccess(
                UsbPortMappingMatchKind.SameDriveLetterPortChange,
                b2,
                a2,
                "Medium",
                "Possible match found, but confidence is limited (same drive letter, port heuristic changed).",
                BuildEvidence(b2, a2).ReasonCodes);
        }

        if (selectedTarget is not null &&
            TryMatchByWeakTopologyEvidence(before.Devices, after.Devices, selectedTarget, out var b6, out var a6, out var confidenceTier))
        {
            return BuildSuccess(
                UsbPortMappingMatchKind.WeakTopologyEvidencePortChange,
                b6,
                a6,
                confidenceTier,
                "Windows exposed limited USB topology, but the selected drive's port-related evidence changed.",
                BuildEvidence(b6, a6).ReasonCodes);
        }

        if (selectedTarget is not null &&
            TryFindSameDriveIdentity(before.Devices, after.Devices, selectedTarget, out var sameBefore, out var sameAfter, out var candidateCount) &&
            sameBefore is not null &&
            sameAfter is not null)
        {
            var evidence = BuildEvidence(sameBefore, sameAfter);
            var reasons = new HashSet<string>(evidence.ReasonCodes, StringComparer.Ordinal)
            {
                "same-device-identity-matched"
            };
            if (candidateCount > 1)
            {
                reasons.Add("multiple-candidate-devices-found");
            }

            if (evidence.PresentTopologyFields.Count == 0)
            {
                reasons.Add("no-topology-fields-exposed");
            }

            return BuildManualLabelRecommended(
                sameBefore,
                sameAfter,
                candidateCount,
                reasons.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                evidence.PresentTopologyFields,
                evidence.MissingTopologyFields);
        }

        return new UsbPortMappingResolution
        {
            Success = false,
            MatchKind = UsbPortMappingMatchKind.None,
            MatchedCandidateCount = 0,
            ReasonCodes = ["no-selected-device-match"],
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
        string hint,
        IReadOnlyList<string>? reasonCodes = null) =>
        new()
        {
            Success = true,
            MatchKind = kind,
            BeforeDevice = before,
            AfterDevice = after,
            OldPortKeyShort = UsbIdentityHasher.ShortKey(before.StablePortKey),
            NewPortKeyShort = UsbIdentityHasher.ShortKey(after.StablePortKey),
            ConfidenceTier = tier,
            UserHint = hint,
            MatchedCandidateCount = 1,
            ReasonCodes = reasonCodes ?? Array.Empty<string>(),
            PresentTopologyFields = BuildEvidence(before, after).PresentTopologyFields,
            MissingTopologyFields = BuildEvidence(before, after).MissingTopologyFields
        };

    private static UsbPortMappingResolution BuildManualLabelRecommended(
        UsbDeviceInfo before,
        UsbDeviceInfo after,
        int candidateCount,
        IReadOnlyList<string> reasonCodes,
        IReadOnlyList<string> presentTopologyFields,
        IReadOnlyList<string> missingTopologyFields) =>
        new()
        {
            Success = false,
            MatchKind = UsbPortMappingMatchKind.ManualLabelRecommended,
            BeforeDevice = before,
            AfterDevice = after,
            OldPortKeyShort = UsbIdentityHasher.ShortKey(before.StablePortKey),
            NewPortKeyShort = UsbIdentityHasher.ShortKey(after.StablePortKey),
            ConfidenceTier = "Manual",
            MatchedCandidateCount = candidateCount,
            ReasonCodes = reasonCodes,
            PresentTopologyFields = presentTopologyFields,
            MissingTopologyFields = missingTopologyFields,
            UserHint =
                "Windows did not expose a reliable physical port path on this device. Save a manual label like Left USB-A, Right USB-C, or Dock Port 1."
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

    private static bool TryMatchByWeakTopologyEvidence(
        IReadOnlyList<UsbDeviceInfo> beforeDevices,
        IReadOnlyList<UsbDeviceInfo> afterDevices,
        UsbTargetInfo target,
        out UsbDeviceInfo before,
        out UsbDeviceInfo after,
        out string confidenceTier)
    {
        before = null!;
        after = null!;
        confidenceTier = string.Empty;
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

        if (!LikelySameSelectedVolume(b, a, target))
        {
            return false;
        }

        if (!HasWeakTopologyChange(b, a))
        {
            return false;
        }

        before = b;
        after = a;
        confidenceTier = ClassifyWeakEvidenceConfidence(b, a);
        return true;
    }

    private static bool LikelySameSelectedVolume(UsbDeviceInfo before, UsbDeviceInfo after, UsbTargetInfo target)
    {
        var targetLetter = NormalizeLetter(target.DriveLetter);
        if (!string.IsNullOrWhiteSpace(targetLetter) &&
            string.Equals(NormalizeLetter(before.DriveLetter), targetLetter, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(NormalizeLetter(after.DriveLetter), targetLetter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(before.VolumeIdentityHash) &&
            string.Equals(before.VolumeIdentityHash, after.VolumeIdentityHash, StringComparison.Ordinal))
        {
            return true;
        }

        var label = target.LabelDisplay;
        if (!string.IsNullOrWhiteSpace(label) &&
            (before.FriendlyName.Contains(label, StringComparison.OrdinalIgnoreCase) ||
             before.VolumeLabel.Contains(label, StringComparison.OrdinalIgnoreCase)) &&
            (after.FriendlyName.Contains(label, StringComparison.OrdinalIgnoreCase) ||
             after.VolumeLabel.Contains(label, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(before.FriendlyName) &&
               string.Equals(before.FriendlyName, after.FriendlyName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasWeakTopologyChange(UsbDeviceInfo before, UsbDeviceInfo after)
    {
        static bool Changed(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            !string.Equals(a, b, StringComparison.Ordinal);

        return Changed(before.LocationPathHash, after.LocationPathHash) ||
               Changed(before.LocationInformationHash, after.LocationInformationHash) ||
               Changed(before.LocationPathsHash, after.LocationPathsHash) ||
               Changed(before.ParentDeviceIdHash, after.ParentDeviceIdHash) ||
               Changed(before.ParentIdPrefixHash, after.ParentIdPrefixHash) ||
               Changed(before.HubKey, after.HubKey) ||
               Changed(before.ControllerKey, after.ControllerKey) ||
               Changed(before.UsbControllerAssociationHash, after.UsbControllerAssociationHash) ||
               Changed(before.UsbHubNameHash, after.UsbHubNameHash) ||
               Changed(before.UsbHubPathHash, after.UsbHubPathHash) ||
               Changed(before.ContainerIdHash, after.ContainerIdHash) ||
               Changed(before.BusReportedSpeed, after.BusReportedSpeed) ||
               before.InferredSpeed != after.InferredSpeed;
    }

    private static string ClassifyWeakEvidenceConfidence(UsbDeviceInfo before, UsbDeviceInfo after)
    {
        var evidence = BuildEvidence(before, after);
        if (evidence.ReasonCodes.Contains("location-path-changed", StringComparer.Ordinal) ||
            evidence.ReasonCodes.Contains("controller-changed", StringComparer.Ordinal) ||
            evidence.ReasonCodes.Contains("hub-parent-changed", StringComparer.Ordinal))
        {
            return "Medium";
        }

        return "Low";
    }

    private static bool TryFindSameDriveIdentity(
        IReadOnlyList<UsbDeviceInfo> beforeDevices,
        IReadOnlyList<UsbDeviceInfo> afterDevices,
        UsbTargetInfo target,
        out UsbDeviceInfo? before,
        out UsbDeviceInfo? after,
        out int candidateCount)
    {
        before = null;
        after = null;
        candidateCount = 0;
        var letter = NormalizeLetter(target.DriveLetter);
        if (string.IsNullOrEmpty(letter))
        {
            return false;
        }

        before = beforeDevices.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(NormalizeLetter(d.DriveLetter), letter, StringComparison.OrdinalIgnoreCase));
        if (before is null)
        {
            return false;
        }

        var beforeMatch = before;
        var candidates = afterDevices.Where(d => LikelySameSelectedVolume(beforeMatch, d, target)).ToList();
        candidateCount = candidates.Count;
        after = candidates.FirstOrDefault(d =>
            !string.IsNullOrWhiteSpace(d.DriveLetter) &&
            string.Equals(NormalizeLetter(d.DriveLetter), letter, StringComparison.OrdinalIgnoreCase))
            ?? candidates.FirstOrDefault();
        return after is not null;
    }

    private static (IReadOnlyList<string> ReasonCodes, IReadOnlyList<string> PresentTopologyFields, IReadOnlyList<string> MissingTopologyFields) BuildEvidence(
        UsbDeviceInfo before,
        UsbDeviceInfo after)
    {
        var reasons = new HashSet<string>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);

        AddFieldEvidence("stablePortKey", before.StablePortKey, after.StablePortKey);
        AddFieldEvidence("locationPath", before.LocationPathHash, after.LocationPathHash);
        AddFieldEvidence("locationInformation", before.LocationInformationHash, after.LocationInformationHash);
        AddFieldEvidence("locationPaths", before.LocationPathsHash, after.LocationPathsHash);
        AddFieldEvidence("controller", before.ControllerKey, after.ControllerKey);
        AddFieldEvidence("controllerAssociation", before.UsbControllerAssociationHash, after.UsbControllerAssociationHash);
        AddFieldEvidence("hub", before.HubKey, after.HubKey);
        AddFieldEvidence("hubName", before.UsbHubNameHash, after.UsbHubNameHash);
        AddFieldEvidence("hubPath", before.UsbHubPathHash, after.UsbHubPathHash);
        AddFieldEvidence("parentDevice", before.ParentDeviceIdHash, after.ParentDeviceIdHash);
        AddFieldEvidence("parentIdPrefix", before.ParentIdPrefixHash, after.ParentIdPrefixHash);
        AddFieldEvidence("container", before.ContainerIdHash, after.ContainerIdHash);
        AddFieldEvidence("busReportedSpeed", before.BusReportedSpeed, after.BusReportedSpeed);
        AddFieldEvidence("serial", before.SerialHash, after.SerialHash);
        AddFieldEvidence("volumeIdentity", before.VolumeIdentityHash, after.VolumeIdentityHash);

        if (!string.IsNullOrWhiteSpace(before.SerialHash) &&
            string.Equals(before.SerialHash, after.SerialHash, StringComparison.Ordinal))
        {
            reasons.Add("serial-matched");
        }

        if (!string.IsNullOrWhiteSpace(before.VolumeIdentityHash) &&
            string.Equals(before.VolumeIdentityHash, after.VolumeIdentityHash, StringComparison.Ordinal))
        {
            reasons.Add("same-device-identity-matched");
        }

        if (Changed(before.LocationPathHash, after.LocationPathHash) ||
            Changed(before.LocationPathsHash, after.LocationPathsHash) ||
            Changed(before.LocationInformationHash, after.LocationInformationHash))
        {
            reasons.Add("location-path-changed");
        }

        if (Changed(before.ControllerKey, after.ControllerKey) ||
            Changed(before.UsbControllerAssociationHash, after.UsbControllerAssociationHash))
        {
            reasons.Add("controller-changed");
        }

        if (Changed(before.HubKey, after.HubKey) ||
            Changed(before.ParentDeviceIdHash, after.ParentDeviceIdHash) ||
            Changed(before.ParentIdPrefixHash, after.ParentIdPrefixHash) ||
            Changed(before.UsbHubNameHash, after.UsbHubNameHash) ||
            Changed(before.UsbHubPathHash, after.UsbHubPathHash))
        {
            reasons.Add("hub-parent-changed");
        }

        if (Changed(before.StablePortKey, after.StablePortKey))
        {
            reasons.Add("stable-port-key-changed");
        }

        if (present.Count == 0)
        {
            reasons.Add("no-topology-fields-exposed");
        }

        return (
            reasons.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            present.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            missing.OrderBy(x => x, StringComparer.Ordinal).ToArray());

        void AddFieldEvidence(string name, string? b, string? a)
        {
            if (!string.IsNullOrWhiteSpace(b) || !string.IsNullOrWhiteSpace(a))
            {
                present.Add(name);
            }
            else
            {
                missing.Add(name);
            }
        }

        static bool Changed(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) &&
            !string.IsNullOrWhiteSpace(b) &&
            !string.Equals(a, b, StringComparison.Ordinal);
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
