using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// Verified artifact download pipeline: source trust validation, expected SHA-256,
/// content-length enforcement, GUID-owned partial file, atomic final rename, partial
/// cleanup, optional Authenticode verification via WinVerifyTrust. Never executes artifacts.
/// </summary>
public sealed class ArtifactDownloadService
{
    private static readonly TimeSpan[] RetryDelays =
    {
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(1000)
    };

    /// <summary>Upper bound for a whole download operation (all attempts).</summary>
    public static readonly TimeSpan DefaultDownloadTimeout = TimeSpan.FromMinutes(30);

    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private readonly OfficialMetadataClient _metadataClient;
    private readonly IAuthenticodeVerifier _authenticodeVerifier;
    private readonly IDiskSpaceProbe _diskSpaceProbe;
    private readonly int _maxAttempts;
    private readonly TimeSpan _downloadTimeout;

    public ArtifactDownloadService(
        OfficialMetadataClient? metadataClient = null,
        IAuthenticodeVerifier? authenticodeVerifier = null,
        IDiskSpaceProbe? diskSpaceProbe = null,
        int maxAttempts = 2,
        TimeSpan? downloadTimeout = null)
    {
        _metadataClient = metadataClient ?? new OfficialMetadataClient();
        _authenticodeVerifier = authenticodeVerifier ?? new WinVerifyTrustAuthenticodeVerifier();
        _diskSpaceProbe = diskSpaceProbe ?? new SystemDiskSpaceProbe();
        _maxAttempts = Math.Max(1, maxAttempts);
        _downloadTimeout = downloadTimeout ?? DefaultDownloadTimeout;
    }

    public sealed record ArtifactDownloadRequest
    {
        public required string RequestedId { get; init; }
        public string Version { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public required Uri ArtifactUri { get; init; }
        public required string ExpectedSha256 { get; init; }
        public long? ExpectedSizeBytes { get; init; }
        public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();
        public bool RequireAuthenticode { get; init; }
        public string? ExpectedPublisher { get; init; }
    }

    public enum DownloadState
    {
        Verified,
        HashMismatch,
        SignatureInvalid,
        UntrustedSource,
        DownloadFailed,
        Cancelled,
        Timeout,
        InsufficientDiskSpace,
        SizeMismatch,
        MissingExpectedHash,
        UnsafeFileName,
        DestinationError
    }

    public sealed record ArtifactDownloadResult
    {
        public required DownloadState State { get; init; }
        public string? FinalPath { get; init; }
        public string? ActualSha256 { get; init; }
        public long BytesWritten { get; init; }
        public string? Reason { get; init; }

        // Provenance (sanitized — never contains query strings or credentials).
        public string? RequestedId { get; init; }
        public string? Version { get; init; }
        public string? SourceUri { get; init; }
        public string? ExpectedSha256Provenance { get; init; }
        public string? SignerSubject { get; init; }
        public bool AuthenticodeChecked { get; init; }
        public bool AuthenticodeValid { get; init; }

        /// <summary>A final artifact exists only when the download was fully verified.</summary>
        public bool VerifiedArtifactPresent =>
            State == DownloadState.Verified && FinalPath is not null;

        public static ArtifactDownloadResult Failed(DownloadState state, string reason) =>
            new() { State = state, Reason = reason };
    }

    public async Task<ArtifactDownloadResult> DownloadAsync(
        ArtifactDownloadRequest request,
        string destinationRoot,
        CancellationToken cancellationToken = default)
    {
        ArtifactDownloadResult Tag(ArtifactDownloadResult r) => r with
        {
            RequestedId = request.RequestedId,
            Version = request.Version,
            SourceUri = SafeUriForProvenance(request.ArtifactUri),
            ExpectedSha256Provenance = request.ExpectedSha256
        };

        // ---- expected hash gate ----
        if (!ResourcePolicyValues.IsHexSha256(request.ExpectedSha256))
        {
            return Tag(ArtifactDownloadResult.Failed(
                DownloadState.MissingExpectedHash,
                "Expected SHA-256 must be a 64-digit hex value; refusing to download without it."));
        }

        // ---- source trust ----
        try
        {
            OfficialMetadataClient.ValidateRequestUri(request.ArtifactUri);
        }
        catch (OfficialMetadataClient.MetadataFetchException ex)
        {
            return Tag(ArtifactDownloadResult.Failed(DownloadState.UntrustedSource, ex.Message));
        }

        var artifactHost = request.ArtifactUri.Host;
        var hostAllowed = request.AllowedHosts.Any(h =>
            string.Equals(h, artifactHost, StringComparison.OrdinalIgnoreCase));
        if (!hostAllowed && !IsGitHubArtifactHost(artifactHost))
        {
            return Tag(ArtifactDownloadResult.Failed(
                DownloadState.UntrustedSource,
                $"Artifact host '{artifactHost}' is not in the request's allowed hosts."));
        }

        // ---- destination filename ----
        if (!TryGetSafeFileName(request.ArtifactUri, out var finalName, out var nameError))
        {
            return Tag(ArtifactDownloadResult.Failed(DownloadState.UnsafeFileName, nameError!));
        }

        string finalPath;
        try
        {
            Directory.CreateDirectory(destinationRoot);
            finalPath = Path.GetFullPath(Path.Combine(destinationRoot, finalName));
            var rootFull = Path.GetFullPath(destinationRoot);
            if (!finalPath.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return Tag(ArtifactDownloadResult.Failed(
                    DownloadState.DestinationError, "Resolved destination escapes the download root."));
            }
        }
        catch (Exception ex)
        {
            return Tag(ArtifactDownloadResult.Failed(
                DownloadState.DestinationError, $"Destination unavailable: {ex.Message}"));
        }

        // ---- disk space ----
        var freeBytes = _diskSpaceProbe.GetFreeBytes(destinationRoot);
        var needed = request.ExpectedSizeBytes ?? 0;
        if (needed > 0 && freeBytes >= 0 && freeBytes < needed)
        {
            return Tag(ArtifactDownloadResult.Failed(
                DownloadState.InsufficientDiskSpace,
                $"Free disk space {freeBytes} bytes is below expected artifact size {needed} bytes."));
        }

        // ---- bounded download into GUID-owned partial ----
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_downloadTimeout);
        var timeoutToken = timeoutCts.Token;

