using System;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class AppSemanticVersionTests
{
    [Theory]
    [InlineData("1.1.4", "1.1.4", 0)]
    [InlineData("v1.1.4", "1.1.4", 0)]
    [InlineData("ForgerEMS-v1.1.4", "v1.1.4", 0)]
    [InlineData("1.1.4.0", "1.1.4", 0)]
    [InlineData("1.1.3", "1.1.4", -1)]
    [InlineData("1.1.5", "1.1.4", 1)]
    [InlineData("1.1.4-beta.2", "1.1.4-beta.3", -1)]
    [InlineData("1.1.4-beta.3", "1.1.4-beta.2", 1)]
    [InlineData("1.1.4-beta.2", "1.1.4", -1)]
    [InlineData("1.1.4", "1.1.4-beta.2", 1)]
    [InlineData("1.1.4-rc.1", "1.1.4-beta.9", 1)]
    public void CompareTo_MatchesExpectedOrder(string a, string b, int expectedSign)
    {
        Assert.True(AppSemanticVersion.TryParse(a, out var av));
        Assert.True(AppSemanticVersion.TryParse(b, out var bv));
        var cmp = av.CompareTo(bv);
        Assert.Equal(expectedSign, Math.Sign(cmp));
    }

    [Fact]
    public void TryParse_RejectsInvalid()
    {
        Assert.False(AppSemanticVersion.TryParse("not-a-version", out _));
        Assert.False(AppSemanticVersion.TryParse("", out _));
    }

    [Theory]
    [InlineData("-1.2.3")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3-")]
    [InlineData("1.2.3-beta..1")]
    [InlineData("1.2.3-alpha_1")]
    [InlineData("1.2.3-01")]
    [InlineData("1.2.3-1.02")]
    [InlineData("1. 2.3")]
    [InlineData("1.2.3 +meta")]
    [InlineData("1.2.3+")]
    [InlineData("1.2.3+a..b")]
    [InlineData("1.2.3+bad_id")]
    [InlineData("1.2.3+a+b")]
    [InlineData("1.2.3-beta+c+d")]
    [InlineData("v")]
    [InlineData("1.2.x")]
    [InlineData("99999999999.0.0")]
    public void TryParse_RejectsMalformed(string input)
    {
        Assert.False(AppSemanticVersion.TryParse(input, out _), input);
    }

    [Theory]
    [InlineData("1.2.3-0")]
    [InlineData("1.2.3-alpha-1")]
    [InlineData("1.2.3-x-y-z")]
    [InlineData("1.2.3+build.5")]
    [InlineData("1.2.3+001")]
    [InlineData("1.2.3-rc.1+meta.9")]
    [InlineData("1.2.3.4")]
    public void TryParse_AcceptsValidEdgeCases(string input)
    {
        Assert.True(AppSemanticVersion.TryParse(input, out _), input);
    }

    [Fact]
    public void BuildMetadata_IgnoredForPrecedence()
    {
        Assert.True(AppSemanticVersion.TryParse("1.2.3+build.5", out var withMeta));
        Assert.True(AppSemanticVersion.TryParse("1.2.3", out var plain));
        Assert.Equal(0, withMeta.CompareTo(plain));
        Assert.Equal(plain, withMeta);
        Assert.Equal(plain.GetHashCode(), withMeta.GetHashCode());
    }

    [Fact]
    public void NumericPrerelease_HugeIdentifiers_CompareWithoutOverflow()
    {
        Assert.True(AppSemanticVersion.TryParse("1.0.0-99999999999999999999", out var a));
        Assert.True(AppSemanticVersion.TryParse("1.0.0-100000000000000000000", out var b));
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(a));
    }

    [Fact]
    public void Equality_AndHash_ConsistentAcrossPrefixForms()
    {
        Assert.True(AppSemanticVersion.TryParse("1.2.3", out var a));
        Assert.True(AppSemanticVersion.TryParse("v1.2.3", out var b));
        Assert.True(AppSemanticVersion.TryParse("ForgerEMS-v1.2.3", out var c));
        Assert.Equal(a, b);
        Assert.Equal(b, c);
        Assert.Equal(a.GetHashCode(), c.GetHashCode());
    }

    [Fact]
    public void Equality_AndHash_ConsistentForConstructedEmptyPrereleaseIdentifiers()
    {
        // The public constructor does not validate prerelease text; CompareTo drops empty
        // identifiers, so the hash must normalize the same way.
        var a = new AppSemanticVersion(1, 2, 3, 0, "alpha..1");
        var b = new AppSemanticVersion(1, 2, 3, 0, "alpha.1");
        Assert.Equal(0, a.CompareTo(b));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void CompareTo_NumericIdentifiers_LongerDigitStringWins()
    {
        Assert.True(AppSemanticVersion.TryParse("1.0.0-beta.10", out var a));
        Assert.True(AppSemanticVersion.TryParse("1.0.0-beta.9", out var b));
        Assert.True(a.CompareTo(b) > 0);
    }

    [Fact]
    public void ToLegacyVersion_DropsPrerelease()
    {
        Assert.True(AppSemanticVersion.TryParse("2.0.0-beta.1", out var v));
        Assert.Equal(new System.Version(2, 0, 0), v.ToLegacyVersion());
    }
}
