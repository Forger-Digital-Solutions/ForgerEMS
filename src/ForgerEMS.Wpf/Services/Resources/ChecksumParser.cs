using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ForgerEMS.Wpf.Services.Resources;

/// <summary>
/// Strict SHA-256 checksum-document parser. Accepts GNU ("<64hex>  &lt;path&gt;" or
/// "&lt;64hex&gt; *&lt;path&gt;") and BSD ("SHA256 (path) = &lt;64hex&gt;") formats only.
/// Filenames are matched exactly and case-sensitively after normalization; substring or
/// wildcard matching is never performed.
/// </summary>
public static class ChecksumParser
{
    // GNU coreutils: "hash  name" (text) or "hash *name" (binary marker)
    private static readonly Regex GnuLine = new(
        @"^(?<hash>[0-9a-fA-F]{64})[ ](?:[ *])(?<file>\S.*)$",
        RegexOptions.Compiled);

    // BSD: "SHA256 (name) = hash"
    private static readonly Regex BsdLine = new(
        @"^SHA256 \((?<file>[^()]*)\) = (?<hash>[0-9a-fA-F]{64})\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Parse all entries into a filename → sha256 map.
    /// Returns null when any non-empty, non-comment line is malformed or when two entries
    /// disagree about the same filename.
    /// </summary>
    public static IReadOnlyDictionary<string, string>? Parse(
        string document,
        bool allowSubdirectoryEntries = false)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in document.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
            {
                continue;
            }

            string? file;
            string? hash;
            var gnu = GnuLine.Match(line);
            var bsd = BsdLine.Match(line);
            if (gnu.Success)
            {
                hash = gnu.Groups["hash"].Value.ToLowerInvariant();
                file = gnu.Groups["file"].Value.TrimStart('*').Trim();
            }
            else if (bsd.Success)
            {
                hash = bsd.Groups["hash"].Value.ToLowerInvariant();
                file = bsd.Groups["file"].Value.Trim();
            }
            else
            {
                return null; // malformed line invalidates the whole document
            }

            if (!IsSafeRelativePath(file, allowSubdirectoryEntries))
            {
                return null;
            }

            if (entries.TryGetValue(file, out var existing) && !existing.Equals(hash, StringComparison.Ordinal))
            {
                return null; // conflicting duplicates invalidate the whole document
            }

            entries[file] = hash;
        }

        return entries;
    }

    /// <summary>
    /// Look up an exact filename. No substring, case-insensitive, or wildcard fallback.
    /// </summary>
    public static string? FindExact(IReadOnlyDictionary<string, string> entries, string fileName) =>
        entries.TryGetValue(fileName, out var hash) ? hash : null;

    /// <summary>
    /// Disallow rooted paths, "..", backslashes, and authority-ish entries. Subdirectory
    /// entries ("w64/file.exe") are only accepted when explicitly permitted by policy.
    /// </summary>
    private static bool IsSafeRelativePath(string file, bool allowSubdirectoryEntries)
    {
        if (file.Length == 0 || file.Contains('\\'))
        {
            return false;
        }

        if (file.Contains("..", StringComparison.Ordinal)
            || file.StartsWith('/') || file.StartsWith("./", StringComparison.Ordinal)
            || file.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        return allowSubdirectoryEntries || !file.Contains('/');
    }
}
