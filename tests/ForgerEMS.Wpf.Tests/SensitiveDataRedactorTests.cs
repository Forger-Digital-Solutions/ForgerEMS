using System;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class SensitiveDataRedactorTests
{
    [Fact]
    public void SanitizeForSupportShare_RedactsWindowsPath()
    {
        var raw = "Log at C:\\Users\\Tester\\secret\\file.log";
        var s = SensitiveDataRedactor.SanitizeForSupportShare(raw);
        Assert.DoesNotContain("Tester", s, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redacted", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeForSupportShare_RedactsProductKeyLikePattern()
    {
        var raw = "Key ABCDE-12345-FGHIJ-67890-KLMNO end";
        var s = SensitiveDataRedactor.SanitizeForSupportShare(raw);
        Assert.DoesNotContain("12345-FGHIJ", s, StringComparison.Ordinal);
        Assert.Contains("redacted", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizeForSupportShare_RedactsSerialLine()
    {
        var raw = "SERIAL: ABC12-XY999-ZZ001";
        var s = SensitiveDataRedactor.SanitizeForSupportShare(raw);
        Assert.Contains("id redacted", s, StringComparison.OrdinalIgnoreCase);
    }
}
