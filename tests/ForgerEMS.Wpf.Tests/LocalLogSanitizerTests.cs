using Kyra.Core;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// Guards Part A of the System Intelligence / Dr. Forge direction pass:
// local log lines must keep real install / backend / report paths visible to
// the operator sitting at the PC. Sanitized support exports remain redacted via
// the strict Sanitize method, exercised separately.
public sealed class LocalLogSanitizerTests
{
    [Theory]
    [InlineData("PowerShell path selected: C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe")]
    [InlineData("Working directory: C:\\Program Files\\ForgerEMS\\backend")]
    [InlineData("Script: C:\\Program Files\\ForgerEMS\\backend\\SystemIntelligence\\Invoke-ForgerEMSSystemScan.ps1")]
    [InlineData("JSON report: C:\\Users\\Daddy_FDS\\AppData\\Local\\ForgerEMS\\Reports\\report.json")]
    [InlineData("Markdown report: C:\\Users\\Daddy_FDS\\AppData\\Local\\ForgerEMS\\Reports\\report.md")]
    [InlineData("USB target: D:\\")]
    public void SanitizeForLocalLog_PreservesRealPaths(string raw)
    {
        var sanitized = UserFacingLogSanitizer.SanitizeForLocalLog(raw);
        Assert.Equal(raw, sanitized);
        Assert.DoesNotContain("[REDACTED_PRIVATE_PATH]", sanitized, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeForLocalLog_StillRedactsSecrets()
    {
        const string raw = "API key: sk-abcdef1234567890XYZ and ghp_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var sanitized = UserFacingLogSanitizer.SanitizeForLocalLog(raw);
        Assert.DoesNotContain("sk-abcdef", sanitized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("ghp_aaaa", sanitized, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeForLocalLog_StillRedactsEmailsAndIps()
    {
        const string raw = "Contact ops@example.com on 10.0.0.4 if BitLocker recovery key: ABC12-DEF34-GHI56-JKL78-MNO90";
        var sanitized = UserFacingLogSanitizer.SanitizeForLocalLog(raw);
        Assert.DoesNotContain("ops@example.com", sanitized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.4", sanitized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("ABC12-DEF34", sanitized, System.StringComparison.Ordinal);
    }

    [Fact]
    public void SanitizeForLocalLog_DoesNotProducePartialPathRedactions()
    {
        // Bug-class guard: "[REDACTED_PRIVATE_PATH] Files\ForgerEMS\backend" must
        // never appear as a fragment in local-log output. Either keep the full path
        // or fully redact — never half.
        const string raw = "Working directory: C:\\Program Files\\ForgerEMS\\backend";
        var sanitized = UserFacingLogSanitizer.SanitizeForLocalLog(raw);
        Assert.DoesNotContain("[REDACTED_PRIVATE_PATH] Files", sanitized, System.StringComparison.Ordinal);
    }

    [Fact]
    public void StrictSanitize_StillRedactsForSharing()
    {
        // The strict path (Sanitize with empty safe roots) is used to build
        // sanitized support exports. It must still redact private paths.
        const string raw = "Working directory: C:\\Users\\Daddy_FDS\\AppData\\Local\\ForgerEMS\\backend";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, safeRoots: null, enabled: true);
        Assert.Contains("[REDACTED_PRIVATE_PATH]", sanitized, System.StringComparison.Ordinal);
        Assert.DoesNotContain("Daddy_FDS", sanitized, System.StringComparison.Ordinal);
    }
}
