using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Human-facing strings for USB Intelligence Pro (beta). Internal enum values stay unchanged.</summary>
public static class UsbIntelligencePanelUiCopy
{
    public const string GuidanceIntro =
        "USB Intelligence helps identify the best port for building Ventoy/ForgerEMS USBs.";

    public const string WorkflowNumbered =
        "1. Select a USB target.\n2. (Optional) Run USB Benchmark for speed context.\n3. Open USB Port Mapping Wizard and follow the steps (capture current port, move USB, detect change, save label).\n4. Advanced users can still use inline mapping under “Advanced” if needed.";

    public const string NotMeasuredClass = "Not measured yet";

    public const string RunBenchmarkToAnalyze = "Run benchmark to analyze this USB";

    public const string ConfidenceLow = "Low";

    public const string ConfidenceMedium = "Medium";

    public const string ConfidenceHigh = "High";

    /// <summary>Legacy tests and callers; maps to <see cref="ConfidenceLow"/>.</summary>
    public const string InsufficientConfidence = ConfidenceLow;

    public const string NeedsBenchmarkRisk = "Needs benchmark";

    public const string NoBenchmarkYet = "No benchmark yet";

    public const string NoPortLabelYet = "No port label saved yet";

    public const string RunBenchmarkRecommended =
        "Run USB Benchmark — measured speeds raise confidence and help compare ports.";

    /// <summary>Prompt when a USB is selected locally but no benchmark completed for that target.</summary>
    public const string UsbSelectedNotBenchmarkedPrompt =
        "This USB hasn't been tested yet. Run a quick benchmark.";

    /// <summary>Shown when topology has not ranked a best port yet or benchmark data is invalid.</summary>
    public const string BestKnownPortPending =
        "No best port yet — run a benchmark.";

    /// <summary>Polish raw parser output for the panel.</summary>
    public static UsbIntelligencePanelUiState FinalizeForDisplay(
        UsbIntelligencePanelUiState raw,
        bool benchmarkSucceeded,
        int combinedConfidenceScore,
        DateTimeOffset? benchmarkTimestampUtc,
        string? bestKnownPortSummaryFromDiagnostics)
    {
        var detected = HumanizeDetectedClass(raw.DetectedClassDisplay);
        var quality = HumanizeQuality(raw.RecommendationQualityDisplay);
        var confScore = HumanizeConfidenceScore(raw.ConfidenceScoreDisplay, benchmarkSucceeded, combinedConfidenceScore);
        var confReason = BuildConfidenceTierExplanation(
            benchmarkSucceeded,
            combinedConfidenceScore,
            raw.ConfidenceReasonDisplay);
        var bench = HumanizeBenchmarkLine(raw.BenchmarkReadWriteDisplay);
        var mapping = HumanizeMapping(raw.MappingLabelDisplay);
        var lastTime = raw.LastBenchmarkTimeDisplay;
        var age = FormatBenchmarkAge(benchmarkTimestampUtc);

        var bestPort = string.IsNullOrWhiteSpace(bestKnownPortSummaryFromDiagnostics)
            ? (string.IsNullOrWhiteSpace(raw.BestKnownPortSummary) ? "—" : raw.BestKnownPortSummary.Trim())
            : bestKnownPortSummaryFromDiagnostics.Trim();

        if (!benchmarkSucceeded)
        {
            bestPort = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(bestPort))
        {
            bestPort = "—";
        }

        var runHint = string.Empty;
        if (!benchmarkSucceeded || combinedConfidenceScore < 55)
        {
            runHint = RunBenchmarkRecommended;
        }

        var builderLine = HumanizeBuilderHintLine(raw.BuilderSummaryLine);
        var lastClock = string.IsNullOrWhiteSpace(lastTime) || lastTime == "—" ? "—" : lastTime;

        return raw with
        {
            BuilderSummaryLine = builderLine,
            DetectedClassDisplay = detected,
            RecommendationQualityDisplay = quality,
            ConfidenceScoreDisplay = confScore,
            ConfidenceReasonDisplay = confReason,
            BenchmarkReadWriteDisplay = bench,
            LastBenchmarkTimeDisplay = lastClock,
            MappingLabelDisplay = mapping,
            BestKnownPortSummary = bestPort,
            BenchmarkAgeSummary = string.IsNullOrWhiteSpace(age) ? "—" : age,
            RunBenchmarkRecommendedLine = runHint
        };
    }

