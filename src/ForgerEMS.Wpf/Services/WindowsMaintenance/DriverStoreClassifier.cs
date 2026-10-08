using System;
using System.Collections.Generic;
using System.Linq;

namespace ForgerEMS.Wpf.Services.WindowsMaintenance;

public enum DriverStoreClassification
{
    /// <summary>Bound to a device binding (connected or disconnected). Protected.</summary>
    BoundProtected,

    /// <summary>Boot-critical package. Protected.</summary>
    BootCriticalProtected,

    /// <summary>Critical device class (System/SCSIAdapter/HDC/Net/Firmware/USB/storage/network). Protected.</summary>
    CriticalClassProtected,

    /// <summary>
    /// Unbound signed package sharing identity with a bound package at a higher version,
    /// when a better rollback candidate exists. Protected.
    /// </summary>
    PossibleSuperseded,

    /// <summary>
    /// Unbound signed package sharing identity with a bound package at a higher version;
    /// the highest such version is the most plausible rollback. Protected.
    /// </summary>
    PossibleRollback,

    /// <summary>Unbound without proof of obsolescence or safety. Protected.</summary>
    UnknownProtected
}

public sealed record DriverStoreAssessment
{
    public required DriverStorePackageClassification Package { get; init; }
    public required DriverStoreClassification Classification { get; init; }
    public required string Explanation { get; init; }

    /// <summary>Always false — classification is diagnostic only; no automatic removal.</summary>
    public bool RemovalPermitted => false;
}

public sealed record DriverStorePackageClassification(
    string? PublishedName,
    string? OriginalName,
    string? Provider,
    string? Class,
    string? Version,
    string? Signer,
    string? SignatureState,
    string? Architecture,
    bool? BootCritical,
    IReadOnlyList<string>? DeviceIds);

/// <summary>
/// Pure classification of Driver Store packages against device bindings. Produces diagnostic
/// explanations only — nothing here ever authorizes deletion.
/// </summary>
public static class DriverStoreClassifier
{
    private static readonly HashSet<string> CriticalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "SCSIAdapter", "HDC", "Net", "Firmware", "USB", "USBDevice",
        "Storage", "DiskDrive", "Volume", "VolumeSnapshot", "Media", "Mouse",
        "Keyboard", "HIDClass", "Display", "Computer"
    };

    public static IReadOnlyList<DriverStoreAssessment> Classify(
        IReadOnlyList<DriverStorePackageClassification> packages,
        IReadOnlyList<WindowsDriverBinding> bindings)
    {
        var boundPublishedNames = new HashSet<string>(
            bindings.Where(b => !string.IsNullOrWhiteSpace(b.PublishedName))
                .Select(b => b.PublishedName!),
            StringComparer.OrdinalIgnoreCase);

        var boundDeviceIds = new HashSet<string>(
            bindings.Where(b => !string.IsNullOrWhiteSpace(b.DeviceId)).Select(b => b.DeviceId!),
            StringComparer.OrdinalIgnoreCase);

        // Identify the single best rollback candidate per identity group: the highest-version
        // unbound signed package below a bound same-identity package. Siblings are PossibleSuperseded.
        var rollbackEligible = new HashSet<DriverStorePackageClassification>();
        var supersededEligible = new HashSet<DriverStorePackageClassification>();
        // Identity correlation requires ALL THREE fields non-null: original INF name,
        // provider, and class. Any unknown disqualifies superseded/rollback candidacy.
        var identityGroups = packages
            .Where(p => !string.IsNullOrWhiteSpace(p.OriginalName)
                && !string.IsNullOrWhiteSpace(p.Provider)
                && !string.IsNullOrWhiteSpace(p.Class))
            .GroupBy(p => IdentityKey(p), StringComparer.OrdinalIgnoreCase);

        foreach (var group in identityGroups)
        {
            var bound = group
                .Where(p => p.PublishedName is not null && boundPublishedNames.Contains(p.PublishedName))
                .ToList();
            if (bound.Count == 0)
            {
                continue;
            }

            var boundMax = bound
                .Select(p => TryParseVersion(p.Version, out var v) ? v : null)
                .Where(v => v is not null)
                .OrderByDescending(v => v)
                .FirstOrDefault();
            if (boundMax is null)
            {
                continue;
            }

            var unboundSignedLower = group
                .Where(p => p.PublishedName is not null
                    && !boundPublishedNames.Contains(p.PublishedName)
                    && IsSigned(p)
                    && TryParseVersion(p.Version, out var v)
                    && v < boundMax)
                .ToList();
            if (unboundSignedLower.Count == 0)
            {
                continue;
            }

            var best = unboundSignedLower
                .OrderByDescending(p => TryParseVersion(p.Version, out var v) ? v : new Version(0, 0))
                .First();
            rollbackEligible.Add(best);
            foreach (var sibling in unboundSignedLower.Where(p => !ReferenceEquals(p, best)))
            {
                supersededEligible.Add(sibling);
            }
        }

        var results = new List<DriverStoreAssessment>(packages.Count);
        foreach (var package in packages)
        {
            var classification = ClassifyOne(
                package, boundPublishedNames, boundDeviceIds, rollbackEligible, supersededEligible);
            results.Add(new DriverStoreAssessment
            {
                Package = package,
                Classification = classification,
                Explanation = Explain(package, classification)
            });
        }

        return results;
    }

    private static string IdentityKey(DriverStorePackageClassification p) =>
        $"{p.OriginalName}|{p.Provider}|{p.Class}";

    private static bool IsSigned(DriverStorePackageClassification package) =>
        string.Equals(package.SignatureState, "Signed", StringComparison.OrdinalIgnoreCase)
        || (!string.IsNullOrWhiteSpace(package.Signer)
            && !string.Equals(package.SignatureState, "Unsigned", StringComparison.OrdinalIgnoreCase));

    private static DriverStoreClassification ClassifyOne(
        DriverStorePackageClassification package,
        HashSet<string> boundPublishedNames,
        HashSet<string> boundDeviceIds,
        HashSet<DriverStorePackageClassification> rollbackEligible,
        HashSet<DriverStorePackageClassification> supersededEligible)
    {
        if (package.BootCritical == true)
        {
            return DriverStoreClassification.BootCriticalProtected;
        }

        if (package.PublishedName is not null && boundPublishedNames.Contains(package.PublishedName))
        {
            return DriverStoreClassification.BoundProtected;
        }

        if (package.Class is not null && CriticalClasses.Contains(package.Class))
        {
            return DriverStoreClassification.CriticalClassProtected;
        }

        if (package.DeviceIds is not null
            && package.DeviceIds.Any(id => id is not null && boundDeviceIds.Contains(id)))
        {
            return DriverStoreClassification.BoundProtected;
        }

        if (rollbackEligible.Contains(package))
        {
            return DriverStoreClassification.PossibleRollback;
        }

        if (supersededEligible.Contains(package))
        {
            return DriverStoreClassification.PossibleSuperseded;
        }

        return DriverStoreClassification.UnknownProtected;
    }

    private static string Explain(
        DriverStorePackageClassification package,
        DriverStoreClassification classification) =>
        classification switch
        {
            DriverStoreClassification.BootCriticalProtected =>
                $"Protected: {package.PublishedName} is marked boot-critical.",
            DriverStoreClassification.BoundProtected =>
                $"Protected: {package.PublishedName} is bound to a device binding (connected or disconnected).",
            DriverStoreClassification.CriticalClassProtected =>
                $"Protected: {package.PublishedName} belongs to critical class '{package.Class}'.",
            DriverStoreClassification.PossibleRollback =>
                $"Protected: {package.PublishedName} shares identity with a bound package at a higher version; possible rollback — manual review required, removal never permitted automatically.",
            DriverStoreClassification.PossibleSuperseded =>
                $"Protected: {package.PublishedName} may be superseded by a bound same-identity package at a higher version; diagnostic only — removal never permitted automatically.",
            _ =>
                $"Protected: {package.PublishedName} is unbound with unknown boot/dependency/architecture/signature state; not eligible for removal."
        };

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var numeric = new string(text.Trim().TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return numeric.Length > 0 && Version.TryParse(numeric, out version!);
    }
}

