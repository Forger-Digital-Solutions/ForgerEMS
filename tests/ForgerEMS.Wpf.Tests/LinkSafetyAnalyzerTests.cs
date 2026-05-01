using System;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class LinkSafetyAnalyzerTests
{
    [Fact]
    public void AnalyzeInvalidUrlReturnsUnknown()
    {
        var r = LinkSafetyAnalyzer.Analyze("not a url");
        Assert.Equal(LinkSafetyBand.Unknown, r.Band);
    }

    [Fact]
    public void AnalyzeHttpAddsCaution()
    {
        var r = LinkSafetyAnalyzer.Analyze("http://example.com/file.txt");
        Assert.Equal(LinkSafetyBand.Caution, r.Band);
        Assert.Contains("HTTP", string.Join(" ", r.Notes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnalyzeExecutableExtensionIsHighRisk()
    {
        var r = LinkSafetyAnalyzer.Analyze("https://evil.example/setup.exe");
        Assert.Equal(LinkSafetyBand.HighRisk, r.Band);
    }

    [Fact]
    public void AnalyzeShortenerHostIsCaution()
    {
        var r = LinkSafetyAnalyzer.Analyze("https://bit.ly/abc123");
        Assert.Equal(LinkSafetyBand.Caution, r.Band);
    }

    [Fact]
    public void FormatReportContainsAssessmentLine()
    {
        var text = LinkSafetyAnalyzer.FormatReport(LinkSafetyAnalyzer.Analyze("https://vendor.example/update.zip"));
        Assert.Contains("Assessment", text, StringComparison.OrdinalIgnoreCase);
    }
}
