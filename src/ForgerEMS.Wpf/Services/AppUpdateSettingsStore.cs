using System;
using System.IO;
using System.Text.Json;
namespace VentoyToolkitSetup.Wpf.Services;

public sealed class AppUpdateSettings
{
    public bool CheckAutomatically { get; set; } = true;

    public DateTimeOffset? LastCheckedUtc { get; set; }

    public string IgnoredVersion { get; set; } = string.Empty;

    public string LastDownloadPath { get; set; } = string.Empty;

    public string LastDownloadSha256 { get; set; } = string.Empty;
}

public sealed class AppUpdateSettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public AppUpdateSettingsStore(string path)
    {
        _path = path;
    }

    public AppUpdateSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new AppUpdateSettings();
            }

            return JsonSerializer.Deserialize<AppUpdateSettings>(File.ReadAllText(_path)) ?? new AppUpdateSettings();
        }
        catch
        {
            return new AppUpdateSettings();
        }
    }

    public void Save(AppUpdateSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
