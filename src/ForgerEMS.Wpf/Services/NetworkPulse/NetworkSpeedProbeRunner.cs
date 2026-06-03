using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public sealed class NetworkSpeedProbeRunner : IDisposable
{
    private readonly HttpClient _httpClient;

    public NetworkSpeedProbeRunner(HttpMessageHandler? handler = null)
    {
        _httpClient = handler is null
            ? new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) })
            {
                Timeout = TimeSpan.FromSeconds(40)
            }
            : new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(40)
            };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ForgerEMS-NetworkPulse/1.0");
    }

    /// <summary>Lightweight download throughput sample (not a full speed test).</summary>
    public async Task<SpeedProbeSample> RunDownloadSampleAsync(int maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes < 4096)
        {
            maxBytes = 4096;
        }

        var uri = new Uri($"https://speed.cloudflare.com/__down?bytes={maxBytes}&forgerems_nocache={Guid.NewGuid():N}");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Pragma.ParseAdd("no-cache");
        var sw = Stopwatch.StartNew();
        long read = 0;
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return new SpeedProbeSample(false, 0, 0);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[8192];
        while (read < maxBytes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var chunk = (int)Math.Min(buffer.Length, maxBytes - read);
            var n = await stream.ReadAsync(buffer.AsMemory(0, chunk), cancellationToken).ConfigureAwait(false);
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        sw.Stop();
        if (read <= 0 || sw.Elapsed.TotalSeconds <= 0)
        {
            return new SpeedProbeSample(false, 0, 0);
        }

        var mbps = (read * 8.0) / (sw.Elapsed.TotalSeconds * 1_000_000.0);
        if (!NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(mbps))
        {
            return new SpeedProbeSample(false, 0, read);
        }

        return new SpeedProbeSample(true, mbps, read);
    }

    /// <summary>Small upload sample; disabled unless settings allow (still conservative size).</summary>
    public async Task<SpeedProbeSample> RunUploadSampleAsync(int maxBytes, CancellationToken cancellationToken)
    {
        maxBytes = Math.Clamp(maxBytes, 2048, 65_536);
        var uri = new Uri("https://speed.cloudflare.com/__up");
        var payload = new byte[maxBytes];
        Random.Shared.NextBytes(payload);
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        var sw = Stopwatch.StartNew();
        using var response = await _httpClient.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        _ = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        sw.Stop();
        if (!response.IsSuccessStatusCode || sw.Elapsed.TotalSeconds <= 0)
        {
            return new SpeedProbeSample(false, 0, maxBytes);
        }

        var mbps = (maxBytes * 8.0) / (sw.Elapsed.TotalSeconds * 1_000_000.0);
        if (!NetworkPulseSpeedSanity.IsPlausibleMeasuredMbps(mbps))
        {
            return new SpeedProbeSample(false, 0, maxBytes);
        }

        return new SpeedProbeSample(true, mbps, maxBytes);
    }

    public async Task<HttpReachabilityProbeResult> TryHttpReachableAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://www.msftconnecttest.com/connecttest.txt?forgerems_nocache={Guid.NewGuid():N}");
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();
            if (!response.IsSuccessStatusCode)
            {
                return new HttpReachabilityProbeResult(false, sw.Elapsed.TotalMilliseconds, (int)response.StatusCode, "HttpStatus");
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var captiveSuspected = !body.Contains("Microsoft Connect Test", StringComparison.OrdinalIgnoreCase);
            return new HttpReachabilityProbeResult(true, sw.Elapsed.TotalMilliseconds, (int)response.StatusCode, captiveSuspected ? "UnexpectedProbeBody" : string.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            sw.Stop();
            return new HttpReachabilityProbeResult(false, sw.Elapsed.TotalMilliseconds, (int)ex.StatusCode.Value, "HttpRequest");
        }
        catch (Exception)
        {
            sw.Stop();
            return new HttpReachabilityProbeResult(false, sw.Elapsed.TotalMilliseconds, null, "HttpUnavailable");
        }
    }

    public void Dispose() => _httpClient.Dispose();
}

public readonly record struct SpeedProbeSample(bool Succeeded, double Mbps, long ByteCount);

public readonly record struct HttpReachabilityProbeResult(
    bool Succeeded,
    double? ElapsedMs,
    int? StatusCode,
    string ErrorCategory);
