using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class UsbBuilderProfileSettings
{
    public int SchemaVersion { get; set; } = UsbBuilderProfileSettingsStore.CurrentSchemaVersion;

    public List<string> IncludedCategoryIds { get; set; } = UsbBuilderProfileSettingsStore.DefaultIncludedCategoryIds.ToList();

    // Phase 1 (schema v2) addition. Keyed by category id, value = selected item
    // ids inside that category. When a v1 settings file is loaded, this map is
    // empty and callers should fall back to the per-item DefaultIncluded /
    // Recommended tier seed; the store applies that mapping in ApplyDefaults.
    public Dictionary<string, List<string>> SelectedItemIdsByCategory { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UsbBuilderProfileSettingsStore
{
    public const int CurrentSchemaVersion = 2;

    public static readonly string[] RequiredCategoryIds = ["core"];

    public static readonly string[] DefaultIncludedCategoryIds =
    [
        "core",
        "forgerems-portable",
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

        settings.SelectedItemIdsByCategory ??= new(StringComparer.OrdinalIgnoreCase);
        // Trim empties and dedupe while preserving order so the persisted file
        // diffs stay small as users toggle items.
        var rekeyed = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (rawKey, rawIds) in settings.SelectedItemIdsByCategory)
        {
            if (string.IsNullOrWhiteSpace(rawKey) || rawIds is null) { continue; }
            var key = rawKey.Trim().ToLowerInvariant();
            var deduped = rawIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (deduped.Count > 0)
            {
                rekeyed[key] = deduped;
            }
        }
        settings.SelectedItemIdsByCategory = rekeyed;
        return settings;
    }

    // Phase 1 default seed when a category has no entry in
    // SelectedItemIdsByCategory: pick every Required + Recommended item from
    // the static catalog for that category. This mirrors the spec's
    // "Select recommended = practical technician baseline, not everything."
    public static IReadOnlyList<string> RecommendedItemIdsForCategory(string categoryId) =>
        UsbBuilderProfileItemCatalog
            .ForCategory(categoryId)
            .Where(i => i.Tier == UsbBuilderProfileItemTier.Required ||
                        i.Tier == UsbBuilderProfileItemTier.Recommended)
            .Select(i => i.Id)
            .ToList();
}
