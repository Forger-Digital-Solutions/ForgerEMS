using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbBuilderProfileSettingsItemPersistenceTests
{
    private static readonly string[] LegacyV1Categories = ["core", "windows", "linux-rescue"];
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    [Fact]
    public void SchemaBumpsToVersion2WhenLoaded()
    {
        var settings = UsbBuilderProfileSettingsStore.ApplyDefaults(new UsbBuilderProfileSettings
        {
            SchemaVersion = 1,
        });
        Assert.Equal(UsbBuilderProfileSettingsStore.CurrentSchemaVersion, settings.SchemaVersion);
        Assert.Equal(2, settings.SchemaVersion);
    }

    [Fact]
    public void RecommendedItemIdsAreSeededFromCatalog()
    {
        var ids = UsbBuilderProfileSettingsStore.RecommendedItemIdsForCategory("linux-rescue");
        Assert.Contains("linux.rescuezilla", ids);
        Assert.DoesNotContain("linux.proxmox", ids); // Optional tier
    }

    [Fact]
    public void LegacyV1Settings_LoadCleanlyWithEmptyItemMap()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"forgerems-usbprofile-{System.Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(new
            {
                SchemaVersion = 1,
                IncludedCategoryIds = LegacyV1Categories
            }, IndentedJson));

            var store = new UsbBuilderProfileSettingsStore(temp);
            var loaded = store.Load();
            Assert.Equal(2, loaded.SchemaVersion);
            Assert.Empty(loaded.SelectedItemIdsByCategory);
            Assert.Contains("core", loaded.IncludedCategoryIds);
            Assert.Contains("linux-rescue", loaded.IncludedCategoryIds);
        }
        finally
        {
            if (File.Exists(temp)) { File.Delete(temp); }
        }
    }

    [Fact]
    public void SaveAndReload_PreservesSelectedItemIds()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"forgerems-usbprofile-{System.Guid.NewGuid():N}.json");
        try
        {
            var store = new UsbBuilderProfileSettingsStore(temp);
            var settings = new UsbBuilderProfileSettings
            {
                IncludedCategoryIds = new List<string> { "core", "linux-rescue" },
                SelectedItemIdsByCategory = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase)
                {
                    ["linux-rescue"] = new() { "linux.rescuezilla", "linux.debian" }
                }
            };
            store.Save(settings);

            var reloaded = store.Load();
            Assert.True(reloaded.SelectedItemIdsByCategory.TryGetValue("linux-rescue", out var ids));
            Assert.Contains("linux.rescuezilla", ids!);
            Assert.Contains("linux.debian", ids!);
        }
        finally
        {
            if (File.Exists(temp)) { File.Delete(temp); }
        }
    }

    [Fact]
    public void ApplyDefaults_DedupesAndLowercasesCategoryKeys()
    {
        var settings = new UsbBuilderProfileSettings
        {
            IncludedCategoryIds = new List<string> { "Core", "core", "WINDOWS" },
            SelectedItemIdsByCategory = new Dictionary<string, List<string>>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Linux-Rescue"] = new() { "linux.rescuezilla", "linux.rescuezilla", " linux.debian " }
            }
        };

        UsbBuilderProfileSettingsStore.ApplyDefaults(settings);

        Assert.Contains("core", settings.IncludedCategoryIds);
        Assert.Contains("windows", settings.IncludedCategoryIds);
        Assert.Single(settings.IncludedCategoryIds, id => id.Equals("core", System.StringComparison.OrdinalIgnoreCase));
        Assert.True(settings.SelectedItemIdsByCategory.ContainsKey("linux-rescue"));
        Assert.Equal(2, settings.SelectedItemIdsByCategory["linux-rescue"].Count); // deduped
    }
}
