using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace ForgerEMS.Wpf.Tests;

public sealed class MachineProfileStoreTests
{
    [Fact]
    public void SaveAndLoadProfile_Works()
    {
        var runtime = CreateRuntimeRoot();
        var store = new MachineProfileStore(runtime, maxHistory: 25);
        var snapshot = new MachineProfileSnapshot
        {
            MachineIdentityHash = "ABC123",
            FriendlyMachineLabel = "Dell Latitude",
            LastScanUtc = DateTimeOffset.UtcNow,
            HealthScore = 82,
            ToolkitReadinessScore = 77,
            ToolkitReadinessLabel = "Mostly Ready",
            BestUse = "Office",
            FlipValueBand = "$200-$320",
            MachineClass = "Business Laptop",
            UsbBenchmarkSummary = "Best port ~80 MB/s",
            ReportPath = @"Runtime\reports\system-intelligence-latest.json"
        };

        store.SaveSnapshot(snapshot);
        var all = store.LoadAll();

        Assert.Single(all);
        Assert.Equal("ABC123", all[0].MachineIdentityHash);
        Assert.Equal(82, all[0].HealthScore);
    }

    [Fact]
    public void CorruptFile_LoadReturnsEmptySafely()
    {
        var runtime = CreateRuntimeRoot();
        var path = MachineProfileStore.ProfilePathForRuntime(runtime);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is invalid json");

        var store = new MachineProfileStore(runtime);
        var all = store.LoadAll();

        Assert.Empty(all);
    }

    [Fact]
    public void LoadAll_AcceptsPascalCasePropertyNames()
    {
        var runtime = CreateRuntimeRoot();
        var path = MachineProfileStore.ProfilePathForRuntime(runtime);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            """[{"MachineIdentityHash":"HASH99","FriendlyMachineLabel":"Test PC","LastScanUtc":"2026-03-01T12:00:00Z","HealthScore":61}]""");

        var store = new MachineProfileStore(runtime);
        var all = store.LoadAll();

        Assert.Single(all);
        Assert.Equal("HASH99", all[0].MachineIdentityHash);
        Assert.Equal(61, all[0].HealthScore);
    }

    [Fact]
    public void HistoryIsCapped()
    {
        var runtime = CreateRuntimeRoot();
        var store = new MachineProfileStore(runtime, maxHistory: 25);
        for (var i = 0; i < 80; i++)
        {
            store.SaveSnapshot(new MachineProfileSnapshot
            {
                MachineIdentityHash = "MACHINE",
                FriendlyMachineLabel = "Device",
                LastScanUtc = DateTimeOffset.UtcNow.AddMinutes(-i),
                HealthScore = 60
            });
        }

        Assert.True(store.LoadAll().Count <= 25);
    }

    [Fact]
    public void Sanitizer_DoesNotPersistSerialLikeOrPrivatePathValues()
    {
        var runtime = CreateRuntimeRoot();
        var store = new MachineProfileStore(runtime);
        store.SaveSnapshot(new MachineProfileSnapshot
        {
            MachineIdentityHash = "X1",
            FriendlyMachineLabel = "ABC12345XYZ999",
            LastScanUtc = DateTimeOffset.UtcNow,
            HealthScore = 75,
            ReportPath = @"C:\Users\Daddy_FDS\Desktop\secret\report.json"
        });

        var path = MachineProfileStore.ProfilePathForRuntime(runtime);
        var json = File.ReadAllText(path);
        Assert.DoesNotContain("ABC12345XYZ999", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\Daddy_FDS", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveSnapshot_DeniedOrInvalidStorage_DoesNotThrow()
    {
        var runtimeFile = Path.Combine(
            Path.GetTempPath(),
            "ForgerEMS-MachineProfileStoreTests",
            Guid.NewGuid().ToString("N"),
            "runtime-root-file");
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeFile)!);
        File.WriteAllText(runtimeFile, "not a directory");
        var store = new MachineProfileStore(runtimeFile);

        var exception = Record.Exception(() => store.SaveSnapshot(new MachineProfileSnapshot
        {
            MachineIdentityHash = "NOCRASH",
            FriendlyMachineLabel = "Dell Latitude",
            LastScanUtc = DateTimeOffset.UtcNow,
            HealthScore = 75
        }));

        Assert.Null(exception);
    }

    private static string CreateRuntimeRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ForgerEMS-MachineProfileStoreTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
