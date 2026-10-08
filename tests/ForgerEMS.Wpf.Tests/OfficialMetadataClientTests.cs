using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class OfficialMetadataClientTests
{
    private static OfficialMetadataClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new Handler(respond)));

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }

    [Fact]
    public async Task GitHubArtifactRedirect_ToCdnWithEncodedQuery_Allowed()
    {
        var requests = new List<HttpRequestMessage>();
        var client = Client(req =>
        {
            requests.Add(req);
            if (req.RequestUri!.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers =
                    {
                        Location = new Uri(
                            "https://release-assets.githubusercontent.com/github/production/1/2/asset.zip?X-Amz-Signature=abc%2fdef&X-Amz-Expires=300")
                    }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1, 2, 3 })
            };
        });

        var stream = await client.OpenStreamAsync(new OfficialMetadataClient.MetadataRequest
        {
            Uri = new Uri("https://github.com/owner/repo/releases/download/v1/a.zip"),
            IsGitHubArtifact = true
        });
        Assert.Equal(2, requests.Count);
        Assert.Equal("release-assets.githubusercontent.com", requests[1].RequestUri!.Host);
        await stream.DisposeAsync();
    }

    [Fact]
    public async Task Redirect_ToUntrustedHost_Rejected()
    {
        var client = Client(req =>
        {
            if (req.RequestUri!.Host == "vendor.example.test")
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("https://evil.example.test/payload.bin") }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(new byte[] { 1 })
            };
        });

        await Assert.ThrowsAsync<OfficialMetadataClient.MetadataFetchException>(() =>
            client.FetchAsync(new OfficialMetadataClient.MetadataRequest
            {
                Uri = new Uri("https://vendor.example.test/meta.json"),
                MaxAttempts = 1
            }));
    }

    [Fact]
    public async Task Redirect_DowngradeToHttp_Rejected()
    {
        var client = Client(req =>
        {
            if (req.RequestUri!.Host == "vendor.example.test")
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("http://vendor.example.test/meta.json") }
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAsync<OfficialMetadataClient.MetadataFetchException>(() =>
            client.FetchAsync(new OfficialMetadataClient.MetadataRequest
            {
                Uri = new Uri("https://vendor.example.test/meta.json"),
                MaxAttempts = 1
            }));
    }

    [Fact]
    public async Task GitHubToken_NotForwardedToArtifactHosts()
    {
        var requests = new List<HttpRequestMessage>();
        var client = Client(req =>
        {
            requests.Add(req);
            if (req.RequestUri!.Host == "api.github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.Found)
                {
                    Headers = { Location = new Uri("https://github.com/o/r/releases/download/v1/a.zip") }
                };
            }

            if (req.RequestUri!.Host == "github.com")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("ok", Encoding.UTF8)
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        var result = await client.FetchAsync(new OfficialMetadataClient.MetadataRequest
        {
            Uri = new Uri("https://api.github.com/repos/o/r/releases/latest"),
            IsGitHubApi = true,
            IsGitHubArtifact = true,
            GitHubToken = "secret-token",
            MaxAttempts = 1
        });

        Assert.Equal(2, requests.Count);
        Assert.Equal("secret-token", requests[0].Headers.Authorization?.Parameter);
        Assert.Null(requests[1].Headers.Authorization);
    }

    [Fact]
    public async Task InitialUri_NonHttps_Rejected()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK));
        await Assert.ThrowsAsync<OfficialMetadataClient.MetadataFetchException>(() =>
            client.FetchAsync(new OfficialMetadataClient.MetadataRequest
            {
                Uri = new Uri("http://insecure.example.test/meta.json"),
                MaxAttempts = 1
            }));
    }

    [Fact]
    public async Task Fetch_SucceedsOnPlain200()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"a\":1}", Encoding.UTF8)
        });
        var result = await client.FetchAsync(new OfficialMetadataClient.MetadataRequest
        {
            Uri = new Uri("https://vendor.example.test/meta.json"),
            MaxAttempts = 1
        });
        Assert.Equal("{\"a\":1}", result.BodyAsString());
    }
}
