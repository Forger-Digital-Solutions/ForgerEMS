using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.Resources;
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

    /// <summary>
    /// Check resource policy descriptors and return typed resolutions. Default implementation
    /// preserves existing test stubs that only implement the resolve facade.
    /// </summary>
    Task<IReadOnlyList<ResourceResolution>> CheckResourcesAsync(
        BackendContext backendContext,
        ResourceCheckRequest? request = null,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ResourceResolution>>(Array.Empty<ResourceResolution>());
}

public sealed record ResolvedItem(
    string Name,
    string Url,
    string Sha256,
    string Source,
    string ResolvedVersion)
{
    public string ResourceId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public IReadOnlyList<string> AllowedHosts { get; init; } = Array.Empty<string>();
    public DateTimeOffset? CheckedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed record ResolvedManifestOverlay(
    string SourceManifestPath,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<ResolvedItem> Items)
{
    public string ManifestSha256 { get; init; } = string.Empty;
}

internal sealed class ResolvedOverlayDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public string? SourceManifestPath { get; set; }
    public string? ManifestSha256 { get; set; }
    public string? PolicySource { get; set; }
    public List<ResolvedItemEntry> Items { get; set; } = new();
}

internal sealed class ResolvedItemEntry
{
    public string? Name { get; set; }
    public string? ResourceId { get; set; }
    public string? Url { get; set; }
    public string? Sha256 { get; set; }
    public string? Source { get; set; }
    public string? ResolvedVersion { get; set; }
    public string? FileName { get; set; }
    public List<string>? AllowedHosts { get; set; }
    public DateTimeOffset? CheckedAtUtc { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
}

/// <summary>
/// Resolves manifest items that carry <c>requiresResolution</c> through the lead-authored
/// resource policy. Only fresh, verified, SHA-256-bound resolutions are overlaid; stale,
/// unverified, unsupported, or manual-only results never produce download URLs.
/// </summary>
public sealed class ManagedDownloadResolverService : IManagedDownloadResolverService
{
    private readonly ResourceCheckService? _injectedCheckService;

    public ManagedDownloadResolverService(HttpClient httpClient)
    {
        _ = httpClient; // transport is owned by OfficialMetadataClient now; ctor kept for callers
    }

    public ManagedDownloadResolverService(ResourceCheckService checkService)
    {
        _injectedCheckService = checkService;
    }

    public async Task<IReadOnlyList<ResourceResolution>> CheckResourcesAsync(
        BackendContext backendContext,
        ResourceCheckRequest? request = null,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var checkService = _injectedCheckService ?? TryCreateCheckService(backendContext, onOutput);
        if (checkService is null)
        {
            return Array.Empty<ResourceResolution>();
        }

        return await checkService.CheckAsync(request, cancellationToken).ConfigureAwait(false);
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

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        var items = doc.RootElement.GetProperty("items");

        var dynamicByResourceId = new Dictionary<string, string>(StringComparer.Ordinal);
        var nameByResourceId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in items.EnumerateArray())
        {
            var requires = item.TryGetProperty("requiresResolution", out var rr) && rr.GetBoolean();
            var resourceId = GetString(item, "resourceId");
            var name = GetString(item, "name");
            if (requires && !string.IsNullOrWhiteSpace(resourceId))
            {
                dynamicByResourceId[resourceId] = name;
                nameByResourceId[resourceId] = name;
            }
        }

        if (dynamicByResourceId.Count == 0)
        {
            return new ResolvedManifestOverlay(manifestPath, DateTimeOffset.UtcNow, Array.Empty<ResolvedItem>())
            {
                ManifestSha256 = ComputeSha256(manifestPath)
            };
        }

        var checkService = _injectedCheckService ?? TryCreateCheckService(backendContext, onOutput);
        if (checkService is null)
        {
            onOutput?.Invoke(MakeLog("Resource policy catalog is unavailable; no overlay items emitted.", LogSeverity.Warning));
            return new ResolvedManifestOverlay(manifestPath, DateTimeOffset.UtcNow, Array.Empty<ResolvedItem>())
            {
                ManifestSha256 = ComputeSha256(manifestPath)
            };
        }

        var resolutions = await checkService.CheckAsync(
            new ResourceCheckRequest { ResourceIds = dynamicByResourceId.Keys.ToList() },
            cancellationToken).ConfigureAwait(false);

