using System;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Regression coverage for the v1.2.3 log-format polish pass: backend-prefixed lines
/// of shape <c>[yyyy-MM-dd HH:mm:ss][LEVEL] message</c> must not be double-stamped by the
/// WPF Full Logs display, and frontend-only lines must still receive a single timestamp.
/// </summary>
public sealed class BackendLogPrefixTests
{
    [Theory]
    [InlineData("[2026-05-27 19:51:40][OK] Managed tools ready: 50", "[OK] Managed tools ready: 50")]
    [InlineData("[2026-05-27 19:51:40][INFO] Downloading Xubuntu... 1.2 GB of 6.1 GB | 20% | 11.8 MB/s current",
                "[INFO] Downloading Xubuntu... 1.2 GB of 6.1 GB | 20% | 11.8 MB/s current")]
    [InlineData("[2026-05-27 19:51:40][WARN] Download appears stalled.", "[WARN] Download appears stalled.")]
    [InlineData("[2026-05-27 19:51:40][ERROR] Verify failed: sha256 mismatch", "[ERROR] Verify failed: sha256 mismatch")]
    [InlineData("[2026-05-27 19:51:40] Bare backend line without level bracket",
                "Bare backend line without level bracket")]
    public void Normalize_StripsBackendDatePrefix_KeepsLevelBracket(string raw, string expected)
    {
        Assert.Equal(expected, BackendLogPrefix.Normalize(raw));
    }

    [Theory]
    [InlineData("Toolkit health scan still running")]
    [InlineData("[INFO] Downloading without a date prefix")]
    [InlineData("")]
    public void Normalize_PassesNonPrefixedLinesThrough(string text)
    {
        Assert.Equal(text, BackendLogPrefix.Normalize(text));
    }

    [Fact]
    public void HasDateTimePrefix_IdentifiesBackendShape()
    {
        Assert.True(BackendLogPrefix.HasDateTimePrefix("[2026-05-27 19:51:40][OK] x"));
        Assert.False(BackendLogPrefix.HasDateTimePrefix("[OK] x"));
        Assert.False(BackendLogPrefix.HasDateTimePrefix("hello"));
    }

    [Fact]
    public void DisplayText_BackendPrefixedLine_DoesNotDoubleTimestamp()
    {
        var stamp = new DateTimeOffset(2026, 5, 27, 19, 51, 40, TimeSpan.Zero);
        var normalized = BackendLogPrefix.Normalize("[2026-05-27 19:51:40][OK] Managed tools ready: 50");
        var line = new LogLine(stamp, normalized, LogSeverity.Success);
        Assert.Equal("[19:51:40][OK] Managed tools ready: 50", line.DisplayText);
        Assert.DoesNotContain("2026-05-27", line.DisplayText);
    }

    [Theory]
    [InlineData("[19:51:40] System Intelligence elevated scan READY")]
    [InlineData("[2026-05-27 19:51:40] System Intelligence elevated scan READY")]
    [InlineData("[2026-05-27T19:51:40] System Intelligence elevated scan READY")]
    public void DisplayText_PreStampedLiveLogLine_DoesNotAddSecondTimestamp(string text)
    {
        var stamp = new DateTimeOffset(2026, 5, 27, 19, 51, 40, TimeSpan.Zero);
        var line = new LogLine(stamp, text, LogSeverity.Info);

        Assert.Equal(text, line.DisplayText);
    }

    [Fact]
    public void DisplayText_RawFrontendLine_GetsExactlyOneTimestamp()
    {
        var stamp = new DateTimeOffset(2026, 5, 27, 19, 51, 40, TimeSpan.Zero);
        var line = new LogLine(stamp, "Toolkit health scan still running", LogSeverity.Info);
        Assert.Equal("[19:51:40] Toolkit health scan still running", line.DisplayText);
    }

    [Fact]
    public void DisplayText_LineStartingWithBracketLevelOnly_HasNoInteriorSpace()
    {
        var stamp = new DateTimeOffset(2026, 5, 27, 19, 51, 40, TimeSpan.Zero);
        var line = new LogLine(stamp, "[WARN] Download appears stalled.", LogSeverity.Warning);
        Assert.Equal("[19:51:40][WARN] Download appears stalled.", line.DisplayText);
    }

    [Fact]
    public void Throttle_StillRecognisesPrefixedProgressLine_AfterNormalize()
    {
        // After normalization the line shape is "[INFO] Downloading X... NNN MB downloaded | M.M MB/s".
        // The throttle's prefix-strip handles the bare [LEVEL] form, so:
        //  - first tick → kept
        //  - second tick within 30s with no percent delta → dropped
        var raw = "[2026-05-27 19:51:40][INFO] Downloading Xubuntu 24.04.4 LTS Desktop (amd64)... 901 MB downloaded | 12.0 MB/s";
        var raw2 = "[2026-05-27 19:51:42][INFO] Downloading Xubuntu 24.04.4 LTS Desktop (amd64)... 905 MB downloaded | 11.7 MB/s";
        var n1 = BackendLogPrefix.Normalize(raw);
        var n2 = BackendLogPrefix.Normalize(raw2);

        var throttle = new ManagedDownloadProgressLogThrottle();
        var t0 = new DateTimeOffset(2026, 5, 27, 19, 51, 40, TimeSpan.Zero);
        Assert.True(throttle.ShouldKeep(n1, t0));
        Assert.False(throttle.ShouldKeep(n2, t0.AddSeconds(2)));
    }
}
