namespace VentoyToolkitSetup.Wpf.Models;

public enum ScriptActionType
{
    VerifyBackend = 0,
    SetupUsb = 1,
    UpdateUsb = 2,
    RevalidateManagedDownloads = 3
}

public sealed class ScriptExecutionResult
{
    public ScriptActionType Action { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public bool Succeeded { get; init; }

    public bool HasWarnings { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public PowerShellRunResult RawRun { get; init; } = new();
}
