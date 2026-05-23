using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderProfilePlannerAndHtmlTests
{
    [Fact]
    public void Catalog_DefinesEstimatesForEveryCategory()
    {
        Assert.Equal(9, UsbBuilderProfileCatalog.All.Count);
        foreach (var definition in UsbBuilderProfileCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.NotEmpty(definition.MediaScanRelativePaths);
            Assert.False(string.IsNullOrWhiteSpace(definition.ManualMediaExplanation));
        }
    }

    [Fact]
    public void StatusResolver_NeverReturnsManualMediaRequiredLabel()
    {
        foreach (var definition in UsbBuilderProfileCatalog.All)
        {
            var status = UsbBuilderProfileStatusResolver.Resolve(definition, isIncluded: true, 0, 0);
            var label = UsbBuilderProfileStatusResolver.ToDisplayLabel(status);
            Assert.DoesNotContain("Manual media required", label, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("macos", UsbBuilderProfilePackStatus.GuidedOfficialDownload)]
    [InlineData("legacy-windows", UsbBuilderProfilePackStatus.UserSuppliedMedia)]
    public void StatusResolver_ManualCategoriesUseFriendlyStatuses(string categoryId, UsbBuilderProfilePackStatus expected)
    {
        var definition = UsbBuilderProfileCatalog.GetRequired(categoryId);
        var status = UsbBuilderProfileStatusResolver.Resolve(definition, isIncluded: true, 0, 0);
        Assert.Equal(expected, status);
    }

    [Fact]
    public void EstimateCalculator_SumsSelectedMinimumAndTypical()
    {
        var options = UsbBuilderProfileCatalog.All
            .Select(d => UsbBuilderProfileOption.FromDefinition(d, d.DefaultIncluded))
            .ToList();

        var totals = UsbBuilderProfileEstimateCalculator.CalculateTotals(options);
        Assert.True(totals.MinimumBytes > 0);
        Assert.True(totals.TypicalBytes > totals.MinimumBytes);
        Assert.True(totals.UserSuppliedPackCount >= 1);
    }

    [Fact]
    public void EstimateCalculator_UserSuppliedLine_IsHonest()
    {
        var mac = UsbBuilderProfileOption.FromDefinition(UsbBuilderProfileCatalog.GetRequired("macos"), included: true);
        var line = UsbBuilderProfileEstimateCalculator.FormatSpaceLine(mac.SpaceEstimate);
        Assert.Contains("User-supplied", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HtmlEscaper_EncodesDangerousCharacters()
    {
        var encoded = UsbHtmlEscaper.Escape("<script>alert(\"x\")</script> & '");
        Assert.DoesNotContain("<script>", encoded, StringComparison.Ordinal);
        Assert.Contains("&lt;", encoded, StringComparison.Ordinal);
        Assert.Contains("&amp;", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void HtmlGenerator_WritesExpectedFilesAndPreservesRawReadme()
    {
        using var temp = new TempFolder();
        var rawReadme = Path.Combine(temp.Path, "README.md");
        File.WriteAllText(rawReadme, "# Raw markdown stays");

        var options = UsbBuilderProfileCatalog.All
            .Select(d => UsbBuilderProfileOption.FromDefinition(d, d.DefaultIncluded))
            .ToList();

        var generator = new UsbHtmlDocumentationGenerator();
        var written = generator.GenerateAll(new UsbHtmlDocumentationRequest
        {
            UsbRoot = temp.Path,
            ProfileOptions = options,
            UsbFreeBytes = 80L * 1024 * 1024 * 1024
        });

        Assert.Contains(written, p => p.EndsWith("README.html", StringComparison.OrdinalIgnoreCase));
        Assert.True(File.Exists(Path.Combine(temp.Path, "_docs", "manual-media-guide.html")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "_logs", "index.html")));
        Assert.True(File.Exists(Path.Combine(temp.Path, "_reports", "index.html")));
        Assert.Equal("# Raw markdown stays", File.ReadAllText(rawReadme));

        var dashboard = File.ReadAllText(Path.Combine(temp.Path, "README.html"));
        Assert.Contains("ForgerEMS Technician USB", dashboard, StringComparison.Ordinal);
        Assert.Contains("Windows", dashboard, StringComparison.Ordinal);
        Assert.Contains("Estimated space", dashboard, StringComparison.Ordinal);

        var guide = File.ReadAllText(Path.Combine(temp.Path, "_docs", "manual-media-guide.html"));
        Assert.Contains("guided", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("official", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be legally redistributed", guide, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thepiratebay", guide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MediaScanner_DetectsExistingUserFilesWithoutDeleting()
    {
        using var temp = new TempFolder();
        var firmware = Path.Combine(temp.Path, "ISO", "Android", "Android-Manual-Firmware-Drop", "Samsung", "user.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(firmware)!);
        File.WriteAllBytes(firmware, new byte[1024]);

        var scanner = new UsbBuilderProfileMediaScanner();
        var results = scanner.ScanAsync(temp.Path, ["android"]).GetAwaiter().GetResult();

        Assert.True(results["android"].TotalBytes >= 1024);
        Assert.True(File.Exists(firmware));
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
