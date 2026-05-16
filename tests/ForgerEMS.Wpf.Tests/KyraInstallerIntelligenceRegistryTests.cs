using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraInstallerIntelligenceRegistryTests
{
    [Fact]
    public void ApplySnapshotToSettings_RepairOnly_EnablesMasterWithoutSubs()
    {
        var s = new CopilotSettings();
        KyraInstallerIntelligenceRegistry.ApplySnapshotToSettings(
            s,
            new KyraInstallerIntelligenceRegistry.InstallerKyraConsentSnapshot(1, 0, 0, 0));
        Assert.True(s.KyraCommunitySharingEnabled);
        Assert.False(s.KyraShareHardwareCompatibilityPerformancePatterns);
        Assert.False(s.KyraShareResolvedIssueFixPatterns);
        Assert.False(s.KyraShareCrashErrorDiagnostics);
    }

    [Fact]
    public void ApplySnapshotToSettings_HardwareOnly_SetsMasterAndHardware()
    {
        var s = new CopilotSettings();
        KyraInstallerIntelligenceRegistry.ApplySnapshotToSettings(
            s,
            new KyraInstallerIntelligenceRegistry.InstallerKyraConsentSnapshot(0, 1, 0, 0));
        Assert.True(s.KyraCommunitySharingEnabled);
        Assert.True(s.KyraShareHardwareCompatibilityPerformancePatterns);
        Assert.False(s.KyraShareResolvedIssueFixPatterns);
        Assert.False(s.KyraShareCrashErrorDiagnostics);
    }

    [Fact]
    public void ApplySnapshotToSettings_AllOff_LeavesCommunityDisabled()
    {
        var s = new CopilotSettings { KyraCommunitySharingEnabled = true, KyraShareResolvedIssueFixPatterns = true };
        KyraInstallerIntelligenceRegistry.ApplySnapshotToSettings(
            s,
            new KyraInstallerIntelligenceRegistry.InstallerKyraConsentSnapshot(0, 0, 0, 0));
        Assert.False(s.KyraCommunitySharingEnabled);
        Assert.False(s.KyraShareHardwareCompatibilityPerformancePatterns);
        Assert.False(s.KyraShareResolvedIssueFixPatterns);
        Assert.False(s.KyraShareCrashErrorDiagnostics);
    }
}
