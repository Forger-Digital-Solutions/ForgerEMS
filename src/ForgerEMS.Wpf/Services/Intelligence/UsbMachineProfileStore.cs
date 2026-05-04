using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class UsbMachineProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly string _profilePath;

    public UsbMachineProfileStore(string runtimeRoot)
    {
        _profilePath = Path.Combine(runtimeRoot, "profiles", "usb-machine-profile.json");
    }

    public static string ComputeMachineFingerprintHash() =>
        UsbIdentityHasher.Sha256Hex($"{Environment.MachineName}|{Environment.OSVersion.Version}");

    public UsbMachineProfile LoadOrCreate()
    {
        try
        {
            if (File.Exists(_profilePath))
            {
                var json = File.ReadAllText(_profilePath);
                var profile = JsonSerializer.Deserialize<UsbMachineProfile>(json, JsonOptions);
                if (profile is not null && !string.IsNullOrWhiteSpace(profile.MachineFingerprintHash))
                {
                    return profile;
                }
            }
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("usb-intelligence.log", $"Machine profile load failed: {ex.Message}");
        }

        return new UsbMachineProfile
        {
            MachineFingerprintHash = ComputeMachineFingerprintHash(),
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    public async Task SaveAsync(UsbMachineProfile profile)
    {
        await Task.Run(() => Save(profile)).ConfigureAwait(false);
    }

    public void Save(UsbMachineProfile profile)
    {
        profile.LastUpdatedUtc = DateTimeOffset.UtcNow;
        var dir = Path.GetDirectoryName(_profilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_profilePath, JsonSerializer.Serialize(profile, JsonOptions));
    }

    /// <summary>Merges snapshot device/controller keys and seen counts into the profile.</summary>
    public void ApplySnapshot(UsbMachineProfile profile, UsbTopologySnapshot snapshot)
    {
        profile.MachineFingerprintHash = string.IsNullOrWhiteSpace(profile.MachineFingerprintHash)
            ? ComputeMachineFingerprintHash()
            : profile.MachineFingerprintHash;

        foreach (var c in snapshot.Controllers)
        {
            if (!string.IsNullOrWhiteSpace(c.ControllerKey) && !profile.KnownControllerKeys.Contains(c.ControllerKey))
            {
                profile.KnownControllerKeys.Add(c.ControllerKey);
            }
        }

        foreach (var d in snapshot.Devices)
        {
            if (!string.IsNullOrWhiteSpace(d.StablePortKey) && !profile.KnownStablePortKeys.Contains(d.StablePortKey))
            {
                profile.KnownStablePortKeys.Add(d.StablePortKey);
            }

            if (string.IsNullOrWhiteSpace(d.StableDeviceKey))
            {
                continue;
            }

            var now = snapshot.GeneratedUtc;
            if (!profile.KnownDevicesByStableKey.TryGetValue(d.StableDeviceKey, out var rec))
            {
                profile.KnownDevicesByStableKey[d.StableDeviceKey] = new UsbKnownDeviceRecord
                {
                    FirstSeenUtc = now,
                    LastSeenUtc = now,
                    SeenCount = 1
                };
            }
            else
            {
                rec.LastSeenUtc = now;
                rec.SeenCount++;
                profile.KnownDevicesByStableKey[d.StableDeviceKey] = rec;
            }

            var letter = NormalizeDriveLetter(d.DriveLetter);
            if (!string.IsNullOrEmpty(letter) &&
                profile.PendingBenchmarkByDriveLetter.TryGetValue(letter, out var pendingBench))
            {
                MergePendingBenchmark(profile, d.StablePortKey, pendingBench, now, d);
                profile.PendingBenchmarkByDriveLetter.Remove(letter);
            }
            else if (!string.IsNullOrWhiteSpace(d.StablePortKey))
            {
                TouchKnownPort(profile, d.StablePortKey, now);
            }
        }

        if (snapshot.SelectedTargetBenchmark is { Succeeded: true } b)
        {
            profile.LastBenchmarkPlaceholder = b.SummaryLine;
        }
    }

    private static string NormalizeDriveLetter(string? driveLetter)
    {
        if (string.IsNullOrWhiteSpace(driveLetter))
        {
            return string.Empty;
        }

        return driveLetter.TrimEnd('\\').TrimEnd(':').ToUpperInvariant();
    }

    private static void TouchKnownPort(UsbMachineProfile profile, string stablePortKey, DateTimeOffset when)
    {
        var rec = profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == stablePortKey);
        if (rec is null)
        {
            return;
        }

        rec.LastSeenUtc = when;
    }

    private static void MergePendingBenchmark(
        UsbMachineProfile profile,
        string stablePortKey,
        UsbIntelligenceBenchmarkResult bench,
        DateTimeOffset when,
        UsbDeviceInfo device)
    {
        if (string.IsNullOrWhiteSpace(stablePortKey))
        {
            return;
        }

        var rec = profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == stablePortKey);
        if (rec is null)
        {
            rec = new UsbKnownPortRecord { StablePortKey = stablePortKey };
            profile.KnownPorts.Add(rec);
        }

        rec.LastBenchmark = bench;
        rec.LastSeenUtc = when;
        rec.Confidence = Math.Max(rec.Confidence, Math.Max(bench.ConfidenceScore, device.ConfidenceScore));
    }

    internal static string ProfilePathForTests(string runtimeRoot) =>
        Path.Combine(runtimeRoot, "profiles", "usb-machine-profile.json");
}
