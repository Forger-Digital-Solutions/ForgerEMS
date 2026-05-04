using System;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Backend workflow for user-confirmed USB port mapping (minimal UI hooks).</summary>
public sealed class UsbGuidedMappingWorkflow
{
    private readonly UsbMappingSessionService _sessions = new();

    public UsbMappingSession? Session { get; private set; }

    public void StartMappingSession()
    {
        Session = _sessions.StartSession();
    }

    public string PromptUserPlugIntoNextPort() =>
        "Plug the same USB device into the next physical port you want to map, wait for it to mount, then capture the after snapshot.";

    public string PromptUserBeforeCapture() =>
        "With the USB in the starting port, capture a before snapshot. Then move it to another port and capture after.";

    public void CaptureBeforeSnapshot(UsbTopologySnapshot snapshot)
    {
        Session ??= _sessions.StartSession();
        _sessions.RecordBefore(Session, snapshot);
    }

    public void CaptureAfterSnapshot(UsbTopologySnapshot snapshot)
    {
        Session ??= _sessions.StartSession();
        _sessions.RecordAfter(Session, snapshot);
    }

    /// <summary>Clears the after snapshot so the user can replug and detect again (wizard retry).</summary>
    public void ClearAfterSnapshotForRetry()
    {
        if (Session is null)
        {
            return;
        }

        Session.AfterSnapshot = null;
        Session.Step = UsbMappingStep.AwaitingAfter;
    }

    /// <summary>Detect port change and save user label onto the best-matching <see cref="UsbKnownPortRecord"/>.</summary>
    public bool TrySaveMappingLabel(
        UsbMachineProfile profile,
        UsbMachineProfileStore store,
        string userLabel,
        out UsbMappingInferenceResult inference,
        out string errorMessage) =>
        TrySaveMappingLabel(
            profile,
            store,
            userLabel,
            out inference,
            out errorMessage,
            mappingTarget: null,
            mode: UsbPortMappingSaveMode.TopologyInference);

    public bool TrySaveMappingLabel(
        UsbMachineProfile profile,
        UsbMachineProfileStore store,
        string userLabel,
        out UsbMappingInferenceResult inference,
        out string errorMessage,
        UsbTargetInfo? mappingTarget,
        UsbPortMappingSaveMode mode)
    {
        inference = new UsbMappingInferenceResult();
        errorMessage = string.Empty;

        if (Session is null)
        {
            errorMessage = "Start the USB port mapping workflow first.";
            return false;
        }

        var inf = _sessions.InferMappingChange(Session);
        inference = inf;
        if (!inf.Success || inf.Diff is null)
        {
            errorMessage = string.IsNullOrWhiteSpace(inf.SuggestionLine)
                ? "Mapping inference failed."
                : inf.SuggestionLine;
            return false;
        }

        if (string.IsNullOrWhiteSpace(userLabel))
        {
            errorMessage = "Enter a short label for this port (e.g. front-left blue).";
            return false;
        }

        if (Session.BeforeSnapshot is null || Session.AfterSnapshot is null)
        {
            errorMessage = "Before and after snapshots required.";
            return false;
        }

        if (mode == UsbPortMappingSaveMode.CurrentPortForSelectedTarget)
        {
            if (mappingTarget is null)
            {
                errorMessage = "Select the USB device used for this mapping.";
                return false;
            }

            var afterOnly = UsbMappingPortResolution.FindAfterDeviceForTarget(Session.AfterSnapshot, mappingTarget);
            if (afterOnly is null || string.IsNullOrWhiteSpace(afterOnly.StablePortKey))
            {
                errorMessage =
                    "Windows did not expose enough USB topology data. You can still try mapping again after the drive remounts, or pick a different toolkit volume.";
                return false;
            }

            var manualMapConf = Math.Min(
                72,
                UsbConfidenceAggregator.PortMappingConfidence(inf.Diff, labelSaved: true));
            var manualLine =
                "Manual label saved for the currently selected USB port heuristic (no port-move proof from topology).";
            PersistLabel(
                profile,
                store,
                afterOnly,
                userLabel.Trim(),
                manualLine,
                manualMapConf,
                Session.AfterSnapshot.GeneratedUtc);
            inference = new UsbMappingInferenceResult
            {
                Success = true,
                Diff = inf.Diff,
                SuggestionLine = manualLine
            };
            return true;
        }

        var resolution = UsbMappingPortResolution.Resolve(Session.BeforeSnapshot, Session.AfterSnapshot, mappingTarget);
        if (!resolution.Success || resolution.AfterDevice is null ||
            string.IsNullOrWhiteSpace(resolution.AfterDevice.StablePortKey))
        {
            errorMessage = string.IsNullOrWhiteSpace(resolution.UserHint)
                ? "Could not detect a stable port-key change for the same device. Try both captures again with only the toolkit USB connected."
                : resolution.UserHint;
            return false;
        }

        var baseConf = UsbConfidenceAggregator.PortMappingConfidence(inf.Diff, labelSaved: true);
        var mapConf = resolution.UsedLimitedConfidenceFallback ? Math.Min(78, baseConf - 12) : baseConf;
        mapConf = Math.Max(30, mapConf);

        var suggestion = inf.SuggestionLine;
        if (resolution.UsedLimitedConfidenceFallback)
        {
            suggestion = string.IsNullOrWhiteSpace(resolution.UserHint)
                ? inf.SuggestionLine
                : resolution.UserHint;
        }

        PersistLabel(
            profile,
            store,
            resolution.AfterDevice,
            userLabel.Trim(),
            suggestion,
            mapConf,
            Session.AfterSnapshot.GeneratedUtc);
        inference = new UsbMappingInferenceResult
        {
            Success = true,
            Diff = inf.Diff,
            SuggestionLine = suggestion
        };
        return true;
    }

    private static void PersistLabel(
        UsbMachineProfile profile,
        UsbMachineProfileStore store,
        UsbDeviceInfo afterMatch,
        string trimmedLabel,
        string lastMappingSuggestionLine,
        int mapConf,
        DateTimeOffset afterGeneratedUtc)
    {
        var rec = profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == afterMatch.StablePortKey);
        if (rec is null)
        {
            rec = new UsbKnownPortRecord { StablePortKey = afterMatch.StablePortKey };
            profile.KnownPorts.Add(rec);
        }

        rec.UserLabel = trimmedLabel;
        rec.LastSeenUtc = afterGeneratedUtc;
        rec.LastMappingSuggestion = lastMappingSuggestionLine;
        rec.MappingConfidenceScore = mapConf;
        rec.Confidence = Math.Max(rec.Confidence, Math.Min(95, mapConf));

        if (!profile.KnownStablePortKeys.Contains(afterMatch.StablePortKey))
        {
            profile.KnownStablePortKeys.Add(afterMatch.StablePortKey);
        }

        profile.UserLabelsPlaceholder = trimmedLabel;
        store.Save(profile);
    }
}
