using System;

namespace VentoyToolkitSetup.Wpf.Services;

public enum ToolkitHealthAutoRefreshDecision
{
    Refresh,
    SkipNoUsbTarget,
    SkipAlreadyRefreshed,
    SkipBusy,
    SkipUnchanged
}

public sealed record ToolkitHealthAutoRefreshEvaluation(
    ToolkitHealthAutoRefreshDecision Decision,
    string? NormalizedUsbRoot,
    string LiveStatus)
{
    public bool ShouldRefresh => Decision == ToolkitHealthAutoRefreshDecision.Refresh;
}

// Single-fire-per-target latch for the launch-readiness auto refresh path.
// Pure logic — no UI / no IO / no MainViewModel dep — so it is trivially
// testable. The orchestrator does not call the PowerShell script itself; it
// only answers "given this USB target and this busy state, should I trigger
// the existing safe toolkit health refresh now?" and records the result via
// MarkRefreshed once the caller's refresh completes successfully.
//
// Spec mapping:
//   * Launch with target detected → Refresh (one shot)
//   * Launch without target → SkipNoUsbTarget with friendly status
//   * Tab switch after launch → SkipAlreadyRefreshed (no duplicate)
//   * USB target swap → Refresh again (one shot for the new target)
//   * Backend already busy → SkipBusy (do not pile work onto a manual scan)
public sealed class ToolkitHealthAutoRefreshOrchestrator
{
    private readonly object _lock = new();
    private string? _lastEvaluatedRoot;
    private DateTimeOffset? _lastEvaluatedAt;

    public string? LastEvaluatedRoot
    {
        get { lock (_lock) { return _lastEvaluatedRoot; } }
    }

    public DateTimeOffset? LastEvaluatedAt
    {
        get { lock (_lock) { return _lastEvaluatedAt; } }
    }

    public ToolkitHealthAutoRefreshEvaluation Evaluate(string? usbRootPath, bool isBackendBusy)
    {
        var normalized = NormalizeRoot(usbRootPath);

        if (string.IsNullOrEmpty(normalized))
        {
            return new ToolkitHealthAutoRefreshEvaluation(
                ToolkitHealthAutoRefreshDecision.SkipNoUsbTarget,
                normalized,
                "Toolkit health refresh skipped: no USB target.");
        }

        if (isBackendBusy)
        {
            return new ToolkitHealthAutoRefreshEvaluation(
                ToolkitHealthAutoRefreshDecision.SkipBusy,
                normalized,
                "Toolkit health refresh deferred: backend busy.");
        }

        string? last;
        lock (_lock)
        {
            last = _lastEvaluatedRoot;
        }

        if (string.Equals(last, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return new ToolkitHealthAutoRefreshEvaluation(
                ToolkitHealthAutoRefreshDecision.SkipAlreadyRefreshed,
                normalized,
                $"Toolkit health already refreshed for {normalized}; reuse on disk.");
        }

        return new ToolkitHealthAutoRefreshEvaluation(
            ToolkitHealthAutoRefreshDecision.Refresh,
            normalized,
            $"Reading toolkit health for {normalized}...");
    }

    public void MarkRefreshed(string? usbRootPath)
    {
        var normalized = NormalizeRoot(usbRootPath);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        lock (_lock)
        {
            _lastEvaluatedRoot = normalized;
            _lastEvaluatedAt = DateTimeOffset.UtcNow;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _lastEvaluatedRoot = null;
            _lastEvaluatedAt = null;
        }
    }

    // Surfaced so the view-model can build "Toolkit health refreshed for X" /
    // friendly warning text without re-deriving the canonical form of the root.
    public static string? NormalizeRoot(string? usbRootPath)
    {
        if (string.IsNullOrWhiteSpace(usbRootPath))
        {
            return null;
        }

        return usbRootPath.Trim().TrimEnd('\\').ToUpperInvariant();
    }
}
