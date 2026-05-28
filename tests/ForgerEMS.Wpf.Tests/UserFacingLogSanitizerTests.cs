using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Regression coverage for the v1.2.3 dev-smoke fix: safe USB/toolkit destination paths
/// must be preserved in user-facing logs, while user-profile / AppData / Temp / OneDrive
/// paths and secrets must still be redacted.
/// </summary>
public sealed class UserFacingLogSanitizerTests
{
    private static readonly string[] UsbSafeRoots = { @"D:\", @"D:\ISO\" };

    [Fact]
    public void Sanitize_KeepsSafeTargetRootDestination()
    {
        var raw = @"Final destination write result: D:\ISO\Linux\kali-linux-2026.1-installer-amd64.iso";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.Contains(@"D:\ISO\Linux\kali-linux-2026.1-installer-amd64.iso", sanitized);
        Assert.DoesNotContain("REDACTED_PRIVATE_PATH", sanitized);
    }

    [Fact]
    public void Sanitize_RedactsUserProfilePath_EvenWithSafeRoots()
    {
        var raw = @"Staging cache: C:\Users\SomeUser\AppData\Local\Temp\stage\file.tmp";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.DoesNotContain("SomeUser", sanitized);
        Assert.DoesNotContain("AppData", sanitized);
        Assert.Contains("REDACTED_PRIVATE_PATH", sanitized);
    }

    [Fact]
    public void Sanitize_RedactsOneDrivePath()
    {
        var raw = @"Staging cache: C:\Users\Alice\OneDrive\Stuff\file.tmp";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.DoesNotContain("OneDrive", sanitized);
        Assert.Contains("REDACTED_PRIVATE_PATH", sanitized);
    }

    [Fact]
    public void Sanitize_WithNoSafeRoots_StillRedactsAllDriveRootedPaths()
    {
        var raw = @"Final destination write result: D:\ISO\Linux\kali-linux-2026.1.iso";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, safeRoots: null);
        Assert.DoesNotContain(@"D:\ISO", sanitized);
        Assert.Contains("REDACTED_PRIVATE_PATH", sanitized);
    }

    [Fact]
    public void Sanitize_RedactsSecretsRegardlessOfPathContext()
    {
        var raw = "headers: Authorization: Bearer abcdefghijklmnopqrstuvwxyz; api-key: sk-fake123456789012345678";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.DoesNotContain("abcdefghijklmnopqrstuvwxyz", sanitized);
        Assert.DoesNotContain("sk-fake123456789012345678", sanitized);
        Assert.Contains("REDACTED", sanitized);
    }

    [Fact]
    public void Sanitize_RedactsTokenInsideUrlQueryString()
    {
        var raw = "Resolved URL: https://example.com/dl?token=ghp_abcdefghijklmnopqrstuvwxyz1234567890";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.DoesNotContain("ghp_abcdefghijklmnopqrstuvwxyz1234567890", sanitized);
    }

    [Fact]
    public void Sanitize_KeepsRelativeDestinationPaths()
    {
        var raw = @"Writing relative dest: ISO\Linux\kali-linux-2026.1.iso";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.Contains(@"ISO\Linux\kali-linux-2026.1.iso", sanitized);
    }

    [Fact]
    public void IsSafeDestinationPath_DetectsSafeAndPrivate()
    {
        Assert.True(UserFacingLogSanitizer.IsSafeDestinationPath(@"D:\ISO\Linux\file.iso", UsbSafeRoots));
        Assert.False(UserFacingLogSanitizer.IsSafeDestinationPath(@"C:\Users\Alice\Desktop\file.iso", UsbSafeRoots));
        Assert.False(UserFacingLogSanitizer.IsSafeDestinationPath(@"E:\OutOfScope\file.iso", UsbSafeRoots));
    }

    [Fact]
    public void Sanitize_PreservesSupportableProgressLine()
    {
        var raw = "Downloading AlmaLinux 10.2 DVD (x86_64)... 12.5% | 144 MB / 1200 MB | 16.6 MB/s | ETA 1m 5s";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.Contains("AlmaLinux 10.2 DVD", sanitized);
        Assert.Contains("16.6 MB/s", sanitized);
    }

    [Fact]
    public void Sanitize_RedactsTempPathEvenIfUnderSafeDrive()
    {
        // A path under D:\ but inside an obvious user-profile fragment should still redact.
        var raw = @"Cache: D:\Users\SomeUser\AppData\Local\Temp\foo.bin";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.DoesNotContain("SomeUser", sanitized);
        Assert.Contains("REDACTED_PRIVATE_PATH", sanitized);
    }

    [Fact]
    public void Sanitize_RedactsEmailAndPrivateIpEvenWithSafePath()
    {
        var raw = @"contact alice@example.com from 10.0.0.5; dest: D:\ISO\file.iso";
        var sanitized = UserFacingLogSanitizer.Sanitize(raw, UsbSafeRoots);
        Assert.DoesNotContain("alice@example.com", sanitized);
        Assert.DoesNotContain("10.0.0.5", sanitized);
        Assert.Contains(@"D:\ISO\file.iso", sanitized);
    }
}
