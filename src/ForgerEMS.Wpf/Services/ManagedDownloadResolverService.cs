using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Models;

namespace ForgerEMS.Wpf.Services;

public interface IManagedDownloadResolverService
{
    Task<ResolvedManifestOverlay> ResolveAsync(
        BackendContext backendContext,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default);

    Task<string> ResolveAndSaveAsync(
        BackendContext backendContext,
        string outputPath,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default);
}

public sealed record ResolvedItem(string Name, string Url, string Sha256, string Source, string ResolvedVersion);

public sealed record ResolvedManifestOverlay(
    string SourceManifestPath,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ResolvedItem> Items);

internal sealed class ResolvedOverlayDocument
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string? SourceManifestPath { get; set; }
    public List<ResolvedItemEntry> Items { get; set; } = new();
}

internal sealed class ResolvedItemEntry
{
    public string? Name { get; set; }
    public string? Url { get; set; }
    public string? Sha256 { get; set; }
    public string? Source { get; set; }
    public string? ResolvedVersion { get; set; }
}

public sealed class ManagedDownloadResolverService : IManagedDownloadResolverService
{
    private static readonly Regex VersionPattern = new(@"\d+(?:\.\d+)+", RegexOptions.Compiled);
    private static readonly Regex HrefPattern = new(@"href\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly TimeSpan ResolutionTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _httpClient;

    public ManagedDownloadResolverService(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<ResolvedManifestOverlay> ResolveAsync(
        BackendContext backendContext,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var manifestPath = ResolveManifestPath(backendContext);
        if (!File.Exists(manifestPath))
        {
            onOutput?.Invoke(MakeLog($"Manifest not found at {manifestPath}; producing empty resolved overlay.", LogSeverity.Warning));
            return new ResolvedManifestOverlay(manifestPath, DateTimeOffset.UtcNow, Array.Empty<ResolvedItem>());
        }

        await using var stream = File.OpenRead(manifestPath);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var items = doc.RootElement.GetProperty("items");

        var resolved = new System.Collections.Generic.List<ResolvedItem>();
        foreach (var item in items.EnumerateArray())
        {
            var strategy = GetString(item, "resolveStrategy");
            if (string.IsNullOrWhiteSpace(strategy) ||
                string.Equals(strategy, "pinned", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var name = GetString(item, "name");
            var hintsProp = item.TryGetProperty("resolveHints", out var h) ? h : default;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(ResolutionTimeout);
                var resolvedItem = strategy.ToLowerInvariant() switch
                {
                    "github-latest" => await ResolveGitHubLatestAsync(name, hintsProp, cts.Token).ConfigureAwait(false),
                    "sourceforge-project" => await ResolveSourceForgeLatestAsync(name, hintsProp, cts.Token).ConfigureAwait(false),
                    "directory-scan" => await ResolveDirectoryScanAsync(name, hintsProp, cts.Token).ConfigureAwait(false),
                    _ => null
                };

                if (resolvedItem is not null)
                {
                    resolved.Add(resolvedItem);
                    onOutput?.Invoke(MakeLog($"Resolved {name}: {resolvedItem.ResolvedVersion} from {resolvedItem.Source}", LogSeverity.Success));
                }
                else
                {
                    onOutput?.Invoke(MakeLog($"Resolver returned no result for {name} (strategy={strategy})", LogSeverity.Warning));
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                onOutput?.Invoke(MakeLog($"Failed to resolve {name} (strategy={strategy}): {ex.Message}", LogSeverity.Warning));
            }
        }

        return new ResolvedManifestOverlay(manifestPath, DateTimeOffset.UtcNow, resolved);
    }

    public async Task<string> ResolveAndSaveAsync(
        BackendContext backendContext,
        string outputPath,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var overlay = await ResolveAsync(backendContext, onOutput, cancellationToken).ConfigureAwait(false);

        var serializable = new ResolvedOverlayDocument
        {
            GeneratedAtUtc = overlay.GeneratedAtUtc,
            SourceManifestPath = overlay.SourceManifestPath,
            Items = overlay.Items.Select(i => new ResolvedItemEntry
            {
                Name = i.Name,
                Url = i.Url,
                Sha256 = i.Sha256,
                Source = i.Source,
                ResolvedVersion = i.ResolvedVersion
            }).ToList()
        };

        var json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
        onOutput?.Invoke(MakeLog($"Wrote resolved overlay with {serializable.Items.Count} items to {outputPath}", LogSeverity.Info));
        return outputPath;
    }

    private async Task<ResolvedItem?> ResolveGitHubLatestAsync(
        string name, JsonElement hints, CancellationToken cancellationToken)
    {
        var repo = GetString(hints, "repo");
        if (string.IsNullOrWhiteSpace(repo)) return null;

        var assetPattern = GetString(hints, "assetPattern") ?? ".*";

        var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{repo}/releases/latest");
        request.Headers.TryAddWithoutValidation("User-Agent", "ForgerEMS-Resolver/1.0");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        var assetRegex = new Regex(assetPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        string? downloadUrl = null;
        string? fileName = null;
        string? releaseTag = GetString(root, "tag_name");
        string? checksumUrl = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var assetName = GetString(asset, "name");
            var url = GetString(asset, "browser_download_url");
            if (string.IsNullOrWhiteSpace(assetName) || string.IsNullOrWhiteSpace(url)) continue;

            if (assetRegex.IsMatch(assetName))
            {
                fileName = assetName;
                downloadUrl = url;
            }
            else if (assetName.Contains("sha256", StringComparison.OrdinalIgnoreCase) ||
                     assetName.Contains("checksum", StringComparison.OrdinalIgnoreCase))
            {
                checksumUrl ??= url;
            }
        }

        if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(fileName)) return null;

        var sha = await TryReadGitHubSha256Async(fileName, checksumUrl, root, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(sha)) return null;

        var version = ExtractVersion(releaseTag ?? string.Empty, fileName);
        return new ResolvedItem(name, downloadUrl, sha, "github-latest", version ?? string.Empty);
    }

    private async Task<ResolvedItem?> ResolveSourceForgeLatestAsync(
        string name, JsonElement hints, CancellationToken cancellationToken)
    {
        var project = GetString(hints, "project");
        var filePattern = GetString(hints, "filePattern") ?? ".*";
        if (string.IsNullOrWhiteSpace(project)) return null;

        var bestReleaseUrl = $"https://sourceforge.net/projects/{project}/best_release.json";
        var request = new HttpRequestMessage(HttpMethod.Get, bestReleaseUrl);
        request.Headers.TryAddWithoutValidation("User-Agent", "ForgerEMS-Resolver/1.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;

        var fileName = GetString(root, "filename") ?? GetString(root, "file_name");
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        var fileRegex = new Regex(filePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
        if (!fileRegex.IsMatch(fileName)) return null;

        var downloadUrl = GetString(root, "url") ?? GetString(root, "download_url");
        if (string.IsNullOrWhiteSpace(downloadUrl)) return null;

        var sha = GetString(root, "sha256");
        if (string.IsNullOrWhiteSpace(sha)) sha = GetString(root, "sha1");

        var version = ExtractVersion(fileName);
        return new ResolvedItem(name, downloadUrl, sha ?? string.Empty, "sourceforge-project", version ?? string.Empty);
    }

    private async Task<ResolvedItem?> ResolveDirectoryScanAsync(
        string name, JsonElement hints, CancellationToken cancellationToken)
    {
        var indexUrl = GetString(hints, "indexUrl");
        var versionPattern = GetString(hints, "versionPattern") ?? @"(\d+(?:\.\d+)+)";
        var filePattern = GetString(hints, "filePattern");
        var checksumUrlTemplate = GetString(hints, "checksumUrlTemplate");
        if (string.IsNullOrWhiteSpace(indexUrl) || string.IsNullOrWhiteSpace(filePattern)) return null;

        using var response = await _httpClient.GetAsync(indexUrl, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) return null;

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var baseUrl = indexUrl.EndsWith("/") ? indexUrl : indexUrl.Substring(0, indexUrl.LastIndexOf('/') + 1);

        var versionRegex = new Regex(versionPattern, RegexOptions.Compiled);
        var fileRegex = new Regex(filePattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var bestVersion = string.Empty;
        var bestUrl = string.Empty;

        foreach (Match hrefMatch in HrefPattern.Matches(body))
        {
            var href = hrefMatch.Groups[1].Value;
            if (href.StartsWith("?", StringComparison.Ordinal) ||
                href.StartsWith("#", StringComparison.Ordinal) ||
                href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!fileRegex.IsMatch(href)) continue;

            var versionMatch = versionRegex.Match(href);
            if (!versionMatch.Success) continue;
            var candidateVersion = versionMatch.Groups[1].Value;

            if (!Version.TryParse(candidateVersion, out var candidateVer)) continue;
            if (Version.TryParse(bestVersion, out var bestVer) && candidateVer <= bestVer)
                continue;

            bestVersion = candidateVersion;
            bestUrl = ResolveAbsoluteUrl(baseUrl, href);
        }

        if (string.IsNullOrWhiteSpace(bestUrl)) return null;

        var sha = string.Empty;
        if (!string.IsNullOrWhiteSpace(checksumUrlTemplate))
        {
            var checksumUrl = checksumUrlTemplate.Replace("$version", bestVersion).Replace("$url", bestUrl);
            var targetFile = ExtractFileName(bestUrl);
            sha = await TryFetchChecksumForFileAsync(checksumUrl, targetFile, cancellationToken).ConfigureAwait(false);
        }

        return new ResolvedItem(name, bestUrl, sha, "directory-scan", bestVersion);
    }

    private async Task<string?> TryReadGitHubSha256Async(
        string fileName, string? checksumUrl, JsonElement releaseRoot, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(checksumUrl))
        {
            try
            {
                var checksumBody = await _httpClient.GetStringAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
                var parsed = ParseSha256FromText(checksumBody, fileName);
                if (!string.IsNullOrWhiteSpace(parsed)) return parsed;
            }
            catch { }
        }

        var body = GetString(releaseRoot, "body");
        return ParseSha256FromText(body, fileName);
    }

    private async Task<string?> TryFetchChecksumForFileAsync(string checksumUrl, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            var body = await _httpClient.GetStringAsync(checksumUrl, cancellationToken).ConfigureAwait(false);
            return ParseSha256FromText(body, fileName);
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseSha256FromText(string? text, string fileName)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.Length < 64) continue;

            var match = Regex.Match(trimmed, @"([0-9a-fA-F]{64})");
            if (!match.Success) continue;

            var afterHash = trimmed.Substring(match.Index + match.Length).Trim();
            var beforeHash = trimmed[..match.Index].Trim();

            if (string.IsNullOrEmpty(afterHash) ||
                afterHash.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
                afterHash.StartsWith("*") ||
                afterHash.Equals("sha256", StringComparison.OrdinalIgnoreCase) ||
                beforeHash.Contains(fileName, StringComparison.OrdinalIgnoreCase) ||
                beforeHash.EndsWith("SHA256", StringComparison.OrdinalIgnoreCase))
            {
                return match.Groups[1].Value.ToLowerInvariant();
            }
        }

        if (lines.Length == 1)
        {
            var solo = Regex.Match(text.Trim(), @"^([0-9a-fA-F]{64})\s*$");
            if (solo.Success) return solo.Groups[1].Value.ToLowerInvariant();
        }

        return null;
    }

    private static string ResolveAbsoluteUrl(string baseUrl, string href)
    {
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return href;

        try
        {
            var baseUri = new Uri(baseUrl);
            return new Uri(baseUri, href).ToString();
        }
        catch
        {
            var sep = baseUrl.EndsWith("/") ? "" : "/";
            return baseUrl + sep + href.TrimStart('/', '\\');
        }
    }

    private static string ExtractFileName(string url)
    {
        try
        {
            var queryIndex = url.IndexOf('?');
            var path = queryIndex > 0 ? url[..queryIndex] : url;
            var slashIndex = path.LastIndexOf('/');
            return slashIndex >= 0 ? path[(slashIndex + 1)..] : path;
        }
        catch
        {
            return url;
        }
    }

    private static string? ExtractVersion(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            var match = VersionPattern.Match(candidate);
            if (match.Success) return match.Value;
        }
        return null;
    }

    private static LogLine MakeLog(string text, LogSeverity severity) =>
        new(DateTimeOffset.UtcNow, text, severity, false, LiveLogChannel.Update);

    private static string ResolveManifestPath(BackendContext backendContext)
    {
        var candidates = new[]
        {
            backendContext.RepoManifestPath,
            backendContext.PrimaryManifestPath,
            Path.Combine(backendContext.RootPath, "manifests", "ForgerEMS.updates.json"),
            Path.Combine(backendContext.RootPath, "ForgerEMS.updates.json")
        };
        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object) return string.Empty;
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
            return string.Empty;
        return prop.GetString() ?? string.Empty;
    }
}
