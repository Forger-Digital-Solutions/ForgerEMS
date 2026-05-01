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

    [Fact]
    public void ToLegacyVersion_DropsPrerelease()
    {
        Assert.True(AppSemanticVersion.TryParse("2.0.0-beta.1", out var v));
        Assert.Equal(new System.Version(2, 0, 0), v.ToLegacyVersion());
    }
}
