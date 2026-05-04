using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Checks published GitHub Releases for <see cref="DefaultOwner"/>/<see cref="DefaultRepo"/>.
/// The newest <i>eligible</i> release is chosen by <c>published_at</c> (newest first), then <c>tag_name</c>/<c>name</c> supply the version.
/// Asset filenames are only used after that release is selected.
/// </summary>
public sealed class GitHubReleaseUpdateCheckService : IUpdateCheckService, IDisposable
{
    public const string DefaultOwner = "Forger-Digital-Solutions";
    public const string DefaultRepo = "ForgerEMS";

    private static readonly Regex ForgerEmsVersionZip = new(
        @"^ForgerEMS-v.+\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

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
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ForgerEMS-UpdateCheck/1.1.12");
        _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        _ownsClient = true;
    }

    public async Task<UpdateCheckResult> CheckForNewerReleaseAsync(
        string installedVersionLabel,
        string? ignoredVersionNormalized,
        UpdateReleaseChannel channel = UpdateReleaseChannel.BetaRcAllowed,
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
            var listUrl = $"https://api.github.com/repos/{DefaultOwner}/{DefaultRepo}/releases?per_page=100";
            using var listResponse = await _httpClient.GetAsync(listUrl, cancellationToken).ConfigureAwait(false);

            if (listResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult
                {
                    Succeeded = false,
                    Outcome = UpdateCheckOutcome.Failed,
                    FailureKind = UpdateCheckFailureKind.UpdateSourceUnreachable,
                    ErrorMessage = "Update source could not be reached.",
                    DiagnosticDetail =
                        "GitHub returned 404 for the releases API. Confirm the repo is public and Forger-Digital-Solutions/ForgerEMS is correct."
                };
            }

            if (!listResponse.IsSuccessStatusCode)
            {
                return await BuildFailedResultFromResponseAsync(listResponse, cancellationToken).ConfigureAwait(false);
            }

            await using var stream = await listResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await ReadReleaseJsonAsync(stream, cancellationToken).ConfigureAwait(false);
            if (document is null)
            {
                return MalformedJsonResult();
            }

            return ProcessReleasesDocument(document.RootElement, installed, ignoredVersionNormalized, channel);
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
                ErrorMessage = "Could not check for updates. Network unavailable.",
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

    private static UpdateCheckResult ProcessReleasesDocument(
        JsonElement root,
        AppSemanticVersion installed,
        string? ignoredVersionNormalized,
        UpdateReleaseChannel channel)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
                ErrorMessage = "Release list response was not a JSON array."
            };
        }

        var sorted = new List<(JsonElement Element, DateTimeOffset PublishedAt)>();
        foreach (var rel in root.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            if (!TryReadPublishedAt(rel, out var publishedAt))
            {
                publishedAt = DateTimeOffset.MinValue;
            }

            sorted.Add((rel, publishedAt));
        }

        sorted.Sort(static (a, b) => b.PublishedAt.CompareTo(a.PublishedAt));

        JsonElement? candidate = null;
        foreach (var (element, _) in sorted)
        {
            var prerelease = element.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            if (channel == UpdateReleaseChannel.StableOnly && prerelease)
            {
                continue;
            }

            candidate = element;
            break;
        }

        if (candidate is null)
        {
            var msg = channel == UpdateReleaseChannel.StableOnly
                ? "No stable ForgerEMS release was found yet. Allow Beta/RC in Settings → App updates to see preview builds published on GitHub."
                : "No published ForgerEMS release was found yet.";

            return new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = UpdateCheckOutcome.NoPublishedRelease,
                UpdateAvailable = false,
                ErrorMessage = msg
            };
        }

        return BuildResultFromReleaseRoot(candidate.Value, installed, ignoredVersionNormalized, channel);
    }

    private static bool TryReadPublishedAt(JsonElement rel, out DateTimeOffset publishedAt)
    {
        if (rel.TryGetProperty("published_at", out var p) &&
            p.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(p.GetString(), out publishedAt))
        {
            return true;
        }

        publishedAt = default;
        return false;
    }

    private static UpdateCheckResult MalformedJsonResult() =>
        new()
        {
            Succeeded = false,
            Outcome = UpdateCheckOutcome.Failed,
            FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
            ErrorMessage = "Could not read release metadata (invalid JSON from GitHub)."
        };

    private static async Task<JsonDocument?> ReadReleaseJsonAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(stream, default, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static UpdateCheckResult BuildResultFromReleaseRoot(
        JsonElement root,
        AppSemanticVersion installed,
        string? ignoredVersionNormalized,
        UpdateReleaseChannel channel)
    {
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
        root.TryGetProperty("name", out var nameProp);
        var releaseName = nameProp.ValueKind == JsonValueKind.String ? nameProp.GetString() : null;

        var label = ReleaseVersionParser.NormalizeLabel(
            string.IsNullOrWhiteSpace(tag) ? releaseName : tag);
        if (string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(releaseName))
        {
            label = ReleaseVersionParser.NormalizeLabel(releaseName);
        }

        var notesUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? string.Empty : string.Empty;

        if (channel == UpdateReleaseChannel.StableOnly &&
            root.TryGetProperty("prerelease", out var pre) &&
            pre.ValueKind == JsonValueKind.True)
        {
            return new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = UpdateCheckOutcome.NoPublishedRelease,
                UpdateAvailable = false,
                LatestVersionLabel = label,
                ReleaseNotesUrl = notesUrl,
                ErrorMessage =
                    "No stable ForgerEMS release was found yet. Allow Beta/RC in Settings → App updates to see preview builds."
            };
        }

        SelectReleaseAssets(root, out var zipName, out var zipUrl, out var matchedPreferredZip, out var exeName, out var exeUrl, out var checksumsUrl, out var instructionsUrl);

        var parsed = ReleaseVersionParser.TryParseFromGitHubRelease(tag, releaseName, out var latestSem);
        if (!parsed)
        {
            var ignored = ReleaseVersionParser.NormalizeIgnored(ignoredVersionNormalized);
            if (!string.IsNullOrEmpty(ignored) &&
                string.Equals(label, ignored, StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateCheckResult
                {
                    Succeeded = true,
                    Outcome = UpdateCheckOutcome.IgnoredVersion,
                    UpdateAvailable = false,
                    LatestVersionLabel = label,
                    ReleaseNotesUrl = notesUrl,
                    RecommendedZipAssetName = zipName,
                    RecommendedZipDownloadUrl = zipUrl,
                    RecommendedZipPatternMatched = matchedPreferredZip,
                    InstallerAssetName = exeName,
                    InstallerDownloadUrl = exeUrl,
                    ChecksumsDownloadUrl = checksumsUrl,
                    DownloadInstructionsUrl = instructionsUrl,
                    VersionComparisonUncertain = true,
                    RecommendedZipAssetMissing = string.IsNullOrWhiteSpace(zipUrl) || !matchedPreferredZip,
                    ErrorMessage = UpdateCheckDisplay.FormatIgnoredVersion(label)
                };
            }

            var missingZip = string.IsNullOrWhiteSpace(zipUrl) || !matchedPreferredZip;
            return new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = UpdateCheckOutcome.UpdateAvailable,
                UpdateAvailable = true,
                LatestVersionLabel = string.IsNullOrWhiteSpace(label) ? tag.Trim() : label,
                ReleaseNotesUrl = notesUrl,
                RecommendedZipAssetName = zipName,
                RecommendedZipDownloadUrl = zipUrl,
                RecommendedZipPatternMatched = matchedPreferredZip,
                InstallerAssetName = exeName,
                InstallerDownloadUrl = exeUrl,
                ChecksumsDownloadUrl = checksumsUrl,
                DownloadInstructionsUrl = instructionsUrl,
                VersionComparisonUncertain = true,
                RecommendedZipAssetMissing = missingZip,
                ErrorMessage =
                    "A newer release may be available (the release tag could not be parsed as a version). Open the GitHub Release page to confirm."
            };
        }

        var ignoredOk = ReleaseVersionParser.NormalizeIgnored(ignoredVersionNormalized);
        var normTag = ReleaseVersionParser.NormalizeLabel(tag);
        if (!string.IsNullOrEmpty(ignoredOk) &&
            string.Equals(normTag, ignoredOk, StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = UpdateCheckOutcome.IgnoredVersion,
                UpdateAvailable = false,
                LatestVersion = latestSem.ToLegacyVersion(),
                LatestVersionLabel = normTag,
                ReleaseNotesUrl = notesUrl,
                RecommendedZipAssetName = zipName,
                RecommendedZipDownloadUrl = zipUrl,
                RecommendedZipPatternMatched = matchedPreferredZip,
                InstallerAssetName = exeName,
                InstallerDownloadUrl = exeUrl,
                ChecksumsDownloadUrl = checksumsUrl,
                DownloadInstructionsUrl = instructionsUrl,
                ErrorMessage = UpdateCheckDisplay.FormatIgnoredVersion(normTag)
            };
        }

        var latestVersion = latestSem.ToLegacyVersion();
        var cmp = latestSem.CompareTo(installed);
        var newer = cmp > 0;
        var outcome = newer
            ? UpdateCheckOutcome.UpdateAvailable
            : cmp == 0
                ? UpdateCheckOutcome.AlreadyLatest
                : UpdateCheckOutcome.InstalledNewerThanLatestPublic;

        var missingPreferredZip = newer && (string.IsNullOrWhiteSpace(zipUrl) || !matchedPreferredZip);

        return new UpdateCheckResult
        {
            Succeeded = true,
            Outcome = outcome,
            UpdateAvailable = newer,
            LatestVersion = latestVersion,
            LatestVersionLabel = normTag,
            ReleaseNotesUrl = notesUrl,
            RecommendedZipAssetName = zipName,
            RecommendedZipDownloadUrl = zipUrl,
            RecommendedZipPatternMatched = matchedPreferredZip,
            InstallerAssetName = exeName,
            InstallerDownloadUrl = exeUrl,
            ChecksumsDownloadUrl = checksumsUrl,
            DownloadInstructionsUrl = instructionsUrl,
            RecommendedZipAssetMissing = missingPreferredZip
        };
    }

    /// <summary>Selects ZIP (preferred patterns first) then a ForgerEMS .exe for Advanced. Sets <paramref name="matchedPreferredZip"/> when tier 1 or 2 matched.</summary>
    private static void SelectReleaseAssets(
        JsonElement root,
        out string? zipName,
        out string? zipUrl,
        out bool matchedPreferredZip,
        out string? exeName,
        out string? exeUrl,
        out string? checksumsUrl,
        out string? instructionsUrl)
    {
        zipName = null;
        zipUrl = null;
        matchedPreferredZip = false;
        exeName = null;
        exeUrl = null;
        checksumsUrl = null;
        instructionsUrl = null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        string? tier1N = null, tier1U = null;
        string? tier2N = null, tier2U = null;
        string? tier3ForgerN = null, tier3ForgerU = null;
        string? tier3AnyN = null, tier3AnyU = null;
        string? setupExeN = null, setupExeU = null;
        string? anyExeN = null, anyExeU = null;

        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameProp) ||
                !asset.TryGetProperty("browser_download_url", out var dlProp))
            {
                continue;
            }

            var name = nameProp.GetString() ?? string.Empty;
            var url = dlProp.GetString();
            if (string.IsNullOrWhiteSpace(url) ||
                !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Equals("CHECKSUMS.sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksumsUrl = url;
                continue;
            }

            if (name.Equals("DOWNLOAD_BETA.txt", StringComparison.OrdinalIgnoreCase))
            {
                instructionsUrl = url;
                continue;
            }

            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("Beta", StringComparison.OrdinalIgnoreCase))
                {
                    tier1N ??= name;
                    tier1U ??= url;
                }
                else if (ForgerEmsVersionZip.IsMatch(name))
                {
                    tier2N ??= name;
                    tier2U ??= url;
                }
                else if (name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase))
                {
                    tier3ForgerN ??= name;
                    tier3ForgerU ??= url;
                }
                else
                {
                    tier3AnyN ??= name;
                    tier3AnyU ??= url;
                }
            }

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                setupExeN ??= name;
                setupExeU ??= url;
            }
            else
            {
                anyExeN ??= name;
                anyExeU ??= url;
            }
        }

        if (tier1U is not null)
        {
            zipName = tier1N;
            zipUrl = tier1U;
            matchedPreferredZip = true;
        }
        else if (tier2U is not null)
        {
            zipName = tier2N;
            zipUrl = tier2U;
            matchedPreferredZip = true;
        }
        else if (tier3ForgerU is not null)
        {
            zipName = tier3ForgerN;
            zipUrl = tier3ForgerU;
            matchedPreferredZip = false;
        }
        else if (tier3AnyU is not null)
        {
            zipName = tier3AnyN;
            zipUrl = tier3AnyU;
            matchedPreferredZip = false;
        }

        if (setupExeU is not null)
        {
            exeName = setupExeN;
            exeUrl = setupExeU;
        }
        else if (anyExeU is not null)
        {
            exeName = anyExeN;
            exeUrl = anyExeU;
        }
    }

    private static async Task<UpdateCheckResult> BuildFailedResultFromResponseAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
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

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult
            {
                Succeeded = false,
                Outcome = UpdateCheckOutcome.Failed,
                FailureKind = UpdateCheckFailureKind.UpdateSourceUnreachable,
                ErrorMessage = "Update source could not be reached.",
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

    /// <summary>Parses version from GitHub <c>tag_name</c> first, then release <c>name</c> (e.g. &quot;ForgerEMS v1.2.0&quot;).</summary>
    public static bool TryParseFromGitHubRelease(string? tagName, string? releaseName, out AppSemanticVersion sem)
    {
        if (AppSemanticVersion.TryParse(tagName, out sem))
        {
            return true;
        }

        var n = NormalizeReleaseTitleForVersion(releaseName);
        return !string.IsNullOrWhiteSpace(n) && AppSemanticVersion.TryParse(n, out sem);
    }

    private static string? NormalizeReleaseTitleForVersion(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var n = name.Trim();
        if (n.StartsWith("ForgerEMS", StringComparison.OrdinalIgnoreCase))
        {
            n = n["ForgerEMS".Length..].Trim();
            if (n.StartsWith('-'))
            {
                n = n[1..].Trim();
            }
        }

        return string.IsNullOrWhiteSpace(n) ? null : n;
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

    public static string BuildUncertainHeadline()
        => "A newer GitHub release may be available. Review the release page before downloading.";
}
