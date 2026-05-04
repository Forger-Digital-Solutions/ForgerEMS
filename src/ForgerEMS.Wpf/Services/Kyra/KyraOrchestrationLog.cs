using System.IO;
using System.Text;
using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Append-only safe diagnostics for Kyra routing (no secrets).</summary>
public static class KyraOrchestrationLog
{
    private static readonly object Sync = new();

    public static void Append(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        var safe = CopilotRedactor.Redact(line.Trim().ReplaceLineEndings(" "), enabled: true);
        if (safe.Length > 2_000)
        {
            safe = safe[..2_000] + "…";
        }

        try
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgerEMS", "logs");
            Directory.CreateDirectory(root);
            var path = Path.Combine(root, "kyra.log");
            var entry = $"{DateTimeOffset.Now:u} {safe}{Environment.NewLine}";
            lock (Sync)
            {
                File.AppendAllText(path, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // logging must never break chat
        }
    }
}
