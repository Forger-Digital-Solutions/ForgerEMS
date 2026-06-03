using System.IO;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Canonical on-USB internal layout for ForgerEMS-owned metadata, logs, and support files.</summary>
public static class UsbInternalLayout
{
    public const string InternalRoot = "_forgerems";
    public const string MetadataFolder = "_forgerems/metadata";
    public const string RawLogsFolder = "_forgerems/logs";
    public const string RawReportsFolder = "_forgerems/reports";
    public const string SupportFolder = "_forgerems/support";
    public const string CacheFolder = "_forgerems/cache";

    public const string ManifestFileName = "ForgerEMS.updates.json";
    public const string ManagedDownloadResultFileName = "ForgerEMS-managed-download-result.json";

    public const int RawLogRetentionCount = 12;
    public const int RawReportRetentionCount = 12;

    public const string ForgerEmsGeneratedReadmeMarker = "# ForgerEMS TechBench USB";

    private static readonly string[] LegacyRootMetadataFiles =
    [
        ManagedDownloadResultFileName,
        ManifestFileName,
        "ForgerEMS.updates"
    ];

    private static readonly string[] SupportDocFileNames =
    [
        "ForgerEMS-Bootstrap-Notes.txt",
        "ForgerEMS-Download-Catalog.txt",
        "ForgerEMS-Managed-Download-Maintenance.txt",
        "ForgerEMS-Link-Inventory.csv"
    ];

    public static string Combine(string usbRoot, string relativePath) =>
        Path.Combine(usbRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), relativePath);

    public static string MetadataDirectory(string usbRoot) => Combine(usbRoot, MetadataFolder);

    public static string MetadataPath(string usbRoot, string fileName) =>
        Path.Combine(MetadataDirectory(usbRoot), fileName);

    public static string ManagedDownloadResultPath(string usbRoot) =>
        MetadataPath(usbRoot, ManagedDownloadResultFileName);

    public static string ManifestPath(string usbRoot) =>
        MetadataPath(usbRoot, ManifestFileName);

    public static string ResolveManagedDownloadResultPath(string usbRoot)
    {
        var primary = ManagedDownloadResultPath(usbRoot);
        if (File.Exists(primary))
        {
            return primary;
        }

        var legacy = Combine(usbRoot, ManagedDownloadResultFileName);
        return File.Exists(legacy) ? legacy : primary;
    }

    public static string ResolveManifestPath(string usbRoot)
    {
        var primary = ManifestPath(usbRoot);
        if (File.Exists(primary))
        {
            return primary;
        }

        foreach (var candidate in new[]
                 {
                     Combine(usbRoot, ManifestFileName),
                     Combine(usbRoot, "ForgerEMS.updates")
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return primary;
    }

    public static string RawLogsDirectory(string usbRoot) => Combine(usbRoot, RawLogsFolder);

    public static string RawReportsDirectory(string usbRoot) => Combine(usbRoot, RawReportsFolder);

    public static string SupportDirectory(string usbRoot) => Combine(usbRoot, SupportFolder);

    public static bool IsSupportDocFileName(string fileName) =>
        SupportDocFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> EnumerateLegacyRootMetadataFiles(string usbRoot) =>
        LegacyRootMetadataFiles
            .Select(name => Combine(usbRoot, name))
            .Where(File.Exists)
            .ToList();
}
