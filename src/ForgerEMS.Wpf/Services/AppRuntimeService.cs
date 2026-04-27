using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IAppRuntimeService
{
    string RuntimeRoot { get; }

    string VentoyRoot { get; }

    string VentoyPackagesRoot { get; }

    string VentoyExtractedRoot { get; }

    string LogsRoot { get; }

    string DiagnosticsRoot { get; }

    string SessionLogPath { get; }

    void EnsureInitialized();

    void AppendSessionLog(LogLine line);

    string WriteDiagnosticReport(string fileName, IEnumerable<string> lines);
}

public sealed class AppRuntimeService : IAppRuntimeService
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private readonly object _sync = new();
    private readonly string _sessionLogPath;
    private bool _initialized;

    public AppRuntimeService()
    {
        RuntimeRoot = Path.Combine(
            ResolveLocalApplicationDataRoot(),
            "ForgerEMS",
            "Runtime");

        VentoyRoot = Path.Combine(RuntimeRoot, "Ventoy");
        VentoyPackagesRoot = Path.Combine(VentoyRoot, "packages");
        VentoyExtractedRoot = Path.Combine(VentoyRoot, "extracted");
        LogsRoot = Path.Combine(RuntimeRoot, "logs");
        DiagnosticsRoot = Path.Combine(RuntimeRoot, "diagnostics");
        _sessionLogPath = Path.Combine(LogsRoot, $"forgerems-session-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log");
    }

    private static string ResolveLocalApplicationDataRoot()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.GetFullPath(localAppData);
        }

        localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("Could not resolve LOCALAPPDATA for the current user.");
        }

        return Path.GetFullPath(localAppData);
    }

    public string RuntimeRoot { get; }

    public string VentoyRoot { get; }

    public string VentoyPackagesRoot { get; }

    public string VentoyExtractedRoot { get; }

    public string LogsRoot { get; }

    public string DiagnosticsRoot { get; }

    public string SessionLogPath => _sessionLogPath;

    public void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            Directory.CreateDirectory(RuntimeRoot);
            Directory.CreateDirectory(VentoyRoot);
            Directory.CreateDirectory(VentoyPackagesRoot);
            Directory.CreateDirectory(VentoyExtractedRoot);
            Directory.CreateDirectory(LogsRoot);
            Directory.CreateDirectory(DiagnosticsRoot);
            _initialized = true;
        }
    }

    public void AppendSessionLog(LogLine line)
    {
        EnsureInitialized();

        var text = $"[{line.Timestamp:yyyy-MM-dd HH:mm:ss}][{line.Severity}] {line.Text}{Environment.NewLine}";
        lock (_sync)
        {
            File.AppendAllText(_sessionLogPath, text, Utf8NoBom);
        }
    }

    public string WriteDiagnosticReport(string fileName, IEnumerable<string> lines)
    {
        EnsureInitialized();

        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "forgerems-diagnostic.txt" : fileName;
        var content = string.Join(Environment.NewLine, lines) + Environment.NewLine;

        lock (_sync)
        {
            foreach (var path in GetDiagnosticWriteCandidates(safeFileName))
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllText(path, content, Utf8NoBom);
                    return path;
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
        }

        throw new IOException("ForgerEMS could not write a diagnostic report to any writable diagnostics location.");
    }

    private IEnumerable<string> GetDiagnosticWriteCandidates(string safeFileName)
    {
        yield return Path.Combine(DiagnosticsRoot, safeFileName);

        var stampedFileName =
            $"{Path.GetFileNameWithoutExtension(safeFileName)}-{DateTime.UtcNow:yyyyMMdd-HHmmss}{Path.GetExtension(safeFileName)}";

        yield return Path.Combine(DiagnosticsRoot, stampedFileName);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ForgerEMS", "Runtime", "diagnostics");
        yield return Path.Combine(tempRoot, stampedFileName);
    }
}