        var partialDir = Path.Combine(destinationRoot, ".partial-" + Guid.NewGuid().ToString("N"));
        var partialPath = Path.Combine(partialDir, finalName + ".partial");
        string? actualSha = null;
        long written = 0;
        long? declaredContentLength = null;

        try
        {
            var attempt = 0;
            while (true)
            {
                attempt++;
                try
                {
                    Directory.CreateDirectory(partialDir);
                    // Delete only our own attempt's partial before retrying; CreateNew stays strict.
                    if (File.Exists(partialPath))
                    {
                        File.Delete(partialPath);
                    }

                    (actualSha, written, declaredContentLength) = await DownloadOnceAsync(
                        request, partialPath, timeoutToken).ConfigureAwait(false);
                    break;
                }
                catch (OperationCanceledException)
                {
                    TryDeleteDirectory(partialDir);
                    var timedOut = !cancellationToken.IsCancellationRequested && timeoutToken.IsCancellationRequested;
                    return Tag(ArtifactDownloadResult.Failed(
                        timedOut ? DownloadState.Timeout : DownloadState.Cancelled,
                        timedOut
                            ? $"Download exceeded the {_downloadTimeout.TotalMinutes:0} minute bound."
                            : "Download cancelled."));
                }
                catch (Exception ex) when (IsTransientDownloadFailure(ex) && attempt < _maxAttempts)
                {
                    await Task.Delay(
                        RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)],
                        timeoutToken).ConfigureAwait(false);
                }
            }

            // ---- post-download checks (never retried) ----
            if (written == 0)
            {
                TryDeleteDirectory(partialDir);
                return Tag(ArtifactDownloadResult.Failed(DownloadState.DownloadFailed, "Empty response body."));
            }

            // Streamed bytes must match the server's declared Content-Length even when no
            // expected size was supplied — a truncated or padded body is never acceptable.
            if (declaredContentLength is { } declaredBytes && written != declaredBytes)
            {
                TryDeleteDirectory(partialDir);
                return Tag(new ArtifactDownloadResult
                {
                    State = DownloadState.SizeMismatch,
                    BytesWritten = written,
                    ActualSha256 = actualSha,
                    Reason = $"Downloaded {written} bytes but the server declared {declaredBytes}."
                });
            }

            if (request.ExpectedSizeBytes is { } expectedSize && written != expectedSize)
            {
                TryDeleteDirectory(partialDir);
                return Tag(new ArtifactDownloadResult
                {
                    State = DownloadState.SizeMismatch,
                    BytesWritten = written,
                    ActualSha256 = actualSha,
                    Reason = $"Downloaded {written} bytes; expected {expectedSize}."
                });
            }

