using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraLocalSpecAnswerBuilderTests
{
    [Fact]
    public void Precision5540Profile_CpuQuestion_ReturnsCpuLine()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell Inc.",
            Model = "Precision 5540",
            Cpu = "Intel(R) Core(TM) i7-9850H CPU @ 2.60GHz",
            CpuCores = 6,
            CpuThreads = 12,
            RamTotalGb = 31.7,
            RamTotal = "32 GB",
            RamSpeed = "2667 MT/s",
            Gpus =
            [
                new SystemGpuProfile { Name = "Intel(R) UHD Graphics 630", GpuKind = "Integrated" },
                new SystemGpuProfile { Name = "NVIDIA Quadro T2000", GpuKind = "Discrete" }
            ]
        };

        Assert.True(KyraLocalSpecAnswerBuilder.TryBuildLocalSpecAnswer("What CPU do I have?", profile, out var r));
        Assert.Contains("i7-9850H", r.Text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grounded in latest System Intelligence scan", r.Text, System.StringComparison.OrdinalIgnoreCase);
        Assert.True(r.GroundedInSystemIntelligence);
    }

    [Fact]
    public void MissingScan_DoesNotInventSpecs()
    {
        Assert.True(KyraLocalSpecAnswerBuilder.TryBuildLocalSpecAnswer("How much RAM?", null, out var r));
        Assert.Contains("Run System Intelligence first", r.Text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no current scan available", r.Text, System.StringComparison.OrdinalIgnoreCase);
        Assert.False(r.GroundedInSystemIntelligence);
    }

    [Fact]
    public void NonSpecQuestion_NotHandled()
    {
        Assert.False(KyraLocalSpecAnswerBuilder.TryBuildLocalSpecAnswer("Write a poem about USB drives.", new SystemProfile(), out _));
    }
}
