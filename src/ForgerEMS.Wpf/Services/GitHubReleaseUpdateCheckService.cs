using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Checks published GitHub Releases for <see cref="DefaultOwner"/>/<see cref="DefaultRepo"/>.
/// The newest <i>eligible</i> release is chosen by the highest <see cref="AppSemanticVersion"/> parsed from
/// <c>tag_name</c>/<c>name</c> (<c>published_at</c> only breaks exact-version ties); releases where neither
/// the tag nor the release name yields a parseable version are skipped and never offered.
/// Asset filenames are only used after that release is selected.
/// </summary>
public sealed class GitHubReleaseUpdateCheckService : IUpdateCheckService, IDisposable
{
    public const string DefaultOwner = "Forger-Digital-Solutions";
    public const string DefaultRepo = "ForgerEMS";

    /// <summary>GitHub API requests use this User-Agent string (see GitHub API guidance).</summary>
    public const string UpdateCheckUserAgent = "ForgerEMS";

    private static readonly Regex ForgerEmsVersionZip = new(
        @"^ForgerEMS-v.+\.zip$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _targetArchitecture;
    private readonly ForgerEMS.Wpf.Services.Resources.OfficialMetadataClient _metadataClient;

    /// <param name="targetArchitecture">RID the app ships as (win-x64 default). Asset selection
    /// prefers names carrying this token and rejects mismatched-arch assets.</param>
    public GitHubReleaseUpdateCheckService(HttpClient? httpClient = null, string targetArchitecture = "win-x64")
    {
        _targetArchitecture = string.IsNullOrWhiteSpace(targetArchitecture)
            ? "win-x64"
            : targetArchitecture.Trim().ToLowerInvariant();
        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsClient = false;
            ConfigureGitHubApiClient(_httpClient);
        }
        else
        {
            var timeoutSeconds = ForgerEmsEnvironmentConfiguration.UpdateTimeoutSeconds;
            var handler = new HttpClientHandler { AllowAutoRedirect = false };
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds <= 0 ? 25 : timeoutSeconds)
            };
            ConfigureGitHubApiClient(_httpClient);
            _ownsClient = true;
        }

        // Secondary artifact fetches (CHECKSUMS, release.json) go through the shared trusted
        // transport: capped responses, manual allowed-host redirects, no token forwarding.
        _metadataClient = new ForgerEMS.Wpf.Services.Resources.OfficialMetadataClient(httpClient);
    }

    /// <summary>GitHub requires a valid User-Agent on every REST request; use <see cref="HttpRequestHeaders.UserAgent"/> (not TryAddWithoutValidation).</summary>
    internal static void ConfigureGitHubApiClient(HttpClient client)
    {
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.Remove("X-GitHub-Api-Version");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(ResolveUpdateUserAgent());

        // Never a default Authorization: the bearer token is attached per-request and only to
        // api.github.com — a client default would leak it to github.com artifact hosts.
        client.DefaultRequestHeaders.Authorization = null;
    }

    /// <summary>Attach the configured bearer token to this request only when it targets the GitHub REST API host.</summary>
    private static void AttachGitHubApiAuth(HttpRequestMessage request)
    {
        var token = ForgerEmsEnvironmentConfiguration.GitHubApiToken;
        if (!string.IsNullOrWhiteSpace(token)
            && string.Equals(request.RequestUri?.Host, "api.github.com", StringComparison.OrdinalIgnoreCase))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    /// <summary>Builds the update-check User-Agent (env override or versioned default with repo contact URL).</summary>
    public static string ResolveUpdateUserAgent()
    {
        var configured = ForgerEmsEnvironmentConfiguration.UpdateUserAgent;
        if (!string.IsNullOrWhiteSpace(configured) &&
            !configured.Equals(UpdateCheckUserAgent, StringComparison.OrdinalIgnoreCase))
        {
            return configured.Trim();
        }

        var owner = SanitizeGitHubPathSegment(ForgerEmsEnvironmentConfiguration.GitHubOwner, DefaultOwner);
        var repo = SanitizeGitHubPathSegment(ForgerEmsEnvironmentConfiguration.GitHubRepo, DefaultRepo);
        return $"{UpdateCheckUserAgent}/{AppReleaseInfo.Version} (+https://github.com/{owner}/{repo})";
    }

    public async Task<UpdateCheckResult> CheckForNewerReleaseAsync(
        string installedVersionLabel,
        string? ignoredVersionNormalized,
        UpdateReleaseChannel channel = UpdateReleaseChannel.StableOnly,
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

        // Overall update-check deadline: bounds the API list fetch AND all secondary
        // metadata fetches combined (each fetch also gets its own linked deadline).
        var overallSeconds = ForgerEmsEnvironmentConfiguration.UpdateTimeoutSeconds;
        using var overallDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overallDeadline.CancelAfter(TimeSpan.FromSeconds(
            Math.Max(60, overallSeconds <= 0 ? 60 : overallSeconds * 2)));

        try
        {
            var owner = SanitizeGitHubPathSegment(ForgerEmsEnvironmentConfiguration.GitHubOwner, DefaultOwner);
            var repo = SanitizeGitHubPathSegment(ForgerEmsEnvironmentConfiguration.GitHubRepo, DefaultRepo);
            var listUrl = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=100";
            using var listRequest = new HttpRequestMessage(HttpMethod.Get, listUrl);
            AttachGitHubApiAuth(listRequest);
            using var listResponse = await _httpClient.SendAsync(
                listRequest, HttpCompletionOption.ResponseHeadersRead, overallDeadline.Token).ConfigureAwait(false);

            if (listResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult
                {
                    Succeeded = false,
                    Outcome = UpdateCheckOutcome.Failed,
                    FailureKind = UpdateCheckFailureKind.UpdateSourceUnreachable,
                    ErrorMessage = "Update source could not be reached.",
                    DiagnosticDetail =
                        $"GitHub returned 404 for the releases API. Confirm the repo is public and {owner}/{repo} is correct."
                };
            }

            if (!listResponse.IsSuccessStatusCode)
            {
                return await BuildFailedResultFromResponseAsync(listResponse, overallDeadline.Token).ConfigureAwait(false);
            }

            const long maxListBytes = ForgerEMS.Wpf.Services.Resources.OfficialMetadataClient.DefaultMetadataMaxBytes;
            if (listResponse.Content.Headers.ContentLength is { } declaredListBytes
                && declaredListBytes > maxListBytes)
            {
                return new UpdateCheckResult
                {
                    Succeeded = false,
                    Outcome = UpdateCheckOutcome.Failed,
                    FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
                    ErrorMessage = "Release metadata response exceeded the size cap.",
                    DiagnosticDetail = $"Content-Length {declaredListBytes} exceeds {maxListBytes} bytes."
                };
            }

            await using var stream = await listResponse.Content.ReadAsStreamAsync(overallDeadline.Token).ConfigureAwait(false);
            using var document = await ReadReleaseJsonAsync(stream, maxListBytes, overallDeadline.Token).ConfigureAwait(false);
            if (document is null)
            {
                return MalformedJsonResult();
            }

            var result = ProcessReleasesDocument(
                document.RootElement, installed, ignoredVersionNormalized, channel,
                owner, repo, _targetArchitecture);
            return await ValidateReleaseManifestAssetsAsync(
                result, document.RootElement, overallDeadline.Token).ConfigureAwait(false);
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
        UpdateReleaseChannel channel,
        string owner,
        string repo,
        string targetArchitecture)
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

        var releasesArrayLength = root.GetArrayLength();
        var sawNonDraftRow = false;
        var sawParsedRow = false;
        var sawMalformedFlags = false;
        JsonElement? candidate = null;
        AppSemanticVersion candidateVersion = default;
        var candidateLabel = string.Empty;
        var candidatePublishedAt = DateTimeOffset.MinValue;

        foreach (var rel in root.EnumerateArray())
        {
            if (rel.ValueKind != JsonValueKind.Object)
            {
                sawMalformedFlags = true;
                continue;
            }

            // draft/prerelease are mandatory booleans in the GitHub releases schema; a missing or
            // wrong-kind flag is malformed metadata and the row is never trusted as published/stable.
            if (!rel.TryGetProperty("draft", out var draft)
                || draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !rel.TryGetProperty("prerelease", out var prerelease)
                || prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                sawMalformedFlags = true;
                continue;
            }

            if (draft.GetBoolean())
            {
                continue;
            }

            sawNonDraftRow = true;

            var tag = rel.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String
                ? tagProp.GetString()
                : null;
            var releaseName = rel.TryGetProperty("name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String
                ? nameProp.GetString()
                : null;

            if (!ReleaseVersionParser.TryParseFromGitHubRelease(tag, releaseName, out var sem, out var label))
            {
                continue;
            }

            sawParsedRow = true;

            if (channel == UpdateReleaseChannel.StableOnly)
            {
                if (prerelease.GetBoolean() || sem.Prerelease is not null)
                {
                    continue;
                }
            }

            var publishedAt = TryReadPublishedAt(rel, out var p) ? p : DateTimeOffset.MinValue;
            if (candidate is null ||
                sem > candidateVersion ||
                (sem == candidateVersion && publishedAt > candidatePublishedAt))
            {
                candidate = rel;
                candidateVersion = sem;
                candidateLabel = label;
                candidatePublishedAt = publishedAt;
            }
        }

        if (candidate is null)
        {
            if ((sawNonDraftRow && !sawParsedRow) || (sawMalformedFlags && !sawParsedRow))
            {
                return new UpdateCheckResult
                {
                    Succeeded = false,
                    Outcome = UpdateCheckOutcome.Failed,
                    FailureKind = UpdateCheckFailureKind.ReleaseMetadataInvalid,
                    ErrorMessage = sawMalformedFlags && !sawNonDraftRow
                        ? "Published release metadata was malformed (release flags missing or invalid)."
                        : "Published release metadata could not be read (no parseable version in tag or name).",
                    ReleasesFetchedCount = releasesArrayLength
                };
            }

            var msg = channel == UpdateReleaseChannel.StableOnly && sawParsedRow
                ? "No stable ForgerEMS release was found yet. Allow Beta/RC in Settings → App updates to see preview builds published on GitHub."
                : "No published ForgerEMS release was found yet.";

            return new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = UpdateCheckOutcome.NoPublishedRelease,
                UpdateAvailable = false,
                ErrorMessage = msg,
                ReleasesFetchedCount = releasesArrayLength
            };
        }

        return BuildResultFromReleaseRoot(
            candidate.Value,
            candidateVersion,
            candidateLabel,
            installed,
            ignoredVersionNormalized,
            releasesArrayLength,
            owner,
            repo,
            targetArchitecture);
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

    /// <summary>Bounded JSON read: streams are copied with a hard byte cap before parsing — declared or actual over-cap returns null (invalid metadata).</summary>
    private static async Task<JsonDocument?> ReadReleaseJsonAsync(
        Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        try
        {
            using var bounded = new MemoryStream();
            var buffer = new byte[64 * 1024];
            long total = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes)
                {
                    return null;
                }

                bounded.Write(buffer, 0, read);
            }

            bounded.Position = 0;
            return await JsonDocument.ParseAsync(bounded, default, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (int AssetCount, string[] Names) CollectAssetMetadata(JsonElement releaseRoot)
    {
        if (!releaseRoot.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (0, Array.Empty<string>());
        }

        var count = assets.GetArrayLength();
        var names = new List<string>(Math.Min(count, 24));
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var n = nameProp.GetString();
            if (!string.IsNullOrWhiteSpace(n) && names.Count < 24)
            {
                names.Add(n!);
            }
        }

        return (count, names.ToArray());
    }

    private static UpdateCheckResult AttachReleaseTelemetry(UpdateCheckResult result, JsonElement releaseRoot, int releasesArrayLength)
    {
        var (assetCount, assetNames) = CollectAssetMetadata(releaseRoot);
        var publishedAt = TryReadPublishedAt(releaseRoot, out var p) ? (DateTimeOffset?)p : null;
        var tagRaw = releaseRoot.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String
            ? tagProp.GetString() ?? string.Empty
            : string.Empty;

        var patched = result with
        {
            ReleasesFetchedCount = releasesArrayLength,
            SelectedReleasePublishedAt = publishedAt,
            SelectedReleaseTagRaw = tagRaw,
            AssetCount = assetCount,
            AssetNamesSnapshot = assetNames
        };

        if (assetCount != 0 ||
            !patched.Succeeded ||
            patched.Outcome == UpdateCheckOutcome.NoPublishedRelease)
        {
            return patched;
        }

        const string noAssets =
            "The latest GitHub release has no published assets yet, so there is nothing safe to download from this check.";
        return patched with
        {
            Outcome = UpdateCheckOutcome.NoSuitableAssets,
            UpdateAvailable = false,
            ErrorMessage = string.IsNullOrWhiteSpace(patched.ErrorMessage) ? noAssets : $"{noAssets} {patched.ErrorMessage}"
        };
    }

    private static UpdateCheckResult BuildResultFromReleaseRoot(
        JsonElement root,
        AppSemanticVersion latestSem,
        string latestLabel,
        AppSemanticVersion installed,
        string? ignoredVersionNormalized,
        int releasesArrayLength,
        string owner,
        string repo,
        string targetArchitecture)
    {
        var tag = root.TryGetProperty("tag_name", out var tagEl) && tagEl.ValueKind == JsonValueKind.String
            ? tagEl.GetString() ?? string.Empty
            : string.Empty;

        // Release notes must point at the exact official repo release page; a malformed or
        // off-repo html_url is replaced with the canonical URL built from trusted inputs.
        var notesUrl = root.TryGetProperty("html_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
            ? urlProp.GetString() ?? string.Empty
            : string.Empty;
        var canonicalNotesUrl =
            $"https://github.com/{owner}/{repo}/releases/tag/{Uri.EscapeDataString(tag)}";
        if (string.IsNullOrWhiteSpace(notesUrl) || !IsCanonicalReleaseNotesUrl(owner, repo, tag, notesUrl))
        {
            notesUrl = string.IsNullOrWhiteSpace(tag) ? string.Empty : canonicalNotesUrl;
        }

        SelectReleaseAssets(root, owner, repo, tag, targetArchitecture,
            out var zipName, out var zipUrl, out var zipSha, out var zipArchQualified,
            out var matchedPreferredZip,
            out var exeName, out var exeUrl, out var exeSha, out var exeArchQualified,
            out var checksumsUrl, out var checksumsSha,
            out var instructionsUrl,
            out var releaseJsonUrl, out var releaseJsonSha);

        var ignoredOk = ReleaseVersionParser.NormalizeIgnored(ignoredVersionNormalized);
        if (!string.IsNullOrEmpty(ignoredOk) &&
            string.Equals(latestLabel, ignoredOk, StringComparison.OrdinalIgnoreCase))
        {
            return AttachReleaseTelemetry(
                new UpdateCheckResult
                {
                    Succeeded = true,
                    Outcome = UpdateCheckOutcome.IgnoredVersion,
                    UpdateAvailable = false,
                    LatestVersion = latestSem.ToLegacyVersion(),
                    LatestVersionLabel = latestLabel,
                    ReleaseNotesUrl = notesUrl,
                    RecommendedZipAssetName = zipName,
                    RecommendedZipDownloadUrl = zipUrl,
                    RecommendedZipPatternMatched = matchedPreferredZip,
                    InstallerAssetName = exeName,
                    InstallerDownloadUrl = exeUrl,
                    ChecksumsDownloadUrl = checksumsUrl,
                    DownloadInstructionsUrl = instructionsUrl,
                    ExpectedZipSha256 = zipSha,
                    ExpectedInstallerSha256 = exeSha,
                    ExpectedChecksumsSha256 = checksumsSha,
                    ExpectedReleaseManifestSha256 = releaseJsonSha,
                    ReleaseManifestDownloadUrl = releaseJsonUrl,
                    SelectedArchitecture = targetArchitecture,
                    ZipArchQualified = zipArchQualified,
                    InstallerArchQualified = exeArchQualified,
                    ErrorMessage = UpdateCheckDisplay.FormatIgnoredVersion(latestLabel)
                },
                root,
                releasesArrayLength);
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

        return AttachReleaseTelemetry(
            new UpdateCheckResult
            {
                Succeeded = true,
                Outcome = outcome,
                UpdateAvailable = newer,
                LatestVersion = latestVersion,
                LatestVersionLabel = latestLabel,
                ReleaseNotesUrl = notesUrl,
                RecommendedZipAssetName = zipName,
                RecommendedZipDownloadUrl = zipUrl,
                RecommendedZipPatternMatched = matchedPreferredZip,
                InstallerAssetName = exeName,
                InstallerDownloadUrl = exeUrl,
                ChecksumsDownloadUrl = checksumsUrl,
                DownloadInstructionsUrl = instructionsUrl,
                ExpectedZipSha256 = zipSha,
                ExpectedInstallerSha256 = exeSha,
                ExpectedChecksumsSha256 = checksumsSha,
                ExpectedReleaseManifestSha256 = releaseJsonSha,
                ReleaseManifestDownloadUrl = releaseJsonUrl,
                SelectedArchitecture = targetArchitecture,
                ZipArchQualified = zipArchQualified,
                InstallerArchQualified = exeArchQualified,
                RecommendedZipAssetMissing = missingPreferredZip
            },
            root,
            releasesArrayLength);
    }

    private static readonly string[] ArchTokensX64 = { "win-x64", "x64", "amd64", "64bit" };
    private static readonly string[] ArchTokensArm64 = { "win-arm64", "arm64", "aarch64" };
    private static readonly string[] ArchTokensX86 = { "win-x86", "x86", "win32", "i386", "i686", "32bit" };

    /// <summary>True when the asset name carries an arch token that matches <paramref name="targetArch"/>.</summary>
    internal static bool IsArchQualified(string name, string targetArch)
    {
        var tokens = targetArch switch
        {
            "win-arm64" => ArchTokensArm64,
            "win-x86" => ArchTokensX86,
            _ => ArchTokensX64
        };
        return tokens.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the asset name carries an arch token for a DIFFERENT architecture.</summary>
    internal static bool IsWrongArchitecture(string name, string targetArch)
    {
        if (IsArchQualified(name, targetArch))
        {
            return false;
        }

        var all = ArchTokensX64.Concat(ArchTokensArm64).Concat(ArchTokensX86);
        return all.Any(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Trusted official asset URL: https, port 443, github.com host, exact
    /// /{owner}/{repo}/releases/download/{tag}/{filename} path, no credentials/query.
    /// </summary>
    internal static bool IsTrustedAssetUrl(
        string owner, string repo, string tag, string assetName, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || (uri.Port != 443 && uri.Port != -1)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedPath =
            $"/{owner}/{repo}/releases/download/{Uri.EscapeDataString(tag)}/{Uri.EscapeDataString(assetName)}";
        return string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal);
    }

    /// <summary>Exact official release page: https://github.com/{owner}/{repo}/releases/tag/{tag}, 443, no credentials/query/fragment.</summary>
    internal static bool IsCanonicalReleaseNotesUrl(string owner, string repo, string tag, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            || (uri.Port != 443 && uri.Port != -1)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedPath =
            $"/{owner}/{repo}/releases/tag/{Uri.EscapeDataString(tag)}";
        return string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal);
    }

    private static string? AssetSha256(JsonElement asset)
    {
        if (asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String)
        {
            var value = d.GetString();
            if (value is not null
                && value.StartsWith("sha256:", StringComparison.Ordinal)
                && Regex.IsMatch(value["sha256:".Length..], "^[0-9a-fA-F]{64}$"))
            {
                return value["sha256:".Length..].ToLowerInvariant();
            }
        }

        return null;
    }

    /// <summary>Selects ZIP (preferred patterns first) then a ForgerEMS .exe for Advanced. Sets <paramref name="matchedPreferredZip"/> when tier 1 or 2 matched. Untrusted-host and wrong-architecture assets are excluded.</summary>
    private static void SelectReleaseAssets(
        JsonElement root,
        string owner,
        string repo,
        string tag,
        string targetArchitecture,
        out string? zipName,
        out string? zipUrl,
        out string? zipSha,
        out bool zipArchQualified,
        out bool matchedPreferredZip,
        out string? exeName,
        out string? exeUrl,
        out string? exeSha,
        out bool exeArchQualified,
        out string? checksumsUrl,
        out string? checksumsSha,
        out string? instructionsUrl,
        out string? releaseJsonUrl,
        out string? releaseJsonSha)
    {
        zipName = zipUrl = zipSha = null;
        zipArchQualified = false;
        matchedPreferredZip = false;
        exeName = exeUrl = exeSha = null;
        exeArchQualified = false;
        checksumsUrl = checksumsSha = instructionsUrl = null;
        releaseJsonUrl = releaseJsonSha = null;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        string? tier1N = null, tier1U = null, tier1S = null;
        string? tier2N = null, tier2U = null, tier2S = null;
        string? tier3ForgerN = null, tier3ForgerU = null, tier3ForgerS = null;
        string? tier3AnyN = null, tier3AnyU = null, tier3AnyS = null;
        string? setupExeN = null, setupExeU = null, setupExeS = null;
        string? anyExeN = null, anyExeU = null, anyExeS = null;
        var tier1Arch = false; var tier2Arch = false; var tier3ForgerArch = false; var tier3AnyArch = false;
        var setupExeArch = false; var anyExeArch = false;

        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !asset.TryGetProperty("name", out var nameProp)
                || !asset.TryGetProperty("browser_download_url", out var dlProp)
                || nameProp.ValueKind != JsonValueKind.String
                || dlProp.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var name = nameProp.GetString() ?? string.Empty;
            var url = dlProp.GetString() ?? string.Empty;

            // Only official same-release download URLs are trusted; random hosts are skipped.
            if (!IsTrustedAssetUrl(owner, repo, tag, name, url))
            {
                continue;
            }

            var sha = AssetSha256(asset);

            if (name.Equals("CHECKSUMS.sha256", StringComparison.OrdinalIgnoreCase))
            {
                checksumsUrl = url;
                checksumsSha = sha;
                continue;
            }

            if (name.Equals("release.json", StringComparison.OrdinalIgnoreCase))
            {
                releaseJsonUrl = url;
                releaseJsonSha = sha;
                continue;
            }

            if (name.Equals("DOWNLOAD_BETA.txt", StringComparison.OrdinalIgnoreCase))
            {
                instructionsUrl = url;
                continue;
            }

            // Wrong-arch assets are never selectable.
            if (IsWrongArchitecture(name, targetArchitecture))
            {
                continue;
            }

            var archQualified = IsArchQualified(name, targetArchitecture);

            if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                if (name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("Beta", StringComparison.OrdinalIgnoreCase))
                {
                    if (tier1U is null || (!tier1Arch && archQualified))
                    {
                        tier1N = name; tier1U = url; tier1S = sha; tier1Arch = archQualified;
                    }
                }
                else if (ForgerEmsVersionZip.IsMatch(name))
                {
                    if (tier2U is null || (!tier2Arch && archQualified))
                    {
                        tier2N = name; tier2U = url; tier2S = sha; tier2Arch = archQualified;
                    }
                }
                else if (name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase))
                {
                    if (tier3ForgerU is null || (!tier3ForgerArch && archQualified))
                    {
                        tier3ForgerN = name; tier3ForgerU = url; tier3ForgerS = sha; tier3ForgerArch = archQualified;
                    }
                }
                else
                {
                    if (tier3AnyU is null || (!tier3AnyArch && archQualified))
                    {
                        tier3AnyN = name; tier3AnyU = url; tier3AnyS = sha; tier3AnyArch = archQualified;
                    }
                }
            }

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                !name.Contains("ForgerEMS", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase))
            {
                if (setupExeU is null || (!setupExeArch && archQualified))
                {
                    setupExeN = name; setupExeU = url; setupExeS = sha; setupExeArch = archQualified;
                }
            }
            else
            {
                if (anyExeU is null || (!anyExeArch && archQualified))
                {
                    anyExeN = name; anyExeU = url; anyExeS = sha; anyExeArch = archQualified;
                }
            }
        }

        if (tier1U is not null)
        {
            zipName = tier1N; zipUrl = tier1U; zipSha = tier1S; zipArchQualified = tier1Arch;
            matchedPreferredZip = true;
        }
        else if (tier2U is not null)
        {
            zipName = tier2N; zipUrl = tier2U; zipSha = tier2S; zipArchQualified = tier2Arch;
            matchedPreferredZip = true;
        }
        else if (tier3ForgerU is not null)
        {
            zipName = tier3ForgerN; zipUrl = tier3ForgerU; zipSha = tier3ForgerS; zipArchQualified = tier3ForgerArch;
            matchedPreferredZip = false;
        }
        else if (tier3AnyU is not null)
        {
            zipName = tier3AnyN; zipUrl = tier3AnyU; zipSha = tier3AnyS; zipArchQualified = tier3AnyArch;
            matchedPreferredZip = false;
        }

        if (setupExeU is not null)
        {
            exeName = setupExeN; exeUrl = setupExeU; exeSha = setupExeS; exeArchQualified = setupExeArch;
        }
        else if (anyExeU is not null)
        {
            exeName = anyExeN; exeUrl = anyExeU; exeSha = anyExeS; exeArchQualified = anyExeArch;
        }
    }

    /// <summary>
    /// When the selected ZIP/EXE names carry no architecture token, the same-release
    /// release.json must prove runtime+version before they are offered. If the manifest is
    /// absent, untrusted, or mismatched, unqualified assets are dropped; a newer release with
    /// no remaining trusted assets reports NoSuitableAssets instead of UpdateAvailable.
    /// </summary>
    private async Task<UpdateCheckResult> ValidateReleaseManifestAssetsAsync(
        UpdateCheckResult result,
        JsonElement releasesRoot,
        CancellationToken cancellationToken)
    {
        if (!result.Succeeded || result.Outcome != UpdateCheckOutcome.UpdateAvailable)
        {
            return result;
        }

        // Fill missing expected digests from the same-release CHECKSUMS.sha256 exact entries.
        result = await TryFillExpectedHashesFromChecksumsAsync(result, cancellationToken)
            .ConfigureAwait(false);

        var zipNeedsManifest = !string.IsNullOrWhiteSpace(result.RecommendedZipDownloadUrl)
            && !result.ZipArchQualified;
        var exeNeedsManifest = !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl)
            && !result.InstallerArchQualified;
        if (!zipNeedsManifest && !exeNeedsManifest)
        {
            return result;
        }

        var manifestOk = await TryValidateReleaseJsonAsync(
            result, releasesRoot, cancellationToken).ConfigureAwait(false);

        var zip = result.RecommendedZipDownloadUrl;
        var exe = result.InstallerDownloadUrl;
        var zipName = result.RecommendedZipAssetName;
        var exeName = result.InstallerAssetName;
        var zipSha = result.ExpectedZipSha256;
        var exeSha = result.ExpectedInstallerSha256;

        if (zipNeedsManifest && !manifestOk)
        {
            zip = null; zipName = null; zipSha = null;
        }

        if (exeNeedsManifest && !manifestOk)
        {
            exe = null; exeName = null; exeSha = null;
        }

        return result with
        {
            RecommendedZipDownloadUrl = zip,
            RecommendedZipAssetName = zipName,
            ExpectedZipSha256 = zipSha,
            InstallerDownloadUrl = exe,
            InstallerAssetName = exeName,
            ExpectedInstallerSha256 = exeSha,
            AssetsValidatedViaReleaseManifest = manifestOk && (zipNeedsManifest || exeNeedsManifest)
        };
    }

    /// <summary>
    /// When a selected asset lacks a GitHub API digest, fetch the same-release CHECKSUMS.sha256
    /// (already trust-validated URL) and bind the exact-entry SHA-256. The request carries no
    /// Authorization header — tokens are never forwarded to artifact hosts.
    /// </summary>
    private async Task<UpdateCheckResult> TryFillExpectedHashesFromChecksumsAsync(
        UpdateCheckResult result,
        CancellationToken cancellationToken)
    {
        var needsZip = !string.IsNullOrWhiteSpace(result.RecommendedZipDownloadUrl)
            && string.IsNullOrWhiteSpace(result.ExpectedZipSha256)
            && !string.IsNullOrWhiteSpace(result.RecommendedZipAssetName);
        var needsExe = !string.IsNullOrWhiteSpace(result.InstallerDownloadUrl)
            && string.IsNullOrWhiteSpace(result.ExpectedInstallerSha256)
            && !string.IsNullOrWhiteSpace(result.InstallerAssetName);
        if (!(needsZip || needsExe) || string.IsNullOrWhiteSpace(result.ChecksumsDownloadUrl))
        {
            return result;
        }

        var timeoutSeconds = ForgerEmsEnvironmentConfiguration.UpdateTimeoutSeconds;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds <= 0 ? 25 : timeoutSeconds));

        try
        {
            var fetched = await _metadataClient.FetchAsync(
                new ForgerEMS.Wpf.Services.Resources.OfficialMetadataClient.MetadataRequest
                {
                    Uri = new Uri(result.ChecksumsDownloadUrl, UriKind.Absolute),
                    IsGitHubArtifact = true,
                    MaxBytes = ForgerEMS.Wpf.Services.Resources.OfficialMetadataClient.DefaultChecksumMaxBytes,
                    MaxAttempts = 2,
                    UserAgent = ResolveUpdateUserAgent()
                },
                deadline.Token).ConfigureAwait(false);

            // When the release's own API digest for CHECKSUMS exists, the bytes must match it
            // before any entry is trusted.
            if (!string.IsNullOrWhiteSpace(result.ExpectedChecksumsSha256))
            {
                var actual = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(fetched.Body)).ToLowerInvariant();
                if (!string.Equals(actual, result.ExpectedChecksumsSha256, StringComparison.Ordinal))
                {
                    return result;
                }
            }

            var entries = ForgerEMS.Wpf.Services.Resources.ChecksumParser.Parse(fetched.BodyAsString());
            if (entries is null)
            {
                return result;
            }

            string? zipSha = result.ExpectedZipSha256;
            string? exeSha = result.ExpectedInstallerSha256;
            if (needsZip)
            {
                zipSha = ForgerEMS.Wpf.Services.Resources.ChecksumParser
                    .FindExact(entries, result.RecommendedZipAssetName!) ?? zipSha;
            }

            if (needsExe)
            {
                exeSha = ForgerEMS.Wpf.Services.Resources.ChecksumParser
                    .FindExact(entries, result.InstallerAssetName!) ?? exeSha;
            }

            return result with
            {
                ExpectedZipSha256 = zipSha,
                ExpectedInstallerSha256 = exeSha
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return result;
        }
    }

    /// <summary>
    /// Fetch and verify the same-release release.json: digest must match the GitHub asset
    /// digest and runtime+version must match the selected release. The request carries no
    /// Authorization header — tokens are never forwarded to artifact hosts.
    /// </summary>
    private async Task<bool> TryValidateReleaseJsonAsync(
        UpdateCheckResult result,
        JsonElement releasesRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.ReleaseManifestDownloadUrl))
        {
            return false;
        }

        var timeoutSeconds = ForgerEmsEnvironmentConfiguration.UpdateTimeoutSeconds;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds <= 0 ? 25 : timeoutSeconds));

        try
        {
            var fetched = await _metadataClient.FetchAsync(
                new ForgerEMS.Wpf.Services.Resources.OfficialMetadataClient.MetadataRequest
                {
                    Uri = new Uri(result.ReleaseManifestDownloadUrl, UriKind.Absolute),
                    IsGitHubArtifact = true,
                    MaxBytes = ForgerEMS.Wpf.Services.Resources.OfficialMetadataClient.DefaultChecksumMaxBytes,
                    MaxAttempts = 2,
                    UserAgent = ResolveUpdateUserAgent()
                },
                deadline.Token).ConfigureAwait(false);

            var body = fetched.Body;

            // If a GitHub asset digest exists, the manifest bytes must match it.
            if (!string.IsNullOrWhiteSpace(result.ExpectedReleaseManifestSha256))
            {
                var actual = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant();
                if (!string.Equals(actual, result.ExpectedReleaseManifestSha256, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var runtime = root.TryGetProperty("runtime", out var rt) && rt.ValueKind == JsonValueKind.String
                ? rt.GetString()
                : null;
            var version = root.TryGetProperty("version", out var ve) && ve.ValueKind == JsonValueKind.String
                ? ve.GetString()
                : null;

            if (!string.Equals(runtime, result.SelectedArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(version)
                || !string.Equals(version, result.LatestVersionLabel, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
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
                    ? "GitHub API rate limit reached (temporary). Installed version is unchanged. Try again in a few minutes from Settings → App updates."
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
                ErrorMessage = "GitHub API rate limit reached (temporary). Installed version is unchanged. Try again in a few minutes from Settings → App updates.",
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
        _metadataClient.Dispose();
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static string SanitizeGitHubPathSegment(string? value, string fallback)
    {
        var s = (value ?? string.Empty).Trim();
        if (s.Length == 0 ||
            s.Contains('/', StringComparison.Ordinal) ||
            s.Contains('\\', StringComparison.Ordinal) ||
            s.Contains('?', StringComparison.Ordinal) ||
            s.Contains('#', StringComparison.Ordinal))
        {
            return fallback;
        }

        return s;
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

    /// <summary>Parses version from GitHub <c>tag_name</c> first, then release <c>name</c> (e.g. &quot;ForgerEMS v1.2.0&quot;). <paramref name="normalizedLabel"/> is the normalized form of whichever source supplied the version.</summary>
    public static bool TryParseFromGitHubRelease(
        string? tagName,
        string? releaseName,
        out AppSemanticVersion sem,
        out string normalizedLabel)
    {
        if (AppSemanticVersion.TryParse(tagName, out sem))
        {
            normalizedLabel = NormalizeLabel(tagName);
            return true;
        }

        var n = NormalizeReleaseTitleForVersion(releaseName);
        if (!string.IsNullOrWhiteSpace(n) && AppSemanticVersion.TryParse(n, out sem))
        {
            normalizedLabel = NormalizeLabel(n);
            return true;
        }

        normalizedLabel = string.Empty;
        return false;
    }

    /// <summary>Parses version from GitHub <c>tag_name</c> first, then release <c>name</c>.</summary>
    public static bool TryParseFromGitHubRelease(
        string? tagName,
        string? releaseName,
        out AppSemanticVersion sem)
        => TryParseFromGitHubRelease(tagName, releaseName, out sem, out _);

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
