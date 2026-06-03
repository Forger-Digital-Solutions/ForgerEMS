using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UpdateCheckDiagnosticsFormatterTests
{
    [Fact]
    public void IsGitHubRateLimited_Detects403RateLimitBody()
    {
        var result = new UpdateCheckResult
        {
            FailureKind = UpdateCheckFailureKind.AccessDeniedOrRateLimited,
            DiagnosticDetail = "HTTP 403: {\"message\":\"API rate limit exceeded for 203.0.113.1\"}"
        };

        Assert.True(UpdateCheckDiagnosticsFormatter.IsGitHubRateLimited(result));
    }

    [Fact]
    public void TryFormatLiveLogMessage_RateLimit_UsesFriendlyNonBlockingCopy()
    {
        var result = new UpdateCheckResult
        {
            FailureKind = UpdateCheckFailureKind.AccessDeniedOrRateLimited,
            ErrorMessage = "GitHub API rate limit reached (temporary). Installed version is unchanged.",
            DiagnosticDetail = "HTTP 403: {\"message\":\"API rate limit exceeded\"}"
        };

        var message = UpdateCheckDiagnosticsFormatter.TryFormatLiveLogMessage(result, manual: false);

        Assert.NotNull(message);
        Assert.Contains("paused", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Installed version unchanged", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AccessDeniedOrRateLimited", message, StringComparison.Ordinal);
        Assert.DoesNotContain("HTTP 403", message, StringComparison.Ordinal);
    }

    [Fact]
    public void RedactForLog_RateLimit_StripsRawJsonBody()
    {
        var redacted = UpdateCheckDiagnosticsFormatter.RedactForLog(
            "HTTP 403: {\"message\":\"API rate limit exceeded for 203.0.113.1\"}");

        Assert.Equal("GitHub API rate limit (HTTP 403/429).", redacted);
        Assert.DoesNotContain("203.0.113.1", redacted, StringComparison.Ordinal);
    }
}
