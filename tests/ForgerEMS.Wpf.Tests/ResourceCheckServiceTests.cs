using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ResourceCheckServiceTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(
        Path.GetTempPath(), "forgerems-check-" + Guid.NewGuid().ToString("N"));

    private static readonly string ShaZ = new string('a', 64);

    private static readonly string PolicyJson = """
        {
          "schemaVersion": 1,
          "defaultChannel": "stable",
          "metadataTimeoutSeconds": 5,
          "maximumAttempts": 1,
          "cacheTtlMinutes": 60,
          "resources": [
            {
              "id": "ventoy",
              "name": "Ventoy",
              "provider": "github-stable",
              "repository": "ventoy/Ventoy",
              "source": "https://github.com/ventoy/Ventoy/releases/latest",
              "assetPattern": "^ventoy-[0-9.]+-windows\\.zip$",
              "architecture": "x64"
            },
            {
              "id": "memtest86plus",
              "name": "MemTest86+",
              "provider": "official-page",
              "source": "https://www.memtest.org/",
              "architecture": "x64",
              "exception": "Manual vendor page only."
            }
          ]
        }
        """;

    private static string ReleaseJson() => $$"""
        {
          "tag_name": "v1.1.12",
          "draft": false,
          "prerelease": false,
          "assets": [
            {
              "name": "ventoy-1.1.12-windows.zip",
              "browser_download_url": "https://github.com/ventoy/Ventoy/releases/download/v1.1.12/ventoy-1.1.12-windows.zip",
              "digest": "sha256:{{ShaZ}}",
              "size": 100
            }
          ]
        }
        """;

    private ResourceCheckService Build(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var catalog = ResourceCatalog.Parse(PolicyJson);
        var transport = new OfficialMetadataClient(new HttpClient(new Handler(respond)));
        return new ResourceCheckService(
            catalog,
            new ResourceProviderResolver(transport, maxAttempts: 1),
            new ResourceMetadataCache(_cacheDir));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            respond(request);
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    [Fact]
    public async Task CheckAll_ResolvesVentoy_AndMarksManualException()
    {
        var calls = 0;
        var svc = Build(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Json(ReleaseJson()));
        });

        var results = await svc.CheckAsync();
        Assert.Equal(2, results.Count);
        var ventoy = results.Single(r => r.ResourceId == "ventoy");
        Assert.Equal(ResourceResolutionState.ResolvedMetadata, ventoy.State);
        Assert.True(ventoy.IsDownloadEligible); // fresh stamp carries ExpiresAtUtc
        Assert.Equal(ShaZ, ventoy.ExpectedSha256);

        var memtest = results.Single(r => r.ResourceId == "memtest86plus");
        Assert.Equal(ResourceResolutionState.RequiresUserAction, memtest.State);
        Assert.False(memtest.IsDownloadEligible);
        Assert.Equal(1, calls); // no HTTP for the manual exception
    }

    [Fact]
    public async Task ConcurrentChecks_SemaphoreDedup_SingleFetch()
    {
        var calls = 0;
        var svc = Build(async _ =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(50); // widen the race window
            return Json(ReleaseJson());
        });

        var first = svc.CheckAsync(new ResourceCheckRequest { ResourceIds = new[] { "ventoy" } });
        var second = svc.CheckAsync(new ResourceCheckRequest { ResourceIds = new[] { "ventoy" } });
        await Task.WhenAll(first, second);
        Assert.Equal(1, calls);
        Assert.True(first.Result.Single().IsDownloadEligible);
        Assert.True(second.Result.Single().IsDownloadEligible);
    }

    [Fact]
    public async Task CancelledCaller_DoesNotPoisonSecondCaller()
    {
        var calls = 0;
        var svc = Build(async _ =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(80);
            return Json(ReleaseJson());
        });

        using var cts = new CancellationTokenSource();
        var cancelled = svc.CheckAsync(
            new ResourceCheckRequest { ResourceIds = new[] { "ventoy" } }, cts.Token);
        cts.CancelAfter(10);

        // The cancelled caller throws; a second caller still resolves cleanly.
        var second = svc.CheckAsync(new ResourceCheckRequest { ResourceIds = new[] { "ventoy" } });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var ok = await second;
        Assert.True(ok.Single().IsDownloadEligible);
    }

    [Fact]
    public async Task SecondCall_UsesCache_NoSecondFetch()
    {
        var calls = 0;
        var svc = Build(_ =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(Json(ReleaseJson()));
        });

        await svc.CheckAsync(new ResourceCheckRequest { ResourceIds = new[] { "ventoy" } });
        var again = await svc.CheckAsync(new ResourceCheckRequest { ResourceIds = new[] { "ventoy" } });
        Assert.Equal(1, calls);
        Assert.True(again.Single().FromCache);
    }

    [Fact]
    public async Task Outage_ReturnsStaleCache_NotEligible()
    {
        // Deterministically prime an EXPIRED cache entry: CheckedAtUtc two hours old,
        // 60-minute TTL — no sleeps, no clock races.
        var catalog = ResourceCatalog.Parse(PolicyJson);
        var descriptor = catalog.Resources.Single(d => d.ResourceId == "ventoy");
        var cache = new ResourceMetadataCache(_cacheDir);
        cache.Store(new ResourceResolution
        {
            Descriptor = descriptor,
            State = ResourceResolutionState.ResolvedMetadata,
            Version = "1.1.12",
            ArtifactUri = "https://github.com/ventoy/Ventoy/releases/download/v1.1.12/ventoy-1.1.12-windows.zip",
            ArtifactFileName = "ventoy-1.1.12-windows.zip",
            ExpectedSha256 = ShaZ,
            SizeBytes = 100,
            CheckedAtUtc = DateTimeOffset.UtcNow.AddHours(-2)
        }, TimeSpan.FromMinutes(60));

        var calls = 0;
        var failing = new ResourceCheckService(
            catalog,
            new ResourceProviderResolver(
                new OfficialMetadataClient(new HttpClient(new Handler(
                    _ =>
                    {
                        Interlocked.Increment(ref calls);
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway));
                    }))),
                maxAttempts: 1),
            cache);

        var result = await failing.CheckAsync(new ResourceCheckRequest { ResourceIds = new[] { "ventoy" } });
        var r = result.Single();
        Assert.Equal(ResourceResolutionState.CachedStale, r.State);
        Assert.False(r.IsDownloadEligible);
        Assert.True(r.FromCache);
        Assert.Equal(1, calls); // exactly one failed network attempt; stale cache still surfaced
    }

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, recursive: true); } catch { }
    }
}
