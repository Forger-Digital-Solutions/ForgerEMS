using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>
/// Coalesces repeated managed-download progress lines so Full Logs / session log files
/// receive one entry per checkpoint instead of one entry per network tick. Live progress
/// UI (current item, percent, speed) is driven by the original ticks via a separate
/// status-text path; this throttle only governs whether a given tick should also be
/// permanently appended to Logs.
/// </summary>
public sealed class ManagedDownloadProgressLogThrottle
{
    /// <summary>Minimum wall-clock spacing between persistent progress entries for the same item.</summary>
    public TimeSpan MinimumCheckpointSpacing { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Minimum percent delta between persistent progress entries for the same item.</summary>
    public double MinimumCheckpointPercentDelta { get; init; } = 10.0;

    private static readonly Regex DownloadingWithItem = new(
        @"Downloading\s+(?<item>.+?)(?:\.\.\.|\s+\d{1,3}(?:\.\d+)?%|\s+\d+\s*(?:KB|MB|GB)\b|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DownloadCompleteWithItem = new(
        @"Download\s+(?:complete|failed|cancelled)\s*:\s*(?<item>[^|\r\n]+?)(?:\s+(?:\d|in\b)|\s*\||$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PercentValue = new(
        @"(?<percent>\d{1,3}(?:\.\d+)?)%",
        RegexOptions.Compiled);

    private static readonly Regex LogPrefix = new(
        @"^\s*(?:\[\d{4}-\d{2}-\d{2}[^\]]*\])?\s*(?:\[(?:INFO|OK|WARN|ERROR|ACTION|INIT|COMPLETE|PROGRESS)\])?\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly Dictionary<string, ItemState> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="logText"/> should be appended to
    /// persistent logs. Per-item ticks are considered progress chatter and dropped unless
    /// the spacing/percent thresholds are exceeded, or the line is a start/100%/error/cancel.
    /// </summary>
    public bool ShouldKeep(string logText, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return true;
        }

        var classification = Classify(logText);
        if (classification.Kind == ProgressLineKind.NotProgress)
        {
            return true;
        }

        var key = classification.ItemKey ?? "__unknown__";

        lock (_sync)
        {
            _state.TryGetValue(key, out var state);

            if (classification.Kind == ProgressLineKind.Terminal)
            {
                _state[key] = new ItemState
                {
                    LastEmittedUtc = nowUtc,
                    LastEmittedPercent = classification.Percent ?? 100.0,
                    HasEmittedStart = true
                };
                return true;
            }

            if (state is null || !state.HasEmittedStart)
            {
                _state[key] = new ItemState
                {
                    LastEmittedUtc = nowUtc,
                    LastEmittedPercent = classification.Percent ?? 0.0,
                    HasEmittedStart = true
                };
                return true;
            }

            var elapsed = nowUtc - state.LastEmittedUtc;
            var percentDelta = classification.Percent.HasValue
                ? Math.Abs(classification.Percent.Value - state.LastEmittedPercent)
                : 0.0;

            if (elapsed >= MinimumCheckpointSpacing ||
                (classification.Percent.HasValue && percentDelta >= MinimumCheckpointPercentDelta))
            {
                state.LastEmittedUtc = nowUtc;
                if (classification.Percent.HasValue)
                {
                    state.LastEmittedPercent = classification.Percent.Value;
                }

                return true;
            }

            return false;
        }
    }

    /// <summary>Clears the throttle's per-item memory (e.g. when a new managed-download run begins).</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _state.Clear();
        }
    }

    internal static ProgressClassification Classify(string logText)
    {
        var trimmed = logText.Trim();
        // Strip the backend's "[2026-05-27 13:42:15][INFO] " timestamp+level prefix so
        // shape checks below behave the same whether we are called with the raw script
        // output or a sanitized in-app log entry.
        var body = LogPrefix.Replace(trimmed, string.Empty);

        // Terminal lines that must always be kept.
        if (body.Contains("Download complete", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Download failed", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("Download cancelled", StringComparison.OrdinalIgnoreCase) ||
            body.Contains("appears stalled", StringComparison.OrdinalIgnoreCase))
        {
            return new ProgressClassification(ProgressLineKind.Terminal, ExtractItemKey(body), TryExtractPercent(body));
        }

        var percent = TryExtractPercent(body);
        if (body.StartsWith("Downloading ", StringComparison.OrdinalIgnoreCase))
        {
            // 100% line also counts as terminal so the final tick is never dropped.
            if (percent.HasValue && percent.Value >= 100.0)
            {
                return new ProgressClassification(ProgressLineKind.Terminal, ExtractItemKey(body), percent);
            }

            return new ProgressClassification(ProgressLineKind.Tick, ExtractItemKey(body), percent);
        }

        if (body.Contains(" MB downloaded", StringComparison.OrdinalIgnoreCase) ||
            body.Contains(" MB / ", StringComparison.OrdinalIgnoreCase) ||
            body.Contains(" downloaded |", StringComparison.OrdinalIgnoreCase) ||
            body.Contains(" of ", StringComparison.OrdinalIgnoreCase) && body.Contains("MB/s", StringComparison.OrdinalIgnoreCase))
        {
            return new ProgressClassification(ProgressLineKind.Tick, ExtractItemKey(body), percent);
        }

        return new ProgressClassification(ProgressLineKind.NotProgress, null, null);
    }

    private static string? ExtractItemKey(string text)
    {
        var match = DownloadingWithItem.Match(text);
        if (match.Success)
        {
            var item = match.Groups["item"].Value.Trim();
            return string.IsNullOrWhiteSpace(item) ? null : item;
        }

        var completeMatch = DownloadCompleteWithItem.Match(text);
        if (completeMatch.Success)
        {
            var item = completeMatch.Groups["item"].Value.Trim();
            return string.IsNullOrWhiteSpace(item) ? null : item;
        }

        return null;
    }

    private static double? TryExtractPercent(string text)
    {
        var match = PercentValue.Match(text);
        if (!match.Success)
        {
            return null;
        }

        if (double.TryParse(
                match.Groups["percent"].Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var percent))
        {
            return percent;
        }

        return null;
    }

    private sealed class ItemState
    {
        public DateTimeOffset LastEmittedUtc;
        public double LastEmittedPercent;
        public bool HasEmittedStart;
    }

    internal enum ProgressLineKind
    {
        NotProgress,
        Tick,
        Terminal
    }

    internal readonly record struct ProgressClassification(ProgressLineKind Kind, string? ItemKey, double? Percent);
}
