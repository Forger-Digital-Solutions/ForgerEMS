using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// File-backed TTL cache for resource resolutions. Entries are keyed by a fingerprint of the
/// full trust descriptor (id/source/provider/repository/baseUrl/allowedHosts/channel/
/// architecture/pattern/schema), written atomically, and revalidated against the descriptor
/// before being trusted — cached metadata never manufactures source authenticity.
/// </summary>
public sealed class ResourceMetadataCache
{
    private const int CacheSchemaVersion = 1;
    private static readonly TimeSpan MaxClockSkew = TimeSpan.FromMinutes(5);

    private readonly string _cacheDirectory;

    public ResourceMetadataCache(string cacheDirectory)
    {
        _cacheDirectory = cacheDirectory;
    }

    public static string FingerprintFor(ResourceDescriptor descriptor)
    {
        var key = string.Join("|",
            CacheSchemaVersion,
            descriptor.ResourceId,
            descriptor.Provider.ToString(),
            descriptor.SourceUri,
            descriptor.Repository ?? string.Empty,
            descriptor.BaseUrl ?? string.Empty,
            string.Join(",", descriptor.AllowedHosts.OrderBy(h => h, StringComparer.OrdinalIgnoreCase)),
            descriptor.Channel,
            descriptor.Architecture,
            descriptor.Platform,
            descriptor.AssetPattern ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();
    }

    private string PathFor(string fingerprint) => Path.Combine(_cacheDirectory, fingerprint + ".json");

    /// <summary>
    /// Returns the cached resolution when present and coherent. <paramref name="isFresh"/> is
    /// true only inside the policy TTL and while the cached artifact URI still satisfies the
    /// descriptor's trust rules; expired or untrusted entries surface as CachedStale/invalid.
    /// </summary>
    public bool TryGet(
        ResourceDescriptor descriptor,
        TimeSpan ttl,
        out ResourceResolution? resolution,
        out bool isFresh)
    {
        resolution = null;
        isFresh = false;
        var path = PathFor(FingerprintFor(descriptor));
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var entry = JsonSerializer.Deserialize<CachedEntry>(json);
            if (entry is null || entry.State != ResourceResolutionState.ResolvedMetadata)
            {
                return false;
            }

            var now = DateTimeOffset.UtcNow;

            // Chronology: CheckedAtUtc must not be in the future (beyond skew); ExpiresAtUtc
            // must be bounded by CheckedAtUtc + TTL (a corrupted entry can't extend itself).
            if (entry.CheckedAtUtc > now + MaxClockSkew)
            {
                return false;
            }

            var effectiveExpiry = entry.ExpiresAtUtc;
            var maxExpiry = entry.CheckedAtUtc + ttl;
            if (effectiveExpiry > maxExpiry)
            {
                effectiveExpiry = maxExpiry;
            }

            // Trust: the cached artifact URI must still satisfy the descriptor's full
            // transport+trust rules (https/443, no credentials/fragment, allowed host,
            // own repository for GitHub releases, safe filename matching the policy
            // pattern) and the cached hash must be a strict SHA-256.
            if (!ResourcePolicyValues.IsHexSha256(entry.ExpectedSha256)
                || !Uri.TryCreate(entry.ArtifactUri, UriKind.Absolute, out var cachedUri)
                || !IsArtifactUriTrusted(descriptor, cachedUri))
            {
                return false;
            }

            isFresh = now < effectiveExpiry;
            resolution = new ResourceResolution
            {
                Descriptor = descriptor,
                State = isFresh
                    ? ResourceResolutionState.ResolvedMetadata
                    : ResourceResolutionState.CachedStale,
                Version = entry.Version,
                ArtifactUri = entry.ArtifactUri,
                ArtifactFileName = entry.ArtifactFileName,
                ExpectedSha256 = entry.ExpectedSha256,
                SizeBytes = entry.SizeBytes,
                PublishedAtUtc = entry.PublishedAtUtc,
                CheckedAtUtc = entry.CheckedAtUtc,
                ExpiresAtUtc = effectiveExpiry,
                // FromCache marks provenance only; it does not imply fresh verification.
                FromCache = true
            };
            return true;
        }
        catch
        {
            return false; // corrupt cache entries are simply absent
        }
    }

    /// <summary>
    /// Host trust for cached entries mirrors live resolution: descriptor allowedHosts plus
    /// the descriptor's own source/baseUrl hosts; GitHub stable providers may also use the
    /// three official GitHub artifact hosts.
    /// </summary>
    internal static bool IsHostTrusted(ResourceDescriptor descriptor, string host)
    {
        var allowed = descriptor.AllowedHosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Uri.TryCreate(descriptor.SourceUri, UriKind.Absolute, out var source))
        {
            allowed.Add(source.Host);
        }

