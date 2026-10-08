using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Services.WindowsMaintenance;

public enum WindowsMaintenanceFailureKind
{
    None,
    BackendUnavailable,
    ScriptMissing,
    ChecksumMismatch,
    Timeout,
    MalformedJson,
    SchemaMismatch,
    Cancelled,
    Failed
}

public sealed record WindowsMaintenanceScanResult
{
    public required bool Succeeded { get; init; }
    public WindowsMaintenanceSnapshot? Snapshot { get; init; }
    public WindowsMaintenanceFailureKind FailureKind { get; init; } = WindowsMaintenanceFailureKind.None;
    public string? FailureReason { get; init; }
    public bool UsedCachedSnapshot { get; init; }

    public static WindowsMaintenanceScanResult Failure(
        WindowsMaintenanceFailureKind kind, string reason) =>
        new() { Succeeded = false, FailureKind = kind, FailureReason = reason };
}

public interface IWindowsMaintenanceService
{
    /// <summary>The last successfully validated snapshot; never replaced by failures.</summary>
    WindowsMaintenanceSnapshot? LastGoodSnapshot { get; }

    /// <summary>
    /// Run the fixed backend/Get-ForgerEMSWindowsMaintenance.ps1 data recipe (script path
    /// derived from <see cref="BackendContext.UpdateScriptPath"/>) and deserialize the exact
    /// schema-1 JSON. Read-only; never installs or downloads updates.
    /// </summary>
    Task<WindowsMaintenanceScanResult> ScanAsync(
        BackendContext context,
        bool checkUpdates,
        bool offline,
        CancellationToken cancellationToken = default);
}

public sealed class WindowsMaintenanceService : IWindowsMaintenanceService
{
    public const string ScriptFileName = "Get-ForgerEMSWindowsMaintenance.ps1";
    public const int SupportedSchemaVersion = 1;
    private const long MaxJsonBytes = 8L * 1024 * 1024;
    private static readonly TimeSpan ScanTimeout = TimeSpan.FromSeconds(120);

    private readonly IPowerShellRunnerService _powerShellRunner;

    public WindowsMaintenanceService(IPowerShellRunnerService powerShellRunner)
    {
        _powerShellRunner = powerShellRunner;
    }

    public WindowsMaintenanceSnapshot? LastGoodSnapshot { get; private set; }

