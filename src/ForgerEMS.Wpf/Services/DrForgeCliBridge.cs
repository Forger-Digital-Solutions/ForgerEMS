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
    public DrForgeDriverStatusView? DriverStatus { get; init; }
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