            if (!string.Equals(actualSha, request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteDirectory(partialDir);
                return Tag(new ArtifactDownloadResult
                {
                    State = DownloadState.HashMismatch,
                    BytesWritten = written,
                    ActualSha256 = actualSha,
                    Reason = "Artifact SHA-256 did not match the expected digest."
                });
            }

            // ---- authenticode (only after hash success; signature is separate evidence) ----
            var authenticodeChecked = false;
            var authenticodeValid = false;
            string? signer = null;
            if (request.RequireAuthenticode)
            {
                authenticodeChecked = true;
                var signature = _authenticodeVerifier.Verify(partialPath, request.ExpectedPublisher);
                authenticodeValid = signature.IsValid;
                signer = signature.SignerSubject;
                if (!signature.IsValid)
                {
                    TryDeleteDirectory(partialDir);
                    return Tag(new ArtifactDownloadResult
                    {
                        State = DownloadState.SignatureInvalid,
                        BytesWritten = written,
                        ActualSha256 = actualSha,
                        Reason = signature.Failure ?? "Authenticode verification failed.",
                        SignerSubject = signer,
                        AuthenticodeChecked = true,
                        AuthenticodeValid = false
                    });
                }
            }

            // ---- atomic final rename: verified-only artifacts reach the destination ----
            File.Move(partialPath, finalPath, overwrite: true);
            TryDeleteDirectory(partialDir);

            return Tag(new ArtifactDownloadResult
            {
                State = DownloadState.Verified,
                FinalPath = finalPath,
                ActualSha256 = actualSha,
                BytesWritten = written,
                SignerSubject = signer,
                AuthenticodeChecked = authenticodeChecked,
                AuthenticodeValid = authenticodeValid
            });
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(partialDir);
            var timedOut = !cancellationToken.IsCancellationRequested && timeoutToken.IsCancellationRequested;
            return Tag(ArtifactDownloadResult.Failed(
                timedOut ? DownloadState.Timeout : DownloadState.Cancelled,
                timedOut ? $"Download exceeded the {_downloadTimeout.TotalMinutes:0} minute bound." : "Download cancelled."));
        }
        catch (Exception ex)
        {
            TryDeleteDirectory(partialDir);
            return Tag(ArtifactDownloadResult.Failed(DownloadState.DownloadFailed, ex.Message));
        }
    }

    private async Task<(string Sha256, long Written, long? DeclaredContentLength)> DownloadOnceAsync(
        ArtifactDownloadRequest request,
        string partialPath,
        CancellationToken cancellationToken)
    {
        await using var artifact = await _metadataClient.OpenStreamAsync(
            new OfficialMetadataClient.MetadataRequest
            {
                Uri = request.ArtifactUri,
                IsGitHubArtifact = IsGitHubArtifactHost(request.ArtifactUri.Host),
                AllowedHosts = request.AllowedHosts,
                MaxBytes = OfficialMetadataClient.DefaultArtifactMaxBytes,
                // Retry lives in this service; inner transport must not retry again.
                MaxAttempts = 1
            }, cancellationToken).ConfigureAwait(false);

        // Enforce declared length even when no expected size was supplied (cap check above);
        // when an expected size exists it must match exactly.
        if (artifact.ContentLength is { } declared
            && request.ExpectedSizeBytes is { } expected
            && declared != expected)
        {
            throw new InvalidDataException(
                $"Declared content length {declared} does not match expected {expected}.");
        }

        await using var file = new FileStream(
            partialPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 256];
        long written = 0;
        while (true)
        {
            var read = await artifact.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            hasher.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            written += read;
        }

        await file.FlushAsync(cancellationToken).ConfigureAwait(false);
        var sha = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
        return (sha, written, artifact.ContentLength);
    }

    /// <summary>
    /// Only network/transport failures are retryable. Hash, signature, size, trust, and
    /// policy rejections are terminal and never retried.
    /// </summary>
    private static bool IsTransientDownloadFailure(Exception ex) =>
        ex is HttpRequestException
            or OfficialMetadataClient.MetadataFetchException { IsTransient: true };

    /// <summary>
    /// Extract a safe final filename from the artifact URL path — never from RequestedId.
    /// Rejects empty names, separators, dot segments, ADS, trailing dots/spaces, and
    /// reserved DOS device names.
    /// </summary>
    internal static bool TryGetSafeFileName(Uri artifactUri, out string fileName, out string? error)
    {
        fileName = string.Empty;
        error = null;

        var rawName = Path.GetFileName(Uri.UnescapeDataString(artifactUri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(rawName) || rawName is "." or "..")
        {
            error = $"Artifact URL has no safe filename: {OfficialMetadataClient.SanitizeForLog(artifactUri)}";
            return false;
        }

        if (rawName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || rawName.Contains(':')           // alternate data streams / drive designators
            || rawName.Contains('/')
            || rawName.Contains('\\')
            || rawName.EndsWith('.')
            || rawName.EndsWith(' '))
        {
            error = $"Artifact filename '{rawName}' contains unsafe characters.";
            return false;
        }

        // Reserved stem check must use the FIRST dot segment: "CON.foo.bar" is still CON.
        var stem = rawName.Split('.')[0];
        if (stem.Length == 0 || WindowsReservedNames.Contains(stem))
        {
            error = $"Artifact filename '{rawName}' is a reserved device name.";
            return false;
        }

        fileName = rawName;
        return true;
    }

    private static string? SafeUriForProvenance(Uri uri)
    {
        try
        {
            return OfficialMetadataClient.SanitizeForLog(uri);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsGitHubArtifactHost(string host) =>
        string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "release-assets.githubusercontent.com", StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup; GUID-owned dir cannot collide with real content
        }
    }
}

public sealed record AuthenticodeResult(bool IsValid, string? SignerSubject, string? Failure);

public interface IAuthenticodeVerifier
{
    AuthenticodeResult Verify(string filePath, string? expectedPublisher);
}

public interface IDiskSpaceProbe
{
    /// <summary>Free bytes on the volume containing the directory; -1 when unknown.</summary>
    long GetFreeBytes(string directory);
}

public sealed class SystemDiskSpaceProbe : IDiskSpaceProbe
{
    public long GetFreeBytes(string directory)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(directory));
            return root is null ? -1 : new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return -1;
        }
    }
}
