using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IManagedDownloadSummaryService
{
    Task<ManagedDownloadSummary> TryLoadAsync(BackendContext backendContext, CancellationToken cancellationToken = default);
}

public sealed class ManagedDownloadSummaryService : IManagedDownloadSummaryService
{
    public async Task<ManagedDownloadSummary> TryLoadAsync(BackendContext backendContext, CancellationToken cancellationToken = default)
    {
        if (!backendContext.IsAvailable)
        {
            return ManagedDownloadSummary.Missing("Managed-download summary is unavailable until the backend is discovered.");
        }

        var manifestSnapshot = await TryBuildManifestSnapshotAsync(backendContext, cancellationToken).ConfigureAwait(false);
        var revalidationSnapshot = await TryLoadLatestRevalidationSnapshotAsync(backendContext, cancellationToken).ConfigureAwait(false);

        if (manifestSnapshot is not null && revalidationSnapshot is not null)
        {
            return new ManagedDownloadSummary
            {
                IsAvailable = true,
                SummaryPath = manifestSnapshot.Path,
                Text =
                    manifestSnapshot.Text.Trim() +
                    Environment.NewLine +
                    Environment.NewLine +
                    "Latest revalidation snapshot" +
                    Environment.NewLine +
                    "==========================" +
                    Environment.NewLine +
                    $"Source: {revalidationSnapshot.Path}" +
                    Environment.NewLine +
                    revalidationSnapshot.Text.Trim(),
                LastUpdatedUtc = Max(manifestSnapshot.LastUpdatedUtc, revalidationSnapshot.LastUpdatedUtc)
            };
        }

        if (manifestSnapshot is not null)
        {
            return new ManagedDownloadSummary
            {
                IsAvailable = true,
                SummaryPath = manifestSnapshot.Path,
                Text = manifestSnapshot.Text.Trim(),
                LastUpdatedUtc = manifestSnapshot.LastUpdatedUtc
            };
        }

        if (revalidationSnapshot is not null)
        {
            return new ManagedDownloadSummary
            {
                IsAvailable = true,
                SummaryPath = revalidationSnapshot.Path,
                Text = revalidationSnapshot.Text.Trim(),
                LastUpdatedUtc = revalidationSnapshot.LastUpdatedUtc
            };
        }

        return ManagedDownloadSummary.Missing("No managed-download summary has been generated yet. Run revalidation to create one, or use a release bundle that already includes history artifacts.");
    }

    private static async Task<SummarySource?> TryBuildManifestSnapshotAsync(BackendContext backendContext, CancellationToken cancellationToken)
    {
        foreach (var manifestPath in GetManifestCandidatePaths(backendContext))
        {
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false));
            if (!document.RootElement.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var managedItems = new List<ManifestItemSummary>();
            var placeholderItems = new List<ManifestItemSummary>();

            foreach (var itemElement in itemsElement.EnumerateArray())
            {
                if (!IsEnabled(itemElement))
                {
                    continue;
                }

                var name = GetString(itemElement, "name");
                var dest = GetString(itemElement, "dest");
                var type = GetString(itemElement, "type", "file");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dest))
                {
                    continue;
                }

                var summaryItem = new ManifestItemSummary(name, dest);
                if (string.Equals(type, "page", StringComparison.OrdinalIgnoreCase))
                {
                    placeholderItems.Add(summaryItem);
                }
                else
                {
                    managedItems.Add(summaryItem);
                }
            }

            var builder = new StringBuilder();
            builder.AppendLine("Managed-download manifest snapshot");
            builder.AppendLine("================================");
            builder.AppendLine(FormattableString.Invariant($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"));
            builder.AppendLine(FormattableString.Invariant($"Manifest: {manifestPath}"));
            builder.AppendLine(FormattableString.Invariant($"Managed auto-download items: {managedItems.Count}"));
            builder.AppendLine(FormattableString.Invariant($"Seeded placeholder/info shortcuts: {placeholderItems.Count}"));
            builder.AppendLine();
            builder.AppendLine("Managed categories");
            builder.AppendLine("------------------");

            foreach (var category in managedItems
                         .GroupBy(item => GetCategoryKey(item.Destination), StringComparer.OrdinalIgnoreCase)
                         .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine(FormattableString.Invariant($"- {category.Key}: {string.Join("; ", category.Select(item => item.Name))}"));
            }

            builder.AppendLine();
            builder.AppendLine("State meanings");
            builder.AppendLine("--------------");
            builder.AppendLine("- managed autodownload item -> manifest file entry downloaded by Setup USB / Update USB");
            builder.AppendLine("- seeded placeholder shortcut -> manifest page entry written even when no payload is downloaded");
            builder.AppendLine("- verified downloaded payload -> file written and checksum-verified during a managed run");
            builder.AppendLine("- fallback shortcut -> seeded or newly written shortcut retained when a managed download fails");

            return new SummarySource(
                manifestPath,
                builder.ToString().Trim(),
                File.GetLastWriteTimeUtc(manifestPath));
        }

        return null;
    }

    private static async Task<SummarySource?> TryLoadLatestRevalidationSnapshotAsync(BackendContext backendContext, CancellationToken cancellationToken)
    {
        foreach (var candidatePath in GetCandidatePaths(backendContext))
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(candidatePath, cancellationToken).ConfigureAwait(false);
            return new SummarySource(candidatePath, text.Trim(), File.GetLastWriteTimeUtc(candidatePath));
        }

        return null;
    }

    private static IEnumerable<string> GetManifestCandidatePaths(BackendContext backendContext)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in new[]
                 {
                     backendContext.PrimaryManifestPath,
                     backendContext.RepoManifestPath,
                     Path.Combine(backendContext.WorkingDirectory, "ForgerEMS.updates.json"),
                     Path.Combine(backendContext.WorkingDirectory, "manifests", "ForgerEMS.updates.json")
                 })
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> GetCandidatePaths(BackendContext backendContext)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(BackendContext.PrimaryManagedSummaryPath))
        {
            paths.Add(BackendContext.PrimaryManagedSummaryPath);
        }

        if (Directory.Exists(backendContext.ReleaseVerificationHistoryRoot))
        {
            var historySummaries = Directory
                .GetFiles(backendContext.ReleaseVerificationHistoryRoot, "managed-download-summary.txt", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc);

            paths.AddRange(historySummaries);
        }

        foreach (var path in paths)
        {
            if (seen.Add(path))
            {
                yield return path;
            }
        }
    }

    private static bool IsEnabled(JsonElement itemElement)
    {
        if (!itemElement.TryGetProperty("enabled", out var enabledProperty))
        {
            return true;
        }

        return enabledProperty.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => true
        };
    }

    private static string GetString(JsonElement element, string propertyName, string defaultValue = "")
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.GetString() ?? defaultValue
            : defaultValue;
    }

    private static string GetCategoryKey(string destination)
    {
        var normalized = destination.Replace('/', '\\');
        var directory = Path.GetDirectoryName(normalized);
        return string.IsNullOrWhiteSpace(directory) ? "(root)" : directory;
    }

    private static DateTimeOffset? Max(DateTimeOffset? left, DateTimeOffset? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        return left >= right ? left : right;
    }

    private sealed record SummarySource(string Path, string Text, DateTimeOffset? LastUpdatedUtc);

    private sealed record ManifestItemSummary(string Name, string Destination);
}
