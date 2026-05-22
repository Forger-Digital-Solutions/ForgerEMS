using System;
using System.Collections.Generic;
using System.IO;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

public sealed class DriveValidationTempFileManager
{
    private readonly List<string> _createdPaths = [];

    public string TempRoot { get; private set; } = string.Empty;

    public void EnsureTempRoot(string volumeRoot)
    {
        var root = volumeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        TempRoot = Path.Combine(root, DriveValidationTargetSafety.TempFolderName);
        Directory.CreateDirectory(TempRoot);
    }

    public string GetSamplePath(DriveValidationSample sample) =>
        Path.Combine(TempRoot, sample.RelativePath);

    public void Track(string fullPath) => _createdPaths.Add(fullPath);

    /// <summary>
    /// Best-effort delete of leftover ForgerEMS Drive Validator temp files from a previous, crashed,
    /// or force-quit run. Only files matching <c>sample-*.bin</c> inside our owned temp folder are
    /// touched — never user data on the volume.
    /// </summary>
    public void CleanupOrphansBeforeRun()
    {
        if (string.IsNullOrWhiteSpace(TempRoot) || !Directory.Exists(TempRoot))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(TempRoot, "sample-*.bin", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Best-effort; leftover will surface in the per-run cleanup status if it stays.
                }
            }
        }
        catch
        {
            // Enumeration failure is non-fatal — the normal write loop will surface real I/O issues.
        }
    }

    public DriveValidationCleanupResult Cleanup()
    {
        var leftover = new List<string>();
        var deleted = 0;
        foreach (var path in _createdPaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    deleted++;
                }
            }
            catch
            {
                leftover.Add(path);
            }
        }

        try
        {
            if (Directory.Exists(TempRoot) && Directory.GetFileSystemEntries(TempRoot).Length == 0)
            {
                Directory.Delete(TempRoot);
            }
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(TempRoot))
            {
                leftover.Add(TempRoot);
            }
        }

        var status = leftover.Count == 0 ? "All temporary validation files removed." : "Some temporary files could not be removed.";
        return new DriveValidationCleanupResult(status, leftover, deleted);
    }
}

public sealed class DriveValidationCleanupResult
{
    public DriveValidationCleanupResult(string status, IReadOnlyList<string> leftover, int deletedCount)
    {
        Status = status;
        LeftoverPaths = leftover;
        DeletedCount = deletedCount;
    }

    public string Status { get; }

    public IReadOnlyList<string> LeftoverPaths { get; }

    public int DeletedCount { get; }
}
