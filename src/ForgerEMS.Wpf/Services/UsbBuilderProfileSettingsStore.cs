using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class UsbBuilderProfileSettings
{
    public int SchemaVersion { get; set; } = UsbBuilderProfileSettingsStore.CurrentSchemaVersion;

    public List<string> IncludedCategoryIds { get; set; } = UsbBuilderProfileSettingsStore.DefaultIncludedCategoryIds.ToList();
}

public sealed class UsbBuilderProfileSettingsStore
{
    public const int CurrentSchemaVersion = 1;

    public static readonly string[] RequiredCategoryIds = ["core"];

    public static readonly string[] DefaultIncludedCategoryIds =
    [
        "core",
        "windows",
        "legacy-windows",
        "linux-rescue",
        "diagnostics",
        "oem-tools"
    ];

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;

    public UsbBuilderProfileSettingsStore(string path) => _path = path;

    public UsbBuilderProfileSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return ApplyDefaults(new UsbBuilderProfileSettings());
            }

            var loaded = JsonSerializer.Deserialize<UsbBuilderProfileSettings>(File.ReadAllText(_path)) ??
                         new UsbBuilderProfileSettings();
            return ApplyDefaults(loaded);
        }
        catch
        {
            return ApplyDefaults(new UsbBuilderProfileSettings());
        }
    }

    public void Save(UsbBuilderProfileSettings settings)
    {
        ApplyDefaults(settings);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }

    public static UsbBuilderProfileSettings ApplyDefaults(UsbBuilderProfileSettings settings)
    {
        settings.SchemaVersion = CurrentSchemaVersion;
        settings.IncludedCategoryIds ??= [];

        var normalized = settings.IncludedCategoryIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var required in RequiredCategoryIds)
        {
            if (!normalized.Contains(required, StringComparer.OrdinalIgnoreCase))
            {
                normalized.Add(required);
            }
        }

        settings.IncludedCategoryIds = normalized;
        return settings;
    }
}
