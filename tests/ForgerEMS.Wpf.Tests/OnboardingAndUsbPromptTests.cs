using System.IO;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class OnboardingAndUsbPromptTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new System.InvalidOperationException("Repo root not found.");
        }
    }

    [Fact]
    public void KyraOnboardingCopy_MentionsCapabilitiesAndExamples()
    {
        Assert.Contains("beta", KyraOnboardingCopy.InitialWelcomeMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USB Intelligence", KyraOnboardingCopy.InitialWelcomeMessage, System.StringComparison.Ordinal);
        Assert.Contains("System Intelligence", KyraOnboardingCopy.InitialWelcomeMessage, System.StringComparison.Ordinal);
        Assert.Contains("map USB ports", KyraOnboardingCopy.InitialWelcomeMessage, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/help", KyraOnboardingCopy.InitialWelcomeMessage, System.StringComparison.Ordinal);
        Assert.Contains("Kyra Advanced settings", KyraOnboardingCopy.InitialWelcomeMessage, System.StringComparison.Ordinal);
        Assert.DoesNotContain("KYRA_PROVIDER_ENVIRONMENT_SETUP.md", KyraOnboardingCopy.InitialWelcomeMessage, System.StringComparison.Ordinal);
    }

    [Fact]
    public void KyraOnboardingCopy_MentionsGatewayWhenRealtimeEnabled()
    {
        var text = KyraOnboardingCopy.BuildInitialWelcomeMessage(new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = true,
            KyraRealtimeGatewayResearchEnabled = true,
            KyraRealtimeGatewayResearchConsent = true
        });

        Assert.Contains("Realtime Gateway is enabled", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Live tools may be used", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("double-check USB targets", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("docs/", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraOnboardingCopy_MentionsOnlineOffWhenGatewayDisabled()
    {
        var text = KyraOnboardingCopy.BuildInitialWelcomeMessage(new CopilotSettings
        {
            KyraRealtimeGatewayEnabled = false,
            KyraRealtimeGatewayResearchEnabled = false
        });

        Assert.Contains("Optional online paths stay off until enabled", text, System.StringComparison.OrdinalIgnoreCase);
        Assert.Contains("double-check USB targets", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbTargetBenchmarkUi_DetectsCompleteStatus()
    {
        var t = new UsbTargetInfo
        {
            IsSelectable = true,
            BenchmarkStatus = "Complete",
            ReadSpeedDisplay = "10 MB/s",
            WriteSpeedDisplay = "10 MB/s"
        };
        Assert.True(UsbTargetBenchmarkUi.HasSuccessfulMeasuredBenchmark(t));
    }

    [Fact]
    public void UsbTargetBenchmarkUi_NotTested_IsFalse()
    {
        var t = new UsbTargetInfo
        {
            IsSelectable = true,
            BenchmarkStatus = "Not tested",
            ReadSpeedDisplay = "Not tested",
            WriteSpeedDisplay = "Not tested"
        };
        Assert.False(UsbTargetBenchmarkUi.HasSuccessfulMeasuredBenchmark(t));
    }

    [Fact]
    public void UsbIntelligencePanelUiCopy_TierLabels_AreLowMediumHigh()
    {
        Assert.Equal(UsbIntelligencePanelUiCopy.ConfidenceLow, UsbIntelligencePanelUiCopy.ConfidenceTierLabel(false, 0));
        Assert.Equal(UsbIntelligencePanelUiCopy.ConfidenceMedium, UsbIntelligencePanelUiCopy.ConfidenceTierLabel(true, 55));
        Assert.Equal(UsbIntelligencePanelUiCopy.ConfidenceHigh, UsbIntelligencePanelUiCopy.ConfidenceTierLabel(true, 72));
    }

    [Fact]
    public void UsbSelectedNotBenchmarkedPrompt_MatchesUserCopy()
    {
        Assert.Equal(
            "This USB hasn't been tested yet. Run a quick benchmark.",
            UsbIntelligencePanelUiCopy.UsbSelectedNotBenchmarkedPrompt);
    }

    [Fact]
    public void MainWindowXaml_WelcomeOverlay_HasQuickActions()
    {
        var path = Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);
        Assert.Contains("Welcome to ForgerEMS", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Run System Scan", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Run USB Benchmark", xaml, System.StringComparison.Ordinal);
        Assert.Contains("Open USB Builder", xaml, System.StringComparison.Ordinal);
    }
}
