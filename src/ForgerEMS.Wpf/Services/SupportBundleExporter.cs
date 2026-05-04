using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Creates a redacted ZIP support bundle for email to support (no API keys in clear text).</summary>
public static class SupportBundleExporter
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static bool TryCreateSupportBundle(
        string zipPath,
        IAppRuntimeService runtime,
        string? usbRootForManagedJson,
        string updateDiagnosticsSummary,
        string configHealthSummary,
        out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(zipPath))
            {
                error = "ZIP path was empty.";
                return false;
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(zipPath));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            AddReadme(zip);
            AddHeader(zip, runtime);
            AddRedactedString(zip, "redacted/update-check-diagnostics.txt", updateDiagnosticsSummary);
            AddRedactedString(zip, "redacted/config-health-summary.txt", configHealthSummary);

            AddRedactedFileIfExists(zip, "runtime/session-latest.log", runtime.SessionLogPath);

            foreach (var (name, path) in CollectRuntimeLogPairs(runtime.LogsRoot))
            {
                AddRedactedFileIfExists(zip, name, path);
            }

            foreach (var (name, path) in CollectLocalAppLogPairs())
            {
                AddRedactedFileIfExists(zip, name, path);
            }

            var si = Path.Combine(runtime.RuntimeRoot, "reports", "system-intelligence-latest.json");
            AddRedactedFileIfExists(zip, "reports/system-intelligence-latest.json", si);

            var bench = Path.Combine(runtime.RuntimeRoot, "cache", "usb-benchmarks.json");
            AddRedactedFileIfExists(zip, "cache/usb-benchmarks.json", bench);

            if (!string.IsNullOrWhiteSpace(usbRootForManagedJson))
            {
                var managed = Path.Combine(
                    usbRootForManagedJson.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    "ForgerEMS-managed-download-result.json");
                AddRedactedFileIfExists(zip, "usb/ForgerEMS-managed-download-result.json", managed);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void AddReadme(ZipArchive zip)
    {
        AddPlain(zip, "README.txt",
            """
            ForgerEMS support bundle (redacted)
            ------------------------------------
            This ZIP is meant for troubleshooting with Forger Digital Solutions.

            What is included (when available on this PC):
            - App version header
            - Redacted runtime session log and recent runtime logs
            - Redacted shared logs under %LOCALAPPDATA%\ForgerEMS\logs (startup, diagnostics, intelligence)
            - Latest System Intelligence JSON (paths redacted)
            - USB benchmark cache JSON (redacted)
            - Managed download result JSON from the selected USB root (if present)
            - Update-check diagnostics and configuration health summaries from the app

            Redaction:
            - Private paths may appear as [REDACTED_PRIVATE_PATH] where the redactor applies.
            - API keys and tokens are not exported from environment variables.

            Where to send:
            ForgerDigitalSolutions@outlook.com

            Do not email passwords, product keys, private documents, or full disk images.
            """);
    }

    private static void AddHeader(ZipArchive zip, IAppRuntimeService runtime)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ForgerEMS support bundle metadata");
        sb.AppendLine($"GeneratedUtc: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"AppVersion: {AppReleaseInfo.Version}");
        sb.AppendLine($"DisplayVersion: {AppReleaseInfo.DisplayVersion}");
        sb.AppendLine($"FORGEREMS_RELEASE_CHANNEL: {ForgerEmsEnvironmentConfiguration.ReleaseChannel}");
        sb.AppendLine($"UpdateSource: {ForgerEmsEnvironmentConfiguration.GitHubOwner}/{ForgerEmsEnvironmentConfiguration.GitHubRepo}");
        sb.AppendLine($"TelemetryEnabled(env): {ForgerEmsEnvironmentConfiguration.TelemetryEnabled}");
        sb.AppendLine($"RuntimeRoot: {CopilotRedactor.Redact(runtime.RuntimeRoot, enabled: true)}");
        AddPlain(zip, "bundle-metadata.txt", sb.ToString());
    }

    private static IEnumerable<(string EntryName, string Path)> CollectRuntimeLogPairs(string logsRoot)
    {
        if (!Directory.Exists(logsRoot))
        {
            yield break;
        }

        var files = Directory.EnumerateFiles(logsRoot, "*.log", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(8)
            .ToList();

        var i = 0;
        foreach (var f in files)
        {
            i++;
            yield return ($"runtime/logs/{i:00}-{SanitizeEntryFileName(f.Name)}", f.FullName);
        }
    }

    private static IEnumerable<(string EntryName, string Path)> CollectLocalAppLogPairs()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            yield break;
        }

        var logs = Path.Combine(root, "ForgerEMS", "logs");
        if (!Directory.Exists(logs))
        {
            yield break;
        }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "startup.log",
            "diagnostics.log",
            "system-intelligence.log",
            "usb-intelligence.log"
        };

        foreach (var name in wanted)
        {
            var p = Path.Combine(logs, name);
            if (File.Exists(p))
            {
                yield return ($"localappdata-logs/{SanitizeEntryFileName(name)}", p);
            }
        }
    }

    private static string SanitizeEntryFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(name) ? "file.log" : name;
    }

    private static void AddPlain(ZipArchive zip, string entryName, string text)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var w = new StreamWriter(entry.Open(), Utf8NoBom);
        w.Write(text);
    }

    private static void AddRedactedString(ZipArchive zip, string entryName, string? text)
    {
        var body = CopilotRedactor.Redact(text ?? string.Empty, enabled: true);
        AddPlain(zip, entryName, body);
    }

    private static void AddRedactedFileIfExists(ZipArchive zip, string entryName, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        string raw;
        try
        {
            raw = File.ReadAllText(path);
        }
        catch
        {
            return;
        }

        AddRedactedString(zip, entryName, raw);
    }
}
