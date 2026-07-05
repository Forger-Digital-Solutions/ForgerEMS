#pragma warning disable CA1305 // UI/status strings use invariant-friendly formatting where values matter.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services;

public enum DrForgeCliBridgeState
{
    NotConfigured,
    PackageFound,
    Ready,
    RunningIntake,
    ReportReady,
    ArchiveReady,
    Unavailable,
    Failed
}

public sealed record DrForgeCliLocationResult(
    bool Found,
    DrForgeCliBridgeState State,
    string? ExecutablePath,
    string Message)
{
    public static DrForgeCliLocationResult NotConfigured() =>
        new(false, DrForgeCliBridgeState.NotConfigured, null,
            "Select a packaged drforge.exe or place the Dr. Forge CLI package under the app-local tools folder.");
}

public sealed class DrForgeCliLocator
{
    public DrForgeCliLocationResult Locate(string? explicitExecutablePath, string? appBaseDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitExecutablePath))
        {
            var explicitResult = ResolveExplicitPath(explicitExecutablePath);
            if (explicitResult is not null)
            {
                return explicitResult;
            }
        }

        var baseDir = string.IsNullOrWhiteSpace(appBaseDirectory)
            ? AppContext.BaseDirectory
            : appBaseDirectory;

        foreach (var candidate in EnumerateAppLocalCandidates(baseDir))
        {
            if (File.Exists(candidate))
            {
                return new DrForgeCliLocationResult(
                    true,
                    DrForgeCliBridgeState.PackageFound,
                    Path.GetFullPath(candidate),
                    "Dr. Forge CLI package found in an app-local location.");
            }
        }

        return DrForgeCliLocationResult.NotConfigured();
    }

    public static IReadOnlyList<string> EnumerateAppLocalCandidates(string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            return [];
        }

        var root = Path.GetFullPath(appBaseDirectory);
        return
        [
            Path.Combine(root, "tools", "drforge", "windows-x64", "drforge.exe"),
            Path.Combine(root, "tools", "drforge", "drforge.exe"),
            Path.Combine(root, "drforge", "windows-x64", "drforge.exe"),
            Path.Combine(root, "drforge", "drforge.exe")
        ];
    }

    private static DrForgeCliLocationResult? ResolveExplicitPath(string rawPath)
    {
        var trimmed = rawPath.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        string candidate;
        try
        {
            candidate = Directory.Exists(trimmed)
                ? Path.Combine(Path.GetFullPath(trimmed), "drforge.exe")
                : Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new DrForgeCliLocationResult(
                false,
                DrForgeCliBridgeState.Unavailable,
                null,
                "The selected Dr. Forge path is not valid.");
        }

        if (!string.Equals(Path.GetFileName(candidate), "drforge.exe", StringComparison.OrdinalIgnoreCase))
        {
            return new DrForgeCliLocationResult(
                false,
                DrForgeCliBridgeState.Unavailable,
                candidate,
                "Select the packaged drforge.exe file, not an internal DLL or helper executable.");
        }

        if (!File.Exists(candidate))
        {
            return new DrForgeCliLocationResult(
                false,
                DrForgeCliBridgeState.NotConfigured,
                candidate,
                "The selected Dr. Forge CLI executable was not found.");
        }

        return new DrForgeCliLocationResult(
            true,
            DrForgeCliBridgeState.PackageFound,
            candidate,
            "Dr. Forge CLI executable found.");
    }
}

public sealed record DrForgeCliManifestInfo(
    bool Found,
    string? ManifestPath,
    string? Schema,
    string? Product,
    string? Version,
    string? Commit,
    string? SafetyMode,
    string Summary);

public sealed record DrForgeChecksumVerificationResult(
    bool Present,
    bool Passed,
    string? ChecksumFilePath,
    int CheckedFileCount,
    IReadOnlyList<string> Failures,
    string Summary);

public sealed record DrForgeCliPackageInspection(
    bool PackageFound,
    string ExecutablePath,
    DrForgeCliManifestInfo Manifest,
    DrForgeChecksumVerificationResult Checksums,
    string Summary);

public sealed class DrForgeCliManifestReader
{
    private const string ManifestFileName = "drforge-cli-release-manifest.json";

    public DrForgeCliPackageInspection InspectPackage(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            var missingManifest = new DrForgeCliManifestInfo(false, null, null, null, null, null, null,
                "Dr. Forge CLI executable is not configured.");
            var missingChecksums = new DrForgeChecksumVerificationResult(false, false, null, 0, [],
                "Checksum verification was not available because the package was not found.");
            return new DrForgeCliPackageInspection(false, executablePath ?? string.Empty, missingManifest, missingChecksums,
                "Dr. Forge CLI package is not configured.");
        }

        var fullExe = Path.GetFullPath(executablePath);
        var manifestPath = FindManifestPath(fullExe);
        var manifest = ReadManifest(manifestPath);
        var checksumPath = ResolveChecksumPath(fullExe, manifestPath, manifestPath is null ? null : ReadChecksumRelativePath(manifestPath));
        var checksums = checksumPath is null
            ? new DrForgeChecksumVerificationResult(false, true, null, 0, [],
                "SHA256SUMS.txt was not found. Package integrity could not be verified locally.")
            : VerifySha256Sums(checksumPath);

        var summary = manifest.Found
            ? $"Manifest {manifest.Schema ?? "unknown"}; version {manifest.Version ?? "unknown"}; checksums: {checksums.Summary}"
            : $"Manifest unavailable; checksums: {checksums.Summary}";

