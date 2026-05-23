using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.DriveValidation;

public static class DriveValidationUiCopy
{
    public const string FeatureTitle = "Drive Validator";

    public const string Intro =
        "Writes temporary ForgerEMS test files into removable USB free space and reads them back to look for verification errors or suspicious capacity behavior. Safe modes never format the drive and never delete your existing files. Results are advisory evidence for a technician, not a guarantee about the underlying media.";

    public const string NotValidatedBuilderHint =
        "Run Drive Validator before building a technician USB.";

    public const string FailedBuilderWarning =
        "This drive failed validation. Building a toolkit on it may produce a corrupt or unreliable USB.";

    public const string CleanupWarningBuilderHint =
        "Validation finished but some temporary ForgerEMS test files could not be removed. Eject and re-insert the drive, or delete the files in the .forgerems-drive-validator folder before building.";

    public const string InsufficientFreeSpaceBuilderHint =
        "Drive Validator could not finish because of low free space. Make room and run validation again before building a toolkit.";

    public const string SafeModeAdvisory =
        "Safe modes (Quick / Sampled / Full Free-Space) write evidence files into free space. They cannot read sectors outside the file system and a passing result is sampled evidence only, not a guarantee about the underlying media.";

    public static string ModeDisplay(DriveValidationMode mode) =>
        mode switch
        {
            DriveValidationMode.QuickSafeCheck => "Quick Safe Check",
            DriveValidationMode.SampledCapacityCheck => "Sampled Capacity Check",
            DriveValidationMode.FullFreeSpaceValidation => "Full Free-Space Validation",
            DriveValidationMode.DestructiveFullMediaValidation => "Destructive Full Media (disabled)",
            _ => mode.ToString()
        };

    public static string StatusDisplay(DriveValidationStatus status) =>
        status switch
        {
            DriveValidationStatus.NotRun => "Not validated",
            DriveValidationStatus.Running => "Running…",
            DriveValidationStatus.Passed => "Passed",
            DriveValidationStatus.PassedWithWarnings => "Passed with warnings",
            DriveValidationStatus.Failed => "Failed",
            DriveValidationStatus.Cancelled => "Cancelled",
            DriveValidationStatus.UnsafeTargetBlocked => "Blocked (unsafe target)",
            DriveValidationStatus.InsufficientFreeSpace => "Insufficient free space",
            DriveValidationStatus.CleanupWarning => "Passed — cleanup warning",
            _ => status.ToString()
        };

    public static string PortStatusDisplay(DriveValidationPortStatus status) =>
        status switch
        {
            DriveValidationPortStatus.NotValidated => "Drive validation: not run",
            DriveValidationPortStatus.ValidatedOk => "Drive validation: OK",
            DriveValidationPortStatus.Warnings => "Drive validation: warnings",
            DriveValidationPortStatus.FailedValidation => "Drive validation: failed",
            _ => status.ToString()
        };

    /// <summary>
    /// Returns the final, user-facing phase line that replaces the live "CleaningUp…" text
    /// once a Drive Validator run has produced a terminal result. The progress bar/text must
    /// not stay on the last in-flight phase after the result is already known.
    /// </summary>
    public static string TerminalPhaseDisplay(DriveValidationStatus status) =>
        status switch
        {
            DriveValidationStatus.Passed => "Validation complete.",
            DriveValidationStatus.PassedWithWarnings => "Validation complete with warnings.",
            DriveValidationStatus.Failed => "Validation failed.",
            DriveValidationStatus.Cancelled => "Validation cancelled.",
            DriveValidationStatus.CleanupWarning => "Validation complete — cleanup warning.",
            DriveValidationStatus.UnsafeTargetBlocked => "Validation blocked — unsafe target.",
            DriveValidationStatus.InsufficientFreeSpace => "Validation blocked — insufficient free space.",
            DriveValidationStatus.NotRun => "—",
            DriveValidationStatus.Running => "Running…",
            _ => StatusDisplay(status)
        };

    public static string TerminalProgressDisplay(DriveValidationStatus status, int regionsCompleted, int regionsPlanned)
    {
        var verdict = status switch
        {
            DriveValidationStatus.Passed => "Passed",
            DriveValidationStatus.PassedWithWarnings => "Passed with warnings",
            DriveValidationStatus.Failed => "Failed",
            DriveValidationStatus.Cancelled => "Cancelled",
            DriveValidationStatus.CleanupWarning => "Passed — cleanup warning",
            DriveValidationStatus.UnsafeTargetBlocked => "Blocked",
            DriveValidationStatus.InsufficientFreeSpace => "Blocked",
            DriveValidationStatus.NotRun => "Not run",
            DriveValidationStatus.Running => "Running",
            _ => status.ToString()
        };

        return regionsPlanned > 0
            ? $"{regionsCompleted}/{regionsPlanned} · {verdict}"
            : verdict;
    }
}
