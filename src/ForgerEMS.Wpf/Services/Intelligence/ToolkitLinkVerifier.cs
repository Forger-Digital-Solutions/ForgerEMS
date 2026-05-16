using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>
/// Beta-safe HTTP metadata verifier for toolkit official URLs (HEAD / minimal ranged GET only).
/// Does not execute payloads or fetch whole archives.
/// </summary>
public sealed class ToolkitLinkVerifier : IDisposable
{
    private const string UserAgent = "ForgerEMS-ToolkitLinkVerifier/1.2 (+metadata-only)";
    private const int MaxFallbackBodyBytes = 1024;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public ToolkitLinkVerifier(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
            return;
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            MaxAutomaticRedirections = 6,
            ConnectTimeout = TimeSpan.FromSeconds(10)
        };
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _ownsHttpClient = true;
    }

    public async Task<ToolkitLinkVerificationSnapshot> VerifyAsync(
        string usbTargetRoot,
        IReadOnlyList<ToolkitLinkVerificationInput> inputs,
        ToolkitLinkVerificationRunOptions options,
        CancellationToken cancellationToken)
    {
        var snapshot = new ToolkitLinkVerificationSnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            TargetRoot = usbTargetRoot.Trim(),
            CompletedSuccessfully = false,
            Entries = []
        };

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(options.OverallDeadline);

        foreach (var input in inputs)
        {
            overall.Token.ThrowIfCancellationRequested();
            var entry = await VerifySingleAsync(input, options.PerRequestTimeout, overall.Token).ConfigureAwait(false);
            snapshot.Entries.Add(entry);
        }

        snapshot.CompletedSuccessfully = true;
        snapshot.SummaryNote =
            "Metadata-only checks (HEAD or ranged GET). Does not download full payloads or execute files.";
        return snapshot;
    }

    private async Task<ToolkitLinkVerificationEntryDto> VerifySingleAsync(
        ToolkitLinkVerificationInput input,
        TimeSpan perRequestTimeout,
        CancellationToken cancellationToken)
    {
        var dto = new ToolkitLinkVerificationEntryDto
        {
            ToolName = string.IsNullOrWhiteSpace(input.ToolName) ? "Unknown tool" : input.ToolName.Trim(),
            CheckedUtc = DateTimeOffset.UtcNow,
            ChecksumMetadataNote = BuildChecksumHint(input.ChecksumStatusHint)
        };

        if (!Uri.TryCreate(input.OfficialUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            dto.Status = ToolkitLinkVerificationStatus.NotChecked;
            dto.DetailReason = "No HTTP(S) official URL on record.";
            dto.UrlFingerprint = string.Empty;
            return dto;
        }

        dto.UrlFingerprint = ToolkitLinkVerificationRedactor.RedactUriFingerprint(uri);
        dto.OriginalHost = uri.Host;

        using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestTimeout.CancelAfter(perRequestTimeout);

        try
        {
            var outcome = await TryHeadThenRangeAsync(uri, requestTimeout.Token).ConfigureAwait(false);
            return ApplyOutcome(dto, uri, outcome, input.LocalFileSizeBytes);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            dto.Status = ToolkitLinkVerificationStatus.UnknownOffline;
            dto.DetailReason = "Timed out waiting for HTTP metadata.";
            return dto;
        }
        catch (HttpRequestException ex)
        {
            dto.Status = ToolkitLinkVerificationStatus.UnknownOffline;
            dto.DetailReason = $"Network error: {SummarizeException(ex)}";
            return dto;
        }
        catch (Exception ex)
        {
            dto.Status = ToolkitLinkVerificationStatus.UnknownOffline;
            dto.DetailReason = $"Unexpected: {SummarizeException(ex)}";
            return dto;
        }
    }

    private async Task<HttpProbeOutcome> TryHeadThenRangeAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
        using var headResponse = await _httpClient.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        var headStatus = (int)headResponse.StatusCode;

        if (ShouldRetryWithSmallGet(headStatus))
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, uri);
            getRequest.Headers.Range = new RangeHeaderValue(0, 0);
            using var getResponse = await _httpClient.SendAsync(getRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var minimalRead = await ReadResponsePrefixOnlyAsync(getResponse, cancellationToken).ConfigureAwait(false);
            return HttpProbeOutcome.From(getResponse, usedRangeFallback: true, minimalRead);
        }

        return HttpProbeOutcome.From(headResponse, usedRangeFallback: false, minimalReadSucceeded: false);
    }

    private static bool ShouldRetryWithSmallGet(int statusCode) =>
        statusCode is 403 or 405 or 501;

    private static ToolkitLinkVerificationEntryDto ApplyOutcome(
        ToolkitLinkVerificationEntryDto dto,
        Uri originalUri,
        HttpProbeOutcome outcome,
        long? localFileSizeBytes)
    {
        dto.HttpStatus = outcome.StatusCode;
        dto.FinalHost = outcome.FinalUri?.Host ?? string.Empty;
        dto.OfficialDomainAligned = HostsLikelyAligned(dto.OriginalHost, dto.FinalHost);

        if (outcome.ContentLength.HasValue)
        {
            dto.ContentLengthNote = $"HTTP Content-Length: {outcome.ContentLength.Value}";
            if (localFileSizeBytes > 0 && outcome.ContentLength == localFileSizeBytes)
            {
                dto.ContentLengthNote += "; matches reported local size (metadata-only)";
            }
        }
        else
        {
            dto.ContentLengthNote = outcome.AcceptRangesNote.Length > 0
                ? outcome.AcceptRangesNote
                : "No Content-Length in response metadata.";
        }

        dto.Status = MapHttpStatus(outcome.StatusCode, outcome.HeadersHadStrongSignals || outcome.BodyValidatedMinimalRead);

        if (!dto.OfficialDomainAligned &&
            dto.Status is ToolkitLinkVerificationStatus.VerifiedMetadata or ToolkitLinkVerificationStatus.Reachable)
        {
            dto.Status = ToolkitLinkVerificationStatus.Warning;
            dto.DetailReason = "Final redirect host differs from official URL host.";
        }

        if (string.IsNullOrWhiteSpace(dto.DetailReason))
        {
            dto.DetailReason = outcome.UsedRangeFallback && !outcome.RangeHonored
                ? "HEAD unsupported/blocked; server ignored Range, so only a small response prefix was read."
                : outcome.UsedRangeFallback
                ? "HEAD unsupported/blocked; used ranged GET for headers."
                : "HEAD metadata accepted.";
        }

        return dto;
    }

    private static ToolkitLinkVerificationStatus MapHttpStatus(int code, bool strongSignals)
    {
        if (code is >= 200 and < 300)
        {
            return strongSignals ? ToolkitLinkVerificationStatus.VerifiedMetadata : ToolkitLinkVerificationStatus.Reachable;
        }

        if (code is 401 or 403 or 408 or 429)
        {
            return ToolkitLinkVerificationStatus.Warning;
        }

        if (code is 404 or 410)
        {
            return ToolkitLinkVerificationStatus.Broken;
        }

        if (code is >= 500 and < 600)
        {
            return ToolkitLinkVerificationStatus.Broken;
        }

        if (code is >= 300 and < 400)
        {
            return ToolkitLinkVerificationStatus.Warning;
        }

        return ToolkitLinkVerificationStatus.Warning;
    }

    private static bool HostsLikelyAligned(string originalHost, string finalHost)
    {
        if (string.IsNullOrWhiteSpace(originalHost) || string.IsNullOrWhiteSpace(finalHost))
        {
            return true;
        }

        if (string.Equals(originalHost, finalHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (finalHost.EndsWith("." + originalHost, StringComparison.OrdinalIgnoreCase) ||
            originalHost.EndsWith("." + finalHost, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(DomainPrefix(originalHost), DomainPrefix(finalHost), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Very lightweight heuristic when CDN swaps sibling domains.</summary>
    private static string DomainPrefix(string host)
    {
        var parts = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? string.Join('.', parts.TakeLast(2)) : host;
    }

    private static async Task<bool> ReadResponsePrefixOnlyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content is null)
        {
            return false;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[Math.Min(MaxFallbackBodyBytes, 1024)];
        var remaining = MaxFallbackBodyBytes;
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return true;
            }

            remaining -= read;
        }

        return true;
    }

    private static string SummarizeException(Exception ex)
    {
        var msg = SensitiveDataRedactor.SanitizeForSupportShare(ex.Message);
        return msg.Length > 160 ? msg[..160] + "…" : msg;
    }

    private static string BuildChecksumHint(string hint)
    {
        var trimmed = (hint ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed) ||
            trimmed.Contains("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Toolkit checksum state unclear from metadata alone.";
        }

        if (trimmed.Contains("Verified", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("checksum match", StringComparison.OrdinalIgnoreCase))
        {
            return "Toolkit shows checksum verified for downloaded artifact (HTTP headers alone cannot prove payload integrity).";
        }

        if (trimmed.Contains("mismatch", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("failed", StringComparison.OrdinalIgnoreCase))
        {
            return "Toolkit reports checksum mismatch; URL metadata checks cannot replace hash verification.";
        }

        return "Checksum/status signal requires toolkit verification.";
    }

    private sealed record HttpProbeOutcome(
        int StatusCode,
        Uri? FinalUri,
        long? ContentLength,
        bool HeadersHadStrongSignals,
        bool BodyValidatedMinimalRead,
        bool UsedRangeFallback,
        bool RangeHonored,
        string AcceptRangesNote)
    {
        public static HttpProbeOutcome From(HttpResponseMessage response, bool usedRangeFallback, bool minimalReadSucceeded)
        {
            var finalUri = response.RequestMessage?.RequestUri;
            var length = response.Content.Headers.ContentLength;
            var rangeHonored = response.StatusCode == HttpStatusCode.PartialContent ||
                response.Content.Headers.ContentRange is not null;
            var strong =
                length.HasValue ||
                response.Content.Headers.LastModified.HasValue ||
                response.Content.Headers.ContentRange is not null ||
                response.Headers.ETag is not null ||
                rangeHonored;
            var acceptRanges = response.Headers.AcceptRanges.Count > 0
                ? "Accept-Ranges: " + string.Join(',', response.Headers.AcceptRanges)
                : string.Empty;

            return new HttpProbeOutcome(
                (int)response.StatusCode,
                finalUri,
                length,
                strong,
                BodyValidatedMinimalRead: usedRangeFallback && rangeHonored && minimalReadSucceeded,
                usedRangeFallback,
                rangeHonored,
                acceptRanges);
        }
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}

internal static class ToolkitLinkVerificationRedactor
{
    public static string RedactUriFingerprint(Uri uri)
    {
        var path = RedactPath(uri.AbsolutePath);
        var fingerprint = $"{uri.Scheme}://{uri.Host}{path}";
        var q = uri.Query;
        if (string.IsNullOrEmpty(q))
        {
            return fingerprint;
        }

        return fingerprint + "?[redacted-query]";
    }

    private static string RedactPath(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || absolutePath == "/")
        {
            return "/";
        }

        var filename = Path.GetFileName(Uri.UnescapeDataString(absolutePath));
        if (string.IsNullOrWhiteSpace(filename) || ContainsSensitivePathCue(absolutePath) || ContainsSensitivePathCue(filename))
        {
            return "/[redacted-path]";
        }

        var trimmed = filename.Length > 96 ? filename[..93] + "…" : filename;
        return "/" + Uri.EscapeDataString(trimmed);
    }

    private static bool ContainsSensitivePathCue(string value) =>
        value.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("sig=", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("apikey", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/user/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/users/", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("/private/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Safe single-line log (host + short path; strips secrets).</summary>
    public static string RedactUrlForLog(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "[invalid-url]";
        }

        return RedactUriFingerprint(uri);
    }
}
