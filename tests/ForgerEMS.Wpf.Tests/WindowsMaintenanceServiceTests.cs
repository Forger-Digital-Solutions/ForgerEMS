using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ForgerEMS.Wpf.Services.WindowsMaintenance;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class WindowsMaintenanceServiceTests : IDisposable
{
    private readonly string _backendDir = Path.Combine(
        Path.GetTempPath(), "forgerems-maint-" + Guid.NewGuid().ToString("N"));

    public WindowsMaintenanceServiceTests()
    {
        Directory.CreateDirectory(_backendDir);
        // Script must exist; no CHECKSUMS.sha256 written so bundled-checksum gate is skipped.
        File.WriteAllText(Path.Combine(_backendDir, WindowsMaintenanceService.ScriptFileName),
            "# fixture script — never executed\n");
    }

    private BackendContext Context() => new()
    {
        IsAvailable = true,
        RootPath = _backendDir,
        WorkingDirectory = _backendDir,
        UpdateScriptPath = Path.Combine(_backendDir, "Update-ForgerEMS.ps1")
    };

    private sealed class FixedRunner(PowerShellRunResult result) : IPowerShellRunnerService
    {
        public Task<PowerShellRunResult> RunAsync(
            PowerShellRunRequest request,
            Action<LogLine>? onOutput = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private static PowerShellRunResult Ok(string json) => new()
    {
        ExitCode = 0,
        StandardOutputText = json
    };

    private static string Snapshot(
        string updatesState = "NoUpdates",
        int? resultCode = 2,
        string? extraItems = null,
        string services = "[]",
        string rebootState = "None",
        string policyState = "None") => $$"""
        {
          "SchemaVersion": 1,
          "CapturedAtUtc": "2026-10-06T00:00:00Z",
          "Source": "fixture",
          "OperatingSystem": {
            "Name": "Windows 11 Pro",
            "Version": "10.0.22631",
            "Build": "22631",
            "Architecture": "x64",
            "SupportStatus": "Supported",
            "ServicingStatus": "InService"
          },
          "Services": {{services}},
          "Reboot": { "State": "{{rebootState}}", "Indicators": [] },
          "Policy": { "State": "{{policyState}}", "Values": [] },
          "Bindings": [],
          "DeviceProblems": [],
          "DriverStore": { "State": "Enumerated", "Packages": [] },
          "Updates": {
            "State": "{{updatesState}}",
            "ResultCode": {{(resultCode?.ToString() ?? "null")}},
            "Items": [{{extraItems}}],
            "History": []
          },
          "Errors": []
        }
        """;

    [Fact]
    public async Task Healthy_CurrentWithResultCode2_Parses()
    {
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(Snapshot())));
        var result = await svc.ScanAsync(Context(), checkUpdates: true, offline: false);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Snapshot!.Updates.ResultCode);
        Assert.Equal("NoUpdates", result.Snapshot.Updates.State);
        Assert.Empty(result.Snapshot.Updates.Items);
        Assert.NotNull(svc.LastGoodSnapshot);
    }

    [Fact]
    public async Task PendingUpdateAndReboot_Surfaced()
    {
        var item = """{"Title":"Cumulative Update","Kind":"Update","RebootRequired":true,"UserActionRequired":false}""";
        var svc = new WindowsMaintenanceService(new FixedRunner(
            Ok(Snapshot(updatesState: "OffersAvailable", resultCode: 2,
                extraItems: item, rebootState: "Pending"))));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.True(result.Succeeded);
        Assert.Single(result.Snapshot!.Updates.Items);
        Assert.True(result.Snapshot.Updates.Items[0].RebootRequired);
        Assert.Equal("Pending", result.Snapshot.Reboot.State);
    }

    [Fact]
    public async Task DisabledService_IsFindingNotDamage()
    {
        var services = """[{"Name":"wuauserv","Status":"Stopped","StartType":"Disabled"}]""";
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(Snapshot(services: services))));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.True(result.Succeeded);
        var service = Assert.Single(result.Snapshot!.Services);
        Assert.Equal("Stopped", service.Status);
        Assert.Equal("Disabled", service.StartType);
    }

    [Fact]
    public async Task PolicyControlled_Surfaced()
    {
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(Snapshot(policyState: "Controlled"))));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.True(result.Succeeded);
        Assert.Equal("Controlled", result.Snapshot!.Policy.State);
    }

    [Fact]
    public async Task FeatureUpdateListed_IsNotInstalledAutomatically()
    {
        var item = """{"Title":"Feature update to Windows 11","Kind":"FeatureUpdate","UserActionRequired":true}""";
        var svc = new WindowsMaintenanceService(new FixedRunner(
            Ok(Snapshot(updatesState: "OffersAvailable", resultCode: 2, extraItems: item))));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.True(result.Succeeded);
        Assert.Equal("FeatureUpdate", result.Snapshot!.Updates.Items[0].Kind);
        Assert.True(result.Snapshot.Updates.Items[0].UserActionRequired);
    }

    [Fact]
    public async Task NonzeroExit_Fails_EvenWithJson()
    {
        var svc = new WindowsMaintenanceService(new FixedRunner(new PowerShellRunResult
        {
            ExitCode = 2,
            StandardOutputText = Snapshot()
        }));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.Failed, result.FailureKind);
        Assert.Null(svc.LastGoodSnapshot);
    }

    [Fact]
    public async Task Timeout_Fails()
    {
        var svc = new WindowsMaintenanceService(new FixedRunner(new PowerShellRunResult
        {
            ExitCode = -1,
            TimedOut = true
        }));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.Timeout, result.FailureKind);
    }

    [Fact]
    public async Task MalformedJson_Fails()
    {
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok("not json at all")));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.MalformedJson, result.FailureKind);
    }

    [Fact]
    public async Task MissingMandatorySection_SchemaMismatch()
    {
        // Drop the trailing Errors property regardless of source line endings.
        var bad = System.Text.RegularExpressions.Regex.Replace(
            Snapshot(), ",\\s*\"Errors\"\\s*:\\s*\\[\\]", string.Empty);
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(bad)));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.SchemaMismatch, result.FailureKind);
    }

    [Theory]
    [InlineData("Services")]
    [InlineData("Reboot")]
    [InlineData("Policy")]
    [InlineData("Updates")]
    [InlineData("Errors")]
    [InlineData("DriverStore")]
    [InlineData("Bindings")]
    [InlineData("DeviceProblems")]
    public async Task NullMandatorySection_SchemaMismatch(string section)
    {
        // Only OperatingSystem may be null (honest probe failure); every other section
        // must reject null — a null list would crash the display mapper.
        using var doc = System.Text.Json.JsonDocument.Parse(Snapshot());
        var dict = new Dictionary<string, object?>();
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            dict[p.Name] = p.Name == section ? null : p.Value;
        }
        var bad = System.Text.Json.JsonSerializer.Serialize(dict);
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(bad)));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.SchemaMismatch, result.FailureKind);
    }

    [Theory]
    [InlineData("\"Reboot\": { \"State\": \"None\", \"Indicators\": null }", "Reboot")]
    [InlineData("\"Policy\": { \"State\": \"None\", \"Values\": null }", "Policy")]
    [InlineData("\"Updates\": { \"State\": \"NoUpdates\", \"ResultCode\": 2, \"Items\": null, \"History\": [] }", "Updates")]
    [InlineData("\"Updates\": { \"State\": \"NoUpdates\", \"ResultCode\": 2, \"Items\": [], \"History\": null }", "Updates")]
    [InlineData("\"DriverStore\": { \"State\": \"Enumerated\", \"Packages\": null }", "DriverStore")]
    public async Task NullNestedSection_SchemaMismatch(string replacement, string section)
    {
        var bad = System.Text.RegularExpressions.Regex.Replace(
            Snapshot(),
            $"\"{section}\"\\s*:\\s*\\{{[^\\}}]*\\}}",
            replacement);
        Assert.Contains(replacement.Substring(0, 20), bad);
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(bad)));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.SchemaMismatch, result.FailureKind);
    }

    [Fact]
    public async Task NullArrayEntry_SchemaMismatch()
    {
        var bad = Snapshot().Replace(
            "\"Services\": []",
            "\"Services\": [null]");
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(bad)));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.SchemaMismatch, result.FailureKind);
    }

    [Fact]
    public async Task NullOperatingSystem_AllowedAsHonestProbeFailure()
    {
        var bad = Snapshot().Replace(
            System.Text.RegularExpressions.Regex.Match(Snapshot(), "\"OperatingSystem\":\\s*\\{[^}]*\\}").Value,
            "\"OperatingSystem\": null");
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(bad)));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.True(result.Succeeded);
        Assert.Null(result.Snapshot!.OperatingSystem);
    }

    [Fact]
    public async Task OverflowSchemaVersion_RejectedNotThrown()
    {
        // GetInt32 on a huge number throws FormatException — TryGetInt32 must reject safely.
        var bad = Snapshot().Replace(
            "\"SchemaVersion\": 1", "\"SchemaVersion\": 99999999999999999999");
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(bad)));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.SchemaMismatch, result.FailureKind);
    }

    [Fact]
    public async Task WrongSchemaVersion_Rejected()
    {
        var bad = Snapshot().Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 2");
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok(bad)));
        var result = await svc.ScanAsync(Context(), true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.SchemaMismatch, result.FailureKind);
    }

    [Fact]
    public async Task FailureDoesNotReplaceLastGoodSnapshot()
    {
        var runner = new SequenceRunner(
            Ok(Snapshot()),
            new PowerShellRunResult { ExitCode = 5, StandardOutputText = "{}" });
        var svc = new WindowsMaintenanceService(runner);
        var first = await svc.ScanAsync(Context(), true, false);
        Assert.True(first.Succeeded);
        var second = await svc.ScanAsync(Context(), true, false);
        Assert.False(second.Succeeded);
        Assert.Same(first.Snapshot, svc.LastGoodSnapshot);
    }

    [Fact]
    public async Task MissingScript_ScriptMissing()
    {
        var emptyDir = Path.Combine(_backendDir, "empty");
        Directory.CreateDirectory(emptyDir);
        var context = new BackendContext
        {
            IsAvailable = true,
            RootPath = emptyDir,
            WorkingDirectory = emptyDir,
            UpdateScriptPath = Path.Combine(emptyDir, "Update-ForgerEMS.ps1")
        };
        var svc = new WindowsMaintenanceService(new FixedRunner(Ok("{}")));
        var result = await svc.ScanAsync(context, true, false);
        Assert.False(result.Succeeded);
        Assert.Equal(WindowsMaintenanceFailureKind.ScriptMissing, result.FailureKind);
    }

    private sealed class SequenceRunner(params PowerShellRunResult[] results) : IPowerShellRunnerService
    {
        private int _index;

        public Task<PowerShellRunResult> RunAsync(
            PowerShellRunRequest request,
            Action<LogLine>? onOutput = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(results[Math.Min(_index++, results.Length - 1)]);
    }

    public void Dispose()
    {
        try { Directory.Delete(_backendDir, recursive: true); } catch { }
    }
}