/// <summary>
/// Applicability matching between a WUA driver offer and local device bindings.
/// An exact hardware-id/compatible-id match only marks the offer as a CANDIDATE — driver
/// ranking, OEM-vs-generic suitability, architecture, and signature state are NOT proven
/// by this data and always require manual review. Nothing here authorizes installation.
/// </summary>
public static class DriverUpdateApplicability
{
    public enum MatchKind
    {
        /// <summary>Exact local hardware-id match — candidate offer, manual review required.</summary>
        ExactHardwareId,

        /// <summary>Compatible-id match — weaker evidence, manual review required.</summary>
        CompatibleId,
        NoMatch,
        Rejected
    }

    public sealed record OfferMatch(
        MatchKind Kind,
        string? MatchedDeviceId,
        string? BoundPublishedName,
        string Reason)
    {
        /// <summary>Always false — offers are advisory, never auto-installable.</summary>
        public bool InstallationPermitted => false;
    }

    public static OfferMatch MatchOffer(
        WindowsUpdateItem offer,
        IReadOnlyList<WindowsDriverBinding> bindings)
    {
        if (!string.Equals(offer.Kind, "Driver", StringComparison.OrdinalIgnoreCase))
        {
            return new OfferMatch(MatchKind.Rejected, null, null, "Offer is not a driver update.");
        }

        var hardwareId = offer.HardwareId;
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return new OfferMatch(MatchKind.NoMatch, null, null,
                "Offer carries no hardware id; local applicability cannot be established.");
        }

        const string caveat =
            " Applicability is a hardware-id match only; driver rank, OEM-vs-generic suitability, " +
            "architecture, and signature state are unknown — manual review required.";

        foreach (var binding in bindings)
        {
            var hardwareIds = binding.HardwareIds?.Where(h => h is not null).Select(h => h!) ?? Enumerable.Empty<string>();
            if (hardwareIds.Contains(hardwareId, StringComparer.OrdinalIgnoreCase))
            {
                return new OfferMatch(MatchKind.ExactHardwareId, binding.DeviceId, binding.PublishedName,
                    "WUA-offered candidate: exact hardware-id match." + caveat);
            }

            var compatibleIds = binding.CompatibleIds?.Where(c => c is not null).Select(c => c!) ?? Enumerable.Empty<string>();
            if (compatibleIds.Contains(hardwareId, StringComparer.OrdinalIgnoreCase))
            {
                return new OfferMatch(MatchKind.CompatibleId, binding.DeviceId, binding.PublishedName,
                    "WUA-offered candidate: compatible-id match." + caveat);
            }
        }

        return new OfferMatch(MatchKind.NoMatch, null, null,
            "Offer hardware id does not match any local hardware or compatible id.");
    }
}
