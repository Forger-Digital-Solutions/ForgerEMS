using System.IO;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class InstallerKyraConsentInnoTests
{
    [Fact]
    public void ForgerEMS_Iss_KyraPage_CheckboxesDefaultUnchecked()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS.iss"));
        Assert.Contains("KyraRepairChk.Checked := False;", iss, StringComparison.Ordinal);
        Assert.Contains("KyraHwChk.Checked := False;", iss, StringComparison.Ordinal);
        Assert.Contains("KyraResolvedChk.Checked := False;", iss, StringComparison.Ordinal);
        Assert.Contains("KyraCrashChk.Checked := False;", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void ForgerEMS_Iss_KyraRegistryValueNames_MatchApp()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS.iss"));
        Assert.Contains("KyraShareRepairIntelligence", iss, StringComparison.Ordinal);
        Assert.Contains("KyraShareHardwarePatterns", iss, StringComparison.Ordinal);
        Assert.Contains("KyraShareResolvedCategories", iss, StringComparison.Ordinal);
        Assert.Contains("KyraShareCrashDiagnostics", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void ForgerEMS_Iss_NoPreCheckedKyraTasks()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS.iss"));
        var tasksIdx = iss.IndexOf("[Tasks]", StringComparison.OrdinalIgnoreCase);
        var codeIdx = iss.IndexOf("[Code]", StringComparison.OrdinalIgnoreCase);
        Assert.True(tasksIdx >= 0 && codeIdx > tasksIdx);
        var tasksSection = iss[tasksIdx..codeIdx];
        Assert.DoesNotContain("Kyra", tasksSection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForgerEMS_Iss_KyraPage_MakesLocalOnlyAndPreviewVisible()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS.iss"));

        Assert.Contains("Keep Local Only (default)", iss, StringComparison.Ordinal);
        Assert.Contains("View What Would Be Shared", iss, StringComparison.Ordinal);
        Assert.Contains("optional and off by default", iss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You can change these choices later in Settings", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void ForgerEMS_Iss_DeepSensorTask_IsShortAndUnchecked()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS.iss"));
        var tasksIdx = iss.IndexOf("[Tasks]", StringComparison.OrdinalIgnoreCase);
        var registryIdx = iss.IndexOf("[Registry]", StringComparison.OrdinalIgnoreCase);
        Assert.True(tasksIdx >= 0 && registryIdx > tasksIdx);
        var tasksSection = iss[tasksIdx..registryIdx];

        Assert.Contains("ForgerEMS Deep Sensor Mode:", tasksSection, StringComparison.Ordinal);
        Assert.Contains("Enable Deep Sensor Mode by default (read-only local hardware sensors", tasksSection, StringComparison.Ordinal);
        Assert.Contains("Elevated Scan may still ask for Windows UAC approval", tasksSection, StringComparison.Ordinal);
        Assert.Contains("Flags: unchecked", tasksSection, StringComparison.Ordinal);
        Assert.DoesNotContain("checkedonce", tasksSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("grant", tasksSection, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("permanent admin", tasksSection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForgerEMS_Iss_DeepSensorRegistry_MapsSelectedToReadOnlyAndUnselectedToOff()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS.iss"));

        Assert.Contains("ValueName: \"DeepSensorMode\"; ValueData: \"ReadOnly\"; Flags: uninsdeletevalue; Tasks: deepsensormode", iss, StringComparison.Ordinal);
        Assert.Contains("ValueName: \"DeepSensorMode\"; ValueData: \"Off\"; Flags: uninsdeletevalue; Check: IsDeepSensorModeTaskDisabled", iss, StringComparison.Ordinal);
        Assert.Contains("the installer does not grant permanent admin permission", iss, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Windows administrator approval when you run Elevated Scan", iss, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bypass UAC", iss, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForgerEMS_Iss_ReadyMemo_SummarizesKyraChoicesCompactly()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var iss = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS.iss"));

        Assert.Contains("function UpdateReadyMemo", iss, StringComparison.Ordinal);
        Assert.Contains("Deep Sensor Mode: off", iss, StringComparison.Ordinal);
        Assert.Contains("Kyra Community Intelligence: local-only", iss, StringComparison.Ordinal);
        Assert.Contains("Kyra Community Intelligence: optional sharing selected", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void InstalledReadme_ExplainsKyraChoicesCanChangeLater()
    {
        var root = KyraIntelligenceNetworkTests.FindRepoRoot();
        var readme = File.ReadAllText(Path.Combine(root, "installer", "ForgerEMS-Installed-README.txt"));

        Assert.Contains("Deep Sensor Mode is optional and off unless you enable it", readme, StringComparison.Ordinal);
        Assert.Contains("Windows UAC/security policy still controls that approval at runtime", readme, StringComparison.Ordinal);
        Assert.Contains("does not", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("grant permanent admin permission", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Leave every box unchecked to keep Kyra Local Only", readme, StringComparison.Ordinal);
        Assert.Contains("turn realtime gateway research on or off", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerEMS never shares API keys, gateway tokens, passwords", readme, StringComparison.Ordinal);
        Assert.Contains("Do not email API keys, passwords, serial numbers, or private documents", readme, StringComparison.Ordinal);
    }
}
