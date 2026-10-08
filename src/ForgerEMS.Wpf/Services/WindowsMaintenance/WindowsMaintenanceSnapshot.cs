using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ForgerEMS.Wpf.Services.WindowsMaintenance;

/// <summary>
/// Typed projection of the fixed backend/Get-ForgerEMSWindowsMaintenance.ps1 JSON output
/// (SchemaVersion 1). Field names match the frozen capture exactly.
/// </summary>
public sealed class WindowsMaintenanceSnapshot
{
    public int SchemaVersion { get; set; }
    public string CapturedAtUtc { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public WindowsOsInfo? OperatingSystem { get; set; }
    public List<WindowsServiceStatus> Services { get; set; } = new();
    public WindowsRebootInfo Reboot { get; set; } = new();
    public WindowsPolicyInfo Policy { get; set; } = new();
    public List<WindowsDriverBinding> Bindings { get; set; } = new();
    public List<WindowsDeviceProblem> DeviceProblems { get; set; } = new();
    public WindowsDriverStoreInfo DriverStore { get; set; } = new();
    public WindowsUpdateInfo Updates { get; set; } = new();
    public List<WindowsProbeError> Errors { get; set; } = new();
}

public sealed class WindowsOsInfo
{
    // The probe emits Revision as a JSON number (OS UBR), not a string.
    public long? Revision { get; set; }
    public string SupportStatus { get; set; } = "UnableToVerify";
    public string ServicingStatus { get; set; } = "UnableToVerify";
    public string? FeatureRelease { get; set; }
    public string? Architecture { get; set; }
    public string? Version { get; set; }
    public string? Build { get; set; }
    public string? Name { get; set; }
    public string? Edition { get; set; }
}

public sealed class WindowsServiceStatus
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StartType { get; set; } = string.Empty;
}

public sealed class WindowsRebootInfo
{
    public string State { get; set; } = "Unknown";
    public List<string> Indicators { get; set; } = new();
}

public sealed class WindowsPolicyInfo
{
    public string State { get; set; } = "Unknown";
    public List<WindowsPolicyValue> Values { get; set; } = new();
    public string? Completeness { get; set; }
}

public sealed class WindowsPolicyValue
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public sealed class WindowsDriverBinding
{
    public List<string?>? HardwareIds { get; set; }
    public string? Date { get; set; }
    public int? ProblemCode { get; set; }
    public bool? BootCritical { get; set; }
    public string? Provider { get; set; }
    public List<string?>? CompatibleIds { get; set; }
    public string? DeviceId { get; set; }
    public string? Class { get; set; }
    public bool? IsSigned { get; set; }
    public string? Name { get; set; }
    public string? PublishedName { get; set; }
    public string? Version { get; set; }
}

public sealed class WindowsDeviceProblem
{
    public string? Class { get; set; }
    public int? Code { get; set; }
    public string? Name { get; set; }
}

public sealed class WindowsDriverStoreInfo
{
    public string State { get; set; } = "UnableToVerify";
    public string? Source { get; set; }
    public List<WindowsDriverStorePackage> Packages { get; set; } = new();
    public string? Reason { get; set; }
}

public sealed class WindowsDriverStorePackage
{
    public string? VersionDateText { get; set; }
    public string? ClassGuid { get; set; }
    public string? OriginalName { get; set; }
    public string? PublishedName { get; set; }
    public string? Class { get; set; }
    public string? Provider { get; set; }
    public List<string>? DeviceIds { get; set; }
    public string? PackageLocation { get; set; }
    public string? Signer { get; set; }
    public string? Architecture { get; set; }
    public string? SignatureState { get; set; }
    public bool? BootCritical { get; set; }
    public string? Version { get; set; }
}

public sealed class WindowsUpdateInfo
{
    public string? Source { get; set; }
    public List<WindowsUpdateItem> Items { get; set; } = new();
    public string? CheckedAtUtc { get; set; }
    public int? ResultCode { get; set; }
    public string State { get; set; } = "NotChecked";
    public List<WindowsUpdateHistoryEntry> History { get; set; } = new();
    public string? Reason { get; set; }
}

public sealed class WindowsUpdateItem
{
    public int? Revision { get; set; }
    public string? Id { get; set; }
    public bool? Optional { get; set; }
    public string? Provider { get; set; }
    public string? Title { get; set; }
    public string? HardwareId { get; set; }
    public bool? RebootRequired { get; set; }
    public string? ApplicabilitySource { get; set; }
    public string? Kind { get; set; }
    public string? Model { get; set; }
    public long? SizeBytes { get; set; }
    public List<WindowsUpdateCategory> Categories { get; set; } = new();
    public bool? IsDownloaded { get; set; }
    public bool? UserActionRequired { get; set; }
    public string? DriverClass { get; set; }
}

public sealed class WindowsUpdateCategory
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? Id { get; set; }
}

public sealed class WindowsUpdateHistoryEntry
{
    public string? Date { get; set; }
    public string? Title { get; set; }
    public int? ResultCode { get; set; }
    public long? HResult { get; set; }
}

public sealed class WindowsProbeError
{
    public string? Stage { get; set; }
    public string? Category { get; set; }
    public long? HResult { get; set; }
}
