using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class SystemIntelligenceFormatterTests
{
    [Fact]
    public void FriendlyUnknownUsesReasonInsteadOfRawUnknown()
    {
        Assert.Equal("Not reported by firmware", SystemIntelligenceFormatter.FriendlyUnknown("UNKNOWN", "Not reported by firmware"));
        Assert.Equal("No sensor exposed", SystemIntelligenceFormatter.FriendlyUnknown("", "No sensor exposed"));
        Assert.Equal("Enabled", SystemIntelligenceFormatter.FriendlyUnknown("Enabled", "Unavailable"));
    }

    [Fact]
    public void RamSpeedSummarySeparatesConfiguredAndRatedSpeeds()
    {
        var summary = SystemIntelligenceFormatter.FormatRamSpeedSummary(
            "32 GB DDR4",
            "2667 MT/s",
            "3200 MT/s",
            "Slots: 2/2 used");

        Assert.Contains("32 GB DDR4", summary);
        Assert.Contains("configured 2667 MT/s", summary);
        Assert.Contains("rated 3200 MT/s", summary);
        Assert.Contains("Slots: 2/2 used", summary);
    }

    [Fact]
    public void BatteryWearExplainsMissingDesignCapacity()
    {
        var wear = SystemIntelligenceFormatter.FormatBatteryWear(null, designCapacityReported: false, fullChargeCapacityReported: true);

        Assert.Equal("Wear unavailable - design capacity not reported", wear);
    }

    [Fact]
    public void VirtualAdaptersAreIgnoredForWarnings()
    {
        Assert.True(SystemIntelligenceFormatter.ShouldIgnoreAdapterForWarnings("VirtualBox Host-Only Network", "VirtualBox Ethernet Adapter"));
        Assert.True(SystemIntelligenceFormatter.ShouldIgnoreAdapterForWarnings("vEthernet", "Hyper-V Virtual Ethernet Adapter"));
        Assert.False(SystemIntelligenceFormatter.ShouldIgnoreAdapterForWarnings("Ethernet", "ASIX USB Ethernet Adapter"));
    }

    [Fact]
    public void TpmFriendlyWordingNormalizesCommonStates()
    {
        Assert.Equal("TPM ready for Windows 11", SystemIntelligenceFormatter.FormatTpmFriendly(true, true, true, true));
        Assert.Equal("TPM disabled in firmware", SystemIntelligenceFormatter.FormatTpmFriendly(true, false, false, false));
        Assert.Equal("TPM present but not ready", SystemIntelligenceFormatter.FormatTpmFriendly(true, true, false, false));
        Assert.Equal("TPM not detected", SystemIntelligenceFormatter.FormatTpmFriendly(false, false, false, false));
    }
}
