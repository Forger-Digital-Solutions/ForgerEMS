using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Writes to the same startup.log path as <see cref="App"/> for cross-cutting diagnostics (async commands, WSL UI, etc.).</summary>
public static class StartupDiagnosticLog
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly object Sync = new();

    public static string GetStartupLogPath()
    {
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        }

        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "ForgerEMS", "logs", "startup.log");
    }

    public static void AppendLine(string message)
    {
        var line = FormattableString.Invariant($"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}");
        foreach (var path in GetStartupLogWriteCandidates())
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.AppendAllText(path, line, Utf8NoBom);
                }

                return;
            }
            catch
            {
            }
        }
    }

    public static void AppendException(string source, Exception exception, IReadOnlyDictionary<string, string>? context = null)
    {
        try
        {
            var builder = new StringBuilder();
            builder.AppendLine(FormattableString.Invariant($"[{DateTimeOffset.UtcNow:O}] ExceptionSource: {source}"));
            builder.AppendLine(FormattableString.Invariant($"ManagedThreadId: {Environment.CurrentManagedThreadId}"));
            builder.AppendLine(FormattableString.Invariant($"Type: {exception.GetType().FullName}"));
            builder.AppendLine(FormattableString.Invariant($"Message: {CopilotRedactor.Redact(exception.Message, enabled: true)}"));
            if (exception.InnerException is not null)
            {
                builder.AppendLine(FormattableString.Invariant(
                    $"Inner: {exception.InnerException.GetType().FullName}: {CopilotRedactor.Redact(exception.InnerException.Message, enabled: true)}"));
            }

            if (context is not null)
            {
                foreach (var pair in context)
                {
                    builder.AppendLine(FormattableString.Invariant($"{pair.Key}: {CopilotRedactor.Redact(pair.Value, enabled: true)}"));
                }
            }

            builder.AppendLine("StackTrace:");
            builder.AppendLine(CopilotRedactor.Redact(exception.ToString(), enabled: true));
            AppendBlock(builder.ToString());
        }
        catch
        {
        }
    }

    private static void AppendBlock(string text)
    {
        foreach (var path in GetStartupLogWriteCandidates())
        {
            try
            {
                lock (Sync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.AppendAllText(path, text + Environment.NewLine, Utf8NoBom);
                }

                return;
            }
            catch
            {
            }
        }
    }

    private static IEnumerable<string> GetStartupLogWriteCandidates()
    {
        yield return GetStartupLogPath();
        yield return Path.Combine(Path.GetTempPath(), "ForgerEMS", "logs", "startup.log");
    }
}
