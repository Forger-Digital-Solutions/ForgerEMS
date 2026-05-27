using System.IO;

namespace VentoyToolkitSetup.Wpf.Services;

public static class UsbBuilderProfileMediaScanner
{
    public const int DefaultMaxFilesPerCategory = 5000;
    public const long DefaultMaxBytesPerCategory = 64L * 1024 * 1024 * 1024;

    public static async Task<IReadOnlyDictionary<string, UsbBuilderProfileMediaScanResult>> ScanAsync(
        string usbRoot,
        IEnumerable<string> categoryIds,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<string, UsbBuilderProfileMediaScanResult>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(usbRoot) || !Directory.Exists(usbRoot))
        {
            foreach (var categoryId in categoryIds)
            {
                results[categoryId] = new UsbBuilderProfileMediaScanResult
                {
                    CategoryId = categoryId,
                    State = UsbBuilderProfileMediaScanState.PathMissing,
                    Note = "USB root not available"
                };
            }

            return results;
        }

        foreach (var categoryId in categoryIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!UsbBuilderProfileCatalog.TryGet(categoryId, out var definition))
            {
                continue;
            }

            results[categoryId] = await Task.Run(
                () => ScanCategory(usbRoot, definition.CategoryId, definition.MediaScanRelativePaths, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    private static UsbBuilderProfileMediaScanResult ScanCategory(
        string usbRoot,
        string categoryId,
        IReadOnlyList<string> relativePaths,
        CancellationToken cancellationToken)
    {
        var fileCount = 0;
        long totalBytes = 0;
        var anyPath = false;
        var truncated = false;

        foreach (var relative in relativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fullPath = Path.Combine(usbRoot, relative);
            if (!Directory.Exists(fullPath))
            {
                continue;
            }

            anyPath = true;
            try
            {
                foreach (var file in EnumerateFilesSafe(fullPath))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (fileCount >= DefaultMaxFilesPerCategory || totalBytes >= DefaultMaxBytesPerCategory)
                    {
                        truncated = true;
                        break;
                    }

                    try
                    {
                        var info = new FileInfo(file);
                        if (!info.Exists)
                        {
                            continue;
                        }

                        totalBytes += info.Length;
                        fileCount++;
                    }
                    catch
                    {
                        // skip unreadable files
                    }
                }
            }
            catch
            {
                truncated = true;
            }

            if (truncated)
            {
                break;
            }
        }

        if (!anyPath)
        {
            return new UsbBuilderProfileMediaScanResult
            {
                CategoryId = categoryId,
                State = UsbBuilderProfileMediaScanState.PathMissing,
                Note = "Expected folders not present yet"
            };
        }

        return new UsbBuilderProfileMediaScanResult
        {
            CategoryId = categoryId,
            State = truncated ? UsbBuilderProfileMediaScanState.Skipped : UsbBuilderProfileMediaScanState.Completed,
            FileCount = fileCount,
            TotalBytes = totalBytes,
            Note = truncated ? "Scan limited for responsiveness" : null
        };
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        var depth = 0;
        const int maxDepth = 6;

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            string[] dirs;
            try
            {
                files = Directory.GetFiles(current);
                dirs = depth < maxDepth ? Directory.GetDirectories(current) : Array.Empty<string>();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var dir in dirs)
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "_archive", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                pending.Push(dir);
            }

            depth++;
        }
    }
}
