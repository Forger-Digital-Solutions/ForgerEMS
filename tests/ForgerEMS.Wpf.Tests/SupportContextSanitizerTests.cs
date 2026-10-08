using System;
using VentoyToolkitSetup.Wpf.Services;
namespace ForgerEMS.Wpf.Tests;

public sealed class SupportContextSanitizerTests
{
    [Fact]
    public void RemovesWindowsPathsAndUnc()
    {
        var s = SupportContextSanitizer.SanitizeForSupport(@"Run from C:\Users\alice\app\foo.exe and \\server\share\secret");
        Assert.DoesNotContain("alice", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[path redacted]", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovesProductKeyLikePattern()
    {
        var s = SupportContextSanitizer.SanitizeForSupport("Key ABCDE-FGHIJ-KLMNO-PQRST-UVWXY trailing");
        Assert.DoesNotContain("ABCDE", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[key redacted]", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovesEmailsAndLongTokens()
    {
        var s = SupportContextSanitizer.SanitizeForSupport(
            "contact user@example.com token 0123456789abcdef0123456789abcdef");
        Assert.DoesNotContain("example.com", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[email redacted]", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[token redacted]", s, StringComparison.OrdinalIgnoreCase);
    }
}
