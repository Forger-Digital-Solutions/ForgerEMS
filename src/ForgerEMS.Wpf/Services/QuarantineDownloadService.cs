using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services;

public static class QuarantineDownloadService
{
    private const string PayloadFileName = "payload.forgerq";
    private const string MetadataFileName = "quarantine.json";

    public static string GetDefaultQuarantineRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgerEMS", "Quarantine");

    public static async Task<QuarantineDownloadResult> DownloadAsync(
        string? rawUrl,
        HttpClient? httpClient = null,
        QuarantineDownloadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new QuarantineDownloadOptions();
        var timestamp = DateTimeOffset.UtcNow;

        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var originalUri))
        {
            return new QuarantineDownloadResult
            {
                Outcome = QuarantineOutcome.InvalidInput,
                States = [SafetyCheckSeverity.InvalidInput],
                Verdict = "Rejected by policy: enter a valid http(s) URL.",
                OriginalUrl = rawUrl?.Trim(),
                UtcTimestamp = timestamp,
                ErrorCategory = "InvalidInput",
                ErrorMessage = "URL could not be parsed as an absolute URI."
            };
        }

        if (originalUri.Scheme != Uri.UriSchemeHttp && originalUri.Scheme != Uri.UriSchemeHttps)
        {
            return new QuarantineDownloadResult
            {
                Outcome = QuarantineOutcome.InvalidInput,
                States = [SafetyCheckSeverity.InvalidInput, SafetyCheckSeverity.RejectedByPolicy],
                Verdict = "Rejected by policy: quarantine downloads support only http(s) URLs.",
                OriginalUrl = originalUri.ToString(),
                UtcTimestamp = timestamp,
                ErrorCategory = "RejectedByPolicy",
                ErrorMessage = "Unsupported URI scheme: " + originalUri.Scheme
            };
        }

        var fixture = KnownSecurityTestFixtureRecognizer.Recognize(originalUri);
        var root = Path.GetFullPath(options.QuarantineRoot ?? GetDefaultQuarantineRoot());
        var downloadDirectory = Path.Combine(root, timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        var payloadPath = Path.Combine(downloadDirectory, PayloadFileName);
        var metadataPath = Path.Combine(downloadDirectory, MetadataFileName);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var states = new List<SafetyCheckSeverity>();
        foreach (var state in fixture.Classifications)
        {
            AddState(states, state);
        }

        Uri finalUri = originalUri;
        HttpStatusCode? statusCode = null;
        string? contentType = null;
        long? contentLength = null;
        long bytesAttempted = 0;
        long bytesWritten = 0;
        string? sha256 = null;
        var markOfTheWebWritten = false;

        try
        {
            Directory.CreateDirectory(downloadDirectory);
            EnsureInsideRoot(root, downloadDirectory);
            EnsureInsideRoot(root, payloadPath);
            EnsureInsideRoot(root, metadataPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new QuarantineDownloadResult
            {
                Outcome = QuarantineOutcome.DownloadFailed,
                States = [SafetyCheckSeverity.DownloadFailed],
                Verdict = "Download failed: ForgerEMS could not prepare the quarantine folder.",
                OriginalUrl = originalUri.ToString(),
                UtcTimestamp = timestamp,
                QuarantineRoot = root,
                DownloadDirectory = downloadDirectory,
                PayloadPath = payloadPath,
                MetadataPath = metadataPath,
                ErrorCategory = ex.GetType().Name,
                ErrorMessage = SafeExceptionSummary(ex),
                Fixture = fixture
            };
        }

        async Task<QuarantineDownloadResult> FinishAsync(
            QuarantineOutcome outcome,
            string verdict,
            string? errorCategory = null,
            string? errorMessage = null,
            bool externalSecurityLikelyIntercepted = false)
        {
            var finalFileExists = SafeFileExists(payloadPath);
            var resultStates = states.ToList();
            AddOutcomeState(resultStates, outcome);
            if (!string.IsNullOrWhiteSpace(sha256))
            {
                AddState(resultStates, SafetyCheckSeverity.HashComputed);
            }

            var result = new QuarantineDownloadResult
            {
                Outcome = outcome,
                States = resultStates,
                Verdict = verdict,
                OriginalUrl = originalUri.ToString(),
                FinalUrl = finalUri.ToString(),
                UtcTimestamp = timestamp,
                HttpStatus = statusCode is null ? null : (int)statusCode.Value,
                HeadersSummary = new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase),
                ContentType = contentType,
                ContentLength = contentLength,
                BytesAttempted = bytesAttempted,
                BytesWritten = bytesWritten,
                Sha256Hex = sha256,
                Fixture = fixture,
                QuarantineRoot = root,
                DownloadDirectory = downloadDirectory,
                PayloadPath = payloadPath,
                MetadataPath = metadataPath,
                ErrorCategory = errorCategory,
                ErrorMessage = errorMessage,
                FinalFileExists = finalFileExists,
                ExternalSecurityLikelyIntercepted = externalSecurityLikelyIntercepted,
                MarkOfTheWebWritten = markOfTheWebWritten
            };

            await WriteMetadataAsync(result, cancellationToken).ConfigureAwait(false);
            return result;
        }

        var ownsClient = httpClient is null;
        using var ownedClient = ownsClient
            ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            : null;
        var client = httpClient ?? ownedClient!;
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"ForgerEMS/{AppReleaseInfo.Version} (quarantine download; no execute)");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        try
        {
            using var responseResult = await SendWithRedirectsAsync(client, originalUri, options.MaxRedirects, timeoutCts.Token).ConfigureAwait(false);
            var response = responseResult.Response;
            finalUri = responseResult.FinalUri;
            statusCode = response.StatusCode;
            contentType = response.Content.Headers.ContentType?.ToString();
            contentLength = response.Content.Headers.ContentLength;
            CopyHeaders(response, headers);

            if (!response.IsSuccessStatusCode)
            {
                return await FinishAsync(
                    QuarantineOutcome.DownloadFailed,
                    "Download failed: HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase + ".",
                    "HttpStatus",
                    "HTTP " + (int)response.StatusCode + " " + response.ReasonPhrase).ConfigureAwait(false);
            }

            if (contentLength is > QuarantineDownloadOptions.DefaultMaxDownloadBytes &&
                options.MaxDownloadBytes == QuarantineDownloadOptions.DefaultMaxDownloadBytes)
            {
                return await FinishAsync(
                    QuarantineOutcome.RejectedByPolicy,
                    "Rejected by policy: content length exceeds the 50 MB quarantine limit.",
                    "MaxDownloadSize",
                    "Content-Length exceeded " + options.MaxDownloadBytes.ToString(CultureInfo.InvariantCulture) + " bytes.").ConfigureAwait(false);
            }

            if (contentLength is not null && contentLength.Value > options.MaxDownloadBytes)
            {
                return await FinishAsync(
                    QuarantineOutcome.RejectedByPolicy,
                    "Rejected by policy: content length exceeds the quarantine limit.",
                    "MaxDownloadSize",
                    "Content-Length exceeded " + options.MaxDownloadBytes.ToString(CultureInfo.InvariantCulture) + " bytes.").ConfigureAwait(false);
            }

            var rejectedForSize = false;
            await using (var networkStream = await response.Content.ReadAsStreamAsync(timeoutCts.Token).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                             payloadPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 81920,
                             FileOptions.SequentialScan))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[81920];
                while (true)
                {
                    var read = await networkStream.ReadAsync(buffer.AsMemory(0, buffer.Length), timeoutCts.Token).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    bytesAttempted += read;
                    if (bytesWritten + read > options.MaxDownloadBytes)
                    {
                        rejectedForSize = true;
                        break;
                    }

                    hash.AppendData(buffer.AsSpan(0, read));
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), timeoutCts.Token).ConfigureAwait(false);
                    bytesWritten += read;
                }

                if (!rejectedForSize)
                {
                    await fileStream.FlushAsync(timeoutCts.Token).ConfigureAwait(false);
                    sha256 = Convert.ToHexString(hash.GetHashAndReset());
                }
            }

            if (rejectedForSize)
            {
                TryDeleteFile(payloadPath);
                return await FinishAsync(
                    QuarantineOutcome.RejectedByPolicy,
                    "Rejected by policy: download exceeded the quarantine size limit.",
                    "MaxDownloadSize",
                    "Stream exceeded " + options.MaxDownloadBytes.ToString(CultureInfo.InvariantCulture) + " bytes.").ConfigureAwait(false);
            }

            if (options.AfterPayloadWriteAsync is not null)
            {
                await options.AfterPayloadWriteAsync(payloadPath, timeoutCts.Token).ConfigureAwait(false);
            }

            if (!SafeFileExists(payloadPath))
            {
                return await FinishAsync(
                    QuarantineOutcome.BlockedByExternalSecurity,
                    "External AV/security intercepted the file before ForgerEMS could retain it.",
                    "ExternalSecurityIntercepted",
                    "Payload vanished before retention verification completed.",
                    externalSecurityLikelyIntercepted: true).ConfigureAwait(false);
            }

            markOfTheWebWritten = TryWriteMarkOfTheWeb(payloadPath, originalUri, finalUri);
            TryMarkReadOnly(payloadPath);

            try
            {
                EnsureInsideRoot(root, payloadPath);
                var retainedLength = VerifyReadableLength(payloadPath);
                if (retainedLength != bytesWritten)
                {
                    return await FinishAsync(
                        QuarantineOutcome.BlockedByExternalSecurity,
                        "External AV/security intercepted or modified the file before ForgerEMS could retain it.",
                        "RetentionMismatch",
                        $"Retained length {retainedLength} did not match bytes written {bytesWritten}.",
                        externalSecurityLikelyIntercepted: true).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return await FinishAsync(
                    QuarantineOutcome.BlockedByExternalSecurity,
                    "External AV/security intercepted the file before ForgerEMS could retain it.",
                    ex.GetType().Name,
                    SafeExceptionSummary(ex),
                    externalSecurityLikelyIntercepted: true).ConfigureAwait(false);
            }

            return await FinishAsync(
                QuarantineOutcome.Quarantined,
                "Downloaded to ForgerEMS quarantine. ForgerEMS did not execute this file.").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return await FinishAsync(
                QuarantineOutcome.BlockedByExternalSecurity,
                "External AV/security intercepted the file before ForgerEMS could retain it.",
                ex.GetType().Name,
                SafeExceptionSummary(ex),
                externalSecurityLikelyIntercepted: true).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return await FinishAsync(
                QuarantineOutcome.DownloadFailed,
                "Download failed: network, TLS, timeout, or server policy prevented completion.",
                ex.GetType().Name,
                SafeExceptionSummary(ex)).ConfigureAwait(false);
        }
    }

    public static async Task<QuarantineDownloadResult> CreateInternalSelfTestAsync(
        string? quarantineRoot = null,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(quarantineRoot ?? GetDefaultQuarantineRoot());
        var timestamp = DateTimeOffset.UtcNow;
        var downloadDirectory = Path.Combine(root, timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
        var payloadPath = Path.Combine(downloadDirectory, PayloadFileName);
        var metadataPath = Path.Combine(downloadDirectory, MetadataFileName);
        var bytes = Encoding.UTF8.GetBytes(KnownSecurityTestFixtureRecognizer.InternalSelfTestPayload);
        var fixture = KnownSecurityTestFixtureRecognizer.RecognizeLocalFile(KnownSecurityTestFixtureRecognizer.InternalSelfTestFileName);

        Directory.CreateDirectory(downloadDirectory);
        EnsureInsideRoot(root, payloadPath);
        await File.WriteAllBytesAsync(payloadPath, bytes, cancellationToken).ConfigureAwait(false);
        TryMarkReadOnly(payloadPath);

        var result = new QuarantineDownloadResult
        {
            Outcome = QuarantineOutcome.Quarantined,
            States = [SafetyCheckSeverity.KnownSafeSecurityTestFixture, SafetyCheckSeverity.SimulatedMalwareTestFixture, SafetyCheckSeverity.Quarantined, SafetyCheckSeverity.HashComputed],
            Verdict = "Downloaded to ForgerEMS quarantine. Internal harmless self-test only; this does not test antivirus detection.",
            OriginalUrl = "forgerems-internal-quarantine-pipeline-test",
            FinalUrl = "forgerems-internal-quarantine-pipeline-test",
            UtcTimestamp = timestamp,
            BytesAttempted = bytes.Length,
            BytesWritten = bytes.Length,
            Sha256Hex = Convert.ToHexString(SHA256.HashData(bytes)),
            Fixture = fixture,
            QuarantineRoot = root,
            DownloadDirectory = downloadDirectory,
            PayloadPath = payloadPath,
            MetadataPath = metadataPath,
            FinalFileExists = File.Exists(payloadPath)
        };

        await WriteMetadataAsync(result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public static string FormatResult(QuarantineDownloadResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Verdict: " + result.Verdict);
        sb.AppendLine("Outcome: " + result.Outcome);
        sb.AppendLine("Classification: " + string.Join(", ", result.States.Select(static s => s.ToString())));
        if (result.Fixture.IsKnown)
        {
            sb.AppendLine("Known safe security test fixture: " + result.Fixture.Description);
        }

        sb.AppendLine("ForgerEMS did not execute this file.");
        sb.AppendLine("ForgerEMS did not unzip archives or shell-open the payload.");
        sb.AppendLine();
        sb.AppendLine("Evidence:");
        sb.AppendLine("- Original URL: " + (result.OriginalUrl ?? "(not provided)"));
        sb.AppendLine("- Final URL: " + (result.FinalUrl ?? "(not reached)"));
        sb.AppendLine("- HTTP status: " + (result.HttpStatus?.ToString(CultureInfo.InvariantCulture) ?? "(not available)"));
        sb.AppendLine("- Content-Type: " + (result.ContentType ?? "(not provided)"));
        sb.AppendLine("- Content-Length: " + (result.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? "(not provided)"));
        sb.AppendLine("- Bytes attempted: " + result.BytesAttempted.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("- Bytes written: " + result.BytesWritten.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("- SHA256: " + (result.Sha256Hex ?? "(not available)"));
        sb.AppendLine("- Final file exists: " + YesNo(result.FinalFileExists));
        sb.AppendLine("- External AV/security likely intercepted: " + YesNo(result.ExternalSecurityLikelyIntercepted));
        sb.AppendLine("- Mark-of-the-Web written: " + YesNo(result.MarkOfTheWebWritten));
        sb.AppendLine("- Quarantine root: " + (result.QuarantineRoot ?? "(not available)"));
        sb.AppendLine("- Quarantine folder: " + (result.DownloadDirectory ?? "(not available)"));
        sb.AppendLine("- Payload path: " + (result.PayloadPath ?? "(not available)"));
        sb.AppendLine("- Metadata path: " + (result.MetadataPath ?? "(not available)"));
        if (!string.IsNullOrWhiteSpace(result.ErrorCategory) || !string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            sb.AppendLine("- Error: " + (result.ErrorCategory ?? "Error") + " - " + (result.ErrorMessage ?? "(no detail)"));
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task WriteMetadataAsync(QuarantineDownloadResult result, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.MetadataPath))
        {
            return;
        }

        var metadata = new
        {
            originalUrl = result.OriginalUrl,
            finalUrl = result.FinalUrl,
            utcTimestamp = result.UtcTimestamp,
            httpStatus = result.HttpStatus,
            headersSummary = result.HeadersSummary,
            contentType = result.ContentType,
            contentLength = result.ContentLength,
            bytesAttempted = result.BytesAttempted,
            bytesWritten = result.BytesWritten,
            sha256 = result.Sha256Hex,
            knownFixtureClassification = result.Fixture.Classifications.Select(static c => c.ToString()).ToArray(),
            knownFixtureName = result.Fixture.Name,
            outcome = result.Outcome.ToString(),
            errorCategory = result.ErrorCategory,
            errorMessage = result.ErrorMessage,
            finalFileExists = result.FinalFileExists,
            avExternalSecurityLikelyIntercepted = result.ExternalSecurityLikelyIntercepted
        };

        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
        var folder = Path.GetDirectoryName(result.MetadataPath);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
        }

        await File.WriteAllTextAsync(result.MetadataPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    private sealed class RedirectResponseResult(HttpResponseMessage response, Uri finalUri) : IDisposable
    {
        public HttpResponseMessage Response { get; } = response;

        public Uri FinalUri { get; } = finalUri;

        public void Dispose()
        {
            Response.Dispose();
        }
    }

    private static async Task<RedirectResponseResult> SendWithRedirectsAsync(
        HttpClient client,
        Uri startUri,
        int maxRedirects,
        CancellationToken cancellationToken)
    {
        var current = startUri;
        for (var redirect = 0; redirect <= maxRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            var response = await client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!IsRedirect(response.StatusCode) || response.Headers.Location is null)
            {
                return new RedirectResponseResult(response, current);
            }

            if (redirect == maxRedirects)
            {
                return new RedirectResponseResult(response, current);
            }

            var next = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current, response.Headers.Location);
            response.Dispose();

            if (next.Scheme != Uri.UriSchemeHttp && next.Scheme != Uri.UriSchemeHttps)
            {
                throw new HttpRequestException("Redirect target uses unsupported scheme: " + next.Scheme);
            }

            current = next;
        }

        throw new HttpRequestException("Too many redirects.");
    }

    private static void CopyHeaders(HttpResponseMessage response, IDictionary<string, string> headers)
    {
        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escaped the ForgerEMS quarantine root.");
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static long VerifyReadableLength(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return stream.Length;
    }

    private static void TryMarkReadOnly(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
        catch
        {
        }
    }

    private static bool TryWriteMarkOfTheWeb(string path, Uri originalUri, Uri finalUri)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var ads = path + ":Zone.Identifier";
            var motw = "[ZoneTransfer]" + Environment.NewLine +
                       "ZoneId=3" + Environment.NewLine +
                       "HostUrl=" + finalUri + Environment.NewLine +
                       "ReferrerUrl=" + originalUri + Environment.NewLine;
            File.WriteAllText(ads, motw, Encoding.UTF8);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void AddOutcomeState(ICollection<SafetyCheckSeverity> states, QuarantineOutcome outcome)
    {
        AddState(states, outcome switch
        {
            QuarantineOutcome.Quarantined => SafetyCheckSeverity.Quarantined,
            QuarantineOutcome.BlockedByExternalSecurity => SafetyCheckSeverity.BlockedByExternalSecurity,
            QuarantineOutcome.DownloadFailed => SafetyCheckSeverity.DownloadFailed,
            QuarantineOutcome.RejectedByPolicy => SafetyCheckSeverity.RejectedByPolicy,
            QuarantineOutcome.InvalidInput => SafetyCheckSeverity.InvalidInput,
            _ => SafetyCheckSeverity.UnknownManualReview
        });
    }

    private static void AddState(ICollection<SafetyCheckSeverity> states, SafetyCheckSeverity state)
    {
        if (!states.Contains(state))
        {
            states.Add(state);
        }
    }

    private static string SafeExceptionSummary(Exception ex)
    {
        return ex.GetType().Name + ": " + ex.Message;
    }

    private static string YesNo(bool value) => value ? "yes" : "no";
}
