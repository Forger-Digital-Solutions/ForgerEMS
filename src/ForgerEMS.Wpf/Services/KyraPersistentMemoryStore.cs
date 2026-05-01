using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Optional on-disk preferences for Kyra (no API keys, no secrets). User-controlled.
/// </summary>
public sealed class KyraPersistentMemoryStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public KyraPersistentMemoryStore(string path) => _path = path;

    public KyraMemoryDocument Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new KyraMemoryDocument();
            }

            return JsonSerializer.Deserialize<KyraMemoryDocument>(File.ReadAllText(_path)) ?? new KyraMemoryDocument();
        }
        catch
        {
            return new KyraMemoryDocument();
        }
    }

    public void Save(KyraMemoryDocument doc)
    {
        SanitizeInPlace(doc);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(doc, JsonOptions));
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch
        {
        }
    }

    /// <summary>Short block safe to prepend to Kyra system context (already scrubbed).</summary>
    public string BuildPromptHint(KyraMemoryDocument doc)
    {
        if (!doc.Enabled || doc.Preferences.Count == 0)
        {
            return string.Empty;
        }

        var lines = doc.Preferences
            .Where(p => !string.IsNullOrWhiteSpace(p.Key) && !string.IsNullOrWhiteSpace(p.Value))
            .Take(12)
            .Select(p => $"{p.Key}: {ScrubValue(p.Value)}");
        return "Kyra local memory (user-approved, non-sensitive): " + string.Join("; ", lines);
    }

    public static void SanitizeInPlace(KyraMemoryDocument doc)
    {
        foreach (var p in doc.Preferences.ToList())
        {
            if (ShouldDropKey(p.Key) || LooksSensitive(p.Value))
            {
                doc.Preferences.Remove(p.Key);
                continue;
            }

            doc.Preferences[p.Key] = ScrubValue(p.Value);
        }
    }

    private static bool ShouldDropKey(string key)
    {
        var k = key.ToLowerInvariant();
        return k.Contains("password", StringComparison.Ordinal) ||
               k.Contains("apikey", StringComparison.Ordinal) ||
               k.Contains("api_key", StringComparison.Ordinal) ||
               k.Contains("secret", StringComparison.Ordinal) ||
               k.Contains("token", StringComparison.Ordinal) ||
               k.Contains("serial", StringComparison.Ordinal) ||
               k.Contains("license", StringComparison.Ordinal);
    }

    private static bool LooksSensitive(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Length > 400)
        {
            return true;
        }

        return Regex.IsMatch(value, @"\b([A-Z0-9]{5}-){4}[A-Z0-9]{5}\b", RegexOptions.IgnoreCase) ||
               Regex.IsMatch(value, @"\b[A-F0-9]{32,}\b", RegexOptions.IgnoreCase);
    }

    private static string ScrubValue(string value)
    {
        var v = value.Trim();
        if (v.Length > 240)
        {
            v = v[..240] + "…";
        }

        return KyraSystemContextSanitizer.SanitizeForExternalProviders(v);
    }
}

public sealed class KyraMemoryDocument
{
    public bool Enabled { get; set; }

    public string SchemaVersion { get; set; } = "1";

    public Dictionary<string, string> Preferences { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
