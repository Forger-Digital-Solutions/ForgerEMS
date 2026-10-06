using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// Safe archive extraction: fresh GUID-owned directory, traversal/rooted-path/symlink rejection,
/// case-insensitive duplicate path rejection, Windows reserved-name and ADS rejection, and
/// actual-streamed inflated-size enforcement (declared entry lengths are not trusted).
/// </summary>
public static class SafeZipExtractor
{
    public const long DefaultMaxInflatedBytes = 4L * 1024 * 1024 * 1024;

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public sealed record ExtractionResult(
        bool Succeeded,
        string? OutputDirectory,
        string? FailureReason);

    /// <summary>Extract into a fresh GUID-owned directory under <paramref name="parentDir"/>.</summary>
    public static async Task<ExtractionResult> ExtractToFreshDirectoryAsync(
        string archivePath,
        string parentDir,
        long maxInflatedBytes = DefaultMaxInflatedBytes,
        CancellationToken cancellationToken = default)
    {
        var outputDir = Path.Combine(parentDir, "extract-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(outputDir);
            await ExtractIntoAsync(archivePath, outputDir, maxInflatedBytes, cancellationToken)
                .ConfigureAwait(false);
            return new ExtractionResult(true, outputDir, null);
        }
        catch (Exception ex)
        {
            // Only the fresh GUID-owned directory is ever deleted here.
            TryDelete(outputDir);
            return new ExtractionResult(false, null, ex.Message);
        }
    }

    private static async Task ExtractIntoAsync(
        string archivePath,
        string outputDir,
        long maxInflatedBytes,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(outputDir);

        // The extraction root and every ancestor must be a real directory — an existing
        // reparse point anywhere in the chain would redirect writes outside the root.
        ThrowIfReparseInChain(root);

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long inflatedTotal = 0;

        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = entry.FullName;
            if (IsSymbolicLink(entry))
            {
                throw new InvalidDataException($"Archive entry '{name}' is a symbolic link; refusing extraction.");
            }

            if (string.IsNullOrWhiteSpace(name) || name.EndsWith('/'))
            {
                continue;
            }

            ValidateEntryName(name);

            var normalized = name.Replace('\\', '/');
            var destination = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Archive entry '{name}' resolves outside the extraction root.");
            }

            if (!seenPaths.Add(destination))
            {
                throw new InvalidDataException($"Duplicate archive entry '{name}' (case-insensitive collision).");
            }

            var parent = Path.GetDirectoryName(destination);
            if (parent is not null)
            {
                Directory.CreateDirectory(parent);
            }

            // Count ACTUAL inflated bytes while copying — a hostile entry can lie about its
            // declared Length. checked arithmetic guards total overflow; the stream cap guards
            // actual size and the mismatch check catches corrupt/hostile headers.
            await using var source = entry.Open();
            await using var target = new FileStream(
                destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            var buffer = new byte[1024 * 1024];
            long entryBytes = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                       .ConfigureAwait(false)) > 0)
            {
                entryBytes += read;
                if (entryBytes > entry.Length)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{name}' decompressed past its declared length.");
                }
                inflatedTotal = checked(inflatedTotal + read);
                if (inflatedTotal > maxInflatedBytes)
                {
                    throw new InvalidDataException(
                        $"Archive inflated size exceeds the {maxInflatedBytes} byte limit.");
                }
                await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            }

            if (entryBytes != entry.Length)
            {
                throw new InvalidDataException(
                    $"Archive entry '{name}' produced {entryBytes} bytes but declared {entry.Length}.");
            }
        }
    }

    /// <summary>Reject traversal, rooted/UNC paths, ADS, reserved names, trailing dot/space.</summary>
    private static void ValidateEntryName(string name)
    {
        if (name.Contains(':') || Path.IsPathRooted(name) || name.StartsWith('/'))
        {
            throw new InvalidDataException($"Archive entry '{name}' uses a rooted or stream path.");
        }

        var normalized = name.Replace('\\', '/');
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment == "." || segment == "..")
            {
                throw new InvalidDataException($"Archive entry '{name}' contains a traversal segment.");
            }
            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                throw new InvalidDataException($"Archive entry '{name}' has a trailing dot/space segment.");
            }
            var stem = segment.Split('.')[0];
            if (stem.Length > 0 && ReservedDeviceNames.Contains(stem))
            {
                throw new InvalidDataException($"Archive entry '{name}' uses a reserved Windows device name.");
            }
        }
    }

    /// <summary>Walk the path from root upward; reject if any existing ancestor is a reparse point.</summary>
    private static void ThrowIfReparseInChain(string root)
    {
        var current = root;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(current))
            {
                var info = new DirectoryInfo(current);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException($"Extraction root '{current}' is a reparse point; refusing extraction.");
                }
            }
            var parent = Path.GetDirectoryName(current);
            if (parent is null || parent == current)
            {
                break;
            }
            current = parent;
        }
    }

    /// <summary>Detect Unix symlink entries via external attributes (mode 0120000).</summary>
    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int symlinkFileType = 0xA000;
        var mode = (int)((entry.ExternalAttributes >> 16) & 0xFFFF);
        return (mode & unixFileTypeMask) == symlinkFileType;
    }

    private static void TryDelete(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // best-effort cleanup; GUID-owned dir cannot collide with real content
        }
    }
}
