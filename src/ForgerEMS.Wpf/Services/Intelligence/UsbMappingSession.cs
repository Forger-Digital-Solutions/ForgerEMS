using System;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public enum UsbMappingStep
{
    Idle = 0,
    AwaitingBefore = 1,
    AwaitingAfter = 2,
    Complete = 3
}

public sealed class UsbMappingSession
{
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset StartedUtc { get; init; } = DateTimeOffset.UtcNow;

    public UsbMappingStep Step { get; set; } = UsbMappingStep.AwaitingBefore;

    public UsbTopologySnapshot? BeforeSnapshot { get; set; }

    public UsbTopologySnapshot? AfterSnapshot { get; set; }
}

public sealed class UsbMappingInferenceResult
{
    public bool Success { get; init; }

    public string SuggestionLine { get; init; } = string.Empty;

    public UsbTopologyDiffResult? Diff { get; init; }
}

public sealed class UsbMappingSessionService
{
    public UsbMappingSession StartSession() =>
        new()
        {
            Step = UsbMappingStep.AwaitingBefore
        };

    public void RecordBefore(UsbMappingSession session, UsbTopologySnapshot snapshot)
    {
        session.BeforeSnapshot = snapshot;
        session.Step = UsbMappingStep.AwaitingAfter;
    }

    public void RecordAfter(UsbMappingSession session, UsbTopologySnapshot snapshot)
    {
        session.AfterSnapshot = snapshot;
        session.Step = UsbMappingStep.Complete;
    }

    public UsbMappingInferenceResult InferMappingChange(UsbMappingSession session)
    {
        if (session.BeforeSnapshot is null || session.AfterSnapshot is null)
        {
            return new UsbMappingInferenceResult
            {
                Success = false,
                SuggestionLine = "Record both before and after USB topology snapshots to infer a port change."
            };
        }

        var diff = UsbTopologyDiffService.Compare(session.BeforeSnapshot, session.AfterSnapshot);
        var suggestion = "Looks like this device was plugged into a new USB path.";
        if (diff.ChangedDevices.Any(c => c.ChangeKind == "SpeedClarified"))
        {
            suggestion =
                "Looks like Windows now reports a clearer USB speed class—often meaning a better port or driver path.";
        }
        else if (diff.ChangedDevices.Any(c => c.ChangeKind == "SpeedChanged"))
        {
            suggestion = "USB speed class changed between snapshots—try to stay on the faster port you just verified.";
        }
        else if (diff.AddedDevices.Count + diff.RemovedDevices.Count > 0)
        {
            suggestion = "Device count changed; confirm only your toolkit USB is connected before mapping ports.";
        }

        return new UsbMappingInferenceResult
        {
            Success = true,
            Diff = diff,
            SuggestionLine = suggestion
        };
    }

    public void SavePlaceholderLabel(UsbMachineProfile profile, string label) =>
        profile.UserLabelsPlaceholder = label;
}
