using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraSlashCommandRouterTests
{
    private static KyraSlashHostSnapshot Host(
        SystemProfile? profile = null,
        string? usbLine = null,
        Action? clearKyraMemoryConfirmed = null,
        Action? clearChatHistory = null,
        Action? openLogsFolder = null) =>
        new()
        {
            LogsRoot = @"C:\temp\logs",
            RuntimeRoot = @"C:\temp\rt",
            ApiFirstRouting = true,
            OfflineFallbackEnabled = true,
            ModeDisplayName = "Hybrid",
            ActiveProviderSummary = "OK",
            ToolStatusSummary = "Stubs",
            MemoryEnabled = false,
            VerboseLiveLogs = false,
            HasSystemIntelligenceScan = true,
            HasToolkitHealthReport = true,
            ToolSettings = new CopilotSettings(),
            UsbSummaryLine = usbLine ?? string.Empty,
            ToolkitSummaryLine = string.Empty,
            LatestWarningSnippet = string.Empty,
            SystemProfile = profile,
            ClearKyraMemoryConfirmed = clearKyraMemoryConfirmed,
            ClearChatHistory = clearChatHistory ?? (() => { }),
            OpenLogsFolder = openLogsFolder,
            BuildSanitizedMemoryPreview = () => "{}"
        };

    [Fact]
    public void Diagnose_ForwardsPromptWithIssue()
    {
        var parse = KyraSlashCommandParser.Parse("/diagnose lag");
        var route = KyraSlashCommandRouter.Handle(parse, Host());
        Assert.False(route.HandledWithoutLlm);
        Assert.Contains("lag", route.ForwardPrompt ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Short answer", route.ForwardPrompt ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Usb_ForwardsWithContextHint()
    {
        var parse = KyraSlashCommandParser.Parse("/usb");
        var route = KyraSlashCommandRouter.Handle(parse, Host(usbLine: "USB: test"));
        Assert.Contains("USB", route.ForwardPrompt ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resale_ForwardsAdvisor()
    {
        var parse = KyraSlashCommandParser.Parse("/resale");
        var route = KyraSlashCommandRouter.Handle(parse, Host());
        Assert.Contains("resale", route.ForwardPrompt ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Listing_ExcludesSerialsInDraft()
    {
        var profile = new SystemProfile
        {
            Manufacturer = "Dell",
            Model = "XPS 13",
            Cpu = "Intel i7",
            RamTotal = "16 GB",
            OperatingSystem = "Windows 11",
            OsBuild = "22631"
        };
        var parse = KyraSlashCommandParser.Parse("/listing facebook");
        var route = KyraSlashCommandRouter.Handle(parse, Host(profile));
        Assert.True(route.HandledWithoutLlm);
        var text = route.ResponseText ?? "";
        Assert.Contains("Dell", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(
            @"(?i)(?:^|[\r\n])[ \t]*serial\s*:\s*\S+|(?:^|[\r\n])[ \t]*service\s+tag\s*:\s*\S+",
            text);
    }

    [Fact]
    public void FixCode_ForwardsAssist()
    {
        var parse = KyraSlashCommandParser.Parse("/fixcode");
        var route = KyraSlashCommandRouter.Handle(parse, Host());
        Assert.Contains("code assist", route.ForwardPrompt ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MemoryClear_RequiresConfirm()
    {
        var cleared = false;
        var parse = KyraSlashCommandParser.Parse("/memory clear");
        var route = KyraSlashCommandRouter.Handle(parse, Host(clearKyraMemoryConfirmed: () => cleared = true));
        Assert.True(route.HandledWithoutLlm);
        Assert.Contains("confirm", route.ResponseText ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.False(cleared);
    }

    [Fact]
    public void MemoryClearConfirm_Clears()
    {
        var cleared = false;
        var parse = KyraSlashCommandParser.Parse("/memory clear confirm");
        var route = KyraSlashCommandRouter.Handle(parse, Host(clearKyraMemoryConfirmed: () => cleared = true));
        Assert.True(cleared);
        Assert.Contains("cleared", route.ResponseText ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clear_InvokesChatOnly_NotDiskMemory()
    {
        var chat = false;
        var disk = false;
        var parse = KyraSlashCommandParser.Parse("/clear");
        var route = KyraSlashCommandRouter.Handle(parse, Host(
            clearChatHistory: () => chat = true,
            clearKyraMemoryConfirmed: () => disk = true));
        Assert.True(route.HandledWithoutLlm);
        Assert.True(chat);
        Assert.False(disk);
    }

    [Fact]
    public void LogsOpen_InvokesFolder()
    {
        var opened = false;
        var parse = KyraSlashCommandParser.Parse("/logs open");
        var route = KyraSlashCommandRouter.Handle(parse, Host(openLogsFolder: () => opened = true));
        Assert.True(route.HandledWithoutLlm);
        Assert.True(opened);
    }

    [Fact]
    public void Provider_DoesNotExposeApiKeyPatterns()
    {
        var parse = KyraSlashCommandParser.Parse("/provider");
        var route = KyraSlashCommandRouter.Handle(parse, Host());
        Assert.True(route.HandledWithoutLlm);
        var text = route.ResponseText ?? "";
        Assert.DoesNotContain("sk-", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ghp_", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Weather", text, StringComparison.OrdinalIgnoreCase);
    }
}
