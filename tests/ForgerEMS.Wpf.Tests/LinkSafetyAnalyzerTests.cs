using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class LinkSafetyAnalyzerTests
{
    [Fact]
    public void AnalyzeInvalidUrlReturnsUnknown()
    {
        var r = LinkSafetyAnalyzer.Analyze("not a url");
        Assert.Equal(LinkSafetyBand.Unknown, r.Band);
        Assert.Contains(SafetyCheckSeverity.InvalidInput, r.States);
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
        Assert.Contains(SafetyCheckSeverity.Suspicious, r.States);
    }

    [Fact]
    public void AnalyzeShortenerHostIsCaution()
    {
        var r = LinkSafetyAnalyzer.Analyze("https://bit.ly/abc123");
        Assert.Equal(LinkSafetyBand.Caution, r.Band);
    }

    [Theory]
    [InlineData("https://www.amtso.org/feature-settings-check-phishing-page/")]
    [InlineData("https://amtso.org/feature-settings-check-phishing-page/")]
    public void AmtsoPhishingUrlRecognizedAsSafeSimulatedPhishingFixture(string url)
    {
        var fixture = KnownSecurityTestFixtureRecognizer.Recognize(new Uri(url));

        Assert.True(fixture.IsKnown);
        Assert.Contains(SafetyCheckSeverity.KnownSafeSecurityTestFixture, fixture.Classifications);
        Assert.Contains(SafetyCheckSeverity.SimulatedPhishingTestFixture, fixture.Classifications);
    }

    [Theory]
    [InlineData("https://www.amtso.org/feature-settings-check-download-of-malware/")]
    [InlineData("https://amtso.org/feature-settings-check-drive-by-download/")]
    [InlineData("https://www.amtso.org/feature-settings-check-compressed-malware/")]
    public void AmtsoMalwareDownloadUrlsRecognizedAsSafeSimulatedMalwareFixture(string url)
    {
        var fixture = KnownSecurityTestFixtureRecognizer.Recognize(new Uri(url));

        Assert.True(fixture.IsKnown);
        Assert.Contains(SafetyCheckSeverity.KnownSafeSecurityTestFixture, fixture.Classifications);
        Assert.Contains(SafetyCheckSeverity.SimulatedMalwareTestFixture, fixture.Classifications);
    }

    [Theory]
    [InlineData("https://secure.eicar.org/eicar.com.txt")]
    [InlineData("https://secure.eicar.org/eicar.com")]
    [InlineData("https://secure.eicar.org/eicar_com.zip")]
    [InlineData("https://secure.eicar.org/eicar_com2.zip")]
    [InlineData("https://www.eicar.org/eicar.com")]
    [InlineData("https://eicar.org/eicar.com")]
    public void EicarUrlsRecognizedAsSafeSimulatedMalwareFixture(string url)
    {
        var fixture = KnownSecurityTestFixtureRecognizer.Recognize(new Uri(url));

        Assert.True(fixture.IsKnown);
        Assert.Contains(SafetyCheckSeverity.KnownSafeSecurityTestFixture, fixture.Classifications);
        Assert.Contains(SafetyCheckSeverity.SimulatedMalwareTestFixture, fixture.Classifications);
    }

    [Theory]
    [InlineData("https://eicar.example.com/eicar.com")]
    [InlineData("https://secure-eicar.org.badsite.test/eicar.com")]
    [InlineData("https://amtso.org.badsite.test/feature-settings-check-phishing-page/")]
    [InlineData("https://fakeamtso.org/feature-settings-check-download-of-malware/")]
    public void LookalikeDomainsDoNotMatchKnownSafeFixtures(string url)
    {
        var fixture = KnownSecurityTestFixtureRecognizer.Recognize(new Uri(url));

        Assert.False(fixture.IsKnown);
    }

    [Fact]
    public async Task AnalyzeAsync_HeadFailureFallsBackToBoundedGet()
    {
        var calls = new List<HttpMethod>();
        using var client = new HttpClient(new DelegateHandler(request =>
        {
            calls.Add(request.Method);
            if (request.Method == HttpMethod.Head)
            {
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('a', 128 * 1024), Encoding.UTF8, "text/html")
            };
            return response;
        }));

        var result = await LinkSafetyAnalyzer.AnalyzeAsync(
            "https://example.test/page",
            client,
            new UrlSafetyAnalysisOptions { MaxHtmlPreviewBytes = 16 * 1024 });

        Assert.Contains(HttpMethod.Head, calls);
        Assert.Contains(HttpMethod.Get, calls);
        Assert.True(result.GetFallbackAttempted);
        Assert.True(result.HtmlPreviewRead);
        Assert.InRange(result.HtmlPreviewBytesRead, 1, 16 * 1024);
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotSavePayloadToQuarantine()
    {
        using var client = new HttpClient(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("harmless"))
            }));

        var result = await LinkSafetyAnalyzer.AnalyzeAsync("https://github.com/owner/repo", client);

        Assert.False(result.AnalyzeSavedPayload);
        Assert.Contains(result.States, s => s is SafetyCheckSeverity.CleanOrLowConcern or SafetyCheckSeverity.UnknownManualReview);
    }

    [Fact]
    public async Task AnalyzeAsync_EicarUrlUsesSimulatedMalwareVerdictNotLowConcern()
    {
        using var client = new HttpClient(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("headers-only-test"))
            }));

        var result = await LinkSafetyAnalyzer.AnalyzeAsync("https://secure.eicar.org/eicar.com.txt", client);

        Assert.Contains(SafetyCheckSeverity.SimulatedMalwareTestFixture, result.States);
        Assert.Contains("Simulated malware", result.Verdict, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(LinkSafetyBand.LowConcern, result.Band);
    }

    [Fact]
    public async Task AnalyzeAsync_DirectExeUrlIsSuspiciousManualReview()
    {
        using var client = new HttpClient(new DelegateHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("metadata-only"))
            }));

        var result = await LinkSafetyAnalyzer.AnalyzeAsync("https://vendor.example/download/tool.exe", client);

        Assert.Contains(SafetyCheckSeverity.Suspicious, result.States);
        Assert.Contains("Manual review", result.Verdict, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FormatReportContainsVerdictAndNoExecutionText()
    {
        var text = LinkSafetyAnalyzer.FormatReport(LinkSafetyAnalyzer.Analyze("https://vendor.example/update.zip"));

        Assert.Contains("Verdict", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("did not", text, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
