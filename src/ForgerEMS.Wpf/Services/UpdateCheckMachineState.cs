using System;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>High-level update-check lifecycle for Settings UI (not persisted).</summary>
public enum UpdateCheckMachineState
{
    IdleNotChecked,
    Checking,
    UpToDate,
    UpdateAvailable,
    FailedOffline,
    FailedRateLimited,
    FailedGitHub,
    NoSuitableAssets,
    ConfigError
}

public static class UpdateCheckMachineStateResolver
{
    public static UpdateCheckMachineState Resolve(bool updateCheckInProgress, UpdateCheckResult? last)
    {
        if (updateCheckInProgress)
        {
            return UpdateCheckMachineState.Checking;
        }

        if (last is null)
        {
            return UpdateCheckMachineState.IdleNotChecked;
        }

        if (!last.Succeeded)
        {
            if (last.FailureKind == UpdateCheckFailureKind.ReleaseMetadataInvalid &&
                (last.ErrorMessage ?? string.Empty).Contains("installed", StringComparison.OrdinalIgnoreCase))
            {
                return UpdateCheckMachineState.ConfigError;
            }

            return last.FailureKind switch
            {
                UpdateCheckFailureKind.Network or UpdateCheckFailureKind.Timeout => UpdateCheckMachineState.FailedOffline,
                UpdateCheckFailureKind.AccessDeniedOrRateLimited =>
                    IsLikelyGitHubRateLimit(last)
                        ? UpdateCheckMachineState.FailedRateLimited
                        : UpdateCheckMachineState.FailedGitHub,
                UpdateCheckFailureKind.HttpError or
                    UpdateCheckFailureKind.UpdateSourceUnreachable or
                    UpdateCheckFailureKind.ReleaseEndpointNotFound or
                    UpdateCheckFailureKind.ReleaseMetadataInvalid => UpdateCheckMachineState.FailedGitHub,
                _ => UpdateCheckMachineState.FailedGitHub
            };
        }

        return last.Outcome switch
        {
            UpdateCheckOutcome.UpdateAvailable when last.UpdateAvailable => UpdateCheckMachineState.UpdateAvailable,
            UpdateCheckOutcome.NoSuitableAssets => UpdateCheckMachineState.NoSuitableAssets,
            UpdateCheckOutcome.AlreadyLatest or
                UpdateCheckOutcome.InstalledNewerThanLatestPublic or
                UpdateCheckOutcome.IgnoredVersion or
                UpdateCheckOutcome.NoPublishedRelease or
                UpdateCheckOutcome.None => UpdateCheckMachineState.UpToDate,
            UpdateCheckOutcome.UpdateAvailable => UpdateCheckMachineState.UpToDate,
            UpdateCheckOutcome.Cancelled or UpdateCheckOutcome.Failed => UpdateCheckMachineState.FailedGitHub,
            _ => UpdateCheckMachineState.UpToDate
        };
    }

    public static string Describe(UpdateCheckMachineState state) => state switch
    {
        UpdateCheckMachineState.IdleNotChecked => "Idle — not checked yet",
        UpdateCheckMachineState.Checking => "Checking…",
        UpdateCheckMachineState.UpToDate => "Up to date on selected channel",
        UpdateCheckMachineState.UpdateAvailable => "Update available",
        UpdateCheckMachineState.FailedOffline => "Failed — offline or network",
        UpdateCheckMachineState.FailedRateLimited => "Failed — rate limited",
        UpdateCheckMachineState.FailedGitHub => "Failed — GitHub or metadata",
        UpdateCheckMachineState.NoSuitableAssets => "No suitable download assets",
        UpdateCheckMachineState.ConfigError => "Configuration error",
        _ => "Unknown"
    };

    private static bool IsLikelyGitHubRateLimit(UpdateCheckResult last)
    {
        var msg = (last.ErrorMessage ?? string.Empty) + " " + (last.DiagnosticDetail ?? string.Empty);
        return msg.Contains("rate", StringComparison.OrdinalIgnoreCase);
    }
}
