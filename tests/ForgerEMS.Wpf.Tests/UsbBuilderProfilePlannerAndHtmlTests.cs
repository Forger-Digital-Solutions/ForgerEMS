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
    public void StatusResolver_UsesTechnicianActionLabels()
    {
        Assert.Equal("Managed Download", UsbBuilderProfileStatusResolver.ToDisplayLabel(UsbBuilderProfilePackStatus.AutoDownloadable));
        Assert.Equal("Official Download Page", UsbBuilderProfileStatusResolver.ToDisplayLabel(UsbBuilderProfilePackStatus.GuidedOfficialDownload));
        Assert.Equal("Manual Media Required", UsbBuilderProfileStatusResolver.ToDisplayLabel(UsbBuilderProfilePackStatus.UserSuppliedMedia));
        Assert.Equal("Vendor Portal / License Required", UsbBuilderProfileStatusResolver.ToDisplayLabel(UsbBuilderProfilePackStatus.LinkOnlyLicenseRestricted));
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
    public void HtmlGenerator_WritesExpectedFilesAndPolishesUsbRoot()
    {
        using var temp = new TempFolder();
        var userReadme = Path.Combine(temp.Path, "README.md");
        File.WriteAllText(userReadme, "# User-owned readme stays");

        var legacyJson = Path.Combine(temp.Path, "ForgerEMS-managed-download-result.json");
        File.WriteAllText(legacyJson, "{\"readiness\":\"READY\"}");

        var visibleLog = Path.Combine(temp.Path, "_logs", "setup_20260101_120000.log");
        Directory.CreateDirectory(Path.Combine(temp.Path, "_logs"));
        File.WriteAllText(visibleLog, "legacy visible log");

        File.WriteAllText(
            Path.Combine(temp.Path, "START-HERE.html"),
            "<!DOCTYPE html><html><head><meta http-equiv=\"refresh\" content=\"0; url=README.html\"/></head></html>");

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
        Assert.False(File.Exists(Path.Combine(temp.Path, "START-HERE.html")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "_docs", "forgerems-usb-dashboard.html")));
        Assert.Equal("# User-owned readme stays", File.ReadAllText(userReadme));
        Assert.False(File.Exists(legacyJson));
        Assert.True(File.Exists(Path.Combine(temp.Path, "_forgerems", "metadata", "ForgerEMS-managed-download-result.json")));
        Assert.False(File.Exists(visibleLog));
        Assert.True(File.Exists(Path.Combine(temp.Path, "_forgerems", "logs", "setup_20260101_120000.log")));

        var dashboard = File.ReadAllText(Path.Combine(temp.Path, "README.html"));
        Assert.Contains("ForgerEMS Technician USB", dashboard, StringComparison.Ordinal);
        Assert.Contains("Windows", dashboard, StringComparison.Ordinal);
        Assert.Contains("Estimated space", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("Markdown README", dashboard, StringComparison.OrdinalIgnoreCase);

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
