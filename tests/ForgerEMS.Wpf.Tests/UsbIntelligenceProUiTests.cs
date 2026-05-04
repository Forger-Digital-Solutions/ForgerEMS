using System;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class UsbIntelligenceProUiTests
{
    [Fact]
    public void UsbIntelligenceLatestPanelReader_ParsesBenchmarkAndLabel()
    {
        var json = """
            {
              "selectedTargetRecommendation": {
                "summary": "OK",
                "detail": "Ready",
                "risk": "low",
                "speed": "usb3",
                "quality": "ideal",
                "classificationLine": "Quality: Ideal",
                "confidenceScore": 88,
                "confidenceReason": "mixed signals",
                "measuredClassification": "usb3"
              },
              "selectedTargetBenchmark": {
                "succeeded": true,
                "writeSpeedMBps": 142.0,
                "readSpeedMBps": 160.0,
                "classification": "usb3",
                "timestamp": "2026-05-01T12:00:00+00:00"
              },
              "combinedConfidenceScore": 85,
              "combinedConfidenceReason": "Confidence from: sequential file benchmark.",
              "selectedTargetPortUserLabel": "Rear Blue USB 3"
            }
            """;

        var state = UsbIntelligenceLatestPanelReader.Parse(json);
        Assert.Equal("Rear Blue USB 3", state.MappingLabelDisplay);
        Assert.Contains("142.0", state.BenchmarkReadWriteDisplay);
        Assert.Contains("160.0", state.BenchmarkReadWriteDisplay);
        Assert.Equal(UsbIntelligencePanelUiCopy.ConfidenceHigh, state.ConfidenceScoreDisplay);
        Assert.Contains("High —", state.ConfidenceReasonDisplay, System.StringComparison.Ordinal);
        Assert.Equal("Ideal", state.RecommendationQualityDisplay);
        Assert.DoesNotContain("USBSTOR", state.BuilderSummaryLine, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\\\.\\", state.BuilderSummaryLine);
    }

    [Fact]
    public void UsbKyraNarrativeBuilder_IncludesMappedLabelAndBenchmark()
    {
        var snap = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SelectedTargetPortUserLabel = "Rear Blue USB 3",
            CombinedConfidenceScore = 82,
            SelectedTargetBenchmark = new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 142,
                ReadSpeedMBps = 155,
                Classification = UsbSpeedMeasurementClass.Usb3,
                ConfidenceScore = 80,
                Timestamp = DateTimeOffset.UtcNow,
                SummaryLine = "ok",
                DetailReason = "Throughput typical."
            },
            SelectedTargetRecommendation = new UsbBuilderRecommendation
            {
                Summary = "x",
                Detail = "y",
                Quality = UsbBuilderQuality.Ideal,
                Risk = UsbPortRiskLevel.Low,
                Speed = UsbSpeedClassification.Usb3
            }
        };

        var n = UsbKyraNarrativeBuilder.Build(snap);
        Assert.Contains("Rear Blue USB 3", n.ShortAnswer, StringComparison.Ordinal);
        Assert.Contains("142", n.ShortAnswer);
        Assert.Contains("high", n.ShortAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraUsbAnswerBuilder_FromJson_UsesMappedPortWording()
    {
        var json = """
            {
              "kyraUsbNarrative": {
                "shortAnswer": "Short answer: ok",
                "likelyCause": "cause",
                "nextStep": "step"
              },
              "selectedTargetPortUserLabel": "Rear Blue USB 3",
              "selectedTargetBenchmark": {
                "succeeded": true,
                "writeSpeedMBps": 142.0,
                "readSpeedMBps": 150.0,
                "classification": "usb3"
              }
            }
            """;

        var ans = KyraUsbAnswerBuilder.TryBuildAnswerFromJson("which port is best", json);
        Assert.NotNull(ans);
        Assert.Contains("Rear Blue USB 3", ans, StringComparison.Ordinal);
        Assert.Contains("142", ans);
        Assert.DoesNotContain("USBSTOR", ans, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbIntelligenceLatestPanelReader_BestPortDiagnosticsIgnoredWithoutSuccessfulBenchmark()
    {
        const string json = """
            {
              "usbDiagnostics": { "usbBestKnownPortSummary": "Rear (~60 MB/s)" },
              "selectedTargetBenchmark": { "succeeded": false, "writeSpeedMBps": 0, "readSpeedMBps": 0 }
            }
            """;

        var state = UsbIntelligenceLatestPanelReader.Parse(json);
        Assert.Equal("—", state.BestKnownPortSummary.Trim());
    }

    [Fact]
    public void UsbIntelligencePanelUiCopy_Finalize_DropsBestPortWhenBenchmarkDidNotSucceed()
    {
        var raw = new UsbIntelligencePanelUiState { BestKnownPortSummary = "Front (~60 MB/s)" };
        var finalized = UsbIntelligencePanelUiCopy.FinalizeForDisplay(
            raw,
            benchmarkSucceeded: false,
            combinedConfidenceScore: 80,
            benchmarkTimestampUtc: null,
            bestKnownPortSummaryFromDiagnostics: "Rear (~90 MB/s)");
        Assert.Equal("—", finalized.BestKnownPortSummary.Trim());
    }

    [Fact]
    public void UsbIntelligenceLatestPanelReader_UnknownPaths_UseFriendlyCopy()
    {
        var json = """
            {
              "selectedTargetRecommendation": {
                "summary": "Wait",
                "detail": "Need data",
                "risk": "unknown",
                "speed": "unknown",
                "quality": "unknown",
                "classificationLine": "",
                "confidenceScore": 0,
                "confidenceReason": "",
                "measuredClassification": "unknown"
              }
            }
            """;

        var state = UsbIntelligenceLatestPanelReader.Parse(json);
        Assert.Equal(UsbIntelligencePanelUiCopy.NotMeasuredClass, state.DetectedClassDisplay);
        Assert.Equal(UsbIntelligencePanelUiCopy.RunBenchmarkToAnalyze, state.RecommendationQualityDisplay);
        Assert.Equal(UsbIntelligencePanelUiCopy.InsufficientConfidence, state.ConfidenceScoreDisplay);
        Assert.Equal(UsbIntelligencePanelUiCopy.NoBenchmarkYet, state.BenchmarkReadWriteDisplay);
        Assert.Contains(UsbIntelligencePanelUiCopy.NeedsBenchmarkRisk, state.BuilderSummaryLine, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbIntelligencePanelUiCopy_LowConfidence_AddsBenchmarkHint()
    {
        var raw = new UsbIntelligencePanelUiState { BuilderSummaryLine = "x" };
        var finalized = UsbIntelligencePanelUiCopy.FinalizeForDisplay(raw, benchmarkSucceeded: false, 20, null, null);
        Assert.Equal(UsbIntelligencePanelUiCopy.RunBenchmarkRecommended, finalized.RunBenchmarkRecommendedLine);
    }

    [Fact]
    public void KyraUsbAnswerBuilder_IncludesMappingWorkflowLines()
    {
        const string json = """
            {
              "kyraUsbNarrative": { "shortAnswer": "s", "likelyCause": "c", "nextStep": "n" }
            }
            """;
        var ans = KyraUsbAnswerBuilder.TryBuildAnswerFromJson("How do I map USB ports?", json);
        Assert.NotNull(ans);
        Assert.Contains("USB Port Mapping Wizard", ans, StringComparison.Ordinal);
        Assert.Contains("detect", ans, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsbDiagnosticsComposer_IncludesBestKnownPortSummary()
    {
        var profile = new UsbMachineProfile();
        profile.KnownPorts.Add(new UsbKnownPortRecord
        {
            StablePortKey = "p1",
            UserLabel = "Front panel",
            LastBenchmark = new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 90,
                ReadSpeedMBps = 95,
                Classification = UsbSpeedMeasurementClass.Usb3,
                ConfidenceScore = 70,
                Timestamp = DateTimeOffset.UtcNow,
                SummaryLine = "fast",
                DetailReason = "ok"
            }
        });
        profile.KnownPorts.Add(new UsbKnownPortRecord
        {
            StablePortKey = "p2",
            UserLabel = "Rear Blue USB 3",
            LastBenchmark = new UsbIntelligenceBenchmarkResult
            {
                Succeeded = true,
                WriteSpeedMBps = 142,
                ReadSpeedMBps = 150,
                Classification = UsbSpeedMeasurementClass.Usb3,
                ConfidenceScore = 72,
                Timestamp = DateTimeOffset.UtcNow,
                SummaryLine = "faster",
                DetailReason = "ok"
            }
        });

        var snap = new UsbTopologySnapshot
        {
            GeneratedUtc = DateTimeOffset.UtcNow,
            SummaryLine = "s",
            SelectedTargetRecommendation = new UsbBuilderRecommendation
            {
                Summary = "ok",
                Detail = "d",
                Risk = UsbPortRiskLevel.Low,
                Speed = UsbSpeedClassification.Usb3,
                Quality = UsbBuilderQuality.Ideal
            }
        };

        var diag = UsbDiagnosticsComposer.Build(snap, profile);
        Assert.Contains("Rear Blue USB 3", diag.UsbBestKnownPortSummary);
        Assert.Contains("142", diag.UsbBestKnownPortSummary);
        Assert.Contains("Current target risk", diag.UsbCurrentTargetRiskSummary);
        Assert.Equal(2, diag.UsbProfileKnownPortsCount);
    }
}
