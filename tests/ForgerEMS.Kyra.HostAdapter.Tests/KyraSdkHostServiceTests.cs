using System.Xml.Linq;

namespace ForgerEMS.Kyra.HostAdapter.Tests;

[Collection(nameof(KyraSdkEnvironmentCollection))]
public class KyraSdkHostServiceTests : IDisposable
{
    private readonly string? _previousFlag;

    public KyraSdkHostServiceTests() =>
        _previousFlag = Environment.GetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable);

    public void Dispose() =>
        RestoreEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, _previousFlag);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("yes")]
    [InlineData("not-a-bool")]
    public void Factory_returns_NotWired_when_flag_false_missing_or_malformed(string? value)
    {
        SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, value);
        var service = KyraHostServiceFactory.Create();
        Assert.IsType<KyraHostServiceNotWired>(service);
    }

    [Fact]
    public void Factory_returns_SDK_service_when_flag_true()
    {
        SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, "true");
        var service = KyraHostServiceFactory.Create();
        using var sdk = Assert.IsType<KyraSdkHostService>(service);
    }

    [Fact]
    public async Task LocalOnly_request_returns_SDK_local_result_when_flag_enabled()
    {
        SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, "true");
        using var service = (KyraSdkHostService)KyraHostServiceFactory.Create();

        var response = await service.ProcessAsync(new KyraHostRequest
        {
            Mode = KyraHostMode.LocalOnly,
            UserPrompt = "hello",
            HostApplicationId = "ForgerEMS",
        });

        Assert.True(response.Succeeded);
        Assert.Equal(KyraHostMode.LocalOnly, response.Mode);
        Assert.True(response.LocalInvoked);
        Assert.False(response.WorkerInvoked);
        Assert.False(string.IsNullOrWhiteSpace(response.Text));
    }

    [Fact]
    public async Task WorkerOnly_without_gateway_returns_NotConfigured_at_adapter()
    {
        SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, "true");
        using var service = (KyraSdkHostService)KyraHostServiceFactory.Create();

        var response = await service.ProcessAsync(new KyraHostRequest
        {
            Mode = KyraHostMode.WorkerOnly,
            UserPrompt = "hello",
        });

        Assert.False(response.Succeeded);
        Assert.Equal("NotConfigured", response.ErrorCode);
        Assert.Equal(KyraHostMode.WorkerOnly, response.Mode);
        Assert.False(response.LocalInvoked);
        Assert.False(response.WorkerInvoked);
    }

    [Fact]
    public async Task Combined_without_cloud_sharing_skips_worker()
    {
        SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, "true");
        using var service = (KyraSdkHostService)KyraHostServiceFactory.Create();

        var response = await service.ProcessAsync(new KyraHostRequest
        {
            Mode = KyraHostMode.Combined,
            UserPrompt = "hello",
            GatewayBaseUrl = "https://gateway.example",
            Privacy = KyraHostPrivacyOptions.SafeDefaults,
        });

        Assert.True(response.Succeeded);
        Assert.Equal(KyraHostMode.Combined, response.Mode);
        Assert.True(response.LocalInvoked);
        Assert.False(response.WorkerInvoked);
        Assert.True(response.WorkerSkippedForPrivacy);
    }

    [Fact]
    public void Host_adapter_csproj_declares_Kyra_Sdk_reference_not_forbidden_internals()
    {
        var root = FindRepoRoot();
        var csproj = Path.Combine(root, "src", "ForgerEMS.Kyra.HostAdapter", "ForgerEMS.Kyra.HostAdapter.csproj");
        var doc = XDocument.Load(csproj);
        var text = File.ReadAllText(csproj);

        Assert.True(
            text.Contains("Kyra.Sdk", StringComparison.OrdinalIgnoreCase),
            "HostAdapter csproj must declare Kyra.Sdk (package or project reference).");

        var projectRefs = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();
        AssertNoForbiddenProjectRefs(projectRefs);
    }

    [Fact]
    public void Host_adapter_assembly_references_Kyra_Sdk_not_forbidden_cores()
    {
        var asm = typeof(KyraSdkHostService).Assembly;
        var names = asm.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        Assert.Contains(names, n => n.Equals("Kyra.Sdk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("Kyra.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("Kyra.Local.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("Kyra.Workers.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.Equals("Kyra.Combined.Core", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Context_mapper_redacts_path_like_report_summary_even_when_sharing_enabled()
    {
        var meta = KyraHostContextMapper.MapSafeMetadata(new KyraHostRequest
        {
            RedactedDeviceReportSummary = @"C:\Users\operator\secret\report.txt",
            Privacy = new KyraHostPrivacyOptions
            {
                AllowCloudContextSharing = true,
                AllowWorkerEnrichment = false,
            },
        });

        Assert.NotNull(meta);
        Assert.Equal("[REDACTED_EMS_CONTEXT]", meta!["emsRedactedReportSummary"]);
    }

    private static void SetEnvironmentVariable(string name, string? value)
    {
        if (value is null)
            Environment.SetEnvironmentVariable(name, null);
        else
            Environment.SetEnvironmentVariable(name, value);
    }

    private static void RestoreEnvironmentVariable(string name, string? value) =>
        Environment.SetEnvironmentVariable(name, value);

    private static void AssertNoForbiddenProjectRefs(IReadOnlyList<string> references)
    {
        string[] forbidden =
        [
            "Kyra.Local.Core", "Kyra.Workers.Core", "Kyra.Combined.Core",
            "Kyra.Combined.App", "Kyra.Workers.App", "Kyra.Local.App", "Kyra.App.Wpf",
        ];
        foreach (var forbiddenRef in forbidden)
        {
            Assert.DoesNotContain(references, r => r.Contains(forbiddenRef, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate ForgerEMS repo root.");
    }
}
