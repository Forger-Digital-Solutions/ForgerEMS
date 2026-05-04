using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

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

    private sealed class DelayUntilCanceledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static HttpClient Client(HttpMessageHandler handler) => new(handler) { Timeout = TimeSpan.FromSeconds(5) };

    /// <summary>One-element GitHub <c>/releases</c> API array with sortable <c>published_at</c>.</summary>
    private static string ReleasesArraySingle(
        string tag,
        string publishedAt = "2024-06-15T12:00:00Z",
        bool prerelease = false,
        string htmlUrl = "https://github.com/x/y/releases/tag/v9",
        string? name = null,
        string assetsJson = "[]")
    {
        var namePart = string.IsNullOrEmpty(name)
            ? ""
            : ",\"name\":\"" + name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        var pre = prerelease ? "true" : "false";
        return "[{\"tag_name\":\"" + tag + "\",\"html_url\":\"" + htmlUrl +
               "\",\"draft\":false,\"prerelease\":" + pre + ",\"published_at\":\"" + publishedAt + "\"" + namePart +
               ",\"assets\":" + assetsJson + "}]";
    }

    private static HttpResponseMessage OkReleases(string jsonBody) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
        };

    [Fact]
    public async Task NewerVersion_Detected()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("v1.2.0")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", ignoredVersionNormalized: null);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.Equal(new Version(1, 2, 0), result.LatestVersion);
    }

    [Fact]
    public async Task SameVersion_NoUpdate()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("v1.1.4")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.AlreadyLatest, result.Outcome);
    }

    [Fact]
    public async Task InstalledNewerThanLatest_Public_IsSuccess()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("v1.1.4")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.5-beta.1", null);
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.InstalledNewerThanLatestPublic, result.Outcome);
    }

    [Fact]
    public async Task InstalledBeta_LatestStable_UpdateAvailable()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("v1.1.4")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4-beta.1", null);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
    }

    [Fact]
    public async Task ForgerEMS_TagPrefix_Parses()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("ForgerEMS-v1.2.0")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.Equal("1.2.0", ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel));
    }

    [Fact]
    public async Task InvalidVersionTag_DoesNotCrash_OffersReleasePage_AndUncertainCompare()
    {
        const string url = "https://github.com/Forger-Digital-Solutions/ForgerEMS/releases/tag/not-a-version";
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("not-a-version", htmlUrl: url)));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
        Assert.True(result.VersionComparisonUncertain);
        Assert.Equal(url, result.ReleaseNotesUrl);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("newer release may be available", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IgnoredVersion_SuppressesUpdate()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("v1.9.0")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", "v1.9.0");
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.IgnoredVersion, result.Outcome);
    }

    [Fact]
    public async Task NetworkFailure_DoesNotThrow()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("offline"));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.ErrorMessage);
        Assert.Equal(UpdateCheckFailureKind.Network, result.FailureKind);
        Assert.Equal(UpdateCheckOutcome.Failed, result.Outcome);
    }

    [Fact]
    public async Task Latest404_EmptyReleases_NoPublishedReleaseMessage()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/releases", StringComparison.Ordinal))
            {
                return OkReleases("[]");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.NoPublishedRelease, result.Outcome);
        Assert.Contains("No published ForgerEMS release", result.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReleasesList_PicksNewestByPublishedAt_AndPrefersBetaZip()
    {
        const string listJson = """
            [{"tag_name":"v1.0.0","html_url":"https://github.com/Forger-Digital-Solutions/ForgerEMS/releases/tag/v1.0.0",
            "draft":false,"prerelease":false,"published_at":"2024-03-01T00:00:00Z",
            "assets":[
              {"name":"ForgerEMS-v1.0.0.zip","browser_download_url":"https://cdn/gh/ForgerEMS-v1.0.0.zip"},
              {"name":"ForgerEMS-Beta-v1.0.0.zip","browser_download_url":"https://cdn/gh/ForgerEMS-Beta-v1.0.0.zip"},
              {"name":"ForgerEMS-Setup-v1.0.0.exe","browser_download_url":"https://cdn/gh/ForgerEMS-Setup-v1.0.0.exe"}
            ]}]
            """;
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/releases", StringComparison.Ordinal))
            {
                return OkReleases(listJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.True(result.Succeeded);
        Assert.Equal(UpdateCheckOutcome.InstalledNewerThanLatestPublic, result.Outcome);
        Assert.Equal("https://cdn/gh/ForgerEMS-Beta-v1.0.0.zip", result.RecommendedZipDownloadUrl);
        Assert.Equal("https://cdn/gh/ForgerEMS-Setup-v1.0.0.exe", result.InstallerDownloadUrl);
    }

    [Fact]
    public async Task ReleasesList404_MapsToUpdateSourceUnreachable()
    {
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/releases", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.False(result.Succeeded);
        Assert.Equal(UpdateCheckFailureKind.UpdateSourceUnreachable, result.FailureKind);
    }

    [Fact]
    public async Task StableOnly_IgnoresPrereleaseOnly_Releases()
    {
        const string listJson = """
            [{"tag_name":"v9.9.9-rc.1","html_url":"https://github.com/x/y/releases/tag/v9.9.9-rc.1",
            "draft":false,"prerelease":true,"published_at":"2026-01-01T00:00:00Z","assets":[]}]
            """;
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/releases", StringComparison.Ordinal))
            {
                return OkReleases(listJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null, UpdateReleaseChannel.StableOnly);
        Assert.True(result.Succeeded);
        Assert.Equal(UpdateCheckOutcome.NoPublishedRelease, result.Outcome);
    }

    [Fact]
    public async Task BetaChannel_IncludesPrerelease_NewerThanInstalled()
    {
        const string listJson = """
            [{"tag_name":"v1.2.0-rc.1","html_url":"https://github.com/x/y/releases/tag/v1.2.0-rc.1",
            "draft":false,"prerelease":true,"published_at":"2025-06-01T00:00:00Z","assets":[]}]
            """;
        var handler = new StubHandler(req =>
        {
            var path = req.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.Contains("/releases", StringComparison.Ordinal))
            {
                return OkReleases(listJson);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null, UpdateReleaseChannel.BetaRcAllowed);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.UpdateAvailable, result.Outcome);
    }

    [Fact]
    public async Task Forbidden_MarkedAsAccessIssue()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"message":"API rate limit exceeded"}""", Encoding.UTF8, "application/json")
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.False(result.Succeeded);
        Assert.Equal(UpdateCheckFailureKind.AccessDeniedOrRateLimited, result.FailureKind);
    }

    [Fact]
    public async Task InvalidJson_Body_ClassifiedAsMetadata()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not json", Encoding.UTF8, "application/json")
        });
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.False(result.Succeeded);
        Assert.Equal(UpdateCheckFailureKind.ReleaseMetadataInvalid, result.FailureKind);
    }

    [Fact]
    public async Task HttpClientTimeout_ReturnsTimeoutFailureKind()
    {
        using var http = new HttpClient(new DelayUntilCanceledHandler()) { Timeout = TimeSpan.FromMilliseconds(20) };
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null);
        Assert.False(result.Succeeded);
        Assert.Equal(UpdateCheckFailureKind.Timeout, result.FailureKind);
        Assert.Contains("timed out", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UserCancellation_ReturnsCancelledOutcome()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("v1.2.0")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await service.CheckForNewerReleaseAsync("1.1.4", null, UpdateReleaseChannel.BetaRcAllowed, cts.Token);
        Assert.False(result.Succeeded);
        Assert.Equal(UpdateCheckFailureKind.Cancelled, result.FailureKind);
        Assert.Equal(UpdateCheckOutcome.Cancelled, result.Outcome);
    }

    [Fact]
    public async Task InvalidInstalledVersion_ReturnsMetadataInvalid()
    {
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("v1.2.0")));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("not-a-version", null);
        Assert.False(result.Succeeded);
        Assert.Equal(UpdateCheckFailureKind.ReleaseMetadataInvalid, result.FailureKind);
    }

    [Fact]
    public async Task NewestPublishedRelease_WinsOverHigherSemanticVersion()
    {
        const string listJson = """
            [
              {"tag_name":"v9.9.9","html_url":"https://github.com/x/y/releases/tag/v9.9.9","draft":false,"prerelease":false,
               "published_at":"2024-01-01T00:00:00Z","assets":[]},
              {"tag_name":"v1.2.0","html_url":"https://github.com/x/y/releases/tag/v1.2.0","draft":false,"prerelease":false,
               "published_at":"2025-06-01T00:00:00Z","assets":[]}
            ]
            """;
        var handler = new StubHandler(req =>
            (req.RequestUri?.AbsolutePath ?? "").Contains("/releases", StringComparison.Ordinal)
                ? OkReleases(listJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.0.0", null);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(new Version(1, 2, 0), result.LatestVersion);
    }

    [Fact]
    public async Task StableChannel_SkipsNewerPrerelease_PicksLatestStableByPublishDate()
    {
        const string listJson = """
            [
              {"tag_name":"v3.0.0-rc.1","html_url":"https://github.com/x/y/releases/tag/v3.0.0-rc.1","draft":false,"prerelease":true,
               "published_at":"2026-06-01T00:00:00Z","assets":[]},
              {"tag_name":"v1.5.0","html_url":"https://github.com/x/y/releases/tag/v1.5.0","draft":false,"prerelease":false,
               "published_at":"2025-01-01T00:00:00Z","assets":[]}
            ]
            """;
        var handler = new StubHandler(req =>
            (req.RequestUri?.AbsolutePath ?? "").Contains("/releases", StringComparison.Ordinal)
                ? OkReleases(listJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.0.0", null, UpdateReleaseChannel.StableOnly);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(new Version(1, 5, 0), result.LatestVersion);
    }

    [Fact]
    public async Task Version_FromTag_NotFromInstallerFilename()
    {
        const string listJson = """
            [{"tag_name":"v1.2.3","html_url":"https://github.com/x/y/releases/tag/v1.2.3","draft":false,"prerelease":false,
              "published_at":"2025-01-01T00:00:00Z",
              "assets":[
                {"name":"ForgerEMS-misleading-9.9.9-Setup.exe","browser_download_url":"https://cdn/gh/misleading.exe"}
              ]}]
            """;
        var handler = new StubHandler(req =>
            (req.RequestUri?.AbsolutePath ?? "").Contains("/releases", StringComparison.Ordinal)
                ? OkReleases(listJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.0.0", null);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(new Version(1, 2, 3), result.LatestVersion);
        Assert.Equal("1.2.3", ReleaseVersionParser.NormalizeLabel(result.LatestVersionLabel));
    }

    [Fact]
    public async Task NonPreferredZip_StillOffersUpdate_AndFlagsRecommendedZipMissing()
    {
        const string listJson = """
            [{"tag_name":"v1.2.0","html_url":"https://github.com/x/y/releases/tag/v1.2.0","draft":false,"prerelease":false,
              "published_at":"2025-01-01T00:00:00Z",
              "assets":[
                {"name":"odd-bundle-name.zip","browser_download_url":"https://cdn/gh/odd-bundle-name.zip"}
              ]}]
            """;
        var handler = new StubHandler(req =>
            (req.RequestUri?.AbsolutePath ?? "").Contains("/releases", StringComparison.Ordinal)
                ? OkReleases(listJson)
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.0.0", null);
        Assert.True(result.Succeeded);
        Assert.True(result.UpdateAvailable);
        Assert.True(result.RecommendedZipAssetMissing);
        Assert.False(result.RecommendedZipPatternMatched);
        Assert.Contains("github.com", result.ReleaseNotesUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("https://cdn/gh/odd-bundle-name.zip", result.RecommendedZipDownloadUrl);
    }

    [Fact]
    public async Task InvalidVersionTag_IgnoredVersion_Suppresses()
    {
        const string url = "https://github.com/x/y/releases/tag/not-a-version";
        var handler = new StubHandler(_ => OkReleases(ReleasesArraySingle("not-a-version", htmlUrl: url)));
        using var http = Client(handler);
        using var service = new GitHubReleaseUpdateCheckService(http);
        var result = await service.CheckForNewerReleaseAsync("1.1.4", "not-a-version");
        Assert.True(result.Succeeded);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.IgnoredVersion, result.Outcome);
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

    [Fact]
    public void ReleaseVersionParser_StripsProductPrefix()
    {
        Assert.Equal("1.0.0", ReleaseVersionParser.NormalizeLabel("ForgerEMS-v1.0.0"));
    }

    [Fact]
    public void AppSemanticVersion_RcSeries_Ordered()
    {
        Assert.True(AppSemanticVersion.TryParse("1.1.12-rc.3", out var a));
        Assert.True(AppSemanticVersion.TryParse("1.1.12-rc.2", out var b));
        Assert.True(a.CompareTo(b) > 0);
    }

    [Fact]
    public void AppSemanticVersion_StableGreaterThanSamePatchRc()
    {
        Assert.True(AppSemanticVersion.TryParse("1.1.12", out var stable));
        Assert.True(AppSemanticVersion.TryParse("1.1.12-rc.9", out var rc));
        Assert.True(stable.CompareTo(rc) > 0);
    }

    [Fact]
    public void AppSemanticVersion_NextMinorBetaGreaterThanPreviousStable()
    {
        Assert.True(AppSemanticVersion.TryParse("1.1.13-beta.1", out var beta));
        Assert.True(AppSemanticVersion.TryParse("1.1.12", out var stable));
        Assert.True(beta.CompareTo(stable) > 0);
    }
}
