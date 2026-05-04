using System;
using System.IO;
using System.Threading;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class IntelligenceLogWriter
{
    private static readonly object Sync = new();

    public static void Append(string logFileName, string line)
    {
        try
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.GetTempPath();
            }

            var path = Path.Combine(root, "ForgerEMS", "logs", logFileName);
            var formatted = $"[{DateTime.UtcNow:O}] {line}{Environment.NewLine}";
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, formatted);
            }
        }
        catch
        {
            // Logging must never break automation.
        }
    }
}
