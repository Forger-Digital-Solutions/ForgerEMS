using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using ForgerEMS.Wpf.Services.WindowsMaintenance;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

// The display mapper must reflect exact probe semantics: WUA ResultCode 2 means the search
// succeeded (real offers may accompany it); NoKnownPendingReboot / NoListedPolicyDetected
// must never render as their opposites; stopped manual services are informational.
public sealed class WindowsMaintenanceDisplayMapperTests
{
    // Tracked sanitized projection of the frozen host capture (original evidence stays in
    // .verify): exact 3 offers, ResultCode 2, NoKnownPendingReboot, NoListedPolicyDetected —
    // with hardware bindings, device problems, and Driver Store inventory omitted.
    private static WindowsMaintenanceSnapshot LoadFrozenLiveSnapshot()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "windows-maintenance-schema1.json");
        var snapshot = JsonSerializer.Deserialize<WindowsMaintenanceSnapshot>(File.ReadAllText(path));
        Assert.NotNull(snapshot);
        return snapshot;
    }

    private static WindowsMaintenanceSnapshot EmptySnapshot() => new()
    {
        SchemaVersion = 1,
        CapturedAtUtc = "2026-10-05T00:00:00Z",
        Source = "test"
    };

    [Fact]
    public void FrozenLiveSnapshot_ThreeOffersResultCode2_ShowsOffersNotHealthy()
    {
        // The frozen host capture has Updates.State=UpdateAvailable, ResultCode=2, 3 offers.
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(LoadFrozenLiveSnapshot());
        Assert.Contains(findings, f => f.Contains("3 Windows update offer(s)", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Contains("no applicable updates", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(findings, f => f.Contains("healthy", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(findings, f => f.Contains("current state", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FrozenLiveSnapshot_NoKnownPendingReboot_NotShownAsPending()
    {
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(LoadFrozenLiveSnapshot());
        Assert.Contains(findings, f => f.Contains("No known pending reboot", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(findings, f => f.Contains("Pending reboot detected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FrozenLiveSnapshot_NoListedPolicy_NotShownAsPolicyControlled()
    {
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(LoadFrozenLiveSnapshot());
        Assert.Contains(findings, f => f.Contains("No listed Windows Update policy detected", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(findings, f => f.Contains("policy-controlled", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(findings, f => f.Contains("listed as configured", StringComparison.OrdinalIgnoreCase));
        // Completeness caveat is preserved verbatim.
        Assert.Contains(findings, f => f.Contains("Selected visible policies only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FrozenLiveSnapshot_SupportStatusUnableToVerify_PassesThrough()
    {
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(LoadFrozenLiveSnapshot());
        Assert.Contains(findings, f => f.Contains("UnableToVerify", StringComparison.Ordinal));
    }

    [Fact]
    public void FrozenLiveSnapshot_StoppedManualService_IsInformationalNotBroken()
    {
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(LoadFrozenLiveSnapshot());
        Assert.Contains(findings, f =>
            f.Contains("wuauserv", StringComparison.OrdinalIgnoreCase) &&
            f.Contains("not proof of a fault", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UpdateAvailable_State_ShowsItems()
    {
        var snapshot = EmptySnapshot();
        snapshot.Updates = new WindowsUpdateInfo
        {
            State = "UpdateAvailable",
            ResultCode = 2,
            Items = { new WindowsUpdateItem { Title = "KB1", Kind = "Driver" } }
        };
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(snapshot);
        Assert.Contains(findings, f => f.Contains("1 Windows update offer(s)", StringComparison.Ordinal));
        Assert.Contains(findings, f => f.Contains("KB1", StringComparison.Ordinal));
    }

    [Fact]
    public void NoApplicableUpdatesOffered_Result2_ZeroItems_ShowsNoOffersLine()
    {
        var snapshot = EmptySnapshot();
        snapshot.Updates = new WindowsUpdateInfo { State = "NoApplicableUpdatesOffered", ResultCode = 2 };
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(snapshot);
        Assert.Contains(findings, f => f.Contains("no applicable updates were offered", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(findings, f => f.Contains("healthy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ResultCode2_WithUnknownState_DoesNotClaimNoOffers()
    {
        var snapshot = EmptySnapshot();
        snapshot.Updates = new WindowsUpdateInfo { State = "UnableToVerify", ResultCode = 2 };
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(snapshot);
        Assert.DoesNotContain(findings, f => f.Contains("no applicable updates", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(findings, f => f.Contains("UnableToVerify", StringComparison.Ordinal));
    }

    [Fact]
    public void CachedState_ShowsCachedWording()
    {
        var snapshot = EmptySnapshot();
        snapshot.Updates = new WindowsUpdateInfo { State = "Cached", CheckedAtUtc = "2026-10-01T00:00:00Z" };
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(snapshot);
        Assert.Contains(findings, f => f.Contains("cache", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NotChecked_ShowsNotPerformed()
    {
        var snapshot = EmptySnapshot();
        snapshot.Updates = new WindowsUpdateInfo { State = "NotChecked" };
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(snapshot);
        Assert.Contains(findings, f => f.Contains("not performed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PendingReboot_State_ShowsPending()
    {
        var snapshot = EmptySnapshot();
        snapshot.Reboot = new WindowsRebootInfo { State = "PendingReboot", Indicators = { "CBS" } };
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(snapshot);
        Assert.Contains(findings, f => f.Contains("Pending reboot detected", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListedPolicy_WithValues_ShowsPolicyControlled()
    {
        var snapshot = EmptySnapshot();
        snapshot.Policy = new WindowsPolicyInfo
        {
            State = "ListedPolicyDetected",
            Values = { new WindowsPolicyValue { Name = "WUServer", Value = "https://wsus.example" } },
            Completeness = "Selected visible policies only"
        };
        var findings = WindowsMaintenanceDisplayMapper.BuildFindings(snapshot);
        Assert.Contains(findings, f => f.Contains("listed as configured", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(findings, f => f.Contains("WUServer", StringComparison.Ordinal));
    }

    [Fact]
    public void FailedStatusLine_RetainsFailurePrefixAndCaptureTime()
    {
        var snapshot = EmptySnapshot();
        var line = WindowsMaintenanceDisplayMapper.BuildStatusLine(snapshot, failed: true, cached: false);
        Assert.StartsWith("Scan failed — showing last-good snapshot captured", line, StringComparison.Ordinal);
        Assert.Contains("2026-10-05", line, StringComparison.Ordinal);
    }

    [Fact]
    public void CachedStatusLine_MarkedAsCached()
    {
        var snapshot = EmptySnapshot();
        var line = WindowsMaintenanceDisplayMapper.BuildStatusLine(snapshot, failed: false, cached: true);
        Assert.Contains("cached", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("2026-10-05", line, StringComparison.Ordinal);
    }
}
