using System.IO;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class UsbRootPolisher
{
    public static void Polish(string usbRoot)
    {
        if (string.IsNullOrWhiteSpace(usbRoot) || !Directory.Exists(usbRoot))
        {
            return;
        }

        EnsureInternalDirectories(usbRoot);
        MigrateLegacyRootMetadata(usbRoot);
        ConsolidateVisibleLogs(usbRoot);
        ConsolidateVisibleReports(usbRoot);
        ConsolidateSupportDocs(usbRoot);
        RemoveGeneratedRootClutter(usbRoot);
        RemoveEmptyForgerEmsWorkflowFolders(usbRoot);
    }

    private static void EnsureInternalDirectories(string usbRoot)
    {
        foreach (var relative in new[]
                 {
                     UsbInternalLayout.MetadataFolder,
                     UsbInternalLayout.RawLogsFolder,
                     UsbInternalLayout.RawReportsFolder,
                     UsbInternalLayout.SupportFolder,
                     UsbInternalLayout.CacheFolder
                 })
        {
            Directory.CreateDirectory(UsbInternalLayout.Combine(usbRoot, relative));
        }
    }

    private static void MigrateLegacyRootMetadata(string usbRoot)
    {
        foreach (var legacyPath in UsbInternalLayout.EnumerateLegacyRootMetadataFiles(usbRoot))
        {
            var fileName = Path.GetFileName(legacyPath);
            var destination = UsbInternalLayout.MetadataPath(usbRoot, fileName);
            MoveForgerEmsOwnedFile(legacyPath, destination);
        }
    }

    private static void ConsolidateVisibleLogs(string usbRoot)
    {
        var visibleLogs = UsbInternalLayout.Combine(usbRoot, "_logs");
        var internalLogs = UsbInternalLayout.RawLogsDirectory(usbRoot);
        Directory.CreateDirectory(internalLogs);

        if (!Directory.Exists(visibleLogs))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(visibleLogs))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MoveForgerEmsOwnedFile(file, Path.Combine(internalLogs, name));
        }

        ApplyRetention(internalLogs, UsbInternalLayout.RawLogRetentionCount);
    }

    private static void ConsolidateVisibleReports(string usbRoot)
    {
        var visibleReports = UsbInternalLayout.Combine(usbRoot, "_reports");
        var internalReports = UsbInternalLayout.RawReportsDirectory(usbRoot);
        Directory.CreateDirectory(internalReports);

        if (!Directory.Exists(visibleReports))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(visibleReports))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MoveForgerEmsOwnedFile(file, Path.Combine(internalReports, name));
        }

        ApplyRetention(internalReports, UsbInternalLayout.RawReportRetentionCount);
    }

    private static void ConsolidateSupportDocs(string usbRoot)
    {
        var docs = UsbInternalLayout.Combine(usbRoot, "_docs");
        var support = UsbInternalLayout.SupportDirectory(usbRoot);
        Directory.CreateDirectory(support);

        if (!Directory.Exists(docs))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(docs))
        {
            var name = Path.GetFileName(file);
            if (!UsbInternalLayout.IsSupportDocFileName(name))
            {
                continue;
            }

            MoveForgerEmsOwnedFile(file, Path.Combine(support, name));
        }
    }

    private static void RemoveGeneratedRootClutter(string usbRoot)
    {
        RemoveIfGeneratedReadme(UsbInternalLayout.Combine(usbRoot, "README.md"));
        RemoveIfRedirectOnlyStartHere(UsbInternalLayout.Combine(usbRoot, "START-HERE.html"));
        RemoveDuplicateDashboardCopy(Path.Combine(UsbInternalLayout.Combine(usbRoot, "_docs"), "forgerems-usb-dashboard.html"));
    }

    private static void RemoveEmptyForgerEmsWorkflowFolders(string usbRoot)
    {
        foreach (var relative in new[] { "_downloads", "_archive" })
        {
            var path = UsbInternalLayout.Combine(usbRoot, relative);
            if (!Directory.Exists(path))
            {
                continue;
            }

            if (!Directory.EnumerateFileSystemEntries(path).Any())
            {
                try
                {
                    Directory.Delete(path, recursive: false);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    private static void RemoveIfGeneratedReadme(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(path);
            if (text.Contains(UsbInternalLayout.ForgerEmsGeneratedReadmeMarker, StringComparison.Ordinal))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // never delete user files on read failure
        }
    }

    private static void RemoveIfRedirectOnlyStartHere(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(path);
            if (text.Contains("url=README.html", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("href=\"README.html\"", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static void RemoveDuplicateDashboardCopy(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // best effort
        }
    }

    private static void MoveForgerEmsOwnedFile(string sourcePath, string destinationPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        var destinationDirectory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        if (File.Exists(destinationPath))
        {
            try
            {
                File.Delete(sourcePath);
            }
            catch
            {
                // keep legacy duplicate if destination already holds the canonical copy
            }

            return;
        }

        try
        {
            File.Move(sourcePath, destinationPath);
        }
        catch
        {
            try
            {
                File.Copy(sourcePath, destinationPath, overwrite: false);
                File.Delete(sourcePath);
            }
            catch
            {
                // best effort migration
            }
        }
    }

    private static void ApplyRetention(string directory, int keepCount)
    {
        if (!Directory.Exists(directory) || keepCount <= 0)
        {
            return;
        }

        var files = Directory.EnumerateFiles(directory)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .ToList();

        foreach (var stale in files.Skip(keepCount))
        {
            try
            {
                stale.Delete();
            }
            catch
            {
                // best effort retention
            }
        }
    }
}
