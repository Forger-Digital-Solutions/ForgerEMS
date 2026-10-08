using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// Bounded trust transport shared by the app updater, resource resolver, and Ventoy discovery.
/// HTTPS-only, no auto redirects, per-request GitHub auth, manual redirect following with a
/// strict host allow-list, response size caps, and bounded transient retries.
/// </summary>
public sealed class OfficialMetadataClient : IDisposable
{
    public const int MaxRedirects = 5;
    public const long DefaultMetadataMaxBytes = 4L * 1024 * 1024;
    public const long DefaultChecksumMaxBytes = 256L * 1024;
    public const long DefaultArtifactMaxBytes = 8L * 1024 * 1024 * 1024;

    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(1000)
    };

    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(5);

    private static readonly HashSet<string> GitHubArtifactRedirectHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "release-assets.githubusercontent.com",
        "objects.githubusercontent.com"
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public OfficialMetadataClient(HttpClient? httpClient = null)
    {
        if (httpClient is null)
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            _ownsClient = true;
        }
        else
        {
            _httpClient = httpClient;
            _ownsClient = false;
        }
    }

    /// <summary>Log-safe URL: scheme + host + path only; query and fragment stripped.</summary>
    public static string SanitizeForLog(Uri uri) => $"{uri.Scheme}://{uri.Host}{uri.AbsolutePath}";

    public sealed record MetadataRequest
    {
        public required Uri Uri { get; init; }

        /// <summary>Extra allowed redirect/artifact hosts from the resource descriptor.</summary>
        public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();

        /// <summary>Accept GitHub artifact redirect targets (githubusercontent / objects) for this URL.</summary>
        public bool IsGitHubArtifact { get; init; }

        /// <summary>GitHub REST API endpoint; allows the configured bearer token for this request only.</summary>
        public bool IsGitHubApi { get; init; }

        public long MaxBytes { get; init; } = DefaultMetadataMaxBytes;
        public int MaxAttempts { get; init; } = 2;
        public string UserAgent { get; init; } = "ForgerEMS-Metadata/1.0";
        public string? GitHubToken { get; init; }
    }

    public sealed record FetchResult(
        byte[] Body,
        Uri FinalUri,
        HttpStatusCode StatusCode,
        long? ContentLength)
    {
        public string BodyAsString() => System.Text.Encoding.UTF8.GetString(Body);
    }

    public class MetadataFetchException : Exception
    {
        public MetadataFetchException(string message, bool isTransient = false) : base(message)
        {
            IsTransient = isTransient;
        }

        public bool IsTransient { get; }
    }

    /// <summary>A validated artifact response stream. Caller owns and must dispose it.</summary>
    public sealed record ArtifactStream(
        Stream Body,
        Uri FinalUri,
        long? ContentLength) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Body.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Open a validated artifact stream (redirect rules identical to FetchAsync) for
    /// callers that must hash/verify without buffering the whole artifact in memory.
    /// </summary>
    public async Task<ArtifactStream> OpenStreamAsync(
        MetadataRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequestUri(request.Uri);
        var initialHost = request.Uri.Host;
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await OpenStreamFollowingRedirectsAsync(request, initialHost, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MetadataFetchException ex) when (ex.IsTransient && attempt < Math.Max(1, request.MaxAttempts))
            {
                var delay = RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)];
                if (ex is MetadataRetryAfterException retryAfter && retryAfter.RetryAfter > delay)
                {
                    delay = retryAfter.RetryAfter;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<ArtifactStream> OpenStreamFollowingRedirectsAsync(
        MetadataRequest request,
        string initialHost,
        CancellationToken cancellationToken)
    {
        var current = request.Uri;
        var allowedHosts = BuildAllowedHosts(request, initialHost);

        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            ValidateRequestUri(current);
            if (!allowedHosts.Contains(current.Host))
            {
                throw new MetadataFetchException(
                    $"Redirect target host '{current.Host}' is not allowed for {SanitizeForLog(request.Uri)}.");
            }

            var message = new HttpRequestMessage(HttpMethod.Get, current);
            message.Headers.UserAgent.ParseAdd(request.UserAgent);

            if (!string.IsNullOrWhiteSpace(request.GitHubToken)
                && request.IsGitHubApi
                && string.Equals(current.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.GitHubToken);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(
                    message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                message.Dispose();
                throw;
            }

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                message.Dispose();
                response.Dispose();
                if (location is null)
                {
                    throw new MetadataFetchException(
                        $"Redirect from {SanitizeForLog(current)} carried no Location header.");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                ValidateRequestUri(next);
                ValidateRedirectTarget(request, current, next, allowedHosts);
                current = next;
                continue;
            }

            if (IsTransientStatus(response.StatusCode) || !response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                var retryAfter = ReadRetryAfter(response);
                message.Dispose();
                response.Dispose();
                if (IsTransientStatus(status))
                {
                    throw new MetadataRetryAfterException(
                        $"Transient HTTP {(int)status} from {SanitizeForLog(current)}.", retryAfter);
                }

                throw new MetadataFetchException(
                    $"HTTP {(int)status} from {SanitizeForLog(current)}.");
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > request.MaxBytes)
            {
                var uri = SanitizeForLog(current);
                message.Dispose();
                response.Dispose();
                throw new MetadataFetchException(
                    $"Response from {uri} exceeds {request.MaxBytes} byte cap.");
            }

            var stream = await response.Content
                .ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new ArtifactStream(
                new ResponseStream(stream, response, message, request.MaxBytes),
                current,
                response.Content.Headers.ContentLength);
        }

        throw new MetadataFetchException(
            $"Exceeded {MaxRedirects} redirects for {SanitizeForLog(request.Uri)}.");
    }

    /// <summary>Stream wrapper that enforces the byte cap and disposes response+request together.</summary>
    private sealed class ResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly HttpResponseMessage _response;
        private readonly HttpRequestMessage _request;
        private readonly long _maxBytes;
        private long _read;

        public ResponseStream(Stream inner, HttpResponseMessage response, HttpRequestMessage request, long maxBytes)
        {
            _inner = inner;
            _response = response;
            _request = request;
            _maxBytes = maxBytes;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            _read += read;
            if (_read > _maxBytes)
            {
                throw new MetadataFetchException($"Response exceeded the {_maxBytes} byte cap.");
            }

            return read;
        }

        public override async Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            _read += read;
            if (_read > _maxBytes)
            {
                throw new MetadataFetchException($"Response exceeded the {_maxBytes} byte cap.");
            }

            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                _response.Dispose();
                _request.Dispose();
            }

            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static HashSet<string> BuildAllowedHosts(MetadataRequest request, string initialHost)
    {
        var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { initialHost };
        if (request.AllowedHosts is not null)
        {
            foreach (var host in request.AllowedHosts)
            {
                if (!string.IsNullOrWhiteSpace(host))
                {
                    allowedHosts.Add(host.Trim());
                }
            }
        }

        // GitHub release artifacts may redirect to GitHub's own asset/CDN hosts; those
        // hosts are only admissible for requests explicitly marked as GitHub artifacts.
        if (request.IsGitHubArtifact)
        {
            foreach (var host in GitHubArtifactRedirectHosts)
            {
                allowedHosts.Add(host);
            }
        }

        return allowedHosts;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    public async Task<FetchResult> FetchAsync(MetadataRequest request, CancellationToken cancellationToken = default)
    {
        ValidateRequestUri(request.Uri);
        var initialHost = request.Uri.Host;
        var attempt = 0;

        while (true)
        {
            attempt++;
            try
            {
                return await FetchFollowingRedirectsAsync(request, initialHost, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (MetadataFetchException ex) when (ex.IsTransient && attempt < Math.Max(1, request.MaxAttempts))
            {
                var delay = RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)];
                if (ex is MetadataRetryAfterException retryAfter && retryAfter.RetryAfter > delay)
                {
                    delay = retryAfter.RetryAfter;
                }

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<FetchResult> FetchFollowingRedirectsAsync(
        MetadataRequest request,
        string initialHost,
        CancellationToken cancellationToken)
    {
        var current = request.Uri;
        var allowedHosts = BuildAllowedHosts(request, initialHost);

        for (var redirectCount = 0; redirectCount <= MaxRedirects; redirectCount++)
        {
            ValidateRequestUri(current);
            if (!allowedHosts.Contains(current.Host))
            {
                throw new MetadataFetchException(
                    $"Redirect target host '{current.Host}' is not allowed for {SanitizeForLog(request.Uri)}.");
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, current);
            message.Headers.UserAgent.ParseAdd(request.UserAgent);

            // Authorization is per-request and only ever sent to api.github.com.
            if (!string.IsNullOrWhiteSpace(request.GitHubToken)
                && request.IsGitHubApi
                && string.Equals(current.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.GitHubToken);
            }

            using var response = await _httpClient.SendAsync(
                message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                var location = response.Headers.Location;
                if (location is null)
                {
                    throw new MetadataFetchException(
                        $"Redirect from {SanitizeForLog(current)} carried no Location header.");
                }

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                ValidateRequestUri(next);
                ValidateRedirectTarget(request, current, next, allowedHosts);
                current = next;
                continue;
            }

            if (IsTransientStatus(response.StatusCode))
            {
                throw new MetadataRetryAfterException(
                    $"Transient HTTP {(int)response.StatusCode} from {SanitizeForLog(current)}.",
                    ReadRetryAfter(response));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new MetadataFetchException(
                    $"HTTP {(int)response.StatusCode} from {SanitizeForLog(current)}.");
            }

            if (response.Content.Headers.ContentLength is { } declared && declared > request.MaxBytes)
            {
                throw new MetadataFetchException(
                    $"Response from {SanitizeForLog(current)} exceeds {request.MaxBytes} byte cap.");
            }

            var body = await ReadBoundedAsync(response.Content, request.MaxBytes, cancellationToken)
                .ConfigureAwait(false);
            return new FetchResult(body, current, response.StatusCode, response.Content.Headers.ContentLength);
        }

        throw new MetadataFetchException(
            $"Exceeded {MaxRedirects} redirects for {SanitizeForLog(request.Uri)}.");
    }

    private void ValidateRedirectTarget(
        MetadataRequest request,
        Uri current,
        Uri next,
        HashSet<string> allowedHosts)
    {
        if (request.IsGitHubArtifact)
        {
            // GitHub artifact redirects may only go to GitHub's own asset/CDN hosts.
            if (!GitHubArtifactRedirectHosts.Contains(next.Host))
            {
                throw new MetadataFetchException(
                    $"GitHub artifact redirect to untrusted host '{next.Host}'.");
            }

            if (ContainsPathTraversal(next))
            {
                throw new MetadataFetchException("Redirect target contains path traversal.");
            }

            return;
        }

        // Non-GitHub sources: redirect must stay on an explicitly allowed host
        // (the initial source host plus descriptor-declared allowedHosts/baseUrl host).
        if (!allowedHosts.Contains(next.Host))
        {
            throw new MetadataFetchException(
                $"Redirect to '{next.Host}' is not permitted for {SanitizeForLog(request.Uri)}.");
        }

        if (ContainsPathTraversal(next))
        {
            throw new MetadataFetchException("Redirect target contains path traversal.");
        }
    }

    public static void ValidateRequestUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            throw new MetadataFetchException($"Only absolute HTTPS URLs are permitted: {uri}.");
        }

        if (uri.Port != 443 && uri.Port != -1)
        {
            throw new MetadataFetchException($"Only port 443 is permitted: {SanitizeForLog(uri)}.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new MetadataFetchException("URLs with embedded credentials are not permitted.");
        }

        if (uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
        {
            throw new MetadataFetchException($"Literal IP hosts are not permitted: {uri.Host}.");
        }

        if (IsLoopbackHost(uri.Host))
        {
            throw new MetadataFetchException($"Loopback hosts are not permitted: {uri.Host}.");
        }
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
        || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "loopback", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Path-only traversal screening — decoded path segments containing ".." or backslash
    /// are rejected. Query strings are intentionally not screened (signed CDN URLs carry
    /// legitimately encoded parameters).
    /// </summary>
    public static bool ContainsPathTraversal(Uri uri)
    {
        var decodedPath = Uri.UnescapeDataString(uri.AbsolutePath);
        foreach (var segment in decodedPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".." || segment.Contains('\\'))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or (HttpStatusCode)308;

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout
        || status == (HttpStatusCode)429
        || (int)status >= 500;

    private static TimeSpan ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta > MaxRetryAfter ? MaxRetryAfter : delta;
        }

        if (retryAfter?.Date is { } date && date > DateTimeOffset.UtcNow)
        {
            var remaining = date - DateTimeOffset.UtcNow;
            return remaining > MaxRetryAfter ? MaxRetryAfter : remaining;
        }

        return TimeSpan.Zero;
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content, long maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > maxBytes)
            {
                throw new MetadataFetchException($"Response exceeded the {maxBytes} byte cap.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private sealed class MetadataRetryAfterException : MetadataFetchException
    {
        public MetadataRetryAfterException(string message, TimeSpan retryAfter) : base(message, isTransient: true)
        {
            RetryAfter = retryAfter;
        }

        public TimeSpan RetryAfter { get; }
    }
}
