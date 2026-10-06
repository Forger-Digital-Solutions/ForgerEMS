using System.Collections.Generic;
using System.Linq;
using ForgerEMS.Wpf.Services.WindowsMaintenance;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class DriverStoreClassifierTests
{
    private static DriverStorePackageClassification Pkg(
        string published = "oem1.inf",
        string? original = "realtek.inf",
        string? provider = "Realtek",
        string? @class = "Net",
        string? version = "10.1.0.0",
        string? signer = "Microsoft Windows Hardware Compatibility Publisher",
        string? sigState = "Signed",
        string? arch = "x64",
        bool? boot = false,
        IReadOnlyList<string>? deviceIds = null) =>
        new(published, original, provider, @class, version, signer, sigState, arch, boot, deviceIds);

    private static WindowsDriverBinding Bound(string published, string? deviceId = "PCI\\VEN_10EC&DEV_8168") =>
        new() { PublishedName = published, DeviceId = deviceId };

    [Fact]
    public void BoundPackage_IsProtected()
    {
        var results = DriverStoreClassifier.Classify(
            new[] { Pkg(published: "oem5.inf") },
            new[] { Bound("oem5.inf") });
        var a = Assert.Single(results);
        Assert.Equal(DriverStoreClassification.BoundProtected, a.Classification);
        Assert.False(a.RemovalPermitted);
    }

    [Fact]
    public void DisconnectedBinding_StillProtects()
    {
        // Bindings list includes disconnected devices; published-name match is enough.
        var results = DriverStoreClassifier.Classify(
            new[] { Pkg(published: "oem7.inf") },
            new[] { new WindowsDriverBinding { PublishedName = "oem7.inf", DeviceId = "PCI\\VEN_DISC" } });
        Assert.Equal(DriverStoreClassification.BoundProtected, results[0].Classification);
    }

    [Theory]
    [InlineData("System")]
    [InlineData("SCSIAdapter")]
    [InlineData("HDC")]
    [InlineData("Net")]
    [InlineData("Firmware")]
    [InlineData("USB")]
    public void CriticalClass_IsProtected(string @class)
    {
        var results = DriverStoreClassifier.Classify(
            new[] { Pkg(published: "oem9.inf", @class: @class) },
            new List<WindowsDriverBinding>());
        Assert.Equal(DriverStoreClassification.CriticalClassProtected, results[0].Classification);
    }

    [Fact]
    public void BootCritical_IsProtected()
    {
        var results = DriverStoreClassifier.Classify(
            new[] { Pkg(published: "oem0.inf", @class: "Other", boot: true) },
            new List<WindowsDriverBinding>());
        Assert.Equal(DriverStoreClassification.BootCriticalProtected, results[0].Classification);
    }

    [Fact]
    public void LowerVersionSameIdentity_PossibleRollback()
    {
        var pkgs = new[]
        {
            Pkg(published: "oem1.inf", @class: "VendorWidget", version: "10.5.0.0"),  // bound, higher version
            Pkg(published: "oem2.inf", @class: "VendorWidget", version: "10.1.0.0")   // unbound, lower version
        };
        var results = DriverStoreClassifier.Classify(pkgs, new[] { Bound("oem1.inf") });
        Assert.Equal(DriverStoreClassification.BoundProtected, results[0].Classification);
        Assert.Equal(DriverStoreClassification.PossibleRollback, results[1].Classification);
        Assert.False(results[1].RemovalPermitted);
        Assert.Contains("manual review", results[1].Explanation, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TwoLowerVersions_BestIsRollback_OtherIsSuperseded()
    {
        var pkgs = new[]
        {
            Pkg(published: "oem1.inf", @class: "VendorWidget", version: "10.5.0.0"),
            Pkg(published: "oem2.inf", @class: "VendorWidget", version: "10.1.0.0"),
            Pkg(published: "oem3.inf", @class: "VendorWidget", version: "10.0.0.0")
        };
        var results = DriverStoreClassifier.Classify(pkgs, new[] { Bound("oem1.inf") });
        Assert.Equal(DriverStoreClassification.PossibleRollback, results[1].Classification);
        Assert.Equal(DriverStoreClassification.PossibleSuperseded, results[2].Classification);
        Assert.All(results, r => Assert.False(r.RemovalPermitted));
    }

    [Fact]
    public void UnknownIdentityFields_NeverSuperseded()
    {
        // Missing original INF name → cannot establish identity → protected-unknown.
        var pkgs = new[]
        {
            Pkg(published: "oem1.inf", @class: "VendorWidget", version: "10.5.0.0"),
            Pkg(published: "oem2.inf", @class: "VendorWidget", original: null, version: "10.1.0.0")
        };
        var results = DriverStoreClassifier.Classify(pkgs, new[] { Bound("oem1.inf") });
        Assert.Equal(DriverStoreClassification.UnknownProtected, results[1].Classification);
    }

    [Fact]
    public void UnsignedUnbound_StaysUnknownProtected()
    {
        var pkgs = new[]
        {
            Pkg(published: "oem1.inf", @class: "VendorWidget", version: "10.5.0.0"),
            Pkg(published: "oem2.inf", @class: "VendorWidget", version: "10.1.0.0", sigState: "Unsigned")
        };
        var results = DriverStoreClassifier.Classify(pkgs, new[] { Bound("oem1.inf") });
        Assert.Equal(DriverStoreClassification.UnknownProtected, results[1].Classification);
    }

    [Fact]
    public void NoBindings_AllUnknownProtected()
    {
        var results = DriverStoreClassifier.Classify(
            new[] { Pkg(@class: "VendorWidget") },
            new List<WindowsDriverBinding>());
        Assert.Equal(DriverStoreClassification.UnknownProtected, results[0].Classification);
    }

    [Fact]
    public void Removal_NeverPermitted_AcrossAllClassifications()
    {
        var pkgs = new[]
        {
            Pkg(published: "oem1.inf", version: "10.5.0.0"),
            Pkg(published: "oem2.inf", version: "10.1.0.0"),
            Pkg(published: "oem9.inf", original: "other.inf", provider: "X", @class: "VendorWidget")
        };
        var results = DriverStoreClassifier.Classify(pkgs, new[] { Bound("oem1.inf") });
        Assert.All(results, r => Assert.False(r.RemovalPermitted));
        Assert.DoesNotContain(results, r =>
            r.Classification is not (DriverStoreClassification.BoundProtected
                or DriverStoreClassification.PossibleRollback
                or DriverStoreClassification.UnknownProtected
                or DriverStoreClassification.CriticalClassProtected
                or DriverStoreClassification.PossibleSuperseded
                or DriverStoreClassification.BootCriticalProtected));
    }
}

public sealed class DriverUpdateApplicabilityTests
{
    private static WindowsUpdateItem Offer(string? hardwareId) => new()
    {
        Title = "Driver update",
        Kind = "Driver",
        HardwareId = hardwareId
    };

    private static WindowsDriverBinding Binding(string deviceId, params string[] hardwareIds) =>
        new() { DeviceId = deviceId, HardwareIds = hardwareIds.ToList<string?>() };

    [Fact]
    public void ExactHardwareId_CandidateOnly()
    {
        var offer = Offer("PCI\\VEN_10EC&DEV_8168");
        var binding = Binding("PCI\\VEN_10EC&DEV_8168&SUBSYS_803B1043", "PCI\\VEN_10EC&DEV_8168");
        var match = DriverUpdateApplicability.MatchOffer(offer, new[] { binding });
        Assert.Equal(DriverUpdateApplicability.MatchKind.ExactHardwareId, match.Kind);
        Assert.False(match.InstallationPermitted);
        Assert.Equal("PCI\\VEN_10EC&DEV_8168&SUBSYS_803B1043", match.MatchedDeviceId);
        Assert.Contains("manual review", match.Reason, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NoHardwareIdMatch_NotApplicable()
    {
        var offer = Offer("PCI\\VEN_8086&DEV_1234");
        var match = DriverUpdateApplicability.MatchOffer(offer, new[] { Binding("PCI\\VEN_10EC&DEV_8168") });
        Assert.Equal(DriverUpdateApplicability.MatchKind.NoMatch, match.Kind);
    }

    [Fact]
    public void MissingHardwareId_NotApplicable()
    {
        var match = DriverUpdateApplicability.MatchOffer(Offer(null), new[] { Binding("PCI\\VEN_X") });
        Assert.Equal(DriverUpdateApplicability.MatchKind.NoMatch, match.Kind);
    }

    [Fact]
    public void NonDriverOffer_Rejected()
    {
        var match = DriverUpdateApplicability.MatchOffer(
            new WindowsUpdateItem { Title = "Cumulative", Kind = "Update" },
            new[] { Binding("PCI\\VEN_X") });
        Assert.Equal(DriverUpdateApplicability.MatchKind.Rejected, match.Kind);
    }

    [Fact]
    public void CompatibleId_WeakerCandidate()
    {
        var offer = Offer("PCI\\VEN_10EC");
        var binding = new WindowsDriverBinding
        {
            DeviceId = "PCI\\VEN_10EC&DEV_8168",
            CompatibleIds = new List<string?> { "PCI\\VEN_10EC" }
        };
        var match = DriverUpdateApplicability.MatchOffer(offer, new[] { binding });
        Assert.Equal(DriverUpdateApplicability.MatchKind.CompatibleId, match.Kind);
        Assert.False(match.InstallationPermitted);
    }
}
