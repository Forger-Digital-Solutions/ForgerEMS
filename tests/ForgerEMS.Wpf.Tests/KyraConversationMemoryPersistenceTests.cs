using System.IO;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraConversationMemoryPersistenceTests
{
    [Fact]
    public void PersistedMemory_DoesNotContainRawUserPathOrFakeApiKey()
    {
        var path = Path.Combine(Path.GetTempPath(), "kyra-mem-persist-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new KyraMemoryStore(path);
            var memory = new KyraConversationMemory(store: store);
            memory.SetPersistenceGate(() => true);
            memory.AddTurn(
                "My files are under C:\\Users\\SecretOperator\\Desktop\\project and key sk-test1234567890abcdef",
                "Kyra will avoid echoing private paths.",
                KyraIntent.GeneralTechQuestion,
                new SystemContext());

            Assert.True(File.Exists(path));
            var json = File.ReadAllText(path);
            Assert.DoesNotContain(@"C:\Users\SecretOperator", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sk-test1234567890abcdef", json, StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void PersistedSnapshot_IncludesRollingStateFields()
    {
        var path = Path.Combine(Path.GetTempPath(), "kyra-mem-snap-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new KyraMemoryStore(path);
            var memory = new KyraConversationMemory(store: store);
            memory.SetPersistenceGate(() => true);
            memory.AddTurn(
                "USB speed question",
                "Pick the large Ventoy partition.",
                KyraIntent.USBBuilderHelp,
                new SystemContext { Device = "Fabrikam 9000" });

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("lastKnownDeviceReference", out var dev));
            Assert.Contains("Fabrikam", dev.GetString() ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
