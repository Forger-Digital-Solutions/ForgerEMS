using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        foreach (var candidatePath in GetCandidatePaths(backendContext))
        {
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(candidatePath, cancellationToken).ConfigureAwait(false);
            return new ManagedDownloadSummary
            {
                IsAvailable = true,
                SummaryPath = candidatePath,
                Text = text.Trim(),
                LastUpdatedUtc = File.GetLastWriteTimeUtc(candidatePath)
            };
        }

        return ManagedDownloadSummary.Missing("No managed-download summary has been generated yet. Run revalidation to create one, or use a release bundle that already includes history artifacts.");
    }

    private static IEnumerable<string> GetCandidatePaths(BackendContext backendContext)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(backendContext.PrimaryManagedSummaryPath))
        {
            paths.Add(backendContext.PrimaryManagedSummaryPath);
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
}
