using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services;

public enum LinkSafetyBand
{
    LowConcern,
    Caution,
    HighRisk,
    Unknown
}

/// <summary>
/// URL safety metadata analysis. Analyze mode reads headers and, for HTML only, a bounded preview.
/// It never writes payload bytes; explicit quarantine download is handled by QuarantineDownloadService.
/// </summary>
public static class LinkSafetyAnalyzer
{
    private static readonly HashSet<string> ShortenerHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "buff.ly", "is.gd", "adf.ly", "cutt.ly", "rebrand.ly", "rb.gy"
    };

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jar", ".scr", ".com", ".pif", ".dll", ".app", ".deb", ".rpm", ".dmg", ".pkg"
    };

    public static LinkSafetyReport Analyze(string? rawInput)
    {
        var notes = new List<string>();
        var states = new List<SafetyCheckSeverity>();
        var worst = LinkSafetyBand.LowConcern;

        void bump(LinkSafetyBand level, SafetyCheckSeverity state, string note)
        {
            notes.Add(note);
            AddState(states, state);
            worst = MaxBand(worst, level);
        }

        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new LinkSafetyReport(
                LinkSafetyBand.Unknown,
                ["Paste an http(s) URL to analyze."],
                [SafetyCheckSeverity.InvalidInput]);
        }

        var trimmed = rawInput.Trim();
        if (trimmed.Length > 4000)
        {
            return new LinkSafetyReport(
                LinkSafetyBand.Unknown,
                ["URL is too long to analyze safely in the UI."],
                [SafetyCheckSeverity.InvalidInput]);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return new LinkSafetyReport(
                LinkSafetyBand.Unknown,
                ["Could not parse as an absolute URL. Include https:// or http://."],
                [SafetyCheckSeverity.InvalidInput]);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            bump(LinkSafetyBand.HighRisk, SafetyCheckSeverity.InvalidInput, $"Scheme \"{uri.Scheme}\" is not http/https. ForgerEMS rejected it.");
            return new LinkSafetyReport(worst, notes, states);
        }

        var fixture = KnownSecurityTestFixtureRecognizer.Recognize(uri);
        if (fixture.IsKnown)
        {
            foreach (var state in fixture.Classifications)
            {
                AddState(states, state);
            }

            notes.Add("Known safe security test fixture: " + fixture.Description);
            worst = fixture.PrimarySeverity is SafetyCheckSeverity.SimulatedMalwareTestFixture or SafetyCheckSeverity.SimulatedPhishingTestFixture
                ? LinkSafetyBand.HighRisk
                : LinkSafetyBand.Caution;
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            bump(LinkSafetyBand.Caution, SafetyCheckSeverity.UnknownManualReview, "HTTP (not HTTPS): traffic can be modified in transit. Prefer HTTPS downloads from the vendor.");
        }

        var host = uri.IdnHost;
        if (string.IsNullOrEmpty(host))
        {
            return new LinkSafetyReport(
                LinkSafetyBand.Unknown,
                ["Host name is missing."],
                [SafetyCheckSeverity.InvalidInput]);
        }

        if (host.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) || host.Contains(".xn--", StringComparison.OrdinalIgnoreCase))
        {
            bump(LinkSafetyBand.Caution, SafetyCheckSeverity.UnknownManualReview, "Punycode / IDN host: visually similar domains are sometimes used in phishing. Verify the spelling carefully.");
        }

        if (Uri.CheckHostName(host) == UriHostNameType.IPv4 || Uri.CheckHostName(host) == UriHostNameType.IPv6)
        {
            bump(LinkSafetyBand.Caution, SafetyCheckSeverity.UnknownManualReview, "Numeric IP address instead of a normal hostname - sometimes used to bypass simple blocklists.");
        }

        var hostKey = TrimWww(host);
        if (ShortenerHosts.Contains(hostKey))
        {
            bump(LinkSafetyBand.Caution, SafetyCheckSeverity.UnknownManualReview, "Known URL shortener: final destination is hidden until you follow the redirect. Prefer the vendor's direct download page.");
        }

        var ext = Path.GetExtension(uri.AbsolutePath);
        if (!string.IsNullOrEmpty(ext) && ExecutableExtensions.Contains(ext) && !fixture.IsKnown)
        {
            bump(LinkSafetyBand.HighRisk, SafetyCheckSeverity.Suspicious, $"Path ends with executable-related extension \"{ext}\". Do not run unknown installers on your main machine.");
        }

        if (uri.Query.Length > 180)
        {
            bump(LinkSafetyBand.Caution, SafetyCheckSeverity.UnknownManualReview, "Very long query string: can hide tokens or tracking parameters. Inspect before sharing.");
        }

        if (host.EndsWith(".ru", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".tk", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".ml", StringComparison.OrdinalIgnoreCase))
        {
            bump(LinkSafetyBand.Caution, SafetyCheckSeverity.UnknownManualReview, "TLD is sometimes used by throwaway or unofficial mirrors - confirm you intended this domain.");
        }

        if (states.Count == 0)
        {
            AddState(states, SafetyCheckSeverity.CleanOrLowConcern);
        }

        notes.Add("Manual review still required. ForgerEMS did not download, execute, unzip, or open this URL.");

        return new LinkSafetyReport(worst, notes, states, fixture);
    }

    public static async Task<UrlSafetyAnalysisResult> AnalyzeAsync(
        string? rawInput,
        HttpClient? httpClient = null,
        UrlSafetyAnalysisOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new UrlSafetyAnalysisOptions();
        var maxPreviewBytes = Math.Clamp(
            options.MaxHtmlPreviewBytes,
            UrlSafetyAnalysisOptions.MinHtmlPreviewBytes,
            UrlSafetyAnalysisOptions.MaximumHtmlPreviewBytes);

        var local = Analyze(rawInput);
        var evidence = new List<string>(local.Notes);
        var states = new List<SafetyCheckSeverity>(local.States);

        if (string.IsNullOrWhiteSpace(rawInput) ||
            !Uri.TryCreate(rawInput.Trim(), UriKind.Absolute, out var originalUri) ||
            (originalUri.Scheme != Uri.UriSchemeHttp && originalUri.Scheme != Uri.UriSchemeHttps))
        {
            return BuildAnalysisResult(
                local.Band,
                states,
                local.Fixture,
                rawInput?.Trim(),
                finalUri: null,
                isHttps: false,
                statusCode: null,
                contentType: null,
                contentLength: null,
                contentDispositionFileName: null,
                suspiciousExtension: null,
                headAttempted: false,
                getFallbackAttempted: false,
                htmlPreviewRead: false,
                htmlPreviewBytesRead: 0,
                evidence,
                "Invalid input. Manual review still required.");
        }

        var ownsClient = httpClient is null;
        using var ownedClient = ownsClient
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            : null;
        var client = httpClient ?? ownedClient!;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ForgerEMS/{AppReleaseInfo.Version} (link safety metadata; no download)");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        HttpResponseMessage? response = null;
        Uri finalUri = originalUri;
        var headAttempted = false;
        var getFallbackAttempted = false;
        var htmlPreviewRead = false;
        var htmlPreviewBytesRead = 0;
        var headFallbackReason = string.Empty;

        try
        {
            headAttempted = true;
            evidence.Add("HTTPS HEAD/HTTP HEAD attempted first; no payload was saved.");
            var head = await SendWithRedirectsAsync(client, HttpMethod.Head, originalUri, options.MaxRedirects, evidence, timeoutCts.Token).ConfigureAwait(false);
            response = head.Response;
            finalUri = head.FinalUri;

            if (ShouldFallbackFromHead(response))
            {
                headFallbackReason = $"HEAD returned {(int)response.StatusCode} {response.ReasonPhrase}".Trim();
                if (response.Content.Headers.ContentType is null &&
                    response.Content.Headers.ContentLength is null &&
                    response.Content.Headers.ContentDisposition is null &&
                    response.IsSuccessStatusCode)
                {
                    headFallbackReason = "HEAD did not provide enough content metadata";
                }

                response.Dispose();
                response = null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
        {
            headFallbackReason = "HEAD failed: " + SafeExceptionSummary(ex);
            evidence.Add(headFallbackReason);
        }

        if (response is null)
        {
            getFallbackAttempted = true;
            evidence.Add("GET fallback used with ResponseHeadersRead. Analyze mode still does not save full files.");
            if (!string.IsNullOrWhiteSpace(headFallbackReason))
            {
                evidence.Add("GET fallback reason: " + headFallbackReason);
            }

            try
            {
                var get = await SendWithRedirectsAsync(client, HttpMethod.Get, originalUri, options.MaxRedirects, evidence, timeoutCts.Token).ConfigureAwait(false);
                response = get.Response;
                finalUri = get.FinalUri;

                if (IsHtml(response.Content.Headers.ContentType))
                {
                    var read = await ReadHtmlPreviewAsync(response, maxPreviewBytes, timeoutCts.Token).ConfigureAwait(false);
                    htmlPreviewRead = read > 0;
                    htmlPreviewBytesRead = read;
                    evidence.Add($"Read bounded HTML preview: {read.ToString("N0", CultureInfo.InvariantCulture)} bytes max {maxPreviewBytes.ToString("N0", CultureInfo.InvariantCulture)}.");
                }
                else
                {
                    evidence.Add("GET fallback stopped after response headers because content is not text/html.");
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or OperationCanceledException)
            {
                evidence.Add("GET fallback failed: " + SafeExceptionSummary(ex));
                return BuildAnalysisResult(
                    LinkSafetyBand.Unknown,
                    AppendState(states, SafetyCheckSeverity.UnknownManualReview),
                    KnownSecurityTestFixtureRecognizer.Recognize(originalUri),
                    originalUri.ToString(),
                    finalUri,
                    originalUri.Scheme == Uri.UriSchemeHttps,
                    null,
                    null,
                    null,
                    null,
                    null,
                    headAttempted,
                    getFallbackAttempted,
                    htmlPreviewRead,
                    htmlPreviewBytesRead,
                    evidence,
                    "URL metadata could not be completed. Manual review still required.");
            }
        }

        using (response)
        {
            var fixture = ChooseFixture(local.Fixture, KnownSecurityTestFixtureRecognizer.Recognize(finalUri));
            foreach (var state in fixture.Classifications)
            {
                AddState(states, state);
            }

            var contentDispositionFileName = GetContentDispositionFileName(response.Content.Headers.ContentDisposition);
            var suspiciousExtension = FindSuspiciousExtension(finalUri, contentDispositionFileName);
            if (!string.IsNullOrWhiteSpace(suspiciousExtension) && !fixture.IsKnown)
            {
                AddState(states, SafetyCheckSeverity.Suspicious);
            }

            if (!response.IsSuccessStatusCode)
            {
                AddState(states, SafetyCheckSeverity.UnknownManualReview);
            }

            evidence.Add("Final URL: " + finalUri);
            evidence.Add("HTTP status: " + (int)response.StatusCode + " " + response.ReasonPhrase);
            evidence.Add("Content-Type: " + (response.Content.Headers.ContentType?.ToString() ?? "(not provided)"));
            evidence.Add("Content-Length: " + (response.Content.Headers.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "(not provided)"));
            if (!string.IsNullOrWhiteSpace(contentDispositionFileName))
            {
                evidence.Add("Content-Disposition filename: " + contentDispositionFileName);
            }

            if (!string.IsNullOrWhiteSpace(suspiciousExtension))
            {
                evidence.Add("Suspicious extension evidence: " + suspiciousExtension);
            }

            evidence.Add("Analyze mode saved payload: no.");
            evidence.Add("ForgerEMS did not execute this URL or any downloaded file.");

            var band = ResolveBand(local.Band, fixture, suspiciousExtension, response.StatusCode);
            return BuildAnalysisResult(
                band,
                states.Count == 0 ? new[] { SafetyCheckSeverity.CleanOrLowConcern } : states.ToArray(),
                fixture,
                originalUri.ToString(),
                finalUri,
                finalUri.Scheme == Uri.UriSchemeHttps,
                response.StatusCode,
                response.Content.Headers.ContentType?.ToString(),
                response.Content.Headers.ContentLength,
                contentDispositionFileName,
                suspiciousExtension,
                headAttempted,
                getFallbackAttempted,
                htmlPreviewRead,
                htmlPreviewBytesRead,
                evidence,
                BuildVerdict(fixture, suspiciousExtension, response.StatusCode, band));
        }
    }

    public static string FormatReport(LinkSafetyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Verdict: " + BuildLocalVerdict(report));
        sb.AppendLine("Assessment: " + BandLabel(report.Band));
        sb.AppendLine();
        foreach (var line in report.Notes)
        {
            sb.AppendLine("- " + line);
        }

        return sb.ToString().TrimEnd();
    }

    public static string FormatReport(UrlSafetyAnalysisResult report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Verdict: " + report.Verdict);
        sb.AppendLine("Classification: " + string.Join(", ", report.States.Select(static s => s.ToString())));
        if (report.Fixture.IsKnown)
        {
            sb.AppendLine("Known safe security test fixture: " + report.Fixture.Description);
        }

        sb.AppendLine("Manual review still required.");
        sb.AppendLine("ForgerEMS did not execute this file or URL.");
        sb.AppendLine();
        sb.AppendLine("Evidence:");
        sb.AppendLine("- Original URL: " + (report.OriginalUrl ?? "(not parsed)"));
        sb.AppendLine("- Final URL: " + (report.FinalUrl ?? "(not reached)"));
        sb.AppendLine("- HTTPS/TLS: " + (report.IsHttps ? "yes" : "no"));
        sb.AppendLine("- HTTP status: " + (report.StatusCode is null ? "(not available)" : ((int)report.StatusCode.Value).ToString(CultureInfo.InvariantCulture)));
        sb.AppendLine("- Content-Type: " + (report.ContentType ?? "(not provided)"));
        sb.AppendLine("- Content-Length: " + (report.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "(not provided)"));
        sb.AppendLine("- Content-Disposition filename: " + (report.ContentDispositionFileName ?? "(not provided)"));
        sb.AppendLine("- Suspicious extension: " + (report.SuspiciousExtension ?? "(none detected)"));
        sb.AppendLine("- HEAD attempted: " + YesNo(report.HeadAttempted));
        sb.AppendLine("- GET fallback attempted: " + YesNo(report.GetFallbackAttempted));
        sb.AppendLine("- Bounded HTML preview read: " + (report.HtmlPreviewRead ? report.HtmlPreviewBytesRead.ToString(CultureInfo.InvariantCulture) + " bytes" : "no"));
        sb.AppendLine("- Analyze saved payload: no");
        foreach (var line in report.Evidence)
        {
            sb.AppendLine("- " + line);
        }

        return sb.ToString().TrimEnd();
    }

    public static string BandLabel(LinkSafetyBand band) => band switch
    {
        LinkSafetyBand.LowConcern => "Low concern (manual review still required)",
        LinkSafetyBand.Caution => "Caution / security test fixture",
        LinkSafetyBand.HighRisk => "High risk or simulated threat test",
        LinkSafetyBand.Unknown => "Unknown / could not classify",
        _ => "Unknown"
    };

    internal static bool IsSuspiciousExecutableExtension(string? extension) =>
        !string.IsNullOrWhiteSpace(extension) && ExecutableExtensions.Contains(extension);

    private static async Task<(HttpResponseMessage Response, Uri FinalUri)> SendWithRedirectsAsync(
        HttpClient client,
        HttpMethod method,
        Uri startUri,
        int maxRedirects,
        List<string> evidence,
        CancellationToken cancellationToken)
    {
        var current = startUri;
        for (var redirect = 0; redirect <= maxRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(method, current);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

            var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
            {
                return (response, current);
            }

            if (redirect == maxRedirects)
            {
                return (response, current);
            }

            var next = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current, response.Headers.Location);
            response.Dispose();

            if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException("Redirect target uses unsupported scheme: " + next.Scheme);
            }

            evidence.Add($"{method.Method} redirect: {current} -> {next}");
            current = next;
        }

        throw new HttpRequestException("Too many redirects.");
    }

    private static bool ShouldFallbackFromHead(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            return true;
        }

        return response.Content.Headers.ContentType is null &&
               response.Content.Headers.ContentLength is null &&
               response.Content.Headers.ContentDisposition is null;
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
    }

    private static async Task<int> ReadHtmlPreviewAsync(
        HttpResponseMessage response,
        int maxPreviewBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[Math.Min(8192, maxPreviewBytes)];
        var total = 0;
        while (total < maxPreviewBytes)
        {
            var read = await stream
                .ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxPreviewBytes - total)), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool IsHtml(MediaTypeHeaderValue? contentType)
    {
        return contentType?.MediaType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? GetContentDispositionFileName(ContentDispositionHeaderValue? contentDisposition)
    {
        var value = contentDisposition?.FileNameStar;
        if (string.IsNullOrWhiteSpace(value))
        {
            value = contentDisposition?.FileName;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().Trim('"');
    }

    private static string? FindSuspiciousExtension(Uri finalUri, string? contentDispositionFileName)
    {
        var pathExtension = Path.GetExtension(finalUri.AbsolutePath);
        if (IsSuspiciousExecutableExtension(pathExtension))
        {
            return pathExtension;
        }

        var dispositionExtension = string.IsNullOrWhiteSpace(contentDispositionFileName)
            ? null
            : Path.GetExtension(contentDispositionFileName);
        return IsSuspiciousExecutableExtension(dispositionExtension) ? dispositionExtension : null;
    }

    private static UrlSafetyAnalysisResult BuildAnalysisResult(
        LinkSafetyBand band,
        IReadOnlyList<SafetyCheckSeverity> states,
        KnownSecurityTestFixture fixture,
        string? originalUrl,
        Uri? finalUri,
        bool isHttps,
        HttpStatusCode? statusCode,
        string? contentType,
        long? contentLength,
        string? contentDispositionFileName,
        string? suspiciousExtension,
        bool headAttempted,
        bool getFallbackAttempted,
        bool htmlPreviewRead,
        int htmlPreviewBytesRead,
        IReadOnlyList<string> evidence,
        string verdict)
    {
        var normalizedStates = states.Count == 0
            ? new[] { SafetyCheckSeverity.UnknownManualReview }
            : states.Distinct().ToArray();

        return new UrlSafetyAnalysisResult
        {
            Severity = PrimarySeverity(normalizedStates, fixture, band),
            Band = band,
            States = normalizedStates,
            Verdict = verdict,
            OriginalUrl = originalUrl,
            FinalUrl = finalUri?.ToString(),
            IsHttps = isHttps,
            StatusCode = statusCode,
            ContentType = contentType,
            ContentLength = contentLength,
            ContentDispositionFileName = contentDispositionFileName,
            SuspiciousExtension = suspiciousExtension,
            HeadAttempted = headAttempted,
            GetFallbackAttempted = getFallbackAttempted,
            HtmlPreviewRead = htmlPreviewRead,
            HtmlPreviewBytesRead = htmlPreviewBytesRead,
            AnalyzeSavedPayload = false,
            Fixture = fixture,
            Evidence = evidence.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    private static SafetyCheckSeverity PrimarySeverity(
        IReadOnlyList<SafetyCheckSeverity> states,
        KnownSecurityTestFixture fixture,
        LinkSafetyBand band)
    {
        if (fixture.IsKnown)
        {
            return fixture.PrimarySeverity;
        }

        if (states.Contains(SafetyCheckSeverity.Suspicious))
        {
            return SafetyCheckSeverity.Suspicious;
        }

        return band switch
        {
            LinkSafetyBand.LowConcern => SafetyCheckSeverity.CleanOrLowConcern,
            LinkSafetyBand.Caution => SafetyCheckSeverity.UnknownManualReview,
            LinkSafetyBand.HighRisk => SafetyCheckSeverity.Suspicious,
            _ => SafetyCheckSeverity.UnknownManualReview
        };
    }

    private static string BuildVerdict(
        KnownSecurityTestFixture fixture,
        string? suspiciousExtension,
        HttpStatusCode statusCode,
        LinkSafetyBand band)
    {
        if (fixture.IsKnown)
        {
            if (fixture.PrimarySeverity == SafetyCheckSeverity.SimulatedPhishingTestFixture)
            {
                return "Simulated phishing test fixture - known safe security test, treat as dangerous for validation.";
            }

            if (fixture.PrimarySeverity == SafetyCheckSeverity.SimulatedMalwareTestFixture)
            {
                return "Simulated malware test fixture - known safe security test, AV may block it.";
            }

            return "Known safe security test fixture - use only for validation.";
        }

        if (!string.IsNullOrWhiteSpace(suspiciousExtension))
        {
            return "Suspicious direct-download metadata. Manual review still required.";
        }

        if ((int)statusCode >= 400)
        {
            return "URL responded with an error status. Manual review still required.";
        }

        return band == LinkSafetyBand.LowConcern
            ? "Low concern from URL metadata. Manual review still required."
            : "Caution from URL metadata. Manual review still required.";
    }

    private static string BuildLocalVerdict(LinkSafetyReport report)
    {
        if (report.Fixture.IsKnown)
        {
            return report.Fixture.PrimarySeverity switch
            {
                SafetyCheckSeverity.SimulatedPhishingTestFixture => "Simulated phishing test fixture - known safe security test.",
                SafetyCheckSeverity.SimulatedMalwareTestFixture => "Simulated malware test fixture - known safe security test.",
                _ => "Known safe security test fixture."
            };
        }

        return report.Band switch
        {
            LinkSafetyBand.LowConcern => "Low concern from local URL heuristics. Manual review still required.",
            LinkSafetyBand.Caution => "Caution from local URL heuristics. Manual review still required.",
            LinkSafetyBand.HighRisk => "Suspicious URL indicators. Manual review still required.",
            _ => "Unknown. Manual review still required."
        };
    }

    private static LinkSafetyBand ResolveBand(
        LinkSafetyBand localBand,
        KnownSecurityTestFixture fixture,
        string? suspiciousExtension,
        HttpStatusCode statusCode)
    {
        if (fixture.IsKnown)
        {
            return fixture.PrimarySeverity is SafetyCheckSeverity.SimulatedMalwareTestFixture or SafetyCheckSeverity.SimulatedPhishingTestFixture
                ? LinkSafetyBand.HighRisk
                : LinkSafetyBand.Caution;
        }

        if (!string.IsNullOrWhiteSpace(suspiciousExtension))
        {
            return LinkSafetyBand.HighRisk;
        }

        if ((int)statusCode >= 400)
        {
            return LinkSafetyBand.Unknown;
        }

        return localBand;
    }

    private static KnownSecurityTestFixture ChooseFixture(KnownSecurityTestFixture first, KnownSecurityTestFixture second)
    {
        return second.IsKnown ? second : first;
    }

    private static string SafeExceptionSummary(Exception ex)
    {
        return ex.GetType().Name + ": " + ex.Message;
    }

    private static LinkSafetyBand MaxBand(LinkSafetyBand a, LinkSafetyBand b)
    {
        if (a == LinkSafetyBand.HighRisk || b == LinkSafetyBand.HighRisk)
        {
            return LinkSafetyBand.HighRisk;
        }

        if (a == LinkSafetyBand.Caution || b == LinkSafetyBand.Caution)
        {
            return LinkSafetyBand.Caution;
        }

        if (a == LinkSafetyBand.Unknown || b == LinkSafetyBand.Unknown)
        {
            return LinkSafetyBand.Unknown;
        }

        return LinkSafetyBand.LowConcern;
    }

    private static List<SafetyCheckSeverity> AppendState(List<SafetyCheckSeverity> states, SafetyCheckSeverity state)
    {
        AddState(states, state);
        return states;
    }

    private static void AddState(List<SafetyCheckSeverity> states, SafetyCheckSeverity state)
    {
        if (!states.Contains(state))
        {
            states.Add(state);
        }
    }

    private static string TrimWww(string host)
    {
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}

public sealed class LinkSafetyReport
{
    public LinkSafetyReport(
        LinkSafetyBand band,
        IReadOnlyList<string> notes,
        IReadOnlyList<SafetyCheckSeverity>? states = null,
        KnownSecurityTestFixture? fixture = null)
    {
        Band = band;
        Notes = notes;
        States = states ?? [];
        Fixture = fixture ?? KnownSecurityTestFixture.None;
    }

    public LinkSafetyBand Band { get; }

    public IReadOnlyList<string> Notes { get; }

    public IReadOnlyList<SafetyCheckSeverity> States { get; }

    public KnownSecurityTestFixture Fixture { get; }
}
