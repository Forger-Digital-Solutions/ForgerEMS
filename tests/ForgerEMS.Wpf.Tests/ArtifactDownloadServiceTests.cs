using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
using Xunit;
using static ForgerEMS.Wpf.Services.Resources.ArtifactDownloadService;

namespace ForgerEMS.Wpf.Tests;

public sealed class ArtifactDownloadServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "forgerems-dl-" + Guid.NewGuid().ToString("N"));

    private static readonly Uri VendorUri =
        new("https://vendor.example.test/files/tool-1.0.zip");

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, cancellationToken));
    }

    private sealed class FixedVerifier(bool valid, string? signer = null, string? failure = null)
        : IAuthenticodeVerifier
    {
        public AuthenticodeResult Verify(string filePath, string? expectedPublisher) =>
            new(valid, signer, failure);
    }

    private sealed class FixedSpace(long freeBytes) : IDiskSpaceProbe
    {
        public long GetFreeBytes(string directory) => freeBytes;
    }

    private static string Sha256(byte[] body) =>
        Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();

    private ArtifactDownloadService Service(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond,
        IAuthenticodeVerifier? verifier = null,
        IDiskSpaceProbe? space = null,
        TimeSpan? timeout = null) =>
        new(new OfficialMetadataClient(new HttpClient(new Handler(respond))),
            verifier ?? new FixedVerifier(true, "Forger Digital Solutions"),
            space ?? new FixedSpace(long.MaxValue),
            maxAttempts: 2,
            downloadTimeout: timeout ?? TimeSpan.FromMinutes(1));

    private ArtifactDownloadRequest Request(byte[] body) => new()
    {
        RequestedId = "tool",
        Version = "1.0",
        Source = "test",
        ArtifactUri = VendorUri,
        ExpectedSha256 = Sha256(body),
        AllowedHosts = new[] { "vendor.example.test" }
    };

    [Fact]
    public async Task ValidDownload_VerifiedAndWritten()
    {
        var body = Encoding.UTF8.GetBytes("payload-bytes");
        var svc = Service((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });

        var result = await svc.DownloadAsync(Request(body), _dir);
        Assert.Equal(DownloadState.Verified, result.State);
        Assert.True(result.VerifiedArtifactPresent);
        Assert.True(File.Exists(result.FinalPath));
        Assert.Equal(body, await File.ReadAllBytesAsync(result.FinalPath!));
        Assert.Equal(Sha256(body), result.ActualSha256);
        Assert.Equal("tool", result.RequestedId);
        Assert.Equal("1.0", result.Version);
        Assert.DoesNotContain('?', result.SourceUri ?? string.Empty);
    }

    [Fact]
    public async Task HashMismatch_NoFinalFile()
    {
        var body = Encoding.UTF8.GetBytes("payload");
        var svc = Service((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body)
        });

        var req = Request(body) with { ExpectedSha256 = new string('0', 64) };
        var result = await svc.DownloadAsync(req, _dir);
        Assert.Equal(DownloadState.HashMismatch, result.State);
        Assert.False(result.VerifiedArtifactPresent);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task EmptyBody_Fails()
    {
        var svc = Service((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(Array.Empty<byte>())
        });
        var result = await svc.DownloadAsync(Request(Array.Empty<byte>()), _dir);
        Assert.Equal(DownloadState.DownloadFailed, result.State);
        Assert.False(result.VerifiedArtifactPresent);
    }

    [Fact]
    public async Task DeclaredContentLength_DiffersFromExpected_SizeMismatch()
    {
        var body = Encoding.UTF8.GetBytes("abc");
        var svc = Service((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(body) // Content-Length = 3
        });
        var req = Request(body) with { ExpectedSizeBytes = 999 };
        var result = await svc.DownloadAsync(req, _dir);
        Assert.True(result.State is DownloadState.SizeMismatch or DownloadState.DownloadFailed);
        Assert.False(result.VerifiedArtifactPresent);
    }

    [Fact]
    public async Task UntrustedArtifactHost_RejectedWithoutRequest()
    {
        var called = false;
        var svc = Service((_, _) =>
        {
            called = true;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var req = new ArtifactDownloadRequest
        {
            RequestedId = "tool",
            ArtifactUri = new Uri("https://evil.example.test/tool.zip"),
            ExpectedSha256 = new string('a', 64),
            AllowedHosts = new[] { "vendor.example.test" }
        };
        var result = await svc.DownloadAsync(req, _dir);
        Assert.Equal(DownloadState.UntrustedSource, result.State);
        Assert.False(called);
    }

    [Fact]
    public async Task HttpUri_Rejected()
    {
        var svc = Service((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var req = Request(new byte[] { 1 }) with
        {
            ArtifactUri = new Uri("http://vendor.example.test/tool.zip")
        };
        var result = await svc.DownloadAsync(req, _dir);
        Assert.Equal(DownloadState.UntrustedSource, result.State);
    }

    [Fact]
    public async Task WrongSigner_SignatureInvalid_NoFinalFile()
    {
        var body = Encoding.UTF8.GetBytes("setup");
        var req = Request(body) with
        {
            ArtifactUri = new Uri("https://vendor.example.test/tool-1.0.exe"),
            AllowedHosts = new[] { "vendor.example.test" },
            RequireAuthenticode = true,
            ExpectedPublisher = "Forger Digital Solutions"
        };
        var svcBad = new ArtifactDownloadService(
            new OfficialMetadataClient(new HttpClient(new Handler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body)
            }))),
            new FixedVerifier(false, "Somebody Else Ltd", "Unexpected signer"),
            new FixedSpace(long.MaxValue));
        var bad = await svcBad.DownloadAsync(req, _dir);
        Assert.Equal(DownloadState.SignatureInvalid, bad.State);
        Assert.False(bad.VerifiedArtifactPresent);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task InsufficientDiskSpace_FailsClosed()
    {
        var svc = Service(
            (_, _) => new HttpResponseMessage(HttpStatusCode.OK),
            space: new FixedSpace(10));
        var req = Request(new byte[] { 1 }) with { ExpectedSizeBytes = 1024 * 1024 };
        var result = await svc.DownloadAsync(req, _dir);
        Assert.Equal(DownloadState.InsufficientDiskSpace, result.State);
    }

    [Fact]
    public async Task CallerCancellation_ReturnsCancelled()
    {
        var svc = Service((_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await svc.DownloadAsync(Request(new byte[] { 1 }), _dir, cts.Token);
        Assert.Equal(DownloadState.Cancelled, result.State);
    }

    [Fact]
    public async Task DescriptorTimeout_DistinctFromCancelled()
    {
        var svc = new ArtifactDownloadService(
            new OfficialMetadataClient(new HttpClient(new Handler((_, ct) =>
            {
                // Simulate a stalled server: delay past the injected timeout.
                ct.WaitHandle.WaitOne(TimeSpan.FromSeconds(30));
                ct.ThrowIfCancellationRequested();
                return new HttpResponseMessage(HttpStatusCode.OK);
            }))),
            new FixedVerifier(true),
            new FixedSpace(long.MaxValue),
            maxAttempts: 1,
            downloadTimeout: TimeSpan.FromMilliseconds(50));

        var result = await svc.DownloadAsync(Request(new byte[] { 1 }), _dir);
        Assert.Equal(DownloadState.Timeout, result.State);
    }

    [Fact]
    public async Task TransientFailure_RetriedOnceThenSucceeds()
    {
        var calls = 0;
        var body = Encoding.UTF8.GetBytes("retry-payload");
        var svc = Service((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        });
        var result = await svc.DownloadAsync(Request(body), _dir);
        Assert.Equal(2, calls);
        Assert.Equal(DownloadState.Verified, result.State);
    }

    [Fact]
    public async Task Retry_CleansOwnPartial_AndSucceeds()
    {
        // First attempt fails mid-stream (transient), leaving a partial; second succeeds.
        var calls = 0;
        var body = Encoding.UTF8.GetBytes("full-body");
        var svc = Service((_, _) =>
        {
            calls++;
            if (calls == 1)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new BrokenStreamContent()
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
        });
        var result = await svc.DownloadAsync(Request(body), _dir);
        Assert.Equal(DownloadState.Verified, result.State);
        // No stale .partial directories remain.
        Assert.Empty(Directory.GetDirectories(_dir).Where(d => d.Contains(".partial-")));
    }

    [Fact]
    public async Task UnsafeFileName_Rejected()
    {
        var svc = Service((_, _) => new HttpResponseMessage(HttpStatusCode.OK));
        var req = Request(new byte[] { 1 }) with
        {
            ArtifactUri = new Uri("https://vendor.example.test/files/..")
        };
        var result = await svc.DownloadAsync(req, _dir);
        Assert.Equal(DownloadState.UnsafeFileName, result.State);
    }

    private sealed class BrokenStreamContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            Task.FromException(new IOException("simulated interrupted stream"));

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
