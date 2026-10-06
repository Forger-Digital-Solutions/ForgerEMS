using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Trust/architecture/digest regression coverage for the release-asset selection layer:
/// only official same-release GitHub download URLs with verified integrity metadata may
/// surface as downloadable update assets.
/// </summary>
public sealed class GitHubReleaseUpdateTrustTests
{
    private const string Repo = "Forger-Digital-Solutions/ForgerEMS";

    private static string AssetUrl(string tag, string name) =>
        $"https://github.com/{Repo}/releases/download/{tag}/{name}";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    private static HttpClient Client(HttpMessageHandler handler) =>
        new(handler) { Timeout = TimeSpan.FromSeconds(5) };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static string Release(
        string tag,
        string assetsJson,
        bool prerelease = false,
        bool draft = false) =>
        "[{\"tag_name\":\"" + tag + "\",\"html_url\":\"https://github.com/" + Repo +
        "/releases/tag/" + tag + "\",\"draft\":" + (draft ? "true" : "false") +
        ",\"prerelease\":" + (prerelease ? "true" : "false") +
        ",\"published_at\":\"2026-01-01T00:00:00Z\",\"assets\":" + assetsJson + "}]";

    private static string Asset(string tag, string name, string? digest = null, string? url = null) =>
        "{\"name\":\"" + name + "\",\"browser_download_url\":\"" + (url ?? AssetUrl(tag, name)) + "\"" +
        (digest is null ? "" : ",\"digest\":\"sha256:" + digest + "\"") + "}";

    private static GitHubReleaseUpdateCheckService Service(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string arch = "win-x64") =>
        new(Client(new StubHandler(respond)), targetArchitecture: arch);

    private static HttpResponseMessage ReleasesOr404(HttpRequestMessage req, string releasesJson) =>
        (req.RequestUri?.AbsolutePath ?? string.Empty).EndsWith("/releases", StringComparison.Ordinal)
            || (req.RequestUri?.AbsolutePath ?? string.Empty).Contains("/releases?", StringComparison.Ordinal)
            ? Json(releasesJson)
            : new HttpResponseMessage(HttpStatusCode.NotFound);

