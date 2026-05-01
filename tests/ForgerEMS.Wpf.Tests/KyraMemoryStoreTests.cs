using System.IO;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraMemoryStoreTests
{
    [Fact]
    public void SanitizeInPlace_DropsSensitiveKeysAndValues()
    {
        var doc = new KyraMemoryDocument
        {
            Enabled = true,
            Preferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["favoriteTool"] = "memtest",
                ["apiKey"] = "should-drop-key",
                ["note"] = "0123456789abcdef0123456789abcdef0123456789abcdef"
            }
        };

        KyraPersistentMemoryStore.SanitizeInPlace(doc);

        Assert.True(doc.Preferences.ContainsKey("favoriteTool"));
        Assert.False(doc.Preferences.ContainsKey("apiKey"));
        Assert.False(doc.Preferences.ContainsKey("note"));
    }

    [Fact]
    public void BuildPromptHint_RespectsEnabledFlag()
    {
        var store = new KyraPersistentMemoryStore(Path.Combine(Path.GetTempPath(), "kyra-mem-test-" + Guid.NewGuid().ToString("N") + ".json"));
        var doc = new KyraMemoryDocument
        {
            Enabled = false,
            Preferences = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["x"] = "y" }
        };
        store.Save(doc);

        Assert.Equal(string.Empty, store.BuildPromptHint(store.Load()));

        doc.Enabled = true;
        store.Save(doc);
        Assert.Contains("Kyra local memory", store.BuildPromptHint(store.Load()), StringComparison.OrdinalIgnoreCase);
    }
}
