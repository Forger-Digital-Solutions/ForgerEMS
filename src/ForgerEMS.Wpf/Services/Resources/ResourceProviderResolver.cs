using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// Policy-driven metadata resolution for each provider kind. Produces
/// <see cref="ResourceResolution"/> instances; performs no downloads of artifacts.
/// Every provider failure maps to a typed per-descriptor state — never aborts the batch.
/// </summary>
public sealed class ResourceProviderResolver
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private readonly OfficialMetadataClient _transport;
    private readonly int _maxAttempts;
    private readonly string? _gitHubToken;

    public ResourceProviderResolver(OfficialMetadataClient transport, int maxAttempts = 2, string? gitHubToken = null)
    {
        _transport = transport;
        _maxAttempts = Math.Max(1, maxAttempts);
        _gitHubToken = gitHubToken;
    }

    public async Task<ResourceResolution> ResolveAsync(
        ResourceDescriptor descriptor, CancellationToken cancellationToken)
    {
        try
        {
            return descriptor.Provider switch
            {
                ResourceProviderKind.GitHubStable =>
                    await ResolveGitHubStableAsync(descriptor, cancellationToken).ConfigureAwait(false),
                ResourceProviderKind.ChecksumIndex =>
                    await ResolveChecksumIndexAsync(descriptor, cancellationToken).ConfigureAwait(false),
                ResourceProviderKind.UbuntuLts =>
                    await ResolveUbuntuLtsAsync(descriptor, cancellationToken).ConfigureAwait(false),
                ResourceProviderKind.OfficialPage =>
                    ResolveOfficialPage(descriptor),
                _ => Fail(descriptor, ResourceResolutionState.Unsupported,
                    ResourceFailureCategory.PolicyUnsupported, $"Unknown provider '{descriptor.Provider}'.")
            };
        }
        catch (OperationCanceledException)
        {
            throw; // timeout/cancellation semantics are owned by the caller
        }
        catch (Exception ex)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid,
                $"Provider failed for '{descriptor.ResourceId}': {ex.Message}");
        }
    }

    // ---------- github-stable ----------

    private async Task<ResourceResolution> ResolveGitHubStableAsync(
        ResourceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var apiUri = new Uri($"https://api.github.com/repos/{descriptor.Repository}/releases/latest");
        var result = await FetchAsync(descriptor, new OfficialMetadataClient.MetadataRequest
        {
            Uri = apiUri,
            IsGitHubApi = true,
            GitHubToken = _gitHubToken,
            MaxBytes = OfficialMetadataClient.DefaultMetadataMaxBytes,
            MaxAttempts = _maxAttempts,
            AllowedHosts = descriptor.AllowedHosts
        }, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.UpstreamUnavailable, "GitHub release metadata could not be fetched.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(result.Body);
        }
        catch (JsonException)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid, "GitHub release metadata was not valid JSON.");
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                    ResourceFailureCategory.MetadataInvalid, "GitHub release metadata was empty or malformed.");
            }

            if (!TryGetBool(root, "draft", out var draft)
                || !TryGetBool(root, "prerelease", out var pre))
            {
                return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                    ResourceFailureCategory.MetadataInvalid,
                    "GitHub release metadata is missing well-formed draft/prerelease flags.");
            }

            if (draft || pre)
            {
                return Fail(descriptor, ResourceResolutionState.RequiresUserAction,
                    ResourceFailureCategory.MetadataInvalid,
                    "Latest GitHub release is a draft or prerelease; stable channel requires manual review.");
            }

            var tag = GetStr(root, "tag_name");
            var name = GetStr(root, "name");
            if (!ReleaseVersionParser.TryParseFromGitHubRelease(tag, name, out var sem, out var versionLabel)
                || sem.Prerelease is not null)
            {
                return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                    ResourceFailureCategory.MetadataInvalid,
                    "Latest GitHub release does not carry an unambiguous stable semantic version.");
            }

            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            {
                return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                    ResourceFailureCategory.MetadataInvalid, "GitHub release carries no asset list.");
            }

            var pattern = descriptor.AssetPattern;
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return Fail(descriptor, ResourceResolutionState.Unsupported,
                    ResourceFailureCategory.PolicyUnsupported, "No anchored asset pattern in policy.");
            }

            Regex assetRegex;
            try
            {
                assetRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException)
            {
                return Fail(descriptor, ResourceResolutionState.Unsupported,
                    ResourceFailureCategory.PolicyUnsupported, $"Asset pattern '{pattern}' is not a valid regex.");
            }

            var matches = new List<(string Name, string Url, string? Digest, long Size, DateTimeOffset? Published)>();
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var assetName = GetStr(asset, "name");
                var url = GetStr(asset, "browser_download_url");
                if (assetName is null || url is null || !assetRegex.IsMatch(assetName))
                {
                    continue;
                }

                string? digest = null;
                var digestRaw = GetStr(asset, "digest");
                if (digestRaw is not null && digestRaw.StartsWith("sha256:", StringComparison.Ordinal))
                {
                    var hex = digestRaw["sha256:".Length..];
                    if (ResourcePolicyValues.IsHexSha256(hex))
                    {
                        digest = hex.ToLowerInvariant();
                    }
                }

                matches.Add((
                    assetName,
                    url,
                    digest,
                    TryGetInt64(asset, "size", out var size) ? size : 0L,
                    TryGetDateTimeOffset(asset, "published_at", out var pub) ? pub : null));
            }

            if (matches.Count == 0)
            {
                return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                    ResourceFailureCategory.MetadataInvalid,
                    $"No release asset matched '{pattern}' for architecture '{descriptor.Architecture}'.");
            }

            if (matches.Count > 1)
            {
                return Fail(descriptor, ResourceResolutionState.RequiresUserAction,
                    ResourceFailureCategory.AmbiguousSelection,
                    $"{matches.Count} release assets matched '{pattern}'; refusing last-wins selection.");
            }

            var selected = matches[0];
            var rawTag = tag ?? string.Empty;
            if (!IsTrustedGitHubAssetUrl(descriptor.Repository!, rawTag, selected.Name, selected.Url, out var urlError))
            {
                return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                    ResourceFailureCategory.MetadataInvalid,
                    $"Asset URL failed trust validation: {urlError}");
            }

            var publishedAt = selected.Published
                ?? (TryGetDateTimeOffset(root, "published_at", out var relPub) ? relPub : null);

            if (string.IsNullOrWhiteSpace(selected.Digest))
            {
                return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                    ResourceFailureCategory.NoExpectedHash,
                    "Selected release asset has no sha256 asset digest; hash-bound metadata required.");
            }

            return new ResourceResolution
            {
                Descriptor = descriptor,
                State = ResourceResolutionState.ResolvedMetadata,
                Version = versionLabel,
                ArtifactUri = selected.Url,
                ArtifactFileName = selected.Name,
                ExpectedSha256 = selected.Digest,
                SizeBytes = selected.Size > 0 ? selected.Size : null,
                PublishedAtUtc = publishedAt
            };
        }
    }

    /// <summary>
    /// Asset URL must be exactly https://github.com/{owner}/{repo}/releases/download/{tag}/{filename}
    /// on port 443 with no credentials or query string.
    /// </summary>
    internal static bool IsTrustedGitHubAssetUrl(
        string repository,
        string rawTag,
        string assetName,
        string url,
        out string? error)
    {
        error = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "not an absolute URI";
            return false;
        }

        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            error = "non-HTTPS scheme";
            return false;
        }

        if (uri.Port != 443 && uri.Port != -1)
        {
            error = "non-443 port";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query))
        {
            error = "credentials or query string present";
            return false;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            error = $"host '{uri.Host}' is not github.com";
            return false;
        }

        var expectedPath =
            $"/{repository}/releases/download/{Uri.EscapeDataString(rawTag)}/{Uri.EscapeDataString(assetName)}";
        if (!string.Equals(uri.AbsolutePath, expectedPath, StringComparison.Ordinal))
        {
            error = $"path '{uri.AbsolutePath}' does not match '{expectedPath}'";
            return false;
        }

        if (OfficialMetadataClient.ContainsPathTraversal(uri))
        {
            error = "path contains traversal";
            return false;
        }

        return true;
    }

    // ---------- checksum-index ----------

    private async Task<ResourceResolution> ResolveChecksumIndexAsync(
        ResourceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var allowedHosts = BuildAllowedHosts(descriptor);
        var checksumUri = new Uri(descriptor.SourceUri);

        var result = await FetchAsync(descriptor, new OfficialMetadataClient.MetadataRequest
        {
            Uri = checksumUri,
            AllowedHosts = allowedHosts,
            MaxBytes = OfficialMetadataClient.DefaultChecksumMaxBytes,
            MaxAttempts = _maxAttempts
        }, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.UpstreamUnavailable, "Checksum index could not be fetched.");
        }

        // PuTTY-style "w64/" subdirectory entries are permitted only when the policy
        // assetPattern itself anchors a subdirectory.
        var allowSubdirs = (descriptor.AssetPattern ?? string.Empty).Contains('/');
        var entries = ChecksumParser.Parse(result.BodyAsString(), allowSubdirs);
        if (entries is null || entries.Count == 0)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid,
                "Checksum index failed strict GNU/BSD parsing (malformed or conflicting entries).");
        }

        var pattern = descriptor.AssetPattern;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Fail(descriptor, ResourceResolutionState.Unsupported,
                ResourceFailureCategory.PolicyUnsupported, "No anchored asset pattern in policy.");
        }

        Regex assetRegex;
        try
        {
            assetRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (ArgumentException)
        {
            return Fail(descriptor, ResourceResolutionState.Unsupported,
                ResourceFailureCategory.PolicyUnsupported, $"Asset pattern '{pattern}' is not a valid regex.");
        }

        var candidates = entries.Keys
            .Where(name => assetRegex.IsMatch(name))
            .Select(name => (Name: name, Version: ExtractHighestVersion(name)))
            .OrderByDescending(c => c.Version)
            .ToList();

        if (candidates.Count == 0)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid,
                $"Checksum index has no entry matching '{pattern}'.");
        }

        var top = candidates[0];
        if (candidates.Count > 1 && Equals(candidates[0].Version, candidates[1].Version))
        {
            return Fail(descriptor, ResourceResolutionState.RequiresUserAction,
                ResourceFailureCategory.AmbiguousSelection,
                $"Multiple files at version {top.Version} matched '{pattern}'; refusing first-wins selection.");
        }

        var hash = ChecksumParser.FindExact(entries, top.Name);
        if (hash is null)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.NoExpectedHash, "Matched file has no SHA-256 entry.");
        }

        // Artifact URI: resolve relative to the redirected checksum document (redirect target
        // hosts are already allow-listed), or relative to the policy baseUrl host.
        var artifactBase = descriptor.BaseUrl is not null
            && Uri.TryCreate(descriptor.BaseUrl, UriKind.Absolute, out var baseUri)
            && allowedHosts.Contains(baseUri.Host)
            ? baseUri
            : result.FinalUri;
        var artifactUri = new Uri(artifactBase, top.Name);

        if (!allowedHosts.Contains(artifactUri.Host))
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid,
                $"Resolved artifact host '{artifactUri.Host}' is not an allowed host.");
        }

        return new ResourceResolution
        {
            Descriptor = descriptor,
            State = ResourceResolutionState.ResolvedMetadata,
            Version = top.Version?.ToString(),
            ArtifactUri = artifactUri.AbsoluteUri,
            ArtifactFileName = Path.GetFileName(top.Name),
            ExpectedSha256 = hash
        };
    }

    // ---------- ubuntu-lts ----------

    private async Task<ResourceResolution> ResolveUbuntuLtsAsync(
        ResourceDescriptor descriptor, CancellationToken cancellationToken)
    {
        var metaUri = new Uri(descriptor.SourceUri);
        var result = await FetchAsync(descriptor, new OfficialMetadataClient.MetadataRequest
        {
            Uri = metaUri,
            AllowedHosts = descriptor.AllowedHosts,
            MaxBytes = OfficialMetadataClient.DefaultMetadataMaxBytes,
            MaxAttempts = _maxAttempts
        }, cancellationToken).ConfigureAwait(false);

        if (result is null)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.UpstreamUnavailable, "Ubuntu meta-release index could not be fetched.");
        }

        var stanzas = ParseUbuntuStanzas(result.BodyAsString());
        var supportedLts = stanzas
            .Where(s => s.Supported
                && s.Dist is not null
                && s.Version is not null
                && Regex.IsMatch(s.Dist, "^[a-z]+$", RegexOptions.None, RegexTimeout))
            .OrderByDescending(s => s.ParsedVersion)
            .ToList();

        if (supportedLts.Count == 0)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid,
                "No supported LTS stanza was found in the official meta-release index.");
        }

        var best = supportedLts[0];
        var sumsUri = new Uri($"https://releases.ubuntu.com/{best.Dist}/SHA256SUMS");
        var sums = await FetchAsync(descriptor, new OfficialMetadataClient.MetadataRequest
        {
            Uri = sumsUri,
            AllowedHosts = new[] { "releases.ubuntu.com" },
            MaxBytes = OfficialMetadataClient.DefaultChecksumMaxBytes,
            MaxAttempts = _maxAttempts
        }, cancellationToken).ConfigureAwait(false);

        if (sums is null)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.UpstreamUnavailable,
                $"SHA256SUMS for Ubuntu {best.Dist} could not be fetched.");
        }

        var entries = ChecksumParser.Parse(sums.BodyAsString());
        if (entries is null)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid, "Ubuntu SHA256SUMS failed strict parsing.");
        }

        var pattern = descriptor.AssetPattern;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return Fail(descriptor, ResourceResolutionState.Unsupported,
                ResourceFailureCategory.PolicyUnsupported, "No anchored asset pattern in policy.");
        }

        var assetRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        var match = entries.Keys.Where(k => assetRegex.IsMatch(k)).ToList();
        if (match.Count != 1)
        {
            return Fail(descriptor, ResourceResolutionState.UnableToVerify,
                ResourceFailureCategory.MetadataInvalid,
                $"Ubuntu SHA256SUMS has {match.Count} entries matching '{pattern}'.");
        }

        return new ResourceResolution
        {
            Descriptor = descriptor,
            State = ResourceResolutionState.ResolvedMetadata,
            Version = best.Version,
            ArtifactUri = new Uri(sumsUri, match[0]).AbsoluteUri,
            ArtifactFileName = match[0],
            ExpectedSha256 = entries[match[0]]
        };
    }

    private sealed record UbuntuStanza(string? Dist, string? Version, bool Supported, Version ParsedVersion);

    private static IReadOnlyList<UbuntuStanza> ParseUbuntuStanzas(string document)
    {
        var stanzas = new List<UbuntuStanza>();
        string? dist = null, version = null;
        var supported = false;

        void Flush()
        {
            if (version is not null && dist is not null
                && System.Version.TryParse(version.Split(' ')[0], out var parsed))
            {
                stanzas.Add(new UbuntuStanza(dist, version.Split(' ')[0], supported, parsed));
            }

            dist = version = null;
            supported = false;
        }

        foreach (var raw in document.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            switch (key)
            {
                case "Dist": dist = value; break;
                case "Version": version = value; break;
                case "Supported": supported = value == "1"; break;
            }
        }

        Flush();
        return stanzas;
    }

    // ---------- official-page ----------

    private static ResourceResolution ResolveOfficialPage(ResourceDescriptor descriptor) =>
        Fail(descriptor, ResourceResolutionState.RequiresUserAction,
            ResourceFailureCategory.PolicyUnsupported,
            descriptor.ExceptionReason
            ?? "Official vendor page requires manual verification; no automated integrity/applicability claim.");

    // ---------- helpers ----------

    private async Task<OfficialMetadataClient.FetchResult?> FetchAsync(
        ResourceDescriptor descriptor,
        OfficialMetadataClient.MetadataRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _transport.FetchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return null; // mapped to UnableToVerify by the caller
        }
    }

    private static IReadOnlyList<string> BuildAllowedHosts(ResourceDescriptor descriptor)
    {
        var hosts = new List<string>(descriptor.AllowedHosts);
        if (Uri.TryCreate(descriptor.SourceUri, UriKind.Absolute, out var source))
        {
            hosts.Add(source.Host);
        }

        if (descriptor.BaseUrl is not null && Uri.TryCreate(descriptor.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            hosts.Add(baseUri.Host);
        }

        return hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Version? ExtractHighestVersion(string fileName)
    {
        var match = Regex.Match(fileName, @"(\d+(?:\.\d+)+)", RegexOptions.None, RegexTimeout);
        return match.Success && System.Version.TryParse(match.Groups[1].Value, out var v) ? v : new Version(0, 0);
    }

    // ---- defensive JSON readers: wrong-kind values never throw ----

    private static string? GetStr(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v)
        && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static bool TryGetBool(JsonElement obj, string name, out bool value)
    {
        value = false;
        if (obj.ValueKind != JsonValueKind.Object
            || !obj.TryGetProperty(name, out var v))
        {
            return false;
        }

        if (v.ValueKind == JsonValueKind.True) { value = true; return true; }
        if (v.ValueKind == JsonValueKind.False) { value = false; return true; }
        return false;
    }

    private static bool TryGetInt64(JsonElement obj, string name, out long value)
    {
        value = 0;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt64(out value);
    }

    private static bool TryGetDateTimeOffset(JsonElement obj, string name, out DateTimeOffset value)
    {
        value = default;
        return obj.ValueKind == JsonValueKind.Object
            && obj.TryGetProperty(name, out var v)
            && v.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(v.GetString(), out value);
    }

    private static ResourceResolution Fail(
        ResourceDescriptor descriptor,
        ResourceResolutionState state,
        ResourceFailureCategory category,
        string reason) =>
        new()
        {
            Descriptor = descriptor,
            State = state,
            FailureCategory = category,
            Reason = reason
        };
}
