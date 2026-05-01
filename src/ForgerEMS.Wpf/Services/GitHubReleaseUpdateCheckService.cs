using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Checks public GitHub Releases for a newer ForgerEMS version. No token required for public repos.
/// </summary>
public sealed class GitHubReleaseUpdateCheckService : IUpdateCheckService, IDisposable
{
    public const string DefaultOwner = "Forger-Digital-Solutions";
    public const string DefaultRepo = "ForgerEMS";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public GitHubReleaseUpdateCheckService(HttpClient? httpClient = null)
    {
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
            return;
        }

        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS-UpdateCheck/1.1.4");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        _ownsClient = true;
    }

    public async Task<UpdateCheckResult> CheckForNewerReleaseAsync(
        string installedVersionLabel,
        string? ignoredVersionNormalized,
        CancellationToken cancellationToken = default)
    {
        if (!AppSemanticVersion.TryParse(installedVersionLabel, out var installed))
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
                ErrorMessage = "Could not determine the installed app version for comparison."
            };
        }

        try
        {
            var latestUrl = $"https://api.github.com/repos/{DefaultOwner}/{DefaultRepo}/releases/latest";
            using var response = await _httpClient
                .GetAsync(latestUrl, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var emptyReleases = await TryReleasesListIsEmptyAsync(cancellationToken).ConfigureAwait(false);
                if (emptyReleases == true)
                {
                    return new UpdateCheckResult
                    {
                        Succeeded = true,
                        Outcome = UpdateCheckOutcome.NoPublishedRelease,
                        UpdateAvailable = false,
                        ErrorMessage = "No public GitHub Release is published for this repo yet."
                    };
                }

                return new UpdateCheckResult
                {
                    Succeeded = false,
                    Outcome = UpdateCheckOutcome.Failed,
                    FailureKind = UpdateCheckFailureKind.ReleaseEndpointNotFound,
                    ErrorMessage = "Release endpoint returned 404. Confirm GitHub owner/repo, public Releases, and that a release is marked Latest.",
                    DiagnosticDetail = await SafeReadBodyPreviewAsync(response, cancellationToken).ConfigureAwait(false)
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                return await BuildFailedResultFromResponseAsync(response, cancellationToken).ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            JsonDocument document;
            try
            {
                document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                return new UpdateCheckResult
                {
                    Succeeded = false,
                    Outcome = UpdateCheckOutcome.Failed,
                    FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
                    ErrorMessage = "Could not read release metadata (invalid JSON from GitHub).",
                    DiagnosticDetail = exception.Message
                };
            }

            using (document)
            {
                return ParseLatestReleaseDocument(document, installed, ignoredVersionNormalized);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Cancelled,
                FailureKind = UpdateCheckFailureKind.Cancelled,
                ErrorMessage = "Update check was cancelled."
            };
        }
        catch (OperationCanceledException)
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.Timeout,
                ErrorMessage = "Update check timed out. Try again later.",
                DiagnosticDetail = "Request timed out or was aborted before completion."
            };
        }
        catch (Exception exception) when (IsLikelyNetworkFailure(exception))
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.Network,
                ErrorMessage = "Could not reach GitHub (network, DNS, or offline). Check your connection and try again.",
                DiagnosticDetail = exception.Message
            };
        }
        catch (Exception exception)
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.Unknown,
                ErrorMessage = "Update check failed unexpectedly.",
                DiagnosticDetail = exception.Message
            };
        }
    }

    private async Task<bool?> TryReleasesListIsEmptyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var url = $"https://api.github.com/repos/{DefaultOwner}/{DefaultRepo}/releases?per_page=1";
            using var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return document.RootElement.GetArrayLength() == 0;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<UpdateCheckResult> BuildFailedResultFromResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var code = (int)response.StatusCode;
        var body = await SafeReadBodyPreviewAsync(response, cancellationToken).ConfigureAwait(false);
        var combined = (body ?? string.Empty).ToLowerInvariant();

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            var rateLimited = combined.Contains("rate limit", StringComparison.Ordinal) ||
                              combined.Contains("api rate limit", StringComparison.Ordinal);
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.AccessDeniedOrRateLimited,
                ErrorMessage = rateLimited
                    ? "GitHub rate-limited this device. Wait a few minutes or try again on a different network."
                    : "GitHub denied access (403). The repo may be private or blocked from this network.",
                DiagnosticDetail = $"HTTP {code}: {body}"
            };
        }

        if (code == 429)
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.AccessDeniedOrRateLimited,
                ErrorMessage = "GitHub rate limit exceeded. Retry later.",
                DiagnosticDetail = $"HTTP {code}: {body}"
            };
        }

        return new UpdateCheckResult
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.HttpError,
            ErrorMessage = $"GitHub returned HTTP {code}. Try again later or check Diagnostics logs.",
            DiagnosticDetail = $"HTTP {code}: {body}"
        };
    }

    private static async Task<string?> SafeReadBodyPreviewAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return text.Length <= 480 ? text : text[..480] + "…";
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLikelyNetworkFailure(Exception exception)
    {
        if (exception is HttpRequestException or IOException)
        {
            return true;
        }

        return exception.InnerException is SocketException or HttpRequestException;
    }

    private static UpdateCheckResult ParseLatestReleaseDocument(
        JsonDocument document,
        AppSemanticVersion installed,
        string? ignoredVersionNormalized)
    {
        var root = document.RootElement;
        if (!root.TryGetProperty("tag_name", out var tagProp))
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
                ErrorMessage = "Release metadata is missing tag_name."
            };
        }

        var tag = tagProp.GetString() ?? string.Empty;
        var label = ReleaseVersionParser.NormalizeLabel(tag);

        var notesUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;

        string? installerUrl = null;
        string? installerName = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out var nameProp) ||
                    !asset.TryGetProperty("browser_download_url", out var dlProp))
                {
                    continue;
                }

                var name = nameProp.GetString() ?? string.Empty;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
                {
                    installerName = name;
                    installerUrl = dlProp.GetString();
                    break;
                }
            }

            if (installerUrl is null)
            {
                var fallback = assets.EnumerateArray().FirstOrDefault(a =>
                    a.TryGetProperty("name", out var n) &&
                    (n.GetString() ?? string.Empty).EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                if (fallback.ValueKind != JsonValueKind.Undefined &&
                    fallback.TryGetProperty("browser_download_url", out var dl) &&
                    fallback.TryGetProperty("name", out var fn))
                {
                    installerName = fn.GetString();
                    installerUrl = dl.GetString();
                }
            }
        }

        if (!AppSemanticVersion.TryParse(tag, out var latestSem))
        {
            return new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = UpdateCheckOutcome.None,
                UpdateAvailable = false,
                LatestVersionLabel = label,
                ReleaseNotesUrl = notesUrl,
                ErrorMessage = "Latest release tag could not be parsed as a version.",
                InstallerAssetName = installerName,
                InstallerDownloadUrl = installerUrl
            };
        }

        var latestVersion = latestSem.ToLegacyVersion();
        var ignored = ReleaseVersionParser.NormalizeIgnored(ignoredVersionNormalized);
        if (!string.IsNullOrEmpty(ignored) &&
            string.Equals(ReleaseVersionParser.NormalizeLabel(label), ignored, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = UpdateCheckOutcome.IgnoredVersion,
                UpdateAvailable = false,
                LatestVersion = latestVersion,
                LatestVersionLabel = label,
                ReleaseNotesUrl = notesUrl,
                InstallerAssetName = installerName,
                InstallerDownloadUrl = installerUrl,
                ErrorMessage = UpdateCheckDisplay.FormatIgnoredVersion(ReleaseVersionParser.NormalizeLabel(label))
            };
        }

        var cmp = latestSem.CompareTo(installed);
        var newer = cmp > 0;
        var outcome = newer
            ? UpdateCheckOutcome.UpdateAvailable
            : cmp == 0
                ? UpdateCheckOutcome.AlreadyLatest
                : UpdateCheckOutcome.InstalledNewerThanLatestPublic;

        return new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = outcome,
            UpdateAvailable = newer,
            LatestVersion = latestVersion,
            LatestVersionLabel = label,
            ReleaseNotesUrl = notesUrl,
            InstallerAssetName = installerName,
            InstallerDownloadUrl = installerUrl
        };
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }
}

public static class ReleaseVersionParser
{
    public static bool TryParseVersion(string? tag, out Version version)
    {
        if (!AppSemanticVersion.TryParse(tag, out var sem))
        {
            version = new Version(0, 0);
            return false;
        }

        version = sem.ToLegacyVersion();
        return true;
    }

    public static string NormalizeLabel(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        var t = tag.Trim();
        while (t.StartsWith("ForgerEMS-", StringComparison.OrdinalIgnoreCase))
        {
            t = t["ForgerEMS-".Length..];
        }

        if (t.Length >= 1 && (t[0] == 'v' || t[0] == 'V'))
        {
            t = t[1..];
        }

        return t;
    }

    public static string NormalizeIgnored(string? ignored)
    {
        if (string.IsNullOrWhiteSpace(ignored))
        {
            return string.Empty;
        }

        return NormalizeLabel(ignored);
    }
}

public static class UpdateNotificationTextBuilder
{
    public static string BuildHeadline(string latestLabel)
        => $"Update available: ForgerEMS v{ReleaseVersionParser.NormalizeLabel(latestLabel)}. Download now?";
}
