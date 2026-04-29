using VentoyToolkitSetup.Wpf.Models;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbTargetInfoTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    public void FormatBytesReturnsExpectedDisplay(long bytes, string expected)
    {
        var actual = UsbTargetInfo.FormatBytes(bytes);

        Assert.Equal(expected, actual);
    }
}
