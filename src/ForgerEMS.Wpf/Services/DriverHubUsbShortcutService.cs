using System.IO;
using System.Text;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class DriverHubShortcutResult
{
    private DriverHubShortcutResult(bool succeeded, string message, string fullPath, string relativePath)
    {
        Succeeded = succeeded;
        Message = message;
        FullPath = fullPath;
        RelativePath = relativePath;
    }

    public bool Succeeded { get; }

    public string Message { get; }

    public string FullPath { get; }

    public string RelativePath { get; }

    public static DriverHubShortcutResult Success(string message, string fullPath, string relativePath) =>
        new(true, message, fullPath, relativePath);

    public static DriverHubShortcutResult Failure(string message) =>
        new(false, message, string.Empty, string.Empty);
}

public static class DriverHubUsbShortcutService
{
    public static DriverHubShortcutResult CreateShortcut(string? usbRoot, DriverHubEntry? entry)
    {
        if (entry is null)
        {
            return DriverHubShortcutResult.Failure("No Driver Hub card was selected.");
        }

        if (string.IsNullOrWhiteSpace(usbRoot))
        {
            return DriverHubShortcutResult.Failure("Select a USB target first.");
        }

        if (string.IsNullOrWhiteSpace(entry.UsbShortcutRelativePath))
        {
            return DriverHubShortcutResult.Failure("This Driver Hub card does not define a USB shortcut path.");
        }

        if (!DriverHubUrlSafety.IsSafeOfficialHttpUrl(entry.EffectiveOfficialPageUrl))
        {
            return DriverHubShortcutResult.Failure("Blocked unsafe or identifier-bearing URL.");
        }

        if (Path.IsPathRooted(entry.UsbShortcutRelativePath))
        {
            return DriverHubShortcutResult.Failure("Shortcut path must be relative to the USB root.");
        }

        try
        {
            var rootFullPath = Path.GetFullPath(usbRoot);
            var candidate = Path.GetFullPath(Path.Combine(rootFullPath, entry.UsbShortcutRelativePath));
            if (!IsInsideRoot(rootFullPath, candidate))
            {
                return DriverHubShortcutResult.Failure("Shortcut path escaped the USB root.");
            }

            if (!candidate.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            {
                return DriverHubShortcutResult.Failure("Driver Hub USB shortcuts must use the .url extension.");
            }

            var directory = Path.GetDirectoryName(candidate);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return DriverHubShortcutResult.Failure("Shortcut folder could not be resolved.");
            }

            Directory.CreateDirectory(directory);

            var content = BuildInternetShortcutContent(entry);
            var finalPath = ResolveNonOverwritingPath(candidate, content);
            if (!string.Equals(finalPath, candidate, StringComparison.OrdinalIgnoreCase) &&
                !IsInsideRoot(rootFullPath, finalPath))
            {
                return DriverHubShortcutResult.Failure("Resolved shortcut path escaped the USB root.");
            }

            if (File.Exists(finalPath))
            {
                return DriverHubShortcutResult.Success("Shortcut already exists on USB.", finalPath, ToRelative(rootFullPath, finalPath));
            }

            File.WriteAllText(finalPath, content, Encoding.ASCII);
            return DriverHubShortcutResult.Success("Shortcut added to USB.", finalPath, ToRelative(rootFullPath, finalPath));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return DriverHubShortcutResult.Failure($"Could not add Driver Hub shortcut: {exception.Message}");
        }
    }

    private static string BuildInternetShortcutContent(DriverHubEntry entry) =>
        "[InternetShortcut]\r\n" +
        $"URL={entry.EffectiveOfficialPageUrl}\r\n" +
        $"Comment=ForgerEMS Driver Hub official vendor source: {entry.Name}\r\n";

    private static string ResolveNonOverwritingPath(string candidate, string content)
    {
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        var existing = File.ReadAllText(candidate);
        if (string.Equals(existing, content, StringComparison.Ordinal))
        {
            return candidate;
        }

        var directory = Path.GetDirectoryName(candidate)!;
        var fileName = Path.GetFileNameWithoutExtension(candidate);
        var extension = Path.GetExtension(candidate);
        for (var index = 2; index < 1000; index++)
        {
            var next = Path.Combine(directory, $"{fileName} ({index}){extension}");
            if (!File.Exists(next))
            {
                return next;
            }
        }

        throw new IOException("Could not find a non-conflicting shortcut name.");
    }

    private static bool IsInsideRoot(string rootPath, string candidatePath)
    {
        var root = Path.GetFullPath(rootPath);
        var candidate = Path.GetFullPath(candidatePath);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        return string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase) ||
               candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToRelative(string rootPath, string fullPath)
    {
        try
        {
            return Path.GetRelativePath(rootPath, fullPath);
        }
        catch
        {
            return fullPath;
        }
    }
}
