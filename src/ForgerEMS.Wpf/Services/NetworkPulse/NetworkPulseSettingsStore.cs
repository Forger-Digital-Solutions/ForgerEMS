using System;
using System.IO;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.NetworkPulse;

public sealed class NetworkPulseSettingsStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public const int CurrentSchemaVersion = 2;

    public NetworkPulseSettingsStore(string path) => _path = path;

    public NetworkPulseSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ApplyDefaults(new NetworkPulseSettings());
            }

            var json = File.ReadAllText(_path);
            var loaded = JsonSerializer.Deserialize<NetworkPulseSettings>(json) ?? new NetworkPulseSettings();
            MigrateLegacyAutoCreatedPauseOnMetered(json, loaded);
            return ApplyDefaults(loaded);
        }
        catch
        {
            return ApplyDefaults(new NetworkPulseSettings());
        }
    }

    public void Save(NetworkPulseSettings settings)
    {
        ApplyDefaults(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }

    public static NetworkPulseSettings ApplyDefaults(NetworkPulseSettings s)
    {
        if (s.RefreshIntervalSeconds <= 0)
        {
            s.RefreshIntervalSeconds = NetworkPulseCadence.DefaultRefreshSeconds(s.Mode);
        }

        if (s.SpeedProbeCadenceSeconds <= 0)
        {
            s.SpeedProbeCadenceSeconds = NetworkPulseCadence.DefaultProbeCadenceSeconds(s.Mode);
        }

        s.RefreshIntervalSeconds = Math.Clamp(s.RefreshIntervalSeconds, 5, 600);
        s.SpeedProbeCadenceSeconds = Math.Clamp(s.SpeedProbeCadenceSeconds, 30, 3600);
        s.SettingsSchemaVersion = CurrentSchemaVersion;
        return s;
    }

    private static void MigrateLegacyAutoCreatedPauseOnMetered(string json, NetworkPulseSettings s)
    {
        if (json.Contains(nameof(NetworkPulseSettings.SettingsSchemaVersion), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var looksLikeOldAutoCreatedDefaults =
            s.Enabled &&
            s.ShowInHeader &&
            s.AutoTestSpeedLightly &&
            s.PauseOnMetered &&
            !s.UploadProbesEnabled &&
            !s.PauseOnBatterySaver &&
            !s.PauseDuringHostHeavyNetwork &&
            s.Mode == NetworkPulseMode.Balanced &&
            (s.RefreshIntervalSeconds == 0 || s.RefreshIntervalSeconds == NetworkPulseCadence.DefaultRefreshSeconds(NetworkPulseMode.Balanced)) &&
            (s.SpeedProbeCadenceSeconds == 0 || s.SpeedProbeCadenceSeconds == NetworkPulseCadence.DefaultProbeCadenceSeconds(NetworkPulseMode.Balanced));

        if (looksLikeOldAutoCreatedDefaults)
        {
            s.PauseOnMetered = false;
        }
    }
}