        var resolved = new List<ResolvedItem>();
        foreach (var resolution in resolutions)
        {
            if (!resolution.IsDownloadEligible)
            {
                onOutput?.Invoke(MakeLog(
                    $"Resolver skipped {resolution.ResourceId}: {resolution.State} — {resolution.Reason}",
                    resolution.State == ResourceResolutionState.RequiresUserAction ? LogSeverity.Info : LogSeverity.Warning));
                continue;
            }

            var name = dynamicByResourceId.TryGetValue(resolution.ResourceId, out var mapped) ? mapped : resolution.ResourceId;
            // Overlay entries carry only this descriptor's trusted hosts — never a global union.
            var hosts = OverlayAllowedHostsFor(resolution.Descriptor);

            resolved.Add(new ResolvedItem(
                name,
                resolution.ArtifactUri!,
                resolution.ExpectedSha256!,
                resolution.Descriptor.Provider.ToString(),
                resolution.Version ?? string.Empty)
            {
                ResourceId = resolution.ResourceId,
                FileName = resolution.ArtifactFileName ?? string.Empty,
                AllowedHosts = hosts.ToList(),
                CheckedAtUtc = resolution.CheckedAtUtc,
                ExpiresAtUtc = resolution.ExpiresAtUtc
            });
            onOutput?.Invoke(MakeLog(
                $"Resolved {name} ({resolution.ResourceId}): {resolution.Version} via {resolution.Descriptor.Provider}",
                LogSeverity.Success));
        }

        return new ResolvedManifestOverlay(manifestPath, DateTimeOffset.UtcNow, resolved)
        {
            ManifestSha256 = ComputeSha256(manifestPath)
        };
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
            SchemaVersion = 1,
            GeneratedAtUtc = overlay.GeneratedAtUtc,
            SourceManifestPath = overlay.SourceManifestPath,
            ManifestSha256 = overlay.ManifestSha256,
            PolicySource = "manifests/resource-policy.json",
            Items = overlay.Items.Select(i => new ResolvedItemEntry
            {
                Name = i.Name,
                ResourceId = i.ResourceId,
                Url = i.Url,
                Sha256 = i.Sha256,
                Source = i.Source,
                ResolvedVersion = i.ResolvedVersion,
                FileName = i.FileName,
                AllowedHosts = i.AllowedHosts.ToList(),
                CheckedAtUtc = i.CheckedAtUtc,
                ExpiresAtUtc = i.ExpiresAtUtc
            }).ToList()
        };

        var json = JsonSerializer.Serialize(serializable, new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Atomic write: temp file then rename.
        var tmp = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
        File.Move(tmp, outputPath, overwrite: true);
        onOutput?.Invoke(MakeLog($"Wrote resolved overlay with {serializable.Items.Count} items to {outputPath}", LogSeverity.Info));
        return outputPath;
    }

    private ResourceCheckService? TryCreateCheckService(BackendContext backendContext, Action<LogLine>? onOutput)
    {
        var policyPath = ResourceCatalog.FindPolicyPath(backendContext);
        if (policyPath is null)
        {
            onOutput?.Invoke(MakeLog("resource-policy.json not found beside manifests; resource checks unavailable.", LogSeverity.Warning));
            return null;
        }

        try
        {
            var catalog = ResourceCatalog.Load(policyPath);
            // Writable per-user cache — never write inside the installed backend root.
            return new ResourceCheckService(
                catalog,
                new ResourceProviderResolver(new OfficialMetadataClient(), catalog.MaximumAttempts),
                new ResourceMetadataCache(ResourceCheckService.DefaultCacheDirectory()));
        }
        catch (ResourceCatalogException ex)
        {
            onOutput?.Invoke(MakeLog($"Resource policy is invalid: {ex.Message}", LogSeverity.Error));
            return null;
        }
    }

    private static IReadOnlyList<string> OverlayAllowedHostsFor(ResourceDescriptor descriptor)
    {
        var hosts = new List<string>(descriptor.AllowedHosts);
        if (Uri.TryCreate(descriptor.SourceUri, UriKind.Absolute, out var source))
        {
            hosts.Add(source.Host);
        }

        if (descriptor.BaseUrl is not null
            && Uri.TryCreate(descriptor.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            hosts.Add(baseUri.Host);
        }

        if (descriptor.Provider == ResourceProviderKind.GitHubStable)
        {
            hosts.Add("github.com");
            hosts.Add("release-assets.githubusercontent.com");
            hosts.Add("objects.githubusercontent.com");
        }

        return hosts.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
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
