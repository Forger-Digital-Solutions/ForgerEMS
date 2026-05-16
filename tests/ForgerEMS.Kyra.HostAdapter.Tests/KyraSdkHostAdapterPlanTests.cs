using System.IO;
using System.Text.Json;
using System.Xml.Linq;

namespace ForgerEMS.Kyra.HostAdapter.Tests;

[Collection(nameof(KyraSdkEnvironmentCollection))]
public class KyraSdkHostAdapterPlanTests : IDisposable
{
    private readonly string? _sdkFlag;

    public KyraSdkHostAdapterPlanTests() =>
        _sdkFlag = Environment.GetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable);

    public void Dispose() =>
        Environment.SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, _sdkFlag);

    [Fact]
    public void Feature_flag_defaults_disabled()
    {
        Environment.SetEnvironmentVariable(ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable, null);
        Assert.False(ForgerEmsKyraSdkFeatureFlags.DefaultEnabled);
        Assert.False(ForgerEmsKyraSdkFeatureFlags.IsActive);
        Assert.Equal("FORGEREMS_KYRA_SDK_ENABLED", ForgerEmsKyraSdkFeatureFlags.EnabledEnvironmentVariable);
    }

    [Fact]
    public void Host_privacy_defaults_disable_cloud_sharing()
    {
        var privacy = KyraHostPrivacyOptions.SafeDefaults;
        Assert.False(privacy.AllowCloudContextSharing);
        Assert.False(privacy.AllowWorkerEnrichment);
    }

    [Fact]
    public async Task NotWired_service_returns_NotWired_without_fake_success()
    {
        IKyraHostService service = new KyraHostServiceNotWired();
        var response = await service.ProcessAsync(new KyraHostRequest
        {
            UserPrompt = "hello",
            Mode = KyraHostMode.LocalOnly,
        });

        Assert.False(response.Succeeded);
        Assert.Equal("NotWired", response.ErrorCode);
        Assert.False(response.LocalInvoked);
        Assert.False(response.WorkerInvoked);
    }

    [Fact]
    public void Host_adapter_csproj_has_no_forbidden_project_refs()
    {
        var root = FindRepoRoot();
        AssertNoForbiddenProjectRefs(Path.Combine(root, "src", "ForgerEMS.Kyra.HostAdapter", "ForgerEMS.Kyra.HostAdapter.csproj"));
        AssertNoForbiddenProjectRefs(Path.Combine(root, "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));
    }

    [Fact]
    public void Wpf_csproj_still_references_Kyra_Core_only_for_kyra_not_sdk()
    {
        var doc = XDocument.Load(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));
        var references = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .ToList();

        Assert.Contains(references, r => r.Contains("Kyra.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Sdk", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.HostAdapter", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Local", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Workers", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Combined", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Nuget_config_points_at_kyra_sdk_feed()
    {
        var config = File.ReadAllText(Path.Combine(FindRepoRoot(), "nuget.config"));
        Assert.Contains("sdk-current/feed", config, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("kyra-sdk-local", config, StringComparison.Ordinal);
    }

    [Fact]
    public void Adapter_plan_doc_states_sdk_only_boundary()
    {
        var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "docs", "KYRA_SDK_EMS_ADAPTER_PLAN.md"));
        Assert.Contains("Kyra.Sdk", text, StringComparison.Ordinal);
        Assert.Contains("Kyra.Local.Core", text, StringComparison.Ordinal);
        Assert.Contains("must NOT reference", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FORGEREMS_KYRA_SDK_ENABLED", text, StringComparison.Ordinal);
        Assert.Contains(ForgerEmsKyraSdkFeatureFlags.DisabledUiLabel, text, StringComparison.Ordinal);
        Assert.Contains("LocalOnly", text, StringComparison.Ordinal);
        Assert.Contains("operator opt-in", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Bearer_token_not_serialized_on_host_request()
    {
        var json = JsonSerializer.Serialize(new KyraHostRequest
        {
            BearerToken = "secret",
            UserPrompt = "hi",
        });
        Assert.DoesNotContain("secret", json, StringComparison.Ordinal);
        Assert.DoesNotContain("BearerToken", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Context_mapper_does_not_send_report_summary_without_cloud_sharing()
    {
        var meta = KyraHostContextMapper.MapSafeMetadata(new KyraHostRequest
        {
            RedactedDeviceReportSummary = "USB scan complete",
            Privacy = KyraHostPrivacyOptions.SafeDefaults,
        });
        Assert.Null(meta);
    }

    [Fact]
    public void MainWindow_still_declares_kyra_copilot_region()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "MainWindow.xaml"));
        Assert.Contains("Kyra", xaml, StringComparison.Ordinal);
        Assert.Contains("Copilot", xaml, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertNoForbiddenProjectRefs(string csprojPath)
    {
        var doc = XDocument.Load(csprojPath);
        var references = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(v => v.Length > 0)
            .ToList();

        Assert.DoesNotContain(references, r => r.Contains("Kyra.Local.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Workers.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Combined.Core", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Combined.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Workers.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.Local.App", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, r => r.Contains("Kyra.App.Wpf", StringComparison.OrdinalIgnoreCase));
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
