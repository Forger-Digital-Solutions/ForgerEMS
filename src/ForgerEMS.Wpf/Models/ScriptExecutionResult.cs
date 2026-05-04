namespace VentoyToolkitSetup.Wpf.Models;

public enum ScriptActionType
{
    VerifyBackend = 0,
    SetupUsb = 1,
    UpdateUsb = 2,
    RevalidateManagedDownloads = 3,
    RenameUsb = 4,
    SystemIntelligence = 5,
    ToolkitHealth = 6
}

public sealed class ScriptExecutionResult
{
    public ScriptActionType Action { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public bool HasWarnings { get; init; }

    /// <summary>Managed downloads completed but one or more items failed with fallback shortcuts (USB still usable).</summary>
    public bool PartiallyStaged { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public PowerShellRunResult RawRun { get; init; } = new();
}