        return new DrForgeCliPackageInspection(true, fullExe, manifest, checksums, summary);
    }

    public DrForgeChecksumVerificationResult VerifySha256Sums(string checksumFilePath)
    {
        if (string.IsNullOrWhiteSpace(checksumFilePath) || !File.Exists(checksumFilePath))
        {
            return new DrForgeChecksumVerificationResult(false, false, checksumFilePath, 0, [],
                "SHA256SUMS.txt was not found.");
        }

        var checksumFullPath = Path.GetFullPath(checksumFilePath);
        var baseDirectory = Path.GetDirectoryName(checksumFullPath) ?? Directory.GetCurrentDirectory();
        var failures = new List<string>();
        var checkedCount = 0;

        foreach (var rawLine in File.ReadLines(checksumFullPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.Length < 66)
            {
                failures.Add("Malformed checksum line.");
                continue;
            }

            var expected = line[..64];
            if (!expected.All(Uri.IsHexDigit))
            {
                failures.Add("Malformed SHA256 hash.");
                continue;
            }

            var relative = line[64..].Trim();
            if (relative.StartsWith("*", StringComparison.Ordinal))
            {
                relative = relative[1..].Trim();
            }

            if (string.IsNullOrWhiteSpace(relative) ||
                Path.IsPathRooted(relative) ||
                relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p == ".."))
            {
                failures.Add("Unsafe checksum path entry.");
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(baseDirectory, relative));
            if (!IsPathInside(candidate, baseDirectory))
            {
                failures.Add("Checksum path escaped the package directory.");
                continue;
            }

            if (!File.Exists(candidate))
            {
                failures.Add($"Missing file: {relative}");
                continue;
            }

            checkedCount++;
            using var stream = File.OpenRead(candidate);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(actual, expected.ToLowerInvariant(), StringComparison.Ordinal))
            {
                failures.Add($"SHA256 mismatch: {relative}");
            }
        }

        var passed = failures.Count == 0 && checkedCount > 0;
        var summary = passed
            ? $"verified {checkedCount} package files"
            : checkedCount == 0
                ? "no package files were verified"
                : $"verified {checkedCount} package files with {failures.Count} failure(s)";

        return new DrForgeChecksumVerificationResult(true, passed, checksumFullPath, checkedCount, failures, summary);
    }

    private static string? FindManifestPath(string executablePath)
    {
        var dir = Path.GetDirectoryName(executablePath);
        for (var i = 0; i < 3 && !string.IsNullOrWhiteSpace(dir); i++)
        {
            var candidate = Path.Combine(dir, ManifestFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        return null;
    }

    private static DrForgeCliManifestInfo ReadManifest(string? manifestPath)
    {
        if (manifestPath is null || !File.Exists(manifestPath))
        {
            return new DrForgeCliManifestInfo(false, manifestPath, null, null, null, null, null,
                "Dr. Forge CLI release manifest was not found.");
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            var schema = ReadString(root, "schema");
            var product = ReadString(root, "product");
            var version = ReadString(root, "version");
            var commit = ReadString(root, "commit");
            string? safetyMode = null;
            if (TryGetProperty(root, "safetyPolicy", out var safety) &&
                safety.ValueKind == JsonValueKind.Object)
            {
                safetyMode = ReadString(safety, "mode");
            }

            var accepted = string.Equals(schema, "drforge-cli-release-manifest/1.0", StringComparison.Ordinal);
            var summary = accepted
                ? "Dr. Forge CLI release manifest accepted."
                : $"Dr. Forge CLI manifest schema is unsupported: {schema ?? "(missing)"}.";

            return new DrForgeCliManifestInfo(accepted, Path.GetFullPath(manifestPath), schema, product, version, commit, safetyMode, summary);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new DrForgeCliManifestInfo(false, Path.GetFullPath(manifestPath), null, null, null, null, null,
                "Dr. Forge CLI release manifest could not be read.");
        }
    }

    private static string? ReadChecksumRelativePath(string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!TryGetProperty(document.RootElement, "packages", out var packages) ||
                packages.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var package in packages.EnumerateArray())
            {
                if (string.Equals(ReadString(package, "status"), "published", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ReadString(package, "platform"), "windows-x64", StringComparison.OrdinalIgnoreCase))
                {
                    return ReadString(package, "checksumFile");
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private static string? ResolveChecksumPath(string executablePath, string? manifestPath, string? checksumRelativePath)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath) && !string.IsNullOrWhiteSpace(checksumRelativePath))
        {
            var root = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
            var candidate = Path.GetFullPath(Path.Combine(root, checksumRelativePath));
            if (IsPathInside(candidate, root) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        var exeDir = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        if (!string.IsNullOrWhiteSpace(exeDir))
        {
            var local = Path.Combine(exeDir, "SHA256SUMS.txt");
            if (File.Exists(local))
            {
                return local;
            }
        }

        return null;
    }

    internal static bool IsPathInside(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public sealed record DrForgeCliProcessResult(
    string ExecutablePath,
    IReadOnlyList<string> Arguments,
    int ExitCode,
    bool TimedOut,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
    public string CombinedOutput =>
        string.IsNullOrWhiteSpace(StandardError)
            ? StandardOutput
            : StandardOutput + Environment.NewLine + "[stderr]" + Environment.NewLine + StandardError;
}

public interface IDrForgeProcessRunner
{
    Task<DrForgeCliProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public sealed class DrForgeProcessRunner : IDrForgeProcessRunner
{
    private const int OutputLimit = 96 * 1024;

    public async Task<DrForgeCliProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath)) ?? Environment.CurrentDirectory,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(timeout);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) => AppendCapped(stdout, e.Data);
        process.ErrorDataReceived += (_, e) => AppendCapped(stderr, e.Data);

        try
        {
            try
            {
                if (!process.Start())
                {
                    return new DrForgeCliProcessResult(executablePath, arguments, 1, false, string.Empty,
                        "Dr. Forge CLI process did not start.");
                }
            }
            catch (Win32Exception ex)
            {
                return new DrForgeCliProcessResult(executablePath, arguments, 1, false, string.Empty,
                    "Could not start Dr. Forge CLI: " + ex.Message);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

            return new DrForgeCliProcessResult(
                executablePath,
                arguments,
                process.ExitCode,
                false,
                stdout.ToString().TrimEnd(),
                stderr.ToString().TrimEnd());
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            var timedOut = !cancellationToken.IsCancellationRequested;
            return new DrForgeCliProcessResult(
                executablePath,
                arguments,
                130,
                timedOut,
                stdout.ToString().TrimEnd(),
                timedOut ? "Dr. Forge CLI command timed out." : "Dr. Forge CLI command was cancelled.");
        }
        catch (Exception ex)
        {
            TryKillProcessTree(process);
            return new DrForgeCliProcessResult(executablePath, arguments, 1, false, stdout.ToString().TrimEnd(),
                "Dr. Forge CLI command failed: " + ex.Message);
        }
    }

    private static void AppendCapped(StringBuilder builder, string? line)
    {
        if (line is null || builder.Length >= OutputLimit)
        {
            return;
        }

        var remaining = OutputLimit - builder.Length;
        if (line.Length > remaining)
        {
            builder.Append(line.AsSpan(0, remaining));
            return;
        }

        builder.AppendLine(line);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}

public sealed record DrForgeCliOperationResult(
    bool Succeeded,
    DrForgeCliBridgeState State,
    string Message,
    string? OutputPath,
    DrForgeCliProcessResult? ProcessResult)
{
    public DrForgeCliVersionInfo? VersionInfo { get; init; }

    public DrForgeDriverStatusView? DriverStatus { get; init; }
}

public sealed record DrForgeCliVersionInfo(
    string ProductLine,
    string? Version,
    string? Commit,
    string SummaryText);

public sealed class DrForgeCliVersionReader
{
    public DrForgeCliVersionInfo ReadText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new DrForgeCliVersionInfo(
                "Dr. Forge CLI",
                null,
                null,
                "Version: unavailable from CLI output; commit: unavailable.");
        }

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var productLine = lines.FirstOrDefault() ?? "Dr. Forge CLI";
        string? version = null;
        string? commit = null;

        foreach (var line in lines)
        {
            version ??= ReadKeyValue(line, "Version");
            commit ??= ReadKeyValue(line, "Commit");
            version ??= TryReadVersionToken(line);
        }

        var summary = "Version: " + (version ?? "unavailable") + "; commit: " + (commit ?? "unavailable") + ".";
        return new DrForgeCliVersionInfo(productLine, version, commit, summary);
    }

    private static string? ReadKeyValue(string line, string key)
    {
        if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var separator = line.IndexOf(':');
        if (separator < 0)
        {
            separator = line.IndexOf('=');
        }

        if (separator < 0 || separator + 1 >= line.Length)
        {
            return null;
        }

        var value = line[(separator + 1)..].Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string? TryReadVersionToken(string line)
    {
        foreach (var token in line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var trimmed = token.Trim().TrimStart('v', 'V').TrimEnd(',', ';');
            if (trimmed.Length > 0 && char.IsDigit(trimmed[0]) && trimmed.Contains('.', StringComparison.Ordinal))
            {
                return trimmed;
            }
        }

        return null;
    }
}

public sealed record DrForgeDriverStatusView(
    string SchemaVersion,
    string Readiness,
    bool SupportedSchema,
    bool? ProductionDriverShipped,
    bool? DriverSupportCompiledIn,
    bool? DriverInstalled,
    bool? DriverRunning,
    bool? UserModeFallbackActive,
    bool? AbsenceIsNormal,
    bool? NoDriverActionTaken,
    int DriverRequiredUnavailableCount,
    string SummaryText);

public static class DrForgeDriverStatusDisplayBuilder
{
    public static string BuildSafeSummary(DrForgeDriverStatusView? status)
    {
        var sb = new StringBuilder();

        if (status is null)
        {
            sb.AppendLine("Dr. Forge is not configured. Select a packaged drforge.exe to check safe user-mode readiness.");
            sb.AppendLine("No production sensor driver is shipped or loaded.");
            sb.AppendLine("No driver install, start, load, or elevation action was taken.");
            sb.AppendLine("Driver-required readings are unavailable until a future signed-driver phase.");
            sb.Append("Reports stay local unless you explicitly export or include them in a support bundle.");
            return sb.ToString();
        }

        if (!status.SupportedSchema)
        {
            sb.AppendLine(status.SummaryText);
            sb.AppendLine("ForgerEMS treats missing or unsupported driver-status output as safe user-mode report mode, not as a driver error.");
            sb.AppendLine("No production sensor driver is shipped or loaded.");
            sb.AppendLine("No driver install, start, load, or elevation action was taken.");
            sb.AppendLine("Driver-required readings are unavailable until a future signed-driver phase.");
            sb.Append("Reports stay local unless you explicitly export or include them in a support bundle.");
            return sb.ToString();
        }

        sb.AppendLine($"Driver status schema: {status.SchemaVersion}.");
        sb.AppendLine(status.UserModeFallbackActive == true
            ? "Dr. Forge is running in safe user-mode fallback."
            : "Dr. Forge user-mode fallback status is unavailable from this CLI.");
        sb.AppendLine("Production driver shipped: " + FormatBool(status.ProductionDriverShipped) + ".");
        sb.AppendLine(status.ProductionDriverShipped == false
            ? "No production sensor driver is shipped or loaded."
            : "ForgerEMS still does not install, start, load, or activate driver support.");
        sb.AppendLine("Driver absence normal/safe: " + FormatBool(status.AbsenceIsNormal) + ".");
        sb.AppendLine("No driver action taken: " + FormatBool(status.NoDriverActionTaken) + ".");
        sb.AppendLine("Driver-required readings unavailable: " + status.DriverRequiredUnavailableCount.ToString(CultureInfo.InvariantCulture) + ".");
        sb.AppendLine("Driver-required readings are unavailable until a future signed-driver phase.");
        sb.AppendLine("No driver install, start, load, or elevation action was taken.");
        sb.Append("Reports stay local unless you explicitly export or include them in a support bundle.");
        return sb.ToString();
    }

    private static string FormatBool(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "unavailable"
    };
}

public sealed record DrForgeReportHistoryItem(
    string Kind,
    string Name,
    string Path,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc);

public sealed record DrForgeReportHistoryView(
    bool IsDrForgeConfigured,
    bool FolderReadable,
    string StatusText,
    IReadOnlyList<DrForgeReportHistoryItem> Items,
    string SummaryText);

public sealed class DrForgeReportHistoryReader
{
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, IEnumerable<string>> _enumerateEntries;
    private readonly Func<string, long> _getLength;
    private readonly Func<string, DateTimeOffset> _getLastWriteTimeUtc;

    public DrForgeReportHistoryReader(
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null,
        Func<string, IEnumerable<string>>? enumerateEntries = null,
        Func<string, long>? getLength = null,
        Func<string, DateTimeOffset>? getLastWriteTimeUtc = null)
    {
        _directoryExists = directoryExists ?? Directory.Exists;
        _fileExists = fileExists ?? File.Exists;
        _enumerateEntries = enumerateEntries ?? (path => Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly));
        _getLength = getLength ?? (path => new FileInfo(path).Length);
        _getLastWriteTimeUtc = getLastWriteTimeUtc ?? (path => new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
    }

    public DrForgeReportHistoryView Read(string? reportsRoot, bool isDrForgeConfigured, int maxItems = 5)
    {
        if (!isDrForgeConfigured)
        {
            const string summary =
                "No Dr. Forge CLI is configured yet. Select a packaged drforge.exe to generate local reports. " +
                "Reports stay local unless explicitly exported or included in a support bundle.";
            return new DrForgeReportHistoryView(false, true, "Not configured", [], summary);
        }

        if (string.IsNullOrWhiteSpace(reportsRoot))
        {
            const string summary =
                "Dr. Forge report folder is unavailable. Reports stay local unless explicitly exported or included in a support bundle.";
            return new DrForgeReportHistoryView(true, false, "Report folder unavailable", [], summary);
        }

        string root;
        try
        {
            root = Path.GetFullPath(reportsRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            const string summary =
                "Dr. Forge report folder is unavailable. Reports stay local unless explicitly exported or included in a support bundle.";
            return new DrForgeReportHistoryView(true, false, "Report folder unavailable", [], summary);
        }

        if (!_directoryExists(root))
        {
            const string summary =
                "No reports found yet. Generate a Dr. Forge report to populate local history. " +
                "Reports stay local unless explicitly exported or included in a support bundle.";
            return new DrForgeReportHistoryView(true, true, "No reports found yet", [], summary);
        }

        try
        {
            var items = _enumerateEntries(root)
                .Select(entry => TryCreateItem(root, entry))
                .Where(item => item is not null)
                .Cast<DrForgeReportHistoryItem>()
                .OrderByDescending(item => item.LastModifiedUtc)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxItems))
                .ToList();

            if (items.Count == 0)
            {
                const string summary =
                    "No reports found yet. Generate a Dr. Forge report to populate local history. " +
                    "Reports stay local unless explicitly exported or included in a support bundle.";
                return new DrForgeReportHistoryView(true, true, "No reports found yet", [], summary);
            }

            var sb = new StringBuilder();
            sb.AppendLine("Recent Dr. Forge reports/history:");
            foreach (var item in items)
            {
                sb.AppendLine("- " + item.Kind + ": " + item.Name + " (" + FormatUtc(item.LastModifiedUtc) + ")");
            }

            sb.Append("Reports stay local unless explicitly exported or included in a support bundle.");
            return new DrForgeReportHistoryView(true, true, items.Count.ToString(CultureInfo.InvariantCulture) + " report item(s) found", items, sb.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            const string summary =
                "Report history is unavailable because the app-managed Dr. Forge report folder could not be read. " +
                "Reports stay local unless explicitly exported or included in a support bundle.";
            return new DrForgeReportHistoryView(true, false, "Report folder unreadable", [], summary);
        }
    }

    private DrForgeReportHistoryItem? TryCreateItem(string reportsRoot, string entry)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(entry);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!DrForgeCliManifestReader.IsPathInside(fullPath, reportsRoot))
        {
            return null;
        }

        var name = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var kind =
            name.StartsWith("drforge-intake-report-", StringComparison.OrdinalIgnoreCase) &&
            (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
             name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                ? "Report"
                : name.StartsWith("drforge-sensor-core-snapshot-", StringComparison.OrdinalIgnoreCase) &&
                  name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? "Snapshot"
                : name.StartsWith("drforge-intake-archive-", StringComparison.OrdinalIgnoreCase)
                    ? "Archive"
                    : name.StartsWith("drforge-intake-", StringComparison.OrdinalIgnoreCase) &&
                      name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        ? "Archive"
                    : string.Empty;
        if (string.IsNullOrWhiteSpace(kind))
        {
            return null;
        }

        long sizeBytes = 0;
        DateTimeOffset lastWrite;
        try
        {
            if (_fileExists(fullPath))
            {
                sizeBytes = Math.Max(0, _getLength(fullPath));
            }

            lastWrite = _getLastWriteTimeUtc(fullPath).ToUniversalTime();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }

        return new DrForgeReportHistoryItem(kind, name, fullPath, sizeBytes, lastWrite);
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}

public sealed record DrForgeParsedReportField(string Name, string Value);

public sealed record DrForgeParsedReportSection(string Title, IReadOnlyList<DrForgeParsedReportField> Fields);

public sealed record DrForgeReportDetailView(
    bool PreviewAvailable,
    string Kind,
    string Name,
    string Path,
    long SizeBytes,
    DateTimeOffset? LastModifiedUtc,
    string ReportSchema,
    string SourceSchema,
    string GeneratedAt,
    string SafetyStatus,
    string KernelDriverLoaded,
    int? AvailableReadingCount,
    int? UnavailableReadingCount,
    int? DriverRequiredUnavailableCount,
    string StatusText,
    string PreviewText,
    string RawPreviewText,
    IReadOnlyList<DrForgeParsedReportSection> ParsedSections,
    string SummaryText);

public sealed class DrForgeReportDetailReader
{
    public const long MaxJsonParseBytes = 512 * 1024;
    public const long MaxMarkdownPreviewBytes = 64 * 1024;
    public const int MaxPreviewCharacters = 4000;

    private static readonly (string PropertyName, string DisplayName)[] KnownSummaryReadings =
    [
        ("cpuLoadPercent", "CPU load"),
        ("memoryUsedPercent", "Memory used"),
        ("storageCapacityBytes", "Storage capacity"),
        ("storageSmartHealth", "Storage SMART health")
    ];

    private readonly string _reportsRoot;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, long> _getLength;
    private readonly Func<string, DateTimeOffset> _getLastWriteTimeUtc;
    private readonly Func<string, string> _readAllText;
    private readonly Func<string, int, string> _readTextPrefix;

    public DrForgeReportDetailReader(
        string reportsRoot,
        Func<string, bool>? fileExists = null,
        Func<string, bool>? directoryExists = null,
        Func<string, long>? getLength = null,
        Func<string, DateTimeOffset>? getLastWriteTimeUtc = null,
        Func<string, string>? readAllText = null,
        Func<string, int, string>? readTextPrefix = null)
    {
        _reportsRoot = TryGetFullPath(reportsRoot) ?? string.Empty;
        _fileExists = fileExists ?? File.Exists;
        _directoryExists = directoryExists ?? Directory.Exists;
        _getLength = getLength ?? (path => new FileInfo(path).Length);
        _getLastWriteTimeUtc = getLastWriteTimeUtc ?? (path => new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero));
        _readAllText = readAllText ?? File.ReadAllText;
        _readTextPrefix = readTextPrefix ?? ReadTextPrefixFromFile;
    }

    public DrForgeReportDetailView Read(string? reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return Unavailable("(none)", string.Empty, "Report", "Preview unavailable: no local Dr. Forge report is selected.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(reportPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Unavailable(SafeFileName(reportPath), reportPath, "Report", "Preview unavailable: the selected report path is not valid.");
        }

        var name = Path.GetFileName(fullPath);
        var kind = InferKind(name, isDirectory: false);
        if (string.IsNullOrWhiteSpace(_reportsRoot) || !DrForgeCliManifestReader.IsPathInside(fullPath, _reportsRoot))
        {
            return Unavailable(name, fullPath, kind, "Preview unavailable: selected report is outside the app-managed Dr. Forge report folder.");
        }

        try
        {
            if (_directoryExists(fullPath))
            {
                kind = InferKind(name, isDirectory: true);
                return BuildArchiveDirectoryDetail(fullPath, name, kind);
            }

            if (!_fileExists(fullPath))
            {
                return Unavailable(name, fullPath, kind, "Preview unavailable: the selected report file was not found.");
            }

            var size = Math.Max(0, _getLength(fullPath));
            var lastWrite = _getLastWriteTimeUtc(fullPath).ToUniversalTime();
            var extension = Path.GetExtension(fullPath);
            if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                return size > MaxJsonParseBytes
                    ? BuildLargeFileDetail(fullPath, name, kind, size, lastWrite, "JSON report is larger than the safe preview parse limit.")
                    : BuildJsonDetail(fullPath, name, kind, size, lastWrite);
            }

            if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return BuildMarkdownDetail(fullPath, name, kind, size, lastWrite);
            }

            if (extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                return BuildArchiveFileDetail(fullPath, name, kind, size, lastWrite);
            }

            return MetadataOnly(fullPath, name, kind, size, lastWrite,
                "Preview unavailable: this Dr. Forge artifact type is not previewed in-app.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
        {
            return Unavailable(name, fullPath, kind, "Preview unavailable: the selected report could not be read or parsed.");
        }
    }

    private DrForgeReportDetailView BuildJsonDetail(
        string path,
        string name,
        string kind,
        long size,
        DateTimeOffset lastWrite)
    {
        var json = _readAllText(path);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var reportSchema = ReadString(root, "reportSchemaVersion") ??
                           ReadString(root, "schemaVersion") ??
                           "Unknown";
        var sourceSchema = ReadString(root, "sourceSchemaVersion") ?? "Unknown";
        var generatedAt = FormatGeneratedAt(ReadString(root, "generatedAtUtc") ??
                                            ReadString(root, "generatedAt"));
        var safety = TryReadSafety(root, out var invariants, out var kernelDriverLoaded);
        var counts = CountSummaryReadings(root);
        var driverRequiredUnavailable = CountDriverRequiredUnavailable(root);

        var parsedSections = BuildParsedSections(
            root,
            name,
            kind,
            size,
            lastWrite,
            reportSchema,
            sourceSchema,
            generatedAt,
            invariants,
            kernelDriverLoaded,
            counts,
            driverRequiredUnavailable);

        var statusLines = new List<string>
        {
            "Local Dr. Forge report preview",
            "Preview is read-only.",
            "Report: " + name,
            "Local report path: app-managed Dr. Forge report root\\" + name,
            "Type: " + kind,
            "Preview status: Preview ready",
            "Modified: " + FormatUtc(lastWrite),
            "Size: " + FormatBytes(size),
            "Report schema: " + reportSchema,
            "Source schema: " + sourceSchema,
            "Generated: " + generatedAt,
            "Safety/invariant result: " + FormatNullableBool(invariants),
            "Kernel driver loaded: " + FormatNullableBool(kernelDriverLoaded),
            "Available readings: " + FormatNullableCount(counts?.Available),
            "Unavailable readings: " + FormatNullableCount(counts?.Unavailable),
            "Driver-required readings unavailable: " + driverRequiredUnavailable.ToString(CultureInfo.InvariantCulture),
            "Driver-required readings may appear as unavailable.",
            "Reports stay local unless you explicitly export or include them in a support bundle.",
            "No driver install, start, load, or elevation action is performed."
        };
        AddParsedSummaryLines(statusLines, parsedSections);

        var preview = BuildJsonPreview(root);
        var rawPreview = BuildRawPreview(json);
        return new DrForgeReportDetailView(
            true,
            kind,
            name,
            path,
            size,
            lastWrite,
            reportSchema,
            sourceSchema,
            generatedAt,
            safety,
            FormatNullableBool(kernelDriverLoaded),
            counts?.Available,
            counts?.Unavailable,
            driverRequiredUnavailable,
            "Preview ready",
            preview,
            rawPreview,
            parsedSections,
            string.Join(Environment.NewLine, statusLines));
    }

    private DrForgeReportDetailView BuildMarkdownDetail(
        string path,
        string name,
        string kind,
        long size,
        DateTimeOffset lastWrite)
    {
        var bytesToRead = (int)Math.Min(size, MaxMarkdownPreviewBytes);
        var preview = CapPreview(_readTextPrefix(path, bytesToRead), out var cappedByCharacters);
        if (size > MaxMarkdownPreviewBytes || cappedByCharacters)
        {
            preview += Environment.NewLine + "[Preview capped for safety.]";
        }

        var summary = string.Join(Environment.NewLine,
            "Local Dr. Forge report preview",
            "Preview is read-only.",
            "Report: " + name,
            "Type: " + kind,
            "Preview status: Preview ready",
            "Modified: " + FormatUtc(lastWrite),
            "Size: " + FormatBytes(size),
            "Markdown is shown as plain text.",
            "Driver-required readings may appear as unavailable.",
            "Reports stay local unless you explicitly export or include them in a support bundle.",
            "No driver install, start, load, or elevation action is performed.");

        return new DrForgeReportDetailView(true, kind, name, path, size, lastWrite, "Markdown", "Unknown", "Unknown",
            "Unknown", "unknown", null, null, null, "Preview ready", preview, preview,
            [BuildReportMetadataSection(name, kind, size, lastWrite, "Preview ready", "Markdown is shown as capped plain text.")],
            summary);
    }

    private DrForgeReportDetailView BuildArchiveFileDetail(
        string path,
        string name,
        string kind,
        long size,
        DateTimeOffset lastWrite)
    {
        return MetadataOnly(path, name, kind, size, lastWrite,
            "Archive preview is metadata only. No archive contents were extracted.");
    }

    private DrForgeReportDetailView BuildArchiveDirectoryDetail(string path, string name, string kind)
    {
        var lastWrite = _getLastWriteTimeUtc(path).ToUniversalTime();
        return MetadataOnly(path, name, kind, 0, lastWrite,
            "Archive folder preview is metadata only. No archive contents were crawled or extracted.");
    }

    private DrForgeReportDetailView BuildLargeFileDetail(
        string path,
        string name,
        string kind,
        long size,
        DateTimeOffset lastWrite,
        string reason)
    {
        return MetadataOnly(path, name, kind, size, lastWrite,
            reason + " Open the containing folder to inspect it manually.");
    }

    private static DrForgeReportDetailView MetadataOnly(
        string path,
        string name,
        string kind,
        long size,
        DateTimeOffset lastWrite,
        string reason)
    {
        var summary = string.Join(Environment.NewLine,
            "Local Dr. Forge report preview",
            "Preview is read-only.",
            "Report: " + name,
            "Type: " + kind,
            "Preview status: Preview unavailable",
            "Modified: " + FormatUtc(lastWrite),
            "Size: " + FormatBytes(size),
            reason,
            "Reports stay local unless you explicitly export or include them in a support bundle.",
            "No driver install, start, load, or elevation action is performed.");

        return new DrForgeReportDetailView(false, kind, name, path, size, lastWrite, "Unknown", "Unknown", "Unknown",
            "Unknown", "unknown", null, null, null, "Preview unavailable", reason, reason,
            [BuildReportMetadataSection(name, kind, size, lastWrite, "Preview unavailable", reason)],
            summary);
    }

    private static DrForgeReportDetailView Unavailable(string name, string path, string kind, string reason)
    {
        var summary = string.Join(Environment.NewLine,
            "Local Dr. Forge report preview",
            "Preview is read-only.",
            "Report: " + (string.IsNullOrWhiteSpace(name) ? "Unavailable" : name),
            "Type: " + kind,
            "Preview status: Preview unavailable",
            reason,
            "Reports stay local unless you explicitly export or include them in a support bundle.",
            "No driver install, start, load, or elevation action is performed.");

        return new DrForgeReportDetailView(false, kind, string.IsNullOrWhiteSpace(name) ? "Unavailable" : name, path, 0,
            null, "Unknown", "Unknown", "Unknown", "Unknown", "unknown", null, null, null, "Preview unavailable",
            reason, reason,
            [BuildReportMetadataSection(string.IsNullOrWhiteSpace(name) ? "Unavailable" : name, kind, 0, null, "Preview unavailable", reason)],
            summary);
    }

    private static IReadOnlyList<DrForgeParsedReportSection> BuildParsedSections(
        JsonElement root,
        string name,
        string kind,
        long size,
        DateTimeOffset lastWrite,
        string reportSchema,
        string sourceSchema,
        string generatedAt,
        bool? safetyInvariants,
        bool? kernelDriverLoaded,
        (int Available, int Unavailable)? summaryCounts,
        int driverRequiredUnavailable)
    {
        var sections = new List<DrForgeParsedReportSection>();
        if (IsKnownReportSchema(reportSchema, sourceSchema))
        {
            AddSection(sections, "Report Summary",
            [
                new("Report", name),
                new("Type", kind),
                new("Generated", generatedAt),
                new("Report schema", reportSchema),
                new("Source schema", sourceSchema),
                new("Available readings", FormatNullableCount(summaryCounts?.Available)),
                new("Unavailable readings", FormatNullableCount(summaryCounts?.Unavailable)),
                new("Driver-required readings unavailable", driverRequiredUnavailable.ToString(CultureInfo.InvariantCulture))
            ]);

            AddDeviceSystemSection(sections, root);
            AddCpuSection(sections, root);
            AddMemorySection(sections, root);
            AddStorageSection(sections, root);
            AddBatterySection(sections, root);
            AddThermalsAndSensorsSection(sections, root, driverRequiredUnavailable);
            AddSection(sections, "Driver / Safety Status",
            [
                new("Safety invariant result", FormatReportBool(safetyInvariants)),
                new("Kernel driver loaded", FormatReportBool(kernelDriverLoaded)),
                new("Driver-required readings unavailable", driverRequiredUnavailable.ToString(CultureInfo.InvariantCulture)),
                new("Driver-required reading handling", "Unavailable when not exposed by the saved report"),
                new("Driver action", "No driver install, start, load, or elevation action is performed")
            ]);
        }

        sections.Add(BuildReportMetadataSection(name, kind, size, lastWrite, "Preview ready", "Selected from the app-managed Dr. Forge report folder."));
        return sections;
    }

    private static bool IsKnownReportSchema(string reportSchema, string sourceSchema) =>
        reportSchema.StartsWith("forge-hardware-intake-report/", StringComparison.OrdinalIgnoreCase) ||
        reportSchema.StartsWith("forge-sensor-core-snapshot/", StringComparison.OrdinalIgnoreCase) ||
        sourceSchema.StartsWith("forge-sensor-core-snapshot/", StringComparison.OrdinalIgnoreCase);

    private static void AddDeviceSystemSection(List<DrForgeParsedReportSection> sections, JsonElement root)
    {
        if (!TryGetProperty(root, "platform", out var platform) || platform.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        AddSection(sections, "Device / System",
        [
            new("OS family", FormatNullableString(platform, "osFamily")),
            new("OS version", FormatNullableString(platform, "osVersion")),
            new("Architecture", FormatNullableString(platform, "architecture")),
            new("Manufacturer", FormatNullableString(platform, "manufacturer")),
            new("Model", FormatNullableString(platform, "model"))
        ]);
    }

    private static void AddCpuSection(List<DrForgeParsedReportSection> sections, JsonElement root)
    {
        var fields = new List<DrForgeParsedReportField>();
        if (TryGetProperty(root, "summary", out var summary) && summary.ValueKind == JsonValueKind.Object &&
            HasAnyProperty(summary, "cpuLoadPercent"))
        {
            fields.Add(new("CPU load", FormatNullableNumber(summary, "cpuLoadPercent", "%")));
        }

        if (TryGetProperty(root, "cpu", out var cpu) && cpu.ValueKind == JsonValueKind.Object)
        {
            AddFirstPresentString(fields, "CPU model", cpu, "name", "model", "brand");
            AddFirstPresentNumber(fields, "Physical cores", cpu, null, "physicalCoreCount", "coreCount", "cores");
            AddFirstPresentNumber(fields, "Logical processors", cpu, null, "logicalProcessorCount", "threadCount", "threads");
            AddFirstPresentNumber(fields, "Reported temperature", cpu, "C", "temperatureCelsius", "packageTemperatureCelsius");
        }

        AddSection(sections, "CPU", fields);
    }

    private static void AddMemorySection(List<DrForgeParsedReportSection> sections, JsonElement root)
    {
        var fields = new List<DrForgeParsedReportField>();
        if (TryGetProperty(root, "summary", out var summary) && summary.ValueKind == JsonValueKind.Object &&
            HasAnyProperty(summary, "memoryUsedPercent"))
        {
            fields.Add(new("Memory used", FormatNullableNumber(summary, "memoryUsedPercent", "%")));
        }

        if (TryGetProperty(root, "memory", out var memory) && memory.ValueKind == JsonValueKind.Object)
        {
            AddFirstPresentBytes(fields, "Total memory", memory, "totalBytes", "installedBytes");
            AddFirstPresentBytes(fields, "Used memory", memory, "usedBytes");
            AddFirstPresentBytes(fields, "Available memory", memory, "availableBytes", "freeBytes");
            AddFirstPresentNumber(fields, "Memory modules", memory, null, "moduleCount", "modules");
        }

        AddSection(sections, "Memory", fields);
    }

    private static void AddStorageSection(List<DrForgeParsedReportSection> sections, JsonElement root)
    {
        var fields = new List<DrForgeParsedReportField>();
        if (TryGetProperty(root, "summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
        {
            if (HasAnyProperty(summary, "storageCapacityBytes"))
            {
                fields.Add(new("Storage capacity", FormatNullableBytes(summary, "storageCapacityBytes")));
            }

            if (HasAnyProperty(summary, "storageSmartHealth"))
            {
                fields.Add(new("Storage SMART health", FormatNullableString(summary, "storageSmartHealth")));
            }
        }

        if (TryGetProperty(root, "storage", out var storage))
        {
            if (storage.ValueKind == JsonValueKind.Object)
            {
                AddFirstPresentBytes(fields, "Reported capacity", storage, "capacityBytes", "totalBytes");
                AddFirstPresentString(fields, "Reported health", storage, "smartHealth", "health");
                AddFirstPresentNumber(fields, "Drive count", storage, null, "driveCount", "diskCount");
            }
            else if (storage.ValueKind == JsonValueKind.Array)
            {
                fields.Add(new("Storage devices listed", storage.GetArrayLength().ToString(CultureInfo.InvariantCulture)));
                foreach (var (drive, index) in storage.EnumerateArray().Take(3).Select((item, index) => (item, index + 1)))
                {
                    if (drive.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var model = ReadFirstString(drive, "model", "name") ?? "Unknown";
                    var health = ReadFirstString(drive, "smartHealth", "health") ?? "Unknown";
                    fields.Add(new("Storage device " + index.ToString(CultureInfo.InvariantCulture), model + "; health: " + health));
                }
            }
        }

        AddSection(sections, "Storage", fields);
    }

    private static void AddBatterySection(List<DrForgeParsedReportSection> sections, JsonElement root)
    {
        if (!TryGetProperty(root, "battery", out var battery) || battery.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var fields = new List<DrForgeParsedReportField>();
        AddFirstPresentNumber(fields, "Charge", battery, "%", "chargePercent", "remainingPercent");
        AddFirstPresentNumber(fields, "Health", battery, "%", "healthPercent", "wearLevelPercent");
        AddFirstPresentNumber(fields, "Cycle count", battery, null, "cycleCount");
        AddFirstPresentString(fields, "Status", battery, "status", "state");
        AddFirstPresentNumber(fields, "Design capacity", battery, "Wh", "designCapacityWh");
        AddFirstPresentNumber(fields, "Full charge capacity", battery, "Wh", "fullChargeCapacityWh");
        AddSection(sections, "Battery", fields);
    }

    private static void AddThermalsAndSensorsSection(
        List<DrForgeParsedReportSection> sections,
        JsonElement root,
        int driverRequiredUnavailable)
    {
        var fields = new List<DrForgeParsedReportField>();
        if (TryGetProperty(root, "summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
        {
            AddFirstPresentNumber(fields, "CPU temperature", summary, "C", "cpuTemperatureCelsius", "cpuPackageTemperatureCelsius");
            AddFirstPresentNumber(fields, "GPU temperature", summary, "C", "gpuTemperatureCelsius");
        }

        if (TryGetProperty(root, "thermals", out var thermals) && thermals.ValueKind == JsonValueKind.Object)
        {
            AddFirstPresentNumber(fields, "Thermal zones", thermals, null, "zoneCount");
            AddFirstPresentString(fields, "Thermal status", thermals, "status", "state");
        }

        if (TryGetProperty(root, "readings", out var readings) && readings.ValueKind == JsonValueKind.Array)
        {
            fields.Add(new("Readings listed", readings.GetArrayLength().ToString(CultureInfo.InvariantCulture)));
        }

        if (driverRequiredUnavailable > 0)
        {
            fields.Add(new("Driver-required unavailable readings", driverRequiredUnavailable.ToString(CultureInfo.InvariantCulture)));
        }

        AddGapFields(fields, root, "ring0Gaps", "Driver-required gap", "reading", "displayName", includeReason: true);
        AddGapFields(fields, root, "wouldUnlock", "Driver-status gap", "displayName", "gapReadingId", includeReason: false);
        AddSection(sections, "Thermals / Sensors", fields);
    }

    private static DrForgeParsedReportSection BuildReportMetadataSection(
        string name,
        string kind,
        long size,
        DateTimeOffset? lastWrite,
        string previewStatus,
        string note)
    {
        var fields = new List<DrForgeParsedReportField>
        {
            new("File name", string.IsNullOrWhiteSpace(name) ? "Unavailable" : name),
            new("Type", string.IsNullOrWhiteSpace(kind) ? "Report" : kind),
            new("Modified", lastWrite.HasValue ? FormatUtc(lastWrite.Value) : "Unknown"),
            new("File size", FormatBytes(size)),
            new("Preview status", previewStatus),
            new("Local path scope", "App-managed Dr. Forge report folder only"),
            new("Preview limits", "JSON 512 KiB parse cap; Markdown 64 KiB read cap; preview 4000 characters"),
            new("Archive handling", kind.Equals("Archive", StringComparison.OrdinalIgnoreCase) ? "Metadata only; no extraction" : "No archive extraction performed"),
            new("Note", note)
        };

        return new DrForgeParsedReportSection("Report Metadata", fields);
    }

    private static void AddSection(
        List<DrForgeParsedReportSection> sections,
        string title,
        IEnumerable<DrForgeParsedReportField> fields)
    {
        var availableFields = fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Name) && !string.IsNullOrWhiteSpace(field.Value))
            .ToList();
        if (availableFields.Count > 0)
        {
            sections.Add(new DrForgeParsedReportSection(title, availableFields));
        }
    }

    private static void AddParsedSummaryLines(
        List<string> summaryLines,
        IReadOnlyList<DrForgeParsedReportSection> sections)
    {
        var fieldLines = sections
            .Where(section => !section.Title.Equals("Report Metadata", StringComparison.OrdinalIgnoreCase))
            .SelectMany(section => section.Fields.Take(6).Select(field => "- " + section.Title + " / " + field.Name + ": " + field.Value))
            .Take(24)
            .ToList();

        if (fieldLines.Count == 0)
        {
            return;
        }

        summaryLines.Add("Parsed report fields:");
        summaryLines.AddRange(fieldLines);
    }

    private static string BuildRawPreview(string text)
    {
        var preview = CapPreview(text, out var capped);
        return capped ? preview + Environment.NewLine + "[Preview capped for safety.]" : preview;
    }

    private static (int Available, int Unavailable)? CountSummaryReadings(JsonElement root)
    {
        if (!TryGetProperty(root, "summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var available = 0;
        var unavailable = 0;
        foreach (var (propertyName, _) in KnownSummaryReadings)
        {
            if (!TryGetProperty(summary, propertyName, out var value) ||
                value.ValueKind == JsonValueKind.Null ||
                value.ValueKind == JsonValueKind.Undefined ||
                (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString())))
            {
                unavailable++;
                continue;
            }

            available++;
        }

        return (available, unavailable);
    }

    private static int CountDriverRequiredUnavailable(JsonElement root)
    {
        var count = 0;
        if (TryGetProperty(root, "ring0Gaps", out var gaps) && gaps.ValueKind == JsonValueKind.Array)
        {
            count += gaps.GetArrayLength();
        }

        if (TryGetProperty(root, "wouldUnlock", out var wouldUnlock) && wouldUnlock.ValueKind == JsonValueKind.Array)
        {
            count += wouldUnlock.GetArrayLength();
        }

        return count;
    }

    private static string TryReadSafety(JsonElement root, out bool? invariants, out bool? kernelDriverLoaded)
    {
        invariants = null;
        kernelDriverLoaded = null;
        if (!TryGetProperty(root, "safety", out var safety) || safety.ValueKind != JsonValueKind.Object)
        {
            return "Unknown";
        }

        invariants = ReadBool(safety, "satisfiesSafetyInvariants");
        kernelDriverLoaded = ReadBool(safety, "kernelDriverLoaded");
        return "safety invariants: " + FormatNullableBool(invariants) + "; kernel driver loaded: " + FormatNullableBool(kernelDriverLoaded);
    }

    private static string BuildJsonPreview(JsonElement root)
    {
        var sb = new StringBuilder();
        if (TryGetProperty(root, "summary", out var summary) && summary.ValueKind == JsonValueKind.Object)
        {
            sb.AppendLine("Summary readings:");
            foreach (var (propertyName, displayName) in KnownSummaryReadings)
            {
                var value = TryGetProperty(summary, propertyName, out var reading)
                    ? FormatJsonValue(reading)
                    : "Unavailable";
                sb.AppendLine("- " + displayName + ": " + value);
            }
        }

        if (TryGetProperty(root, "findings", out var findings) && findings.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine("Findings:");
            foreach (var finding in findings.EnumerateArray().Take(12))
            {
                sb.AppendLine("- " + FormatFinding(finding));
            }
        }

        if (TryGetProperty(root, "ring0Gaps", out var gaps) && gaps.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine("Driver-required unavailable readings:");
            foreach (var gap in gaps.EnumerateArray().Take(12))
            {
                var reading = gap.ValueKind == JsonValueKind.Object
                    ? ReadString(gap, "reading") ?? ReadString(gap, "displayName") ?? "Unavailable reading"
                    : "Unavailable reading";
                var reason = gap.ValueKind == JsonValueKind.Object
                    ? ReadString(gap, "reason") ?? "Unavailable"
                    : "Unavailable";
                sb.AppendLine("- " + reading + ": " + reason);
            }
        }

        if (TryGetProperty(root, "wouldUnlock", out var wouldUnlock) && wouldUnlock.ValueKind == JsonValueKind.Array)
        {
            sb.AppendLine("Driver-status unavailable readings:");
            foreach (var gap in wouldUnlock.EnumerateArray().Take(12))
            {
                var reading = gap.ValueKind == JsonValueKind.Object
                    ? ReadString(gap, "displayName") ?? ReadString(gap, "gapReadingId") ?? "Unavailable reading"
                    : "Unavailable reading";
                sb.AppendLine("- " + reading + ": Unavailable");
            }
        }

        var text = sb.Length == 0 ? "No previewable Dr. Forge report sections were found." : sb.ToString().TrimEnd();
        var preview = CapPreview(text, out var capped);
        return capped ? preview + Environment.NewLine + "[Preview capped for safety.]" : preview;
    }

    private static string FormatFinding(JsonElement finding)
    {
        if (finding.ValueKind != JsonValueKind.Object)
        {
            return FormatJsonValue(finding);
        }

        var severity = ReadString(finding, "severity") ?? "Info";
        var message = ReadString(finding, "message") ?? "Unavailable";
        return severity + ": " + message;
    }

    private static void AddFirstPresentString(
        List<DrForgeParsedReportField> fields,
        string displayName,
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out _))
            {
                continue;
            }

            fields.Add(new DrForgeParsedReportField(displayName, FormatNullableString(element, propertyName)));
            return;
        }
    }

    private static void AddFirstPresentNumber(
        List<DrForgeParsedReportField> fields,
        string displayName,
        JsonElement element,
        string? unit,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out _))
            {
                continue;
            }

            fields.Add(new DrForgeParsedReportField(displayName, FormatNullableNumber(element, propertyName, unit)));
            return;
        }
    }

    private static void AddFirstPresentBytes(
        List<DrForgeParsedReportField> fields,
        string displayName,
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetProperty(element, propertyName, out _))
            {
                continue;
            }

            fields.Add(new DrForgeParsedReportField(displayName, FormatNullableBytes(element, propertyName)));
            return;
        }
    }

    private static void AddGapFields(
        List<DrForgeParsedReportField> fields,
        JsonElement root,
        string propertyName,
        string displayPrefix,
        string primaryNameProperty,
        string fallbackNameProperty,
        bool includeReason)
    {
        if (!TryGetProperty(root, propertyName, out var gaps) || gaps.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var (gap, index) in gaps.EnumerateArray().Take(6).Select((item, index) => (item, index + 1)))
        {
            if (gap.ValueKind != JsonValueKind.Object)
            {
                fields.Add(new DrForgeParsedReportField(
                    displayPrefix + " " + index.ToString(CultureInfo.InvariantCulture),
                    "Unavailable"));
                continue;
            }

            var reading = ReadString(gap, primaryNameProperty) ??
                          ReadString(gap, fallbackNameProperty) ??
                          "Unavailable reading";
            var value = includeReason
                ? reading + ": " + (ReadString(gap, "reason") ?? "Unavailable")
                : reading + ": Unavailable";
            fields.Add(new DrForgeParsedReportField(
                displayPrefix + " " + index.ToString(CultureInfo.InvariantCulture),
                value));
        }
    }

    private static bool HasAnyProperty(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetProperty(element, propertyName, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static string? ReadFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = ReadString(element, propertyName);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string FormatNullableString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            return "Unavailable";
        }

        return value.GetString()!;
    }

    private static string FormatNullableNumber(JsonElement element, string propertyName, string? unit)
    {
        if (!TryGetProperty(element, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "Unavailable";
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
        {
            return "Unavailable";
        }

        var formatted = number.ToString("0.###", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(unit) ? formatted : formatted + " " + unit;
    }

    private static string FormatNullableBytes(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return "Unavailable";
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var bytes)
            ? FormatBytes(Math.Max(0, bytes))
            : "Unavailable";
    }

    private static string FormatReportBool(bool? value) => value switch
    {
        true => "Yes",
        false => "No",
        null => "Unknown"
    };

    private static string FormatJsonValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "Unavailable",
        JsonValueKind.Undefined => "Unavailable",
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? "Unavailable" : value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "yes",
        JsonValueKind.False => "no",
        _ => "Unavailable"
    };

    private static string FormatNullableBool(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "unknown"
    };

    private static string FormatNullableCount(int? value) =>
        value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "Unknown";

    private static string FormatGeneratedAt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var generatedAt)
            ? FormatUtc(generatedAt)
            : value;
    }

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string InferKind(string name, bool isDirectory)
    {
        if (isDirectory || name.StartsWith("drforge-intake-archive-", StringComparison.OrdinalIgnoreCase))
        {
            return "Archive";
        }

        if (name.StartsWith("drforge-sensor-core-snapshot-", StringComparison.OrdinalIgnoreCase))
        {
            return "Snapshot";
        }

        return "Report";
    }

    private static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " bytes";
        }

        var kib = bytes / 1024d;
        if (kib < 1024)
        {
            return kib.ToString("0.#", CultureInfo.InvariantCulture) + " KiB";
        }

        return (kib / 1024d).ToString("0.##", CultureInfo.InvariantCulture) + " MiB";
    }

    private static string CapPreview(string text, out bool capped)
    {
        capped = text.Length > MaxPreviewCharacters;
        return capped ? text[..MaxPreviewCharacters] : text;
    }

    private static string ReadTextPrefixFromFile(string path, int byteCount)
    {
        if (byteCount <= 0)
        {
            return string.Empty;
        }

        using var stream = File.OpenRead(path);
        var buffer = new byte[byteCount];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string SafeFileName(string? path)
    {
        try
        {
            return string.IsNullOrWhiteSpace(path) ? "Unavailable" : Path.GetFileName(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Unavailable";
        }
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public sealed class DrForgeDriverStatusReader
{
    private const string CurrentSchema = "forger-sensor-driver-preflight/1.1";

    public DrForgeDriverStatusView ReadJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Unavailable("Driver status: not reported by this Dr. Forge CLI.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var schema = ReadString(root, "schemaVersion") ?? "Unavailable";
            var readiness = ReadString(root, "readiness") ?? "Unavailable";
            var supported = string.Equals(schema, CurrentSchema, StringComparison.Ordinal);
            if (!supported)
            {
                return new DrForgeDriverStatusView(
                    schema,
                    readiness,
                    false,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    0,
                    $"Driver status: unsupported schema {schema}; detailed fields ignored.");
            }

            var productionDriverShipped = ReadBool(root, "productionDriverShipped");
            var driverSupportCompiledIn = ReadBool(root, "driverSupportCompiledIn");
            var userModeFallbackActive = ReadBool(root, "userModeFallbackActive");
            var absenceIsNormal = ReadBool(root, "absenceIsNormal");
            var installed = ReadDriverCheck(root, "driver installed");
            var running = ReadDriverCheck(root, "driver running");
            var noAction = ReadNoDriverActionTaken(root);
            var unavailableCount = ReadWouldUnlockCount(root);

            var summary = string.Join("; ",
                "Driver status: " + schema,
                "production driver shipped: " + FormatBool(productionDriverShipped),
                "driver installed: " + FormatBool(installed),
                "driver running: " + FormatBool(running),
                "user-mode fallback active: " + FormatBool(userModeFallbackActive),
                "no driver action taken: " + FormatBool(noAction),
                "driver-required readings unavailable: " + unavailableCount.ToString(CultureInfo.InvariantCulture));

            return new DrForgeDriverStatusView(
                schema,
                readiness,
                true,
                productionDriverShipped,
                driverSupportCompiledIn,
                installed,
                running,
                userModeFallbackActive,
                absenceIsNormal,
                noAction,
                unavailableCount,
                summary);
        }
        catch (JsonException)
        {
            return Unavailable("Driver status: JSON could not be parsed; treating driver status as unavailable.");
        }
    }

    private static DrForgeDriverStatusView Unavailable(string summary) =>
        new("Unavailable", "Unavailable", false, null, null, null, null, null, null, null, 0, summary);

    private static bool? ReadDriverCheck(JsonElement root, string checkName)
    {
        if (!TryGetProperty(root, "checks", out var checks) || checks.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var check in checks.EnumerateArray())
        {
            if (!string.Equals(ReadString(check, "name"), checkName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var outcome = ReadString(check, "outcome");
            var detail = ReadString(check, "detail") ?? string.Empty;
            if (string.Equals(outcome, "pass", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (detail.Contains("No driver is installed", StringComparison.OrdinalIgnoreCase) ||
                detail.Contains("No driver is running", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(outcome, "not-applicable", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        return null;
    }

    private static bool? ReadNoDriverActionTaken(JsonElement root)
    {
        var safetyNote = ReadString(root, "safetyNote") ?? string.Empty;
        if (safetyNote.Contains("Nothing was installed", StringComparison.OrdinalIgnoreCase) &&
            safetyNote.Contains("no elevation was requested", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (safetyNote.Contains("installed", StringComparison.OrdinalIgnoreCase) ||
            safetyNote.Contains("started", StringComparison.OrdinalIgnoreCase) ||
            safetyNote.Contains("loaded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private static int ReadWouldUnlockCount(JsonElement root)
    {
        if (!TryGetProperty(root, "wouldUnlock", out var wouldUnlock) || wouldUnlock.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        return wouldUnlock.GetArrayLength();
    }

    private static string FormatBool(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "unavailable"
    };

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public sealed class DrForgeCliRunner
{
    public static readonly TimeSpan ReadinessTimeout = TimeSpan.FromSeconds(15);
    public static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(120);
    public static readonly TimeSpan ReportTransformTimeout = TimeSpan.FromSeconds(30);

    private readonly IDrForgeProcessRunner _processRunner;

    public DrForgeCliRunner(IDrForgeProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new DrForgeProcessRunner();
    }

    public static IReadOnlyList<string> BuildVersionArguments() => ["--version"];

    public static IReadOnlyList<string> BuildSensorCoreHelpArguments() => ["sensor-core", "--help"];

    public static IReadOnlyList<string> BuildDriverStatusArguments() => ["sensors", "driver-status", "--json"];

    public static IReadOnlyList<string> BuildSnapshotArguments() => ["sensor-core", "snapshot", "--json"];

    public static IReadOnlyList<string> BuildReportArguments(string snapshotPath, string outputPath) =>
        ["sensor-core", "report", snapshotPath, "--format", "json", "--out", outputPath];

    public static IReadOnlyList<string> BuildArchiveArguments(string snapshotPath, string outputDirectory) =>
        ["sensor-core", "archive", snapshotPath, "--out", outputDirectory];

    public async Task<DrForgeCliOperationResult> CheckReadinessAsync(
        string executablePath,
        CancellationToken cancellationToken = default)
    {
        var version = await RunAsync(executablePath, BuildVersionArguments(), ReadinessTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!version.Succeeded)
        {
            return Failure(DrForgeCliBridgeState.Failed, "Dr. Forge --version failed.", null, version);
        }

        var versionInfo = new DrForgeCliVersionReader().ReadText(version.StandardOutput);
        var help = await RunAsync(executablePath, BuildSensorCoreHelpArguments(), ReadinessTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!help.Succeeded)
        {
            return Failure(DrForgeCliBridgeState.Failed, "Dr. Forge sensor-core help failed.", null, help);
        }

        var driverStatusResult = await RunAsync(executablePath, BuildDriverStatusArguments(), ReadinessTimeout, cancellationToken)
            .ConfigureAwait(false);
        var driverStatus = driverStatusResult.Succeeded
            ? new DrForgeDriverStatusReader().ReadJson(driverStatusResult.StandardOutput)
            : new DrForgeDriverStatusReader().ReadJson(null);
        var statusSuffix = driverStatusResult.Succeeded
            ? " " + driverStatus.SummaryText
            : " Driver status: not reported by this CLI; continuing with the user-mode bridge.";

        return new DrForgeCliOperationResult(
            true,
            DrForgeCliBridgeState.Ready,
            (FirstNonEmptyLine(version.StandardOutput) ?? "Dr. Forge CLI is ready.") + statusSuffix,
            null,
            help)
        {
            VersionInfo = versionInfo,
            DriverStatus = driverStatus
        };
    }

    public async Task<DrForgeCliOperationResult> CaptureSnapshotAsync(
        string executablePath,
        string snapshotOutputPath,
        CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(executablePath, BuildSnapshotArguments(), SnapshotTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return Failure(DrForgeCliBridgeState.Failed, "Dr. Forge snapshot capture failed.", snapshotOutputPath, result);
        }

        if (string.IsNullOrWhiteSpace(result.StandardOutput) ||
            !result.StandardOutput.TrimStart().StartsWith("{", StringComparison.Ordinal))
        {
            return Failure(DrForgeCliBridgeState.Failed, "Dr. Forge snapshot did not return JSON.", snapshotOutputPath, result);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshotOutputPath))!);
        await File.WriteAllTextAsync(snapshotOutputPath, result.StandardOutput, Encoding.UTF8, cancellationToken)
            .ConfigureAwait(false);

        return new DrForgeCliOperationResult(true, DrForgeCliBridgeState.RunningIntake,
            "Dr. Forge snapshot captured.", snapshotOutputPath, result);
    }

    public async Task<DrForgeCliOperationResult> GenerateReportAsync(
        string executablePath,
        string snapshotPath,
        string reportOutputPath,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportOutputPath))!);
        var result = await RunAsync(
                executablePath,
                BuildReportArguments(snapshotPath, reportOutputPath),
                ReportTransformTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded && File.Exists(reportOutputPath)
            ? new DrForgeCliOperationResult(true, DrForgeCliBridgeState.ReportReady,
                "Dr. Forge intake report is ready.", reportOutputPath, result)
            : Failure(DrForgeCliBridgeState.Failed, "Dr. Forge report generation failed.", reportOutputPath, result);
    }

    public async Task<DrForgeCliOperationResult> GenerateArchiveAsync(
        string executablePath,
        string snapshotPath,
        string archiveOutputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(archiveOutputDirectory);
        var result = await RunAsync(
                executablePath,
                BuildArchiveArguments(snapshotPath, archiveOutputDirectory),
                ReportTransformTimeout,
                cancellationToken)
            .ConfigureAwait(false);

        return result.Succeeded && Directory.Exists(archiveOutputDirectory)
            ? new DrForgeCliOperationResult(true, DrForgeCliBridgeState.ArchiveReady,
                "Dr. Forge intake archive is ready.", archiveOutputDirectory, result)
            : Failure(DrForgeCliBridgeState.Failed, "Dr. Forge archive generation failed.", archiveOutputDirectory, result);
    }

    private async Task<DrForgeCliProcessResult> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
        {
            return new DrForgeCliProcessResult(executablePath, arguments, 1, false, string.Empty,
                "Dr. Forge CLI executable was not found.");
        }

        return await _processRunner.RunAsync(executablePath, arguments, timeout, cancellationToken)
            .ConfigureAwait(false);
    }

    private static DrForgeCliOperationResult Failure(
        DrForgeCliBridgeState state,
        string message,
        string? outputPath,
        DrForgeCliProcessResult result)
    {
        var detail = result.TimedOut
            ? message + " The command timed out."
            : !string.IsNullOrWhiteSpace(result.StandardError)
                ? message + " " + result.StandardError
                : message;

        return new DrForgeCliOperationResult(false, state, detail, outputPath, result);
    }

    private static string? FirstNonEmptyLine(string text) =>
        text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
            ?.Trim();
}

public sealed record DrForgeReadingView(string Name, string Value);

public sealed record DrForgeIntakeReportView(
    string ReportSchema,
    string SourceSchema,
    string Platform,
    string SafetyMode,
    IReadOnlyList<DrForgeReadingView> KeyReadings,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Ring0Gaps,
    string SummaryText);

public sealed class DrForgeIntakeResultReader
{
    public DrForgeIntakeReportView ReadReport(string reportPath)
    {
        var json = File.ReadAllText(reportPath);
        return ReadJson(json);
    }

    public DrForgeIntakeReportView ReadJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var reportSchema = ReadString(root, "reportSchemaVersion") ?? "Unavailable";
        var sourceSchema = ReadString(root, "sourceSchemaVersion") ?? "Unavailable";
        var platform = FormatPlatform(root);
        var safety = FormatSafety(root);
        var readings = ReadKeyReadings(root);
        var findings = ReadFindings(root);
        var notes = ReadStringArray(root, "notes");
        var gaps = ReadRing0Gaps(root);

        var sb = new StringBuilder();
        sb.AppendLine("Dr. Forge Intake Summary");
        sb.AppendLine($"Report schema: {reportSchema}");
        sb.AppendLine($"Source schema: {sourceSchema}");
        sb.AppendLine($"Platform: {platform}");
        sb.AppendLine($"Safety mode: {safety}");
        sb.AppendLine("Key readings:");
        foreach (var reading in readings)
        {
            sb.AppendLine($"- {reading.Name}: {reading.Value}");
        }

        sb.AppendLine("Findings:");
        foreach (var finding in findings.DefaultIfEmpty("None reported."))
        {
            sb.AppendLine($"- {finding}");
        }

        sb.AppendLine("Notes:");
        foreach (var note in notes.DefaultIfEmpty("None reported."))
        {
            sb.AppendLine($"- {note}");
        }

        sb.AppendLine("Ring-0/deep telemetry gaps:");
        foreach (var gap in gaps.DefaultIfEmpty("None reported by this intake report."))
        {
            sb.AppendLine($"- {gap}");
        }

        return new DrForgeIntakeReportView(reportSchema, sourceSchema, platform, safety, readings, findings, notes, gaps,
            sb.ToString().TrimEnd());
    }

    private static string FormatPlatform(JsonElement root)
    {
        if (!TryGetProperty(root, "platform", out var platform) || platform.ValueKind != JsonValueKind.Object)
        {
            return "Unavailable";
        }

        var os = ReadString(platform, "osFamily") ?? "Unavailable";
        var arch = ReadString(platform, "architecture") ?? "Unavailable";
        return $"{os} / {arch}";
    }

    private static string FormatSafety(JsonElement root)
    {
        if (!TryGetProperty(root, "safety", out var safety) || safety.ValueKind != JsonValueKind.Object)
        {
            return "User-mode; safety status unavailable";
        }

        var pass = ReadBool(safety, "satisfiesSafetyInvariants");
        var driver = ReadBool(safety, "kernelDriverLoaded");
        return $"User-mode; safety invariants: {FormatBool(pass)}; kernel driver loaded: {FormatBool(driver)}";
    }

    private static IReadOnlyList<DrForgeReadingView> ReadKeyReadings(JsonElement root)
    {
        if (!TryGetProperty(root, "summary", out var summary) || summary.ValueKind != JsonValueKind.Object)
        {
            return
            [
                new("CPU load", "Unavailable"),
                new("Memory used", "Unavailable"),
                new("Storage capacity", "Unavailable"),
                new("Storage SMART health", "Unavailable")
            ];
        }

        return
        [
            new("CPU load", FormatNullableNumber(summary, "cpuLoadPercent", "%")),
            new("Memory used", FormatNullableNumber(summary, "memoryUsedPercent", "%")),
            new("Storage capacity", FormatNullableNumber(summary, "storageCapacityBytes", "bytes")),
            new("Storage SMART health", FormatNullableString(summary, "storageSmartHealth"))
        ];
    }

    private static IReadOnlyList<string> ReadFindings(JsonElement root)
    {
        if (!TryGetProperty(root, "findings", out var findings) || findings.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return findings.EnumerateArray()
            .Select(f =>
            {
                var severity = ReadString(f, "severity") ?? "Info";
                var message = ReadString(f, "message") ?? "Unavailable";
                return $"{severity}: {message}";
            })
            .ToList();
    }

    private static IReadOnlyList<string> ReadRing0Gaps(JsonElement root)
    {
        if (!TryGetProperty(root, "ring0Gaps", out var gaps) || gaps.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return gaps.EnumerateArray()
            .Select(g =>
            {
                var reading = ReadString(g, "reading") ?? "Unavailable reading";
                var reason = ReadString(g, "reason") ?? "Unavailable";
                return $"{reading}: {reason}";
            })
            .ToList();
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static string FormatNullableNumber(JsonElement element, string propertyName, string unit)
    {
        if (!TryGetProperty(element, propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return "Unavailable";
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? $"{number.ToString("0.###", CultureInfo.InvariantCulture)} {unit}"
            : "Unavailable";
    }

    private static string FormatNullableString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            return "Unavailable";
        }

        return value.GetString()!;
    }

    private static string FormatBool(bool? value) => value switch
    {
        true => "yes",
        false => "no",
        null => "Unavailable"
    };

    private static bool? ReadBool(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        TryGetProperty(element, propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }
}

public sealed class DrForgeCliSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string SelectedExecutablePath { get; set; } = string.Empty;
    public string LastReadinessState { get; set; } = DrForgeCliBridgeState.NotConfigured.ToString();
    public string LastReportPath { get; set; } = string.Empty;
    public string LastArchivePath { get; set; } = string.Empty;
}

public sealed class DrForgeCliSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public DrForgeCliSettingsStore(string path)
    {
        _path = path;
    }

    public DrForgeCliSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new DrForgeCliSettings();
            }

            return JsonSerializer.Deserialize<DrForgeCliSettings>(File.ReadAllText(_path), Options)
                   ?? new DrForgeCliSettings();
        }
        catch
        {
            return new DrForgeCliSettings();
        }
    }

    public void Save(DrForgeCliSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options), Encoding.UTF8);
    }
}
