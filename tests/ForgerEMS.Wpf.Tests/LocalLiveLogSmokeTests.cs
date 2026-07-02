using Kyra.Core;
using VentoyToolkitSetup.Wpf.Infrastructure;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Source-level smoke for the System Intelligence / Dr. Forge direction pass,
// Part A. Reproduces the EXACT three-stage MainViewModel.AppendLog pipeline:
//   BackendLogPrefix.Normalize -> UserFacingLogSanitizer.SanitizeForLocalLog
//   -> UsbLogDisplayNormalizer.NormalizeHashProviderLabels
// against the real log strings the app emits during a scan, and asserts no
// local live-log surface shows [REDACTED_PRIVATE_PATH]. Sanitized support
// exports are verified to STILL redact in a separate assertion.
public sealed class LocalLiveLogSmokeTests
{
    private const string PrivatePathPlaceholder = "[REDACTED_PRIVATE_PATH]";

    // Mirrors MainViewModel.AppendLog so the smoke exercises the same transform
    // the Live Logs panel and View Full Logs window display.
    private static string LocalLiveLogRender(string raw)
    {
        var stripped = BackendLogPrefix.Normalize(raw);
        var redacted = UserFacingLogSanitizer.SanitizeForLocalLog(stripped, enabled: true);
        return UsbLogDisplayNormalizer.NormalizeHashProviderLabels(redacted);
    }

    public static TheoryData<string> RealLocalLogLines() => new()
    {
        // PowerShell path (MainViewModel.cs:4549)
        @"[INFO] PowerShell path selected: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
        // Backend working directory (MainViewModel.cs:7287, 8136) — Program Files install
        @"[INFO] Working directory: C:\Program Files\ForgerEMS\backend",
        @"[INFO] Working directory: C:\Program Files (x86)\ForgerEMS\backend",
        // Script path (MainViewModel.cs:8139)
        @"[INFO] Script: C:\Program Files\ForgerEMS\backend\SystemIntelligence\Invoke-ForgerEMSSystemScan.ps1",
        // JSON report (MainViewModel.cs:5292) — under %LOCALAPPDATA% with a real username
        @"JSON report: C:\Users\Daddy_FDS\AppData\Local\ForgerEMS\Reports\system-intelligence-latest.json",
        // Markdown report (MainViewModel.cs:5295)
        @"Markdown report: C:\Users\Daddy_FDS\AppData\Local\ForgerEMS\Reports\system-intelligence-latest.md",
        // USB target (MainViewModel.DriverHub.cs:87)
        @"USB target: D:\",
    };

    [Theory]
    [MemberData(nameof(RealLocalLogLines))]
    public void LocalLiveLog_NeverShowsPrivatePathPlaceholder(string raw)
    {
        var rendered = LocalLiveLogRender(raw);
        Assert.DoesNotContain(PrivatePathPlaceholder, rendered, System.StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(RealLocalLogLines))]
    public void LocalLiveLog_HasNoPartialRedactionFragments(string raw)
    {
        var rendered = LocalLiveLogRender(raw);
        // The exact bug-class the spec called out: "[REDACTED_PRIVATE_PATH] Files\ForgerEMS\backend".
        Assert.DoesNotContain($"{PrivatePathPlaceholder} Files", rendered, System.StringComparison.Ordinal);
        Assert.DoesNotContain($"{PrivatePathPlaceholder}\\", rendered, System.StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLiveLog_PreservesProgramFilesInstallPath()
    {
        var rendered = LocalLiveLogRender(@"[INFO] Working directory: C:\Program Files\ForgerEMS\backend");
        Assert.Contains(@"C:\Program Files\ForgerEMS\backend", rendered, System.StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLiveLog_PreservesPowerShellPath()
    {
        var rendered = LocalLiveLogRender(
            @"[INFO] PowerShell path selected: C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe");
        Assert.Contains(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe", rendered, System.StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLiveLog_PreservesReportPathsUnderLocalAppData()
    {
        var json = LocalLiveLogRender(
            @"JSON report: C:\Users\Daddy_FDS\AppData\Local\ForgerEMS\Reports\system-intelligence-latest.json");
        var md = LocalLiveLogRender(
            @"Markdown report: C:\Users\Daddy_FDS\AppData\Local\ForgerEMS\Reports\system-intelligence-latest.md");
        Assert.Contains(@"\ForgerEMS\Reports\system-intelligence-latest.json", json, System.StringComparison.Ordinal);
        Assert.Contains(@"\ForgerEMS\Reports\system-intelligence-latest.md", md, System.StringComparison.Ordinal);
    }

    [Fact]
    public void LocalLiveLog_PreservesUsbTargetPath()
    {
        var rendered = LocalLiveLogRender(@"USB target: D:\");
        Assert.Contains(@"D:\", rendered, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizedSupportExport_StillRedactsPrivatePaths()
    {
        // The strict path (used for Copy Sanitized Summary / Create Support Bundle)
        // must STILL redact a user-profile path. This is the deliberate split: local
        // live logs show real paths, sanitized exports do not.
        const string raw = @"JSON report: C:\Users\Daddy_FDS\AppData\Local\ForgerEMS\Reports\report.json";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, safeRoots: null, enabled: true);
        Assert.Contains(PrivatePathPlaceholder, sanitized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Daddy_FDS", sanitized, System.StringComparison.Ordinal);
    }
}
