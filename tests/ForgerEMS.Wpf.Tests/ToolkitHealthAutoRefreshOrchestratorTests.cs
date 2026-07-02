using VentoyToolkitSetup.Wpf.Services;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class ToolkitHealthAutoRefreshOrchestratorTests
{
    [Fact]
    public void LaunchWithUsbTarget_TriggersOneRefresh()
    {
        var orch = new ToolkitHealthAutoRefreshOrchestrator();
        var eval = orch.Evaluate("D:\\", isBackendBusy: false);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.Refresh, eval.Decision);
        Assert.Contains("Reading toolkit health for D:", eval.LiveStatus);
    }

    [Fact]
    public void LaunchWithoutUsbTarget_SkipsWithFriendlyStatus()
    {
        var orch = new ToolkitHealthAutoRefreshOrchestrator();
        var eval = orch.Evaluate(null, isBackendBusy: false);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.SkipNoUsbTarget, eval.Decision);
        Assert.Contains("no USB target", eval.LiveStatus, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BackendBusy_DefersRefresh()
    {
        var orch = new ToolkitHealthAutoRefreshOrchestrator();
        var eval = orch.Evaluate("D:\\", isBackendBusy: true);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.SkipBusy, eval.Decision);
    }

    [Fact]
    public void TabSwitchAfterRefresh_DoesNotRefireForSameTarget()
    {
        var orch = new ToolkitHealthAutoRefreshOrchestrator();
        var first = orch.Evaluate("D:\\", isBackendBusy: false);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.Refresh, first.Decision);
        orch.MarkRefreshed(first.NormalizedUsbRoot);

        var second = orch.Evaluate("D:\\", isBackendBusy: false);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.SkipAlreadyRefreshed, second.Decision);
    }

    [Fact]
    public void UsbSwap_TriggersOneNewRefresh()
    {
        var orch = new ToolkitHealthAutoRefreshOrchestrator();
        orch.MarkRefreshed(ToolkitHealthAutoRefreshOrchestrator.NormalizeRoot("D:\\"));

        var afterSwap = orch.Evaluate("E:\\", isBackendBusy: false);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.Refresh, afterSwap.Decision);

        orch.MarkRefreshed(afterSwap.NormalizedUsbRoot);

        var sameAfter = orch.Evaluate("E:\\", isBackendBusy: false);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.SkipAlreadyRefreshed, sameAfter.Decision);
    }

    [Theory]
    [InlineData("D:\\", "D:")]
    [InlineData("d:", "D:")]
    [InlineData("  D:\\  ", "D:")]
    [InlineData("e:\\", "E:")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void NormalizeRoot_BehavesIdempotently(string? input, string? expected)
    {
        Assert.Equal(expected, ToolkitHealthAutoRefreshOrchestrator.NormalizeRoot(input));
    }

    [Fact]
    public void Reset_ClearsLatch()
    {
        var orch = new ToolkitHealthAutoRefreshOrchestrator();
        orch.MarkRefreshed("D:\\");
        Assert.NotNull(orch.LastEvaluatedRoot);
        orch.Reset();
        Assert.Null(orch.LastEvaluatedRoot);
    }

    [Fact]
    public void Orchestrator_DoesNotCarryEmptyRootForwardAsLatch()
    {
        var orch = new ToolkitHealthAutoRefreshOrchestrator();
        orch.MarkRefreshed(null);
        orch.MarkRefreshed("");
        Assert.Null(orch.LastEvaluatedRoot);

        // After acknowledging a real target, an absent target still reports
        // SkipNoUsbTarget rather than SkipAlreadyRefreshed.
        orch.MarkRefreshed("D:\\");
        var eval = orch.Evaluate(null, isBackendBusy: false);
        Assert.Equal(ToolkitHealthAutoRefreshDecision.SkipNoUsbTarget, eval.Decision);
    }
}
