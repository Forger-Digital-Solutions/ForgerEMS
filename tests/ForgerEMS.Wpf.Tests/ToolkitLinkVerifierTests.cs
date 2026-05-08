using System.Net;
using System.Net.Http;
using System.Linq;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitLinkVerifierTests
{
    [Fact]
    public async Task VerifyAsync_HeadSuccess_ReturnsVerifiedMetadataWithLength()
    {
        using var handler = new DelegateHttpMessageHandler((req, _) =>
        {
            Assert.Equal(HttpMethod.Head, req.Method);
            var ok = new HttpResponseMessage(HttpStatusCode.OK);
            ok.RequestMessage = req;
            ok.Content = new ByteArrayContent([]);
            ok.Content.Headers.ContentLength = 4096;
            return ok;
        });
        using var http = new HttpClient(handler);
        using var verifier = new ToolkitLinkVerifier(http);

        var snapshot = await verifier.VerifyAsync(
            @"E:\",
            [new ToolkitLinkVerificationInput("ToolA", "https://vendor.example/file.iso", null, "Verified")],
            new ToolkitLinkVerificationRunOptions { PerRequestTimeout = TimeSpan.FromSeconds(2), OverallDeadline = TimeSpan.FromSeconds(5) },
            CancellationToken.None);

        Assert.True(snapshot.CompletedSuccessfully);
        Assert.Single(snapshot.Entries);
        var e = snapshot.Entries[0];
        Assert.Equal(ToolkitLinkVerificationStatus.VerifiedMetadata, e.Status);
        Assert.Equal(200, e.HttpStatus);
        Assert.Contains("4096", e.ContentLengthNote, StringComparison.Ordinal);
        Assert.Equal("vendor.example", e.OriginalHost);
        Assert.Equal("vendor.example", e.FinalHost);
    }

    [Fact]
    public async Task VerifyAsync_Head405ThenRangedGet_SucceedsWithoutLargeDownload()
    {
        var calls = 0;
        using var handler = new DelegateHttpMessageHandler((req, _) =>
        {
            calls++;
            if (req.Method == HttpMethod.Head)
            {
                var headResp = new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                headResp.RequestMessage = req;
                return headResp;
            }

            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.NotNull(req.Headers.Range);
            var range = req.Headers.Range.Ranges.First();
            Assert.Equal(0L, range.From);
            Assert.Equal(0L, range.To);
            var ok = new HttpResponseMessage(HttpStatusCode.PartialContent);
            ok.RequestMessage = req;
            ok.Content = new ByteArrayContent([]);
            ok.Content.Headers.ContentLength = 100;
            ok.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(0, 0, 100);
            return ok;
        });
        using var http = new HttpClient(handler);
        using var verifier = new ToolkitLinkVerifier(http);

        var snapshot = await verifier.VerifyAsync(
            @"E:\",
            [new ToolkitLinkVerificationInput("ToolB", "https://files.vendor.example/app.zip", 100L, "unknown")],
            new ToolkitLinkVerificationRunOptions { PerRequestTimeout = TimeSpan.FromSeconds(5), OverallDeadline = TimeSpan.FromSeconds(10) },
            CancellationToken.None);

        Assert.Equal(2, calls);
        Assert.Equal(ToolkitLinkVerificationStatus.VerifiedMetadata, snapshot.Entries[0].Status);
        Assert.True(snapshot.Entries[0].DetailReason.Contains("ranged GET", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task VerifyAsync_RangedGetIgnoredByServer_ReadsOnlySmallPrefix()
    {
        CountingContent? content = null;
        using var handler = new DelegateHttpMessageHandler((req, _) =>
        {
            if (req.Method == HttpMethod.Head)
            {
                var headResp = new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                headResp.RequestMessage = req;
                return headResp;
            }

            Assert.Equal(HttpMethod.Get, req.Method);
            Assert.NotNull(req.Headers.Range);
            content = new CountingContent(totalBytes: 8L * 1024 * 1024);
            var ok = new HttpResponseMessage(HttpStatusCode.OK);
            ok.RequestMessage = req;
            ok.Content = content;
            ok.Content.Headers.ContentLength = 8L * 1024 * 1024;
            return ok;
        });
        using var http = new HttpClient(handler);
        using var verifier = new ToolkitLinkVerifier(http);

        var snapshot = await verifier.VerifyAsync(
            @"E:\",
            [new ToolkitLinkVerificationInput("Large", "https://vendor.example/large.iso", null, "unknown")],
            new ToolkitLinkVerificationRunOptions { PerRequestTimeout = TimeSpan.FromSeconds(5), OverallDeadline = TimeSpan.FromSeconds(10) },
            CancellationToken.None);

        Assert.NotNull(content);
        Assert.True(content!.BytesRead <= 1024, $"Expected at most 1024 bytes read, got {content.BytesRead}.");
        Assert.Contains("server ignored Range", snapshot.Entries[0].DetailReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_PerRequestTimeout_MapsToUnknownOffline()
    {
        using var handler = new HangUntilCanceledHandler();
        using var http = new HttpClient(handler);
        using var verifier = new ToolkitLinkVerifier(http);

        var snapshot = await verifier.VerifyAsync(
            @"E:\",
            [new ToolkitLinkVerificationInput("Slow", "https://slow.example/x", null, "Verified")],
            new ToolkitLinkVerificationRunOptions { PerRequestTimeout = TimeSpan.FromMilliseconds(30), OverallDeadline = TimeSpan.FromSeconds(2) },
            CancellationToken.None);

        Assert.Equal(ToolkitLinkVerificationStatus.UnknownOffline, snapshot.Entries[0].Status);
        Assert.Contains("Timed out", snapshot.Entries[0].DetailReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task VerifyAsync_UserCancellation_PropagatesInsteadOfSavingSuccess()
    {
        using var handler = new HangUntilCanceledHandler();
        using var http = new HttpClient(handler);
        using var verifier = new ToolkitLinkVerifier(http);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            verifier.VerifyAsync(
                @"E:\",
                [new ToolkitLinkVerificationInput("Slow", "https://slow.example/x", null, "Verified")],
                new ToolkitLinkVerificationRunOptions { PerRequestTimeout = TimeSpan.FromSeconds(5), OverallDeadline = TimeSpan.FromSeconds(10) },
                cts.Token));
    }

    [Fact]
    public async Task VerifyAsync_OverallDeadline_PropagatesInsteadOfSavingSuccess()
    {
        using var handler = new HangUntilCanceledHandler();
        using var http = new HttpClient(handler);
        using var verifier = new ToolkitLinkVerifier(http);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            verifier.VerifyAsync(
                @"E:\",
                [new ToolkitLinkVerificationInput("Slow", "https://slow.example/x", null, "Verified")],
                new ToolkitLinkVerificationRunOptions { PerRequestTimeout = TimeSpan.FromSeconds(5), OverallDeadline = TimeSpan.FromMilliseconds(30) },
                CancellationToken.None));
    }

    [Fact]
    public async Task VerifyAsync_Redirect307_CapturesFinalHostAndWarningWhenMisaligned()
    {
        using var inner = new Redirect307ThenOkHandler();
        using var redirecting = new AutoRedirectDelegatingHandler(inner);
        using var http = new HttpClient(redirecting);
        using var verifier = new ToolkitLinkVerifier(http);

        var snapshot = await verifier.VerifyAsync(
            @"E:\",
            [new ToolkitLinkVerificationInput("CDN", "https://www.vendor.example/start", null, "Verified")],
            new ToolkitLinkVerificationRunOptions { PerRequestTimeout = TimeSpan.FromSeconds(5), OverallDeadline = TimeSpan.FromSeconds(10) },
            CancellationToken.None);

        var e = snapshot.Entries[0];
        Assert.Equal("cdn.otherdomain.net", e.FinalHost, StringComparer.OrdinalIgnoreCase);
        Assert.False(e.OfficialDomainAligned);
        Assert.Equal(ToolkitLinkVerificationStatus.Warning, e.Status);
    }

    [Fact]
    public void RedactUriFingerprint_StripsQuerySecrets()
    {
        var redacted = ToolkitLinkVerificationRedactor.RedactUrlForLog(
            "https://vendor.example/path/download?token=super-secret&sig=abc123");
        Assert.Contains("vendor.example", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted-query]", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sig=", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactUriFingerprint_StripsPrivatePathSegments()
    {
        var redacted = ToolkitLinkVerificationRedactor.RedactUrlForLog(
            "https://vendor.example/users/alice/private/license-token/download.bin?token=super-secret");

        Assert.Contains("vendor.example", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted-path]", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted-query]", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alice", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("license-token", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("super-secret", redacted, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Mirrors SocketsHttpHandler redirect follow for synthetic test doubles.</summary>
    private sealed class AutoRedirectDelegatingHandler : DelegatingHandler
    {
        public AutoRedirectDelegatingHandler(HttpMessageHandler inner)
            : base(inner)
        {
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var code = (int)response.StatusCode;
            if (code is >= 300 and < 400 && response.Headers.Location is not null)
            {
                var location = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(request.RequestUri!, response.Headers.Location);
                response.Dispose();
                using var redirected = new HttpRequestMessage(request.Method, location);
                foreach (var header in request.Headers)
                {
                    redirected.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                return await base.SendAsync(redirected, cancellationToken).ConfigureAwait(false);
            }

            return response;
        }
    }

    private sealed class Redirect307ThenOkHandler : HttpMessageHandler
    {
        private int _n;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _n++;
            if (_n == 1)
            {
                Assert.Equal(HttpMethod.Head, request.Method);
                var redirect = new HttpResponseMessage(HttpStatusCode.RedirectKeepVerb);
                redirect.RequestMessage = request;
                redirect.Headers.Location = new Uri("https://cdn.otherdomain.net/file.bin");
                return Task.FromResult(redirect);
            }

            Assert.Equal("cdn.otherdomain.net", request.RequestUri!.Host, StringComparer.OrdinalIgnoreCase);
            var ok = new HttpResponseMessage(HttpStatusCode.OK);
            ok.RequestMessage = request;
            ok.Content = new ByteArrayContent([]);
            ok.Content.Headers.ContentLength = 10;
            return Task.FromResult(ok);
        }
    }

    private sealed class DelegateHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler;

        public DelegateHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
            _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request, cancellationToken));
    }

    private sealed class HangUntilCanceledHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return null!;
        }
    }

    private sealed class CountingContent : HttpContent
    {
        private readonly long _totalBytes;

        public CountingContent(long totalBytes) => _totalBytes = totalBytes;

        public long BytesRead { get; private set; }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = _totalBytes;
            return true;
        }

        protected override Task<Stream> CreateContentReadStreamAsync() =>
            Task.FromResult<Stream>(new CountingStream(this, _totalBytes));

        private sealed class CountingStream : Stream
        {
            private readonly CountingContent _owner;
            private readonly long _totalBytes;
            private long _position;

            public CountingStream(CountingContent owner, long totalBytes)
            {
                _owner = owner;
                _totalBytes = totalBytes;
            }

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _totalBytes;

            public override long Position
            {
                get => _position;
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (_position >= _totalBytes)
                {
                    return 0;
                }

                var read = (int)Math.Min(count, _totalBytes - _position);
                Array.Fill(buffer, (byte)'x', offset, read);
                _position += read;
                _owner.BytesRead += read;
                return read;
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (_position >= _totalBytes)
                {
                    return ValueTask.FromResult(0);
                }

                var read = (int)Math.Min(buffer.Length, _totalBytes - _position);
                buffer[..read].Span.Fill((byte)'x');
                _position += read;
                _owner.BytesRead += read;
                return ValueTask.FromResult(read);
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