    public static string HumanizeBuilderHintLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return line;
        }

        var t = line;
        t = ReplaceToken(t, "Risk: unknown", $"Risk: {NeedsBenchmarkRisk}", StringComparison.OrdinalIgnoreCase);
        t = ReplaceToken(t, "Risk: Unknown", $"Risk: {NeedsBenchmarkRisk}", StringComparison.OrdinalIgnoreCase);
        t = ReplaceToken(t, "| Speed class: unknown |", "| Speed class: Not measured yet |", StringComparison.OrdinalIgnoreCase);
        t = ReplaceToken(t, "Speed class: unknown", "Speed class: Not measured yet", StringComparison.OrdinalIgnoreCase);
        return t;
    }

    private static string ReplaceToken(string haystack, string oldVal, string newVal, StringComparison cmp) =>
        haystack.Replace(oldVal, newVal, cmp);

    private static string HumanizeDetectedClass(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim() == "—")
        {
            return NotMeasuredClass;
        }

        if (s.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return NotMeasuredClass;
        }

        return s;
    }

    private static string HumanizeQuality(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim() == "—")
        {
            return RunBenchmarkToAnalyze;
        }

        if (s.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return RunBenchmarkToAnalyze;
        }

        return s;
    }

    private static string HumanizeConfidenceScore(string s, bool benchOk, int combined)
    {
        _ = s;
        return ConfidenceTierLabel(benchOk, combined);
    }

    public static string ConfidenceTierLabel(bool benchmarkSucceeded, int combinedScore)
    {
        var tierScore = benchmarkSucceeded ? combinedScore : Math.Min(combinedScore, 39);
        return tierScore switch
        {
            >= 70 => ConfidenceHigh,
            >= 40 => ConfidenceMedium,
            _ => ConfidenceLow
        };
    }

    /// <summary>Short, non-technical explanation paired with <see cref="ConfidenceTierLabel"/>.</summary>
    public static string BuildConfidenceTierExplanation(bool benchmarkSucceeded, int combinedScore, string? existingReason)
    {
        var tier = ConfidenceTierLabel(benchmarkSucceeded, combinedScore);
        string core = tier switch
        {
            ConfidenceHigh =>
                "High — benchmark and topology signals agree well for this stick; port picks are more reliable.",
            ConfidenceMedium =>
                "Medium — some measured or mapped data exists; results can still change if you switch ports or hubs.",
            _ => benchmarkSucceeded
                ? "Low — a benchmark ran, but overall confidence is still weak; try another USB 3 port or cable."
                : "Low — no successful file benchmark yet, so speeds and best-port hints are mostly guesses."
        };

        if (string.IsNullOrWhiteSpace(existingReason))
        {
            return core;
        }

        var extra = existingReason.Trim();
        if (extra.Length > 140)
        {
            extra = extra[..137] + "...";
        }

        return $"{core} ({extra})";
    }

    private static string HumanizeBenchmarkLine(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim() == "—")
        {
            return NoBenchmarkYet;
        }

        if (s.Contains("No successful benchmark", StringComparison.OrdinalIgnoreCase))
        {
            return NoBenchmarkYet;
        }

        return s;
    }

    private static string HumanizeMapping(string s)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim() == "—")
        {
            return NoPortLabelYet;
        }

        return s;
    }

    private static string FormatBenchmarkAge(DateTimeOffset? benchUtc)
    {
        if (benchUtc is null)
        {
            return string.Empty;
        }

        var age = DateTimeOffset.UtcNow - benchUtc.Value.ToUniversalTime();
        if (age.TotalMinutes < 2)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return $"about {(int)age.TotalMinutes} min ago";
        }

        if (age.TotalHours < 48)
        {
            return $"about {(int)age.TotalHours} h ago";
        }

        return $"{(int)age.TotalDays} days ago";
    }
}
