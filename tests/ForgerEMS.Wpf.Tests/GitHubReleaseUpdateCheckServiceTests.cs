using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class GitHubReleaseUpdateCheckServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_respond(request));
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { Timeout = TimeSpan.FromSeconds(5) };

    private static string LatestReleaseJson(string tag, string htmlUrl = "https://github.com/x/y/releases/tag/v9")
        => $$"""
            {"tag_name":"{{tag}}","html_url":"{{htmlUrl}}","assets":[]}
            """;

    [Fact]
    public async Task NewerVersion_Detected()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LatestReleaseJson("v1.2.0"), Encoding.UTF8, "application/json")
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync(new Version(1, 1, 4), ignoredVersionNormalized: null);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(new Version(1, 2, 0), result.LatestVersion);
    }

    [Fact]
    public async Task SameVersion_NoUpdate()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LatestReleaseJson("v1.1.4"), Encoding.UTF8, "application/json")
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync(new Version(1, 1, 4), null);
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task InvalidVersionTag_DoesNotCrash_AndNoUpdateFlag()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LatestReleaseJson("not-a-version"), Encoding.UTF8, "application/json")
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync(new Version(1, 1, 4), null);
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task IgnoredVersion_SuppressesUpdate()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(LatestReleaseJson("v1.9.0"), Encoding.UTF8, "application/json")
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync(new Version(1, 1, 4), "v1.9.0");
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
    }

    [Fact]
    public async Task NetworkFailure_DoesNotThrow()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync(new Version(1, 1, 4), null);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void UpdateNotificationText_MatchesExpectedShape()
    {
        var text = UpdateNotificationTextBuilder.BuildHeadline("v1.1.5");
        Assert.Contains("ForgerEMS", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.1.5", text, StringComparison.Ordinal);
        Assert.Contains("Download", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseVersionParser_NormalizesPrereleaseSuffix()
    {
        Assert.True(ReleaseVersionParser.TryParseVersion("v2.0.0-beta.1", out var v));
        Assert.Equal(new Version(2, 0, 0), v);
    }
}
