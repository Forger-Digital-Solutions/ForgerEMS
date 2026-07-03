#pragma warning disable CA1305 // Locale-sensitive calls in test assertions
using System;
using System.IO;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Part G — pin Drive Validator doc wording so a future edit can't accidentally reintroduce
/// "genuine"/"certified"/"100%"/"NAND certified" language or drop the wizard-flow/full-mode
/// safety paragraph the LEGAL and FAQ docs are required to carry.
/// </summary>
public sealed class DriveValidatorDocsTests
{
    private static string RepoRoot()
    {
        // Tests run from .../tests/ForgerEMS.Wpf.Tests/bin/Release/net8.0-windows.../ — walk up to repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Could not locate repo root (no ForgerEMS.sln found while walking up).");
    }

    private static string ReadDoc(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/FAQ.md")]
    [InlineData("docs/LEGAL.md")]
    [InlineData("docs/PRIVACY.md")]
    [InlineData("docs/ABOUT_FORGEREMS.md")]
    public void Doc_DoesNotMakeGenuineOrCertifiedClaim(string relativePath)
    {
        var content = ReadDoc(relativePath);
        // The literal phrases the brief forbids. Allow plain "certificate" usage that explicitly
        // negates ("not a 100% authenticity certificate", "does not certify") — we filter those.
        var lowered = content.ToLowerInvariant();
        Assert.DoesNotContain("100% genuine", lowered, StringComparison.Ordinal);
        Assert.DoesNotContain("certified genuine", lowered, StringComparison.Ordinal);
        Assert.DoesNotContain("guaranteed authentic", lowered, StringComparison.Ordinal);
        Assert.DoesNotContain("nand certified", lowered, StringComparison.Ordinal);
        Assert.DoesNotContain("validrive", lowered, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("docs/FAQ.md")]
    [InlineData("docs/LEGAL.md")]
    public void Doc_MentionsFileSystemLevelLimitation(string relativePath)
    {
        var content = ReadDoc(relativePath);
        Assert.Contains("file-system", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("NAND", content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docs/FAQ.md")]
    [InlineData("docs/LEGAL.md")]
    [InlineData("docs/ABOUT_FORGEREMS.md")]
    public void Doc_MentionsDriveValidatorWizard(string relativePath)
    {
        var content = ReadDoc(relativePath);
        Assert.Contains("Drive Validator Wizard", content, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("docs/FAQ.md")]
    [InlineData("docs/LEGAL.md")]
    public void Doc_MentionsFullFreeSpaceConfirmationOrHeaviness(string relativePath)
    {
        var content = ReadDoc(relativePath);
        Assert.Contains("Full Free-Space", content, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            content.Contains("heavy", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("acknowledgement", StringComparison.OrdinalIgnoreCase) ||
            content.Contains("typed confirmation", StringComparison.OrdinalIgnoreCase),
            $"{relativePath} must describe Full Free-Space as heavy writes / require acknowledgement.");
    }

    [Theory]
    [InlineData("docs/FAQ.md")]
    [InlineData("docs/LEGAL.md")]
    public void Doc_MentionsDestructiveModeUnavailable(string relativePath)
    {
        var content = ReadDoc(relativePath);
        Assert.Contains("not available in this build", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DevSmokeChecklist_MentionsWizardSteps()
    {
        var content = ReadDoc("docs/DEV_BETA_SMOKE_CHECKLIST_v1.2.4.md");
        Assert.Contains("Drive Validator Wizard", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("media integrity tile map", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Open Drive Validator", content, StringComparison.OrdinalIgnoreCase);
    }
}
