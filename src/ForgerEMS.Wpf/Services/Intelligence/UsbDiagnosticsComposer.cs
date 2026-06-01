using System;
using System.Collections.Generic;
using System.Linq;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.DriveValidation;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static class UsbDiagnosticsComposer
{
    public static UsbDiagnosticsEmbeddedSection Build(UsbTopologySnapshot snapshot, UsbMachineProfile? profile)
    {
        var rec = snapshot.SelectedTargetRecommendation;
        var diff = snapshot.TopologyDiff;
        var issues = new List<UsbDiagnosticIssue>();
        var changed = diff is not null &&
                      diff.DiffConfidenceReason != "No prior snapshot to compare." &&
                      (diff.AddedDevices.Count > 0 || diff.RemovedDevices.Count > 0 || diff.ChangedDevices.Count > 0);

        var summaryLine = diff?.SummaryLine ?? snapshot.SummaryLine;
        var recommendLine = rec?.Detail ?? diff?.RecommendationLine ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(diff?.RecommendationLine) &&
            !string.Equals(recommendLine, diff.RecommendationLine, StringComparison.Ordinal))
        {
            recommendLine = $"{recommendLine} {diff.RecommendationLine}".Trim();
        }

        var confidence = "medium";
        if (rec?.Quality is UsbBuilderQuality.Ideal or UsbBuilderQuality.Good)
        {
            confidence = "high";
        }
        else if (rec?.Quality is UsbBuilderQuality.Unknown)
        {
            confidence = "low";
        }

        if (rec?.Quality == UsbBuilderQuality.Slow)
        {
            issues.Add(new UsbDiagnosticIssue
            {
                Severity = DiagnosticSeverityLevel.Warning,
                Message = "USB link class looks like USB 2; large ISO copies will be slow."
            });
        }

        if (rec?.Quality == UsbBuilderQuality.Risky)
        {
            issues.Add(new UsbDiagnosticIssue
            {
                Severity = DiagnosticSeverityLevel.Warning,
                Message = "USB path changed since the last snapshot; reconnect and rescan before a long build."
            });
        }

        if (rec?.Quality == UsbBuilderQuality.Unknown)
        {
            issues.Add(new UsbDiagnosticIssue
            {
                Severity = DiagnosticSeverityLevel.Unknown,
                Message = "USB speed class could not be confirmed from WMI heuristics."
            });
        }

        if (changed && diff!.ChangedDevices.Any(c => c.ChangeKind == "SpeedClarified") &&
            issues.TrueForAll(i => i.Severity != DiagnosticSeverityLevel.Warning))
        {
            issues.Add(new UsbDiagnosticIssue
            {
                Severity = DiagnosticSeverityLevel.Ok,
                Message = "Speed classification improved from unknown to a concrete USB class."
            });
        }

        var portRecEarly = !string.IsNullOrWhiteSpace(snapshot.SelectedTargetStablePortKey) && profile is not null
            ? profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == snapshot.SelectedTargetStablePortKey)
            : null;
        if (portRecEarly is not null &&
            portRecEarly.LastDriveValidationPortStatus != DriveValidationPortStatus.NotValidated)
        {
            var dvMsg = string.IsNullOrWhiteSpace(portRecEarly.LastDriveValidationSummary)
                ? DriveValidationUiCopy.PortStatusDisplay(portRecEarly.LastDriveValidationPortStatus)
                : portRecEarly.LastDriveValidationSummary;

            // Treat history older than 30 days as advisory — the port may be holding a different drive now.
            var stale = portRecEarly.LastDriveValidationUtc is { } when
                ? DateTimeOffset.UtcNow - portRecEarly.LastDriveValidationUtc.Value > TimeSpan.FromDays(30)
                : false;

            var severity = portRecEarly.LastDriveValidationPortStatus switch
            {
                DriveValidationPortStatus.ValidatedOk => stale ? DiagnosticSeverityLevel.Unknown : DiagnosticSeverityLevel.Ok,
                DriveValidationPortStatus.Warnings => DiagnosticSeverityLevel.Warning,
                DriveValidationPortStatus.FailedValidation => DiagnosticSeverityLevel.Warning,
                _ => DiagnosticSeverityLevel.Unknown
            };

            issues.Add(new UsbDiagnosticIssue
            {
                Severity = severity,
                Message = stale
                    ? "Drive Validator (stale history >30d): " + dvMsg
                    : "Drive Validator: " + dvMsg
            });
        }

        var bench = snapshot.SelectedTargetBenchmark;
        if (bench is { Succeeded: true })
        {
            if (bench.ReadLikelyCached || bench.ReadIsEstimate)
            {
                confidence = "medium";
                issues.Add(new UsbDiagnosticIssue
                {
                    Severity = DiagnosticSeverityLevel.Warning,
                    Message = "Benchmark read speed may be cached; trust the measured write speed more than the read number."
                });
            }
            else if (bench.Classification == UsbSpeedMeasurementClass.Bottleneck)
            {
                issues.Add(new UsbDiagnosticIssue
                {
                    Severity = DiagnosticSeverityLevel.Warning,
                    Message = "Benchmark shows a bottlenecked link (asymmetric or slow vs expected USB 3)."
                });
            }
            else if (!string.IsNullOrWhiteSpace(bench.SummaryLine))
            {
                issues.Add(new UsbDiagnosticIssue
                {
                    Severity = DiagnosticSeverityLevel.Ok,
                    Message = "Benchmark: " + bench.SummaryLine
                });
            }
        }

        if (issues.Count == 0)
        {
            issues.Add(new UsbDiagnosticIssue
            {
                Severity = DiagnosticSeverityLevel.Ok,
                Message = rec?.Summary ?? "USB topology scan completed."
            });
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ElevatedTelemetrySummary))
        {
            issues.Add(new UsbDiagnosticIssue
            {
                Severity = snapshot.ElevatedTelemetryState switch
                {
                    ElevatedScanTelemetryState.Fresh => DiagnosticSeverityLevel.Ok,
                    ElevatedScanTelemetryState.Stale => DiagnosticSeverityLevel.Warning,
                    _ => DiagnosticSeverityLevel.Info
                },
                Message = snapshot.ElevatedTelemetrySummary
            });
        }

        var overall = MapOverall(issues);
        var portCount = profile?.KnownPorts?.Count > 0
            ? profile.KnownPorts.Count
            : profile?.KnownStablePortKeys?.Count ?? 0;

        var portRec = !string.IsNullOrWhiteSpace(snapshot.SelectedTargetStablePortKey) &&
                      !string.IsNullOrWhiteSpace(snapshot.SelectedTargetPortUserLabel) &&
                      profile is not null
            ? profile.KnownPorts.FirstOrDefault(p => p.StablePortKey == snapshot.SelectedTargetStablePortKey)
            : null;

        var mapSummary = !string.IsNullOrWhiteSpace(snapshot.SelectedTargetPortLabelStatusLine)
            ? snapshot.SelectedTargetPortLabelStatusLine
            : string.Empty;

        var recommendWithBench = recommendLine;
        if (bench is { Succeeded: true } && !string.IsNullOrWhiteSpace(bench.SummaryLine))
        {
            var readNote = bench.ReadLikelyCached || bench.ReadIsEstimate
                ? " read unverified/cache suspected"
                : $" read {bench.ReadSpeedMBps:0.0} MB/s verified";
            var portNote = bench.AttachedToVerifiedPort == false
                ? " Benchmark measured on unverified current port; save/update a port label to attach it to a physical port."
                : string.Empty;
            recommendWithBench = bench.ReadLikelyCached || bench.ReadIsEstimate
                ? $"{recommendWithBench} Measured: write {bench.WriteSpeedMBps:0.0} MB/s verified; read unverified (cache suspected). Raw cached read sample {bench.ReadSpeedMBps:0.0} MB/s.{portNote}".Trim()
                : $"{recommendWithBench} Measured: write {bench.WriteSpeedMBps:0.0} MB/s, {readNote} ({bench.Classification}).{portNote}".Trim();
        }

        var riskSummary = rec is null
            ? "Current target risk: unknown (no recommendation)."
            : $"Current target risk: {rec.Risk}.";

        var bestPort = BuildBestKnownPortSummary(profile);

        return new UsbDiagnosticsEmbeddedSection
        {
            UsbSummaryLine = summaryLine,
            UsbRecommendationLine = string.IsNullOrWhiteSpace(recommendWithBench) ? rec?.Summary ?? string.Empty : recommendWithBench,
            UsbConfidence = confidence,
            UsbIssues = issues,
            UsbChangedSinceLastScan = changed,
            UsbProfileKnownPortsCount = portCount,
            UsbOverallSeverity = overall,
            LastBenchmark = bench,
            MappingConfidenceScore = !string.IsNullOrWhiteSpace(snapshot.SelectedTargetPortUserLabel)
                ? portRec?.MappingConfidenceScore ?? 0
                : 0,
            MappingConfidenceSummary = mapSummary,
            CombinedConfidenceScore = snapshot.CombinedConfidenceScore,
            CombinedConfidenceSummary = string.IsNullOrWhiteSpace(snapshot.CombinedConfidenceReason)
                ? (rec?.ConfidenceReason ?? string.Empty)
                : snapshot.CombinedConfidenceReason,
            UsbCurrentTargetRiskSummary = riskSummary,
            UsbBestKnownPortSummary = bestPort,
            ElevatedTelemetrySummary = snapshot.ElevatedTelemetrySummary,
            ElevatedTelemetrySource = snapshot.ElevatedTelemetrySource,
            ElevatedTelemetryConfidence = snapshot.ElevatedTelemetryConfidence,
            ElevatedTelemetryCollectedAtUtc = snapshot.ElevatedTelemetryCollectedAtUtc,
            ElevatedTelemetryIsStale = snapshot.ElevatedTelemetryState == ElevatedScanTelemetryState.Stale
        };
    }

    private static string BuildBestKnownPortSummary(UsbMachineProfile? profile)
    {
        if (profile?.KnownPorts is not { Count: > 0 })
        {
            return string.Empty;
        }

        var ranked = profile.KnownPorts
            .Where(p => p.LastBenchmark?.Succeeded == true &&
                        p.LastBenchmark.WriteSpeedMBps > 0 &&
                        !p.LastBenchmark.ReadLikelyCached &&
                        !p.LastBenchmark.ReadIsEstimate &&
                        p.LastBenchmark.AttachedToVerifiedPort != false)
            .OrderByDescending(p => p.LastBenchmark!.WriteSpeedMBps)
            .FirstOrDefault();

        if (ranked is null)
        {
            var unverified = profile.UnverifiedBenchmarkByDriveLetter.Values
                .Where(b => b.Succeeded &&
                            b.WriteSpeedMBps > 0 &&
                            !b.ReadLikelyCached &&
                            !b.ReadIsEstimate)
                .OrderByDescending(b => b.WriteSpeedMBps)
                .FirstOrDefault();
            if (unverified is not null)
            {
                return $"Best measured port: Unverified current port (~{unverified.WriteSpeedMBps:0.0} MB/s write; label needed).";
            }

            var labeled = profile.KnownPorts.Count(p => !string.IsNullOrWhiteSpace(p.UserLabel));
            return labeled > 0
                ? $"{labeled} mapped port(s); benchmark a port to rank them by speed."
                : string.Empty;
        }

        var bestBenchmark = ranked.LastBenchmark!;
        var label = !string.IsNullOrWhiteSpace(ranked.UserLabel)
            ? ranked.UserLabel.Trim()
            : !string.IsNullOrWhiteSpace(bestBenchmark.AttachedPortLabel)
                ? bestBenchmark.AttachedPortLabel.Trim()
                : "Unverified current port";
        return $"Best measured port: {label} (~{bestBenchmark.WriteSpeedMBps:0.0} MB/s write).";
    }

    private static DiagnosticSeverityLevel MapOverall(List<UsbDiagnosticIssue> issues)
    {
        if (issues.Any(i => i.Severity == DiagnosticSeverityLevel.Blocked))
        {
            return DiagnosticSeverityLevel.Blocked;
        }

        if (issues.Any(i => i.Severity == DiagnosticSeverityLevel.Warning))
        {
            return DiagnosticSeverityLevel.Warning;
        }

        if (issues.Any(i => i.Severity == DiagnosticSeverityLevel.Unknown))
        {
            return DiagnosticSeverityLevel.Unknown;
        }

        return DiagnosticSeverityLevel.Ok;
    }
}
