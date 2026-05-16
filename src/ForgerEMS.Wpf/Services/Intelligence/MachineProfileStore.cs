using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class MachineProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _profilesPath;
    private readonly int _maxHistory;

    public MachineProfileStore(string runtimeRoot, int maxHistory = 40)
    {
        _profilesPath = Path.Combine(runtimeRoot, "profiles", "machine-profiles.json");
        _maxHistory = Math.Clamp(maxHistory, 25, 50);
    }

    public IReadOnlyList<MachineProfileSnapshot> LoadAll()
    {
        try
        {
            if (!File.Exists(_profilesPath))
            {
                return [];
            }

            var json = File.ReadAllText(_profilesPath);
            var snapshots = JsonSerializer.Deserialize<List<MachineProfileSnapshot>>(json, JsonOptions) ?? [];
            return snapshots
                .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.MachineIdentityHash))
                .OrderByDescending(snapshot => snapshot.LastScanUtc)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    public void SaveSnapshot(MachineProfileSnapshot snapshot)
    {
        try
        {
            var safe = Sanitize(snapshot);
            var list = LoadAll().ToList();
            var duplicate = list.FirstOrDefault(item =>
                item.MachineIdentityHash.Equals(safe.MachineIdentityHash, StringComparison.Ordinal) &&
                Math.Abs((item.LastScanUtc - safe.LastScanUtc).TotalMinutes) < 1d);
            if (duplicate is not null)
            {
                list.Remove(duplicate);
            }

            list.Add(safe);
            list = list
                .OrderByDescending(item => item.LastScanUtc)
                .Take(_maxHistory)
                .ToList();
            var directory = Path.GetDirectoryName(_profilesPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_profilesPath, JsonSerializer.Serialize(list, JsonOptions));
        }
        catch
        {
            // Profile history is best-effort local context; denied/corrupt storage must not crash release flows.
        }
    }

    public bool TryGetLatestForMachine(string machineIdentityHash, out MachineProfileSnapshot snapshot)
    {
        snapshot = new MachineProfileSnapshot();
        if (string.IsNullOrWhiteSpace(machineIdentityHash))
        {
            return false;
        }

        var latest = LoadAll().FirstOrDefault(item => item.MachineIdentityHash.Equals(machineIdentityHash, StringComparison.Ordinal));
        if (latest is null)
        {
            return false;
        }

        snapshot = latest;
        return true;
    }

    public bool TryGetPreviousForMachine(string machineIdentityHash, out MachineProfileSnapshot snapshot)
    {
        snapshot = new MachineProfileSnapshot();
        if (string.IsNullOrWhiteSpace(machineIdentityHash))
        {
            return false;
        }

        var second = LoadAll()
            .Where(item => item.MachineIdentityHash.Equals(machineIdentityHash, StringComparison.Ordinal))
            .OrderByDescending(item => item.LastScanUtc)
            .Skip(1)
            .FirstOrDefault();
        if (second is null)
        {
            return false;
        }

        snapshot = second;
        return true;
    }

    public static string ComputeMachineIdentityHash(string machineLabel, string manufacturer, string model, string os)
    {
        var payload = $"{machineLabel}|{manufacturer}|{model}|{os}".Trim().ToLowerInvariant();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    public static string ProfilePathForRuntime(string runtimeRoot) =>
        Path.Combine(runtimeRoot, "profiles", "machine-profiles.json");

    private static MachineProfileSnapshot Sanitize(MachineProfileSnapshot input)
    {
        var clone = new MachineProfileSnapshot
        {
            MachineIdentityHash = input.MachineIdentityHash,
            FriendlyMachineLabel = SanitizeLabel(input.FriendlyMachineLabel),
            LastScanUtc = input.LastScanUtc,
            HealthScore = Math.Clamp(input.HealthScore, 0, 100),
            ToolkitReadinessScore = input.ToolkitReadinessScore.HasValue ? Math.Clamp(input.ToolkitReadinessScore.Value, 0, 100) : null,
            ToolkitReadinessLabel = string.IsNullOrWhiteSpace(input.ToolkitReadinessLabel) ? "Unknown / Limited Data" : input.ToolkitReadinessLabel.Trim(),
            BestUse = string.IsNullOrWhiteSpace(input.BestUse) ? "Unknown" : input.BestUse.Trim(),
            FlipValueBand = string.IsNullOrWhiteSpace(input.FlipValueBand) ? "Unknown" : input.FlipValueBand.Trim(),
            MachineClass = string.IsNullOrWhiteSpace(input.MachineClass) ? "Unknown" : input.MachineClass.Trim(),
            UsbBenchmarkSummary = string.IsNullOrWhiteSpace(input.UsbBenchmarkSummary) ? "Not available" : input.UsbBenchmarkSummary.Trim(),
            ReportPath = SanitizeReportPath(input.ReportPath),
            NotesPlaceholder = "Notes placeholder (UI pending)."
        };

        return clone;
    }

    private static string SanitizeLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown machine";
        }

        var trimmed = value.Trim();
        return LooksSerialLike(trimmed) ? "Redacted machine label" : trimmed;
    }

    private static bool LooksSerialLike(string value)
    {
        if (value.Length < 8)
        {
            return false;
        }

        var alphaNumeric = value.Count(char.IsLetterOrDigit);
        return alphaNumeric >= 8 && value.Any(char.IsDigit) && value.Any(char.IsLetter) && !value.Contains(' ');
    }

    private static string SanitizeReportPath(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath))
        {
            return string.Empty;
        }

        var normalized = reportPath.Replace('\\', '/');
        var marker = "/runtime/";
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return "Runtime/" + normalized[(index + marker.Length)..].Replace('/', '\\');
        }

        return reportPath.StartsWith("Runtime\\", StringComparison.OrdinalIgnoreCase)
            ? reportPath
            : Path.GetFileName(reportPath);
    }
}
