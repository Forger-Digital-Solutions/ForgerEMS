using System;
using System.IO;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class ElevatedScanDiagnostics
{
    public const int TimeoutExitCode = 1460;

    public static readonly TimeSpan ElevatedScanWaitTimeout = TimeSpan.FromMinutes(15);

    public static bool InferLikelyMissingOutput(string reportsDirectory, PowerShellRunResult runResult)
    {
        if (runResult.Succeeded)
        {
            return false;
        }

        var heartbeat = Path.Combine(reportsDirectory, "elevated-scan-heartbeat.json");
        var resultMarker = Path.Combine(reportsDirectory, "elevated-scan-result.json");
        return File.Exists(heartbeat) && !File.Exists(resultMarker);
    }

    public static void WriteStartedMarker(
        string reportsDirectory,
        string correlationId,
        bool appElevated,
        string deepSensorMode,
        string deepSensorSource,
        string powerShellDisplayPath,
        bool backendScriptExists,
        bool workingDirectoryExists)
    {
        Directory.CreateDirectory(reportsDirectory);
        var path = Path.Combine(reportsDirectory, "elevated-scan-started.json");
        var payload = new
        {
            kind = "elevated-scan-started",
            utc = DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            appElevated,
            deepSensorMode,
            deepSensorSource,
            powerShellPath = powerShellDisplayPath,
            backendScriptPresent = backendScriptExists,
            workingDirectoryPresent = workingDirectoryExists
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, ElevatedScanJson.Options));
    }

    public static void WriteErrorMarker(string reportsDirectory, ElevatedScanFailureAnalysis analysis, string correlationId)
    {
        Directory.CreateDirectory(reportsDirectory);
        var path = Path.Combine(reportsDirectory, "elevated-scan-error.json");
        var payload = new
        {
            kind = "elevated-scan-error",
            utc = DateTimeOffset.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            correlationId,
            failureKind = analysis.Kind.ToString(),
            advanced = analysis.AdvancedDiagnosticsLine
        };
        File.WriteAllText(path, JsonSerializer.Serialize(payload, ElevatedScanJson.Options));
    }

    public static string BuildPowerShellQuotedFileArgs(string scriptPath, string outputDirectory, bool writeMarkers)
    {
        var markerArg = writeMarkers ? " -WriteElevatedScanMarkers" : string.Empty;
        return $"-NoProfile -ExecutionPolicy Bypass -File {SingleQuotePs(scriptPath)} -OutputDirectory {SingleQuotePs(outputDirectory)}{markerArg}";
    }

    private static string SingleQuotePs(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static class ElevatedScanJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true
        };
    }
}
