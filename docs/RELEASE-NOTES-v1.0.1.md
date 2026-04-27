# ForgerEMS v1.0.1 Release Notes

## What Changed

- Added a visible Current Task panel with READY, WORKING, WARNING, ERROR, and COMPLETE states.
- Added determinate and indeterminate progress feedback for long-running operations.
- Added lifecycle logging with `[INIT]`, `[INFO]`, `[OK]`, `[WARN]`, `[ERROR]`, `[ACTION]`, and `[COMPLETE]` prefixes.
- Added download heartbeat and progress logging for managed downloads, including percent, MB counts, speed, ETA, and retry/stall messaging when available.
- Added a real-world USB benchmark that writes, flushes, reads, and deletes a 512 MB or 1 GB temp file.
- Tightened benchmark safety so system, boot, internal, unknown, EFI, and blocked targets are refused.
- Clarified dry-run mode with a visible banner and warning log.
- Clarified Ventoy wording as "Install / Update Ventoy on Selected USB" and logs the partition-modification warning before launch.
- Improved backend version display with Frontend, Backend, and compatibility status.
- Added TODO-safe official manual shortcut entries for CrystalDiskMark, Samsung Magician, and WD Dashboard.

## What Did Not Change

- The backend PowerShell architecture remains intact.
- Existing script names and entrypoints were not renamed.
- Existing buttons and actions remain available.
- Manifest format remains unchanged.
- Checksum validation remains required where managed downloads depend on it.

## QA Checklist

- Launch the app and confirm frontend version `1.0.1` appears in backend/status logs.
- Confirm backend detection reports Compatible, Warning, or Error without hard-failing unless scripts are missing.
- Toggle dry-run and confirm the banner reads `DRY RUN MODE — NO CHANGES WILL BE MADE`.
- Run Verify and confirm lifecycle logs include start/end entries.
- Run Update USB on a safe test USB and confirm large downloads emit progress or heartbeat logs every few seconds.
- Confirm already-valid checksummed files are skipped instead of re-downloaded.
- Run Benchmark USB on a safe removable USB and verify write/read speeds plus Slow/Usable/Fast classification.
- Confirm Benchmark USB is blocked for system, boot, internal, unknown, EFI, or otherwise unsafe targets.
- Run Install / Update Ventoy on a safe selected USB and confirm the warning/confirmation flow appears before launch.
- Build the WPF app and confirm the publish output still produces `ForgerEMS.exe`.
