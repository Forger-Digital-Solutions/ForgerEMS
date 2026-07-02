using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderProfilePlannerAndHtmlTests
{
    [Fact]
    public void Catalog_DefinesEstimatesForEveryCategory()
    {
        Assert.Equal(10, UsbBuilderProfileCatalog.All.Count);
        foreach (var definition in UsbBuilderProfileCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(definition.DisplayName));
            Assert.NotEmpty(definition.MediaScanRelativePaths);
            Assert.False(string.IsNullOrWhiteSpace(definition.ManualMediaExplanation));
        }
    }

    [Fact]
    public void Catalog_DiagnosticsCategoryCopyClarifiesUsbToolsAndDrForgeBoundary()
    {
        var definition = UsbBuilderProfileCatalog.GetRequired("diagnostics");

        Assert.Equal("diagnostics", definition.CategoryId);
        Assert.Equal("Diagnostic Tools for USB", definition.DisplayName);
        Assert.Equal("Diagnostic Tools for USB", UsbBuilderProfileCatalog.GetSummaryLabel("diagnostics"));
        Assert.Contains("USB/downloadable", definition.ShortDescription, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Dr. Forge", definition.ShortDescription, StringComparison.Ordinal);
        Assert.Contains("not the removed ForgerEMS Diagnostics tab", definition.ManualMediaExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("expanded repair analysis and advanced system inventory", definition.ManualMediaExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("deep hardware diagnostics", definition.ManualMediaExplanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerEMS builds and manages the repair USB", definition.ManualMediaExplanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Catalog_ForgerEmsPortableCategoryUsesUsbAppDocsAndLogsFolders()
    {
        var definition = UsbBuilderProfileCatalog.GetRequired("forgerems-portable");

        Assert.Equal("ForgerEMS Portable App", definition.DisplayName);
        Assert.True(definition.DefaultIncluded);
        Assert.Equal("ForgerEMS Portable App", UsbBuilderProfileCatalog.GetSummaryLabel("forgerems-portable"));
        Assert.Contains(@"_apps\ForgerEMS", definition.MediaScanRelativePaths);
        Assert.Contains(@"_docs\ForgerEMS", definition.MediaScanRelativePaths);
        Assert.Contains(@"_logs\ForgerEMS", definition.MediaScanRelativePaths);
        Assert.Contains("Terms", definition.ManualMediaExplanation, StringComparison.OrdinalIgnoreCase);
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
    public void EstimateCalculator_UsesSelectedItemsAndSeparatesManagedFromManual()
    {
        var windows = UsbBuilderProfileOption.FromDefinition(UsbBuilderProfileCatalog.GetRequired("windows"), included: true);
        windows.LoadItems(UsbBuilderProfileItemCatalog.ForCategory("windows"), new HashSet<string>());

        var win11Link = windows.Items.First(i => string.Equals(i.ManifestEntryName, "Windows 11 Download Page", StringComparison.Ordinal));
        var win11Drop = windows.Items.First(i => i.Id == "windows.win11-drop");
        var adkGuide = windows.Items.First(i => string.Equals(i.ManifestEntryName, "Windows ADK and WinPE Info", StringComparison.Ordinal));

        win11Link.IsSelected = true;
        win11Drop.IsSelected = true;
        adkGuide.IsSelected = true;

        var totals = UsbBuilderProfileEstimateCalculator.CalculateTotals([windows]);

        Assert.Equal(3, totals.SelectedItemCount);
        Assert.Equal(0, totals.ManagedDownloadBytes);
        Assert.Equal(1, totals.ManualUserSuppliedItemCount);
        Assert.Equal("none", totals.ManagedDownloadDisplay);
        Assert.Equal("varies", totals.ManualUserSuppliedDisplay);
        Assert.Contains("Selected: 3", windows.SelectedItemSummaryText, StringComparison.Ordinal);
        Assert.Equal("Managed downloads: none", windows.ManagedDownloadsSummaryText);
        Assert.Equal("USB space: 202 KB + manual varies", windows.SelectedUsbFootprintSummaryText);
        Assert.Equal("Manual/user-supplied: varies", windows.ManualUserSuppliedSummaryText);
    }

    [Fact]
    public void ProfileItemSelection_BuildsSerializableManifestSelectors()
    {
        var diagnostics = UsbBuilderProfileOption.FromDefinition(UsbBuilderProfileCatalog.GetRequired("diagnostics"), included: true);
        diagnostics.LoadItems(UsbBuilderProfileItemCatalog.ForCategory("diagnostics"), new HashSet<string>());
        diagnostics.Items.First(i => string.Equals(i.ManifestEntryName, "Rufus 4.14 Portable (x64)", StringComparison.Ordinal)).IsSelected = true;

        var selectors = UsbBuilderProfileItemSelection.BuildSelectedManifestSelectors([diagnostics]);

        Assert.Contains("name:Rufus 4.14 Portable (x64)", selectors);
        Assert.Contains(@"dest:Tools\Portable\USB\rufus-4.14p.exe", selectors);
        Assert.DoesNotContain(selectors, s => s.Contains("Download Page", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProfileItemSelection_IncludesForgerEmsPortableAppPaths()
    {
        var portable = UsbBuilderProfileOption.FromDefinition(UsbBuilderProfileCatalog.GetRequired("forgerems-portable"), included: true);
        portable.LoadItems(UsbBuilderProfileItemCatalog.ForCategory("forgerems-portable"), new HashSet<string>());
        foreach (var item in portable.Items)
        {
            item.IsSelected = true;
        }

        var selectors = UsbBuilderProfileItemSelection.BuildSelectedManifestSelectors([portable]);

        Assert.Contains(@"dest:_apps\ForgerEMS\ForgerEMS.exe", selectors);
        Assert.Contains(@"dest:_docs\ForgerEMS\TERMS_OF_USE.md", selectors);
        Assert.Equal(3, portable.SelectedItemCount);
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

        var written = UsbHtmlDocumentationGenerator.GenerateAll(new UsbHtmlDocumentationRequest
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
        Assert.Contains("ForgerEMS Portable App", dashboard, StringComparison.Ordinal);
        Assert.Contains("_apps/ForgerEMS/ForgerEMS.exe", dashboard, StringComparison.Ordinal);
        Assert.Contains("Windows", dashboard, StringComparison.Ordinal);
        Assert.Contains("Estimated space", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("Markdown README", dashboard, StringComparison.OrdinalIgnoreCase);

        var guide = File.ReadAllText(Path.Combine(temp.Path, "_docs", "manual-media-guide.html"));
        Assert.Contains("guided", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"_apps\ForgerEMS", guide, StringComparison.Ordinal);
        Assert.Contains(@"_docs\ForgerEMS", guide, StringComparison.Ordinal);
        Assert.Contains(@"_logs\ForgerEMS", guide, StringComparison.Ordinal);
        Assert.Contains("official", guide, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot be legally redistributed", guide, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("thepiratebay", guide, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MediaScanner_DetectsExistingUserFilesWithoutDeleting()
    {
        using var temp = new TempFolder();
        var firmware = Path.Combine(temp.Path, "ISO", "Android", "Android-Manual-Firmware-Drop", "Samsung", "user.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(firmware)!);
        File.WriteAllBytes(firmware, new byte[1024]);

        var results = await UsbBuilderProfileMediaScanner.ScanAsync(temp.Path, ["android"]);

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