    [Fact]
    public async Task FutureVersion_ValidDigest_BindsExpectedSha()
    {
        var sha = new string('a', 64);
        var json = Release("v1.2.5", "[" + Asset("v1.2.5", "ForgerEMS-v1.2.5-win-x64.zip", sha) + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(sha, result.ExpectedZipSha256);
        Assert.True(result.ZipArchQualified);
        Assert.Equal("win-x64", result.SelectedArchitecture);
    }

    [Fact]
    public async Task MalformedDigest_NotBound()
    {
        var json = Release("v1.2.5", "[" + Asset("v1.2.5", "ForgerEMS-v1.2.5-win-x64.zip", "zzz-not-hex") + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.ExpectedZipSha256);
    }

    [Fact]
    public async Task UntrustedAssetHost_NotSelectable()
    {
        var json = Release("v1.2.5", "[" + Asset(
            "v1.2.5", "ForgerEMS-v1.2.5-win-x64.zip", new string('a', 64),
            url: "https://evil-cdn.example.test/ForgerEMS-v1.2.5-win-x64.zip") + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable); // release exists; only the download path is withheld
        Assert.Null(result.RecommendedZipDownloadUrl);
        Assert.False(result.HasRecommendedZipDownload);
    }

    [Fact]
    public async Task WrongArchitecture_NotSelectable()
    {
        var json = Release("v1.2.5",
            "[" + Asset("v1.2.5", "ForgerEMS-v1.2.5-win-arm64.zip", new string('a', 64)) + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.RecommendedZipDownloadUrl);
    }

    [Fact]
    public async Task UnqualifiedName_WithoutManifest_DroppedFromDownloadable()
    {
        // "ForgerEMS-v1.2.5.zip" carries no arch token; without a same-release release.json
        // it must not surface as an automated download.
        var json = Release("v1.2.5", "[" + Asset("v1.2.5", "ForgerEMS-v1.2.5.zip", new string('b', 64)) + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.RecommendedZipDownloadUrl);
    }

    [Fact]
    public async Task UnqualifiedName_WithMatchingReleaseJson_KeepsDownload()
    {
        var sha = new string('c', 64);
        var manifestBody = "{\"product\":\"ForgerEMS\",\"version\":\"1.2.5\",\"channel\":\"stable\",\"runtime\":\"win-x64\"}";
        var manifestSha = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(manifestBody))).ToLowerInvariant();
        var assets = "["
            + Asset("v1.2.5", "ForgerEMS-v1.2.5.zip", sha) + ","
            + Asset("v1.2.5", "release.json", manifestSha) + "]";
        var json = Release("v1.2.5", assets);
        var svc = Service(req =>
        {
            var url = req.RequestUri?.AbsoluteUri ?? string.Empty;
            return url.EndsWith("/release.json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(manifestBody, Encoding.UTF8)
                }
                : ReleasesOr404(req, json);
        });
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.Equal(AssetUrl("v1.2.5", "ForgerEMS-v1.2.5.zip"), result.RecommendedZipDownloadUrl);
        Assert.True(result.AssetsValidatedViaReleaseManifest);
    }

    [Fact]
    public async Task UnqualifiedName_WithMismatchedRuntime_Dropped()
    {
        var sha = new string('e', 64);
        var manifestBody = "{\"product\":\"ForgerEMS\",\"version\":\"1.2.5\",\"runtime\":\"win-arm64\"}";
        var manifestSha = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(manifestBody))).ToLowerInvariant();
        var assets = "["
            + Asset("v1.2.5", "ForgerEMS-v1.2.5.zip", sha) + ","
            + Asset("v1.2.5", "release.json", manifestSha) + "]";
        var json = Release("v1.2.5", assets);
        var svc = Service(req =>
        {
            var url = req.RequestUri?.AbsoluteUri ?? string.Empty;
            return url.EndsWith("/release.json", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestBody, Encoding.UTF8) }
                : ReleasesOr404(req, json);
        });
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.Null(result.RecommendedZipDownloadUrl);
    }

    [Fact]
    public async Task MissingIntegrity_StillUpdateAvailable_ButNoAutoDownloadSurface()
    {
        // Trusted URL, no digest, no CHECKSUMS asset -> URL survives but presenter must not
        // expose a download action without an expected hash.
        var json = Release("v1.2.5", "[" + Asset("v1.2.5", "ForgerEMS-v1.2.5-win-x64.zip") + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.True(result.UpdateAvailable);
        Assert.NotNull(result.RecommendedZipDownloadUrl);
        Assert.Null(result.ExpectedZipSha256);

        var view = UpdateCheckUiPresenter.Map(result, isManualCheck: true, installedVersionLabel: "1.2.4");
        Assert.Equal(string.Empty, view.PendingInstallerUrl);
        Assert.Equal(System.Windows.Visibility.Collapsed, view.DownloadButtonVisibility);
    }

    [Fact]
    public async Task Downgrade_InstalledNewer_ReportsInstalledNewer()
    {
        var json = Release("v1.2.0", "[" + Asset("v1.2.0", "ForgerEMS-v1.2.0-win-x64.zip", new string('a', 64)) + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        Assert.False(result.UpdateAvailable);
        Assert.Equal(UpdateCheckOutcome.InstalledNewerThanLatestPublic, result.Outcome);
    }

    [Fact]
    public async Task Presenter_DownloadRequiresExpectedHash()
    {
        var sha = new string('a', 64);
        var json = Release("v1.2.5", "[" + Asset("v1.2.5", "ForgerEMS-v1.2.5-win-x64.zip", sha) + "]");
        var svc = Service(r => ReleasesOr404(r, json));
        var result = await svc.CheckForNewerReleaseAsync("1.2.4", null);
        var view = UpdateCheckUiPresenter.Map(result, isManualCheck: true, installedVersionLabel: "1.2.4");
        Assert.Equal(result.RecommendedZipDownloadUrl, view.PendingInstallerUrl);
        Assert.Equal(sha, view.PendingInstallerExpectedSha256);
        Assert.Equal(System.Windows.Visibility.Visible, view.DownloadButtonVisibility);
    }

    [Theory]
    [InlineData("https://github.com/Forger-Digital-Solutions/ForgerEMS/releases/download/v1.2.5/a.zip", true)]
    [InlineData("https://github.com/Other/Repo/releases/download/v1.2.5/a.zip", false)]
    [InlineData("https://evil.example.test/a.zip", false)]
    [InlineData("http://github.com/Forger-Digital-Solutions/ForgerEMS/releases/download/v1.2.5/a.zip", false)]
    [InlineData("https://user:pw@github.com/Forger-Digital-Solutions/ForgerEMS/releases/download/v1.2.5/a.zip", false)]
    [InlineData("https://github.com/Forger-Digital-Solutions/ForgerEMS/releases/download/v1.2.5/a.zip?x=1", false)]
    [InlineData("https://github.com:444/Forger-Digital-Solutions/ForgerEMS/releases/download/v1.2.5/a.zip", false)]
    public void IsTrustedAssetUrl_Validation(string url, bool expected)
    {
        Assert.Equal(expected, GitHubReleaseUpdateCheckService.IsTrustedAssetUrl(
            "Forger-Digital-Solutions", "ForgerEMS", "v1.2.5", "a.zip", url));
    }

    [Theory]
    [InlineData("ForgerEMS-v1.2.5-win-x64.zip", "win-x64", true)]
    [InlineData("ForgerEMS-v1.2.5.zip", "win-x64", false)]
    [InlineData("ForgerEMS-v1.2.5-win-arm64.zip", "win-x64", false)]
    [InlineData("ForgerEMS-v1.2.5-amd64.zip", "win-x64", true)]
    [InlineData("ForgerEMS-v1.2.5-win-arm64.zip", "win-arm64", true)]
    public void ArchQualification(string name, string arch, bool qualified)
    {
        Assert.Equal(qualified, GitHubReleaseUpdateCheckService.IsArchQualified(name, arch));
        Assert.Equal(!qualified && name.Contains("arm64", StringComparison.OrdinalIgnoreCase),
            GitHubReleaseUpdateCheckService.IsWrongArchitecture(name, arch));
    }
}