    public async Task<WindowsMaintenanceScanResult> ScanAsync(
        BackendContext context,
        bool checkUpdates,
        bool offline,
        CancellationToken cancellationToken = default)
    {
        if (!context.IsAvailable || string.IsNullOrWhiteSpace(context.UpdateScriptPath))
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.BackendUnavailable,
                context.DiagnosticMessage);
        }

        var backendDir = Path.GetDirectoryName(context.UpdateScriptPath);
        if (string.IsNullOrWhiteSpace(backendDir))
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.BackendUnavailable,
                "Could not determine the backend script directory.");
        }

        var scriptPath = Path.Combine(backendDir, ScriptFileName);
        if (!File.Exists(scriptPath))
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.ScriptMissing,
                $"Bundled maintenance script not found at {scriptPath}.");
        }

        // Bundled backends ship CHECKSUMS.sha256; validate the script's entry when present.
        var checksumCatalog = Path.Combine(backendDir, "CHECKSUMS.sha256");
        if (File.Exists(checksumCatalog) && !ValidateScriptChecksum(backendDir, scriptPath, checksumCatalog, out var checksumError))
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.ChecksumMismatch, checksumError);
        }

        var arguments = new List<string>();
        if (checkUpdates)
        {
            arguments.Add("-CheckUpdates");
        }

        if (offline)
        {
            arguments.Add("-Offline");
        }

        PowerShellRunResult result;
        try
        {
            result = await _powerShellRunner.RunAsync(
                new PowerShellRunRequest
                {
                    DisplayName = "Windows maintenance scan",
                    WorkingDirectory = backendDir,
                    ScriptPath = scriptPath,
                    Arguments = arguments,
                    Timeout = ScanTimeout,
                    HeartbeatKind = PowerShellHeartbeatKind.LongRunningScan
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.Cancelled, "Scan cancelled.");
        }
        catch (Exception ex)
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.Failed,
                $"Maintenance runner failed: {ex.Message}");
        }

        if (result.TimedOut)
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.Timeout,
                $"Maintenance scan exceeded the {ScanTimeout.TotalSeconds}s timeout.");
        }

        if (!result.Succeeded)
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.Failed,
                $"Maintenance script exited with code {result.ExitCode}.");
        }

        var json = ExtractJson(result.StandardOutputText);
        if (json is null)
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.MalformedJson,
                "Script produced no JSON payload.");
        }

        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.MalformedJson,
                "Script JSON payload exceeded the 8 MiB cap.");
        }

        WindowsMaintenanceSnapshot? snapshot;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return WindowsMaintenanceScanResult.Failure(
                    WindowsMaintenanceFailureKind.MalformedJson,
                    "Maintenance payload is not a JSON object.");
            }

            if (!doc.RootElement.TryGetProperty("SchemaVersion", out var schemaEl)
                || schemaEl.ValueKind != JsonValueKind.Number
                || !schemaEl.TryGetInt32(out var schemaVersion)
                || schemaVersion != SupportedSchemaVersion)
            {
                return WindowsMaintenanceScanResult.Failure(
                    WindowsMaintenanceFailureKind.SchemaMismatch,
                    "Maintenance snapshot SchemaVersion is not 1.");
            }

            if (!ValidateMandatorySections(doc.RootElement, out var sectionError))
            {
                return WindowsMaintenanceScanResult.Failure(
                    WindowsMaintenanceFailureKind.SchemaMismatch, sectionError);
            }

            snapshot = JsonSerializer.Deserialize<WindowsMaintenanceSnapshot>(json);
            if (snapshot is null)
            {
                return WindowsMaintenanceScanResult.Failure(
                    WindowsMaintenanceFailureKind.MalformedJson, "Empty maintenance snapshot.");
            }
        }
        catch (JsonException ex)
        {
            return WindowsMaintenanceScanResult.Failure(
                WindowsMaintenanceFailureKind.MalformedJson, ex.Message);
        }

        // LastGoodSnapshot is only replaced by a fully validated snapshot.
        LastGoodSnapshot = snapshot;
        return new WindowsMaintenanceScanResult { Succeeded = true, Snapshot = snapshot };
    }

    private static bool ValidateMandatorySections(JsonElement root, out string error)
    {
        // OperatingSystem is the ONLY section permitted to be null — an honest probe
        // failure. Every other mandatory section must be present with its proper kind;
        // a null list/object would crash the display mapper on accepted data.
        foreach (var section in new[] { "OperatingSystem", "Services", "Reboot", "Policy", "Bindings", "DeviceProblems", "DriverStore", "Updates", "Errors" })
        {
            if (!root.TryGetProperty(section, out var value))
            {
                error = $"Maintenance snapshot is missing mandatory section '{section}'.";
                return false;
            }

            var expectedKind = section is "Services" or "Bindings" or "Errors" or "DeviceProblems"
                ? JsonValueKind.Array
                : JsonValueKind.Object;
            var allowNull = section is "OperatingSystem";
            if (value.ValueKind != expectedKind && !(allowNull && value.ValueKind == JsonValueKind.Null))
            {
                error = $"Maintenance section '{section}' is missing or has unexpected JSON kind {value.ValueKind}.";
                return false;
            }
        }

        // Nested mandatory lists/objects must also be present — never null.
        foreach (var (section, member, kind) in new (string, string, JsonValueKind)[]
        {
            ("Reboot", "Indicators", JsonValueKind.Array),
            ("Policy", "Values", JsonValueKind.Array),
            ("Updates", "Items", JsonValueKind.Array),
            ("Updates", "History", JsonValueKind.Array),
            ("DriverStore", "Packages", JsonValueKind.Array),
        })
        {
            if (root.TryGetProperty(section, out var sectionEl)
                && sectionEl.ValueKind == JsonValueKind.Object)
            {
                if (!sectionEl.TryGetProperty(member, out var memberEl)
                    || memberEl.ValueKind != kind)
                {
                    error = $"Maintenance section '{section}.{member}' is missing or has unexpected JSON kind.";
                    return false;
                }
            }
        }

        // Array entries themselves must be non-null objects/strings — a null element
        // reaching the mapper is a crash waiting to happen.
        foreach (var listPath in new[]
        {
            ("Services", (string?)null),
            ("Bindings", (string?)null),
            ("DeviceProblems", (string?)null),
            ("Errors", (string?)null),
            ("Updates", (string?)"Items"),
            ("Updates", (string?)"History"),
            ("Policy", (string?)"Values"),
            ("DriverStore", (string?)"Packages"),
        })
        {
            var (parent, member) = listPath;
            JsonElement list;
            if (member is null)
            {
                list = root.GetProperty(parent);
            }
            else if (root.TryGetProperty(parent, out var parentEl)
                     && parentEl.ValueKind == JsonValueKind.Object
                     && parentEl.TryGetProperty(member, out var memberEl)
                     && memberEl.ValueKind == JsonValueKind.Array)
            {
                list = memberEl;
            }
            else
            {
                continue;
            }

            if (list.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var idx = 0;
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    error = $"Maintenance section '{(member is null ? parent : parent + "." + member)}' contains a null entry at index {idx}.";
                    return false;
                }
                idx++;
            }
        }

        // Source and capture time identify provenance; both must be present strings.
        if (!root.TryGetProperty("Source", out var src) || src.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(src.GetString()))
        {
            error = "Maintenance snapshot has no Source.";
            return false;
        }

        if (!root.TryGetProperty("CapturedAtUtc", out var cap) || cap.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(cap.GetString(), out _))
        {
            error = "Maintenance snapshot has no parseable CapturedAtUtc.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>The script may emit progress lines; the payload is the JSON object.</summary>
    private static string? ExtractJson(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        var trimmed = stdout.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        return start >= 0 && end > start ? trimmed[start..(end + 1)] : null;
    }

    private static bool ValidateScriptChecksum(
        string backendDir, string scriptPath, string checksumCatalogPath, out string error)
    {
        error = string.Empty;
        try
        {
            var linePattern = new Regex(@"^(?<hash>[a-fA-F0-9]{64})\s+\*?(?<path>.+)$");
            foreach (var line in File.ReadLines(checksumCatalogPath))
            {
                var match = linePattern.Match(line.Trim());
                if (!match.Success)
                {
                    continue;
                }

                var rel = match.Groups["path"].Value.Trim().Replace('/', '\\');
                if (!string.Equals(rel, ScriptFileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                using var stream = File.OpenRead(scriptPath);
                var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
                if (string.Equals(actual, match.Groups["hash"].Value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                error = $"Bundled checksum mismatch for '{ScriptFileName}'.";
                return false;
            }

            error = $"CHECKSUMS.sha256 has no entry for '{ScriptFileName}'.";
            return false;
        }
        catch (Exception ex)
        {
            error = $"Bundled checksum validation failed: {ex.Message}";
            return false;
        }
    }
}
