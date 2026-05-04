using System.IO;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Persists redacted Kyra conversation memory under LocalApplicationData.</summary>
public sealed class KyraMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _filePath;
    private readonly object _sync = new();

    public KyraMemoryStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgerEMS",
            "kyra",
            "memory.json");
    }

    public string FilePath => _filePath;

    public bool TryLoad(out KyraMemorySnapshot snapshot)
    {
        snapshot = new KyraMemorySnapshot();
        try
        {
            lock (_sync)
            {
                if (!File.Exists(_filePath))
                {
                    return false;
                }

                var json = File.ReadAllText(_filePath);
                var loaded = JsonSerializer.Deserialize<KyraMemorySnapshot>(json, JsonOptions);
                if (loaded is null)
                {
                    return false;
                }

                snapshot = loaded;
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    public void Save(KyraMemorySnapshot snapshot)
    {
        try
        {
            lock (_sync)
            {
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, JsonOptions));
            }
        }
        catch
        {
            // Never break chat on disk failure
        }
    }

    public void ClearFile()
    {
        try
        {
            lock (_sync)
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
        }
        catch
        {
        }
    }
}