        if (descriptor.BaseUrl is not null
            && Uri.TryCreate(descriptor.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            allowed.Add(baseUri.Host);
        }

        if (descriptor.Provider == ResourceProviderKind.GitHubStable)
        {
            allowed.Add("github.com");
            allowed.Add("release-assets.githubusercontent.com");
            allowed.Add("objects.githubusercontent.com");
        }

        return allowed.Contains(host);
    }

    /// <summary>
    /// Full URI trust validation for a cached (or resolved) artifact: https on port 443,
    /// no credentials or fragment, trusted host, GitHub-stable URLs must be release
    /// downloads on the descriptor's own repository, and the leaf filename must satisfy
    /// the anchored policy assetPattern when one exists.
    /// </summary>
    internal static bool IsArtifactUriTrusted(ResourceDescriptor descriptor, Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps
            || (uri.Port != 443 && uri.Port != -1)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !IsHostTrusted(descriptor, uri.Host))
        {
            return false;
        }

        if (descriptor.Provider == ResourceProviderKind.GitHubStable)
        {
            var repo = (descriptor.Repository ?? string.Empty).Trim('/');
            var expectedPrefix = "/" + repo + "/releases/download/";
            if (repo.Length == 0
                || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                || !uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return false;
            }
        }

        var leaf = Path.GetFileName(Uri.UnescapeDataString(uri.AbsolutePath));
        if (string.IsNullOrWhiteSpace(leaf)
            || leaf.Contains('/') || leaf.Contains('\\') || leaf.Contains(':')
            || leaf is "." or ".."
            || leaf.EndsWith('.') || leaf.EndsWith(' '))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(descriptor.AssetPattern))
        {
            return true;
        }

        // Patterns containing '/' are relative-path patterns anchored under the
        // descriptor's baseUrl (e.g. PuTTY '^w64/putty-...'). Match them against
        // the decoded path relative to that exact allowed root; plain leaf
        // patterns keep matching the filename only.
        var candidate = leaf;
        if (descriptor.AssetPattern.Contains('/'))
        {
            if (string.IsNullOrWhiteSpace(descriptor.BaseUrl)
                || !Uri.TryCreate(descriptor.BaseUrl, UriKind.Absolute, out var baseUri)
                || !uri.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal))
            {
                return false;
            }

            var relative = Uri.UnescapeDataString(uri.AbsolutePath[baseUri.AbsolutePath.Length..]);
            var segments = relative.Split('/');
            if (segments.Any(s => s.Length == 0
                    || s.Contains('\\') || s.Contains(':')
                    || s is "." or ".."
                    || s.EndsWith('.') || s.EndsWith(' ')))
            {
                return false;
            }

            candidate = relative;
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(
                candidate, descriptor.AssetPattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                TimeSpan.FromSeconds(2)))
        {
            return false;
        }

        return true;
    }

    public void Store(ResourceResolution resolution, TimeSpan ttl)
    {
        if (resolution.State != ResourceResolutionState.ResolvedMetadata)
        {
            return;
        }

        Directory.CreateDirectory(_cacheDirectory);
        var path = PathFor(FingerprintFor(resolution.Descriptor));
        var entry = new CachedEntry
        {
            Version = resolution.Version,
            ArtifactUri = resolution.ArtifactUri,
            ArtifactFileName = resolution.ArtifactFileName,
            ExpectedSha256 = resolution.ExpectedSha256,
            SizeBytes = resolution.SizeBytes,
            PublishedAtUtc = resolution.PublishedAtUtc,
            CheckedAtUtc = resolution.CheckedAtUtc,
            ExpiresAtUtc = resolution.CheckedAtUtc + ttl,
            State = resolution.State
        };

        var json = JsonSerializer.Serialize(entry);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    private sealed class CachedEntry
    {
        public string? Version { get; set; }
        public string? ArtifactUri { get; set; }
        public string? ArtifactFileName { get; set; }
        public string? ExpectedSha256 { get; set; }
        public long? SizeBytes { get; set; }
        public DateTimeOffset? PublishedAtUtc { get; set; }
        public DateTimeOffset CheckedAtUtc { get; set; }
        public DateTimeOffset ExpiresAtUtc { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public ResourceResolutionState State { get; set; }
    }
}
