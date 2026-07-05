using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class DrForgeCliBridgeTests
{
    [Fact]
    public void Locator_MissingPackageReturnsFriendlyNotConfigured()
    {
        using var temp = new TempDir();

        var result = new DrForgeCliLocator().Locate(null, temp.Path);

        Assert.False(result.Found);
        Assert.Equal(DrForgeCliBridgeState.NotConfigured, result.State);
        Assert.Contains("Select", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManifestSchemaAccepted_AndChecksumVerificationPasses()
    {
        using var temp = CreatePackage("hello");

        var inspection = new DrForgeCliManifestReader().InspectPackage(temp.ExecutablePath);

        Assert.True(inspection.PackageFound);
        Assert.True(inspection.Manifest.Found);
        Assert.Equal("drforge-cli-release-manifest/1.0", inspection.Manifest.Schema);
        Assert.True(inspection.Checksums.Present);
        Assert.True(inspection.Checksums.Passed);
        Assert.Equal(1, inspection.Checksums.CheckedFileCount);
    }

    [Fact]
    public void ChecksumVerificationReportsFailure()
    {
        using var temp = CreatePackage("hello");
        File.WriteAllText(temp.ExecutablePath, "changed");

        var inspection = new DrForgeCliManifestReader().InspectPackage(temp.ExecutablePath);

        Assert.True(inspection.Checksums.Present);
        Assert.False(inspection.Checksums.Passed);
        Assert.Contains("mismatch", string.Join(" ", inspection.Checksums.Failures), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessCommandConstruction_UsesVersionSensorCoreHelpAndDriverStatusProbe()
    {
        using var temp = CreatePackage("exe");
        var fake = new FakeDrForgeProcessRunner(
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildVersionArguments(), "Dr. Forge 0.8.0"),
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildSensorCoreHelpArguments(), "Usage: drforge sensor-core"),
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildDriverStatusArguments(), CurrentNoDriverStatusJson));
        var runner = new DrForgeCliRunner(fake);

        var result = await runner.CheckReadinessAsync(temp.ExecutablePath);

        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "--version" }, fake.Calls[0].Arguments);
        Assert.Equal(new[] { "sensor-core", "--help" }, fake.Calls[1].Arguments);
        Assert.Equal(new[] { "sensors", "driver-status", "--json" }, fake.Calls[2].Arguments);
        Assert.NotNull(result.DriverStatus);
        Assert.True(result.DriverStatus.SupportedSchema);
        Assert.False(result.DriverStatus.ProductionDriverShipped);
        Assert.False(result.DriverStatus.DriverInstalled);
        Assert.False(result.DriverStatus.DriverRunning);
        Assert.True(result.DriverStatus.UserModeFallbackActive);
        Assert.True(result.DriverStatus.NoDriverActionTaken);
        Assert.Contains("production driver shipped: no", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("user-mode fallback active: yes", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadinessDriverStatusProbe_IsNonFatalForOlderCli()
    {
        using var temp = CreatePackage("exe");
        var fake = new FakeDrForgeProcessRunner(
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildVersionArguments(), "Dr. Forge 0.8.0"),
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildSensorCoreHelpArguments(), "Usage: drforge sensor-core"),
            new DrForgeCliProcessResult(temp.ExecutablePath, DrForgeCliRunner.BuildDriverStatusArguments(), 2, false, "", "unknown command"));
        var runner = new DrForgeCliRunner(fake);

        var result = await runner.CheckReadinessAsync(temp.ExecutablePath);

        Assert.True(result.Succeeded);
        Assert.Equal(3, fake.Calls.Count);
        Assert.NotNull(result.DriverStatus);
        Assert.False(result.DriverStatus.SupportedSchema);
        Assert.Contains("not reported", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReportAndArchiveCommandConstruction_UsesSnapshotBoundary()
    {
        using var temp = CreatePackage("exe");
        var snapshot = Path.Combine(temp.Path, "snapshot.json");
        var report = Path.Combine(temp.Path, "report.json");
        var archive = Path.Combine(temp.Path, "archive");
        File.WriteAllText(snapshot, "{}");

        var fake = new FakeDrForgeProcessRunner(
            onRun: call =>
            {
                if (call.Arguments.Contains("--out") && call.Arguments.Contains(report))
                    File.WriteAllText(report, "{}");
                if (call.Arguments.Contains("--out") && call.Arguments.Contains(archive))
                    Directory.CreateDirectory(archive);
            },
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildReportArguments(snapshot, report), "report ok"),
            Success(temp.ExecutablePath, DrForgeCliRunner.BuildArchiveArguments(snapshot, archive), "archive ok"));
        var runner = new DrForgeCliRunner(fake);

        var reportResult = await runner.GenerateReportAsync(temp.ExecutablePath, snapshot, report);
        var archiveResult = await runner.GenerateArchiveAsync(temp.ExecutablePath, snapshot, archive);

        Assert.True(reportResult.Succeeded);
        Assert.True(archiveResult.Succeeded);
        Assert.Equal(new[] { "sensor-core", "report", snapshot, "--format", "json", "--out", report }, fake.Calls[0].Arguments);
        Assert.Equal(new[] { "sensor-core", "archive", snapshot, "--out", archive }, fake.Calls[1].Arguments);
        Assert.DoesNotContain("--include-service", fake.Calls.SelectMany(c => c.Arguments));
    }

    [Fact]
    public async Task TimeoutHandling_ReturnsFailedState()
    {
        using var temp = CreatePackage("exe");
        var fake = new FakeDrForgeProcessRunner(
            new DrForgeCliProcessResult(temp.ExecutablePath, DrForgeCliRunner.BuildVersionArguments(), 130, true, "", "timed out"));

        var result = await new DrForgeCliRunner(fake).CheckReadinessAsync(temp.ExecutablePath);

        Assert.False(result.Succeeded);
        Assert.Equal(DrForgeCliBridgeState.Failed, result.State);
        Assert.Contains("timed out", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonZeroStderr_ReturnsFailedState()
    {
        using var temp = CreatePackage("exe");
        var fake = new FakeDrForgeProcessRunner(
            new DrForgeCliProcessResult(temp.ExecutablePath, DrForgeCliRunner.BuildVersionArguments(), 2, false, "", "bad package"));

        var result = await new DrForgeCliRunner(fake).CheckReadinessAsync(temp.ExecutablePath);

        Assert.False(result.Succeeded);
        Assert.Contains("bad package", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IntakeReportReader_RendersNullReadingsAsUnavailable()
    {
        var json = """
        {
          "reportSchemaVersion": "forge-hardware-intake-report/1.0",
          "sourceSchemaVersion": "forge-sensor-core/1.0",
          "platform": { "osFamily": "Windows", "architecture": "X64" },
          "safety": { "satisfiesSafetyInvariants": true, "kernelDriverLoaded": false },
          "summary": {
            "cpuLoadPercent": null,
            "memoryUsedPercent": 42.5,
            "storageCapacityBytes": null,
            "storageSmartHealth": null
          },
          "service": { "requested": false, "available": false, "contributedReadings": 0 },
          "findings": [{ "severity": "Unavailable", "message": "fan RPM: unavailable - requires ring-0" }],
          "ring0Gaps": [{ "reading": "fan RPM", "reason": "Requires ring-0 sensor access; no kernel driver is loaded." }],
          "notes": ["Windows snapshot detected."]
        }
        """;

        var view = new DrForgeIntakeResultReader().ReadJson(json);

        Assert.Contains(view.KeyReadings, r => r.Name == "CPU load" && r.Value == "Unavailable");
        Assert.Contains(view.KeyReadings, r => r.Name == "Storage capacity" && r.Value == "Unavailable");
        Assert.DoesNotContain("CPU load: 0", view.SummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Storage capacity: 0", view.SummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriverStatusReader_AcceptsCurrentNoDriverSchemaAsSafeUserModeState()
    {
        var view = new DrForgeDriverStatusReader().ReadJson(CurrentNoDriverStatusJson);

        Assert.Equal("forger-sensor-driver-preflight/1.1", view.SchemaVersion);
        Assert.True(view.SupportedSchema);
        Assert.Equal("NotImplementedInThisBuild", view.Readiness);
        Assert.False(view.ProductionDriverShipped);
        Assert.False(view.DriverSupportCompiledIn);
        Assert.False(view.DriverInstalled);
        Assert.False(view.DriverRunning);
        Assert.True(view.UserModeFallbackActive);
        Assert.True(view.AbsenceIsNormal);
        Assert.True(view.NoDriverActionTaken);
        Assert.Equal(2, view.DriverRequiredUnavailableCount);
        Assert.Contains("driver installed: no", view.SummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("error", view.SummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriverStatusDisplayBuilder_RendersCurrentNoDriverStatusAsNormal()
    {
        var view = new DrForgeDriverStatusReader().ReadJson(CurrentNoDriverStatusJson);

        var summary = DrForgeDriverStatusDisplayBuilder.BuildSafeSummary(view);

        Assert.Contains("Driver status schema: forger-sensor-driver-preflight/1.1.", summary, StringComparison.Ordinal);
        Assert.Contains("Dr. Forge is running in safe user-mode fallback.", summary, StringComparison.Ordinal);
        Assert.Contains("Production driver shipped: no.", summary, StringComparison.Ordinal);
        Assert.Contains("No production sensor driver is shipped or loaded.", summary, StringComparison.Ordinal);
        Assert.Contains("Driver absence normal/safe: yes.", summary, StringComparison.Ordinal);
        Assert.Contains("No driver action taken: yes.", summary, StringComparison.Ordinal);
        Assert.Contains("Driver-required readings unavailable: 2.", summary, StringComparison.Ordinal);
        Assert.Contains("Driver-required readings are unavailable until a future signed-driver phase.", summary, StringComparison.Ordinal);
        Assert.Contains("No driver install, start, load, or elevation action was taken.", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("error", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriverStatusReader_ToleratesUnknownSchemaWithoutInventingDriverState()
    {
        const string json = """
        {
          "schemaVersion": "forger-sensor-driver-preflight/9.9",
          "readiness": "Future",
          "productionDriverShipped": true,
          "unknown": { "newShape": true }
        }
        """;

        var view = new DrForgeDriverStatusReader().ReadJson(json);

        Assert.False(view.SupportedSchema);
        Assert.Null(view.ProductionDriverShipped);
        Assert.Null(view.DriverInstalled);
        Assert.Contains("unsupported schema", view.SummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DriverStatusDisplayBuilder_TreatsMissingDriverStatusAsSafeUserModeUnavailable()
    {
        var summary = DrForgeDriverStatusDisplayBuilder.BuildSafeSummary(null);

        Assert.Contains("Dr. Forge is not configured.", summary, StringComparison.Ordinal);
        Assert.Contains("No production sensor driver is shipped or loaded.", summary, StringComparison.Ordinal);
        Assert.Contains("No driver install, start, load, or elevation action was taken.", summary, StringComparison.Ordinal);
        Assert.Contains("Driver-required readings are unavailable", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("error", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VersionReader_ExtractsVersionAndCommitWhenAvailable()
    {
        const string versionText = """
        Dr. Forge 0.8.0
        Version: 0.8.0
        Commit: 91d1e1b
        """;

        var view = new DrForgeCliVersionReader().ReadText(versionText);

        Assert.Equal("Dr. Forge 0.8.0", view.ProductLine);
        Assert.Equal("0.8.0", view.Version);
        Assert.Equal("91d1e1b", view.Commit);
        Assert.Contains("Version: 0.8.0; commit: 91d1e1b.", view.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportHistoryReader_NoDrForgeConfiguredShowsSafeUnavailableState()
    {
        using var temp = new TempDir();

        var view = new DrForgeReportHistoryReader().Read(temp.Path, isDrForgeConfigured: false);

        Assert.False(view.IsDrForgeConfigured);
        Assert.Empty(view.Items);
        Assert.Contains("No Dr. Forge CLI is configured yet", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Reports stay local", view.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportHistoryReader_ConfiguredButNoReportsShowsEmptyState()
    {
        using var temp = new TempDir();

        var view = new DrForgeReportHistoryReader().Read(temp.Path, isDrForgeConfigured: true);

        Assert.True(view.IsDrForgeConfigured);
        Assert.True(view.FolderReadable);
        Assert.Empty(view.Items);
        Assert.Contains("No reports found yet", view.SummaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("error", view.SummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportHistoryReader_ConfiguredReportsShowsRecentHistory()
    {
        using var temp = new TempDir();
        var olderReport = Path.Combine(temp.Path, "drforge-intake-report-20260704-100000.json");
        var newerReport = Path.Combine(temp.Path, "drforge-intake-report-20260704-110000.json");
        var archive = Path.Combine(temp.Path, "drforge-intake-archive-20260704-120000");
        var ignoredSnapshot = Path.Combine(temp.Path, "drforge-sensor-core-snapshot-20260704-120000.json");
        var timestamps = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase)
        {
            [olderReport] = new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero),
            [newerReport] = new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero),
            [archive] = new DateTimeOffset(2026, 7, 4, 12, 0, 0, TimeSpan.Zero),
            [ignoredSnapshot] = new DateTimeOffset(2026, 7, 4, 13, 0, 0, TimeSpan.Zero)
        };
        var reader = new DrForgeReportHistoryReader(
            directoryExists: _ => true,
            enumerateEntries: _ => timestamps.Keys,
            getLastWriteTimeUtc: path => timestamps[path]);

        var view = reader.Read(temp.Path, isDrForgeConfigured: true, maxItems: 5);

        Assert.Equal(4, view.Items.Count);
        Assert.Equal("Snapshot", view.Items[0].Kind);
        Assert.Equal("Archive", view.Items[1].Kind);
        Assert.Equal("Report", view.Items[2].Kind);
        Assert.Contains("Recent Dr. Forge reports/history:", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("drforge-intake-archive-20260704-120000", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Reports stay local", view.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportHistoryReader_InaccessibleFolderFailsGracefully()
    {
        using var temp = new TempDir();
        var reader = new DrForgeReportHistoryReader(
            directoryExists: _ => true,
            enumerateEntries: _ => throw new UnauthorizedAccessException("blocked"),
            getLastWriteTimeUtc: _ => DateTimeOffset.UtcNow);

        var view = reader.Read(temp.Path, isDrForgeConfigured: true);

        Assert.True(view.IsDrForgeConfigured);
        Assert.False(view.FolderReadable);
        Assert.Empty(view.Items);
        Assert.Contains("could not be read", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Reports stay local", view.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportDetailReader_ValidLocalJsonReportLoadsSafeSummary()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-20260704-123456.json");
        File.WriteAllText(report,
            """
            {
              "reportSchemaVersion": "forge-hardware-intake-report/1.0",
              "sourceSchemaVersion": "forge-sensor-core-snapshot/1.0",
              "generatedAtUtc": "2026-07-04T12:34:56Z",
              "platform": { "osFamily": "Windows", "osVersion": "11", "architecture": "x64" },
              "safety": { "satisfiesSafetyInvariants": true, "kernelDriverLoaded": false },
              "summary": {
                "cpuLoadPercent": 12.5,
                "memoryUsedPercent": null,
                "storageCapacityBytes": 1024,
                "storageSmartHealth": "OK",
                "cpuTemperatureCelsius": 41
              },
              "cpu": { "model": "Example CPU", "physicalCoreCount": 8, "logicalProcessorCount": 16 },
              "memory": { "totalBytes": 17179869184, "usedBytes": null },
              "battery": { "chargePercent": null, "healthPercent": 92, "cycleCount": 140, "status": "Discharging" },
              "findings": [{ "severity": "Info", "message": "User-mode report generated." }],
              "ring0Gaps": [{ "reading": "Fan RPM", "reason": "Requires a future driver-backed provider." }]
            }
            """);

        var view = new DrForgeReportDetailReader(temp.Path).Read(report);

        Assert.True(view.PreviewAvailable);
        Assert.Equal("Report", view.Kind);
        Assert.Equal("forge-hardware-intake-report/1.0", view.ReportSchema);
        Assert.Equal("forge-sensor-core-snapshot/1.0", view.SourceSchema);
        Assert.Equal("2026-07-04 12:34:56 UTC", view.GeneratedAt);
        Assert.Equal("no", view.KernelDriverLoaded);
        Assert.Equal(3, view.AvailableReadingCount);
        Assert.Equal(1, view.UnavailableReadingCount);
        Assert.Equal(1, view.DriverRequiredUnavailableCount);
        Assert.Contains("Preview status: Preview ready", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Memory used: Unavailable", view.PreviewText, StringComparison.Ordinal);
        Assert.Contains("Local report path: app-managed Dr. Forge report root\\drforge-intake-report-20260704-123456.json", view.SummaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("Memory used: 0", view.PreviewText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reports stay local unless you explicitly export or include them in a support bundle.", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("No driver install, start, load, or elevation action is performed.", view.SummaryText, StringComparison.Ordinal);
        AssertParsedSection(view, "Report Summary");
        AssertParsedSection(view, "Device / System");
        AssertParsedSection(view, "CPU");
        AssertParsedSection(view, "Memory");
        AssertParsedSection(view, "Storage");
        AssertParsedSection(view, "Battery");
        AssertParsedSection(view, "Thermals / Sensors");
        AssertParsedSection(view, "Driver / Safety Status");
        AssertParsedSection(view, "Report Metadata");
        AssertParsedField(view, "CPU", "CPU load", "12.5 %");
        AssertParsedField(view, "Memory", "Memory used", "Unavailable");
        AssertParsedField(view, "Storage", "Storage capacity", "1 KiB");
        AssertParsedField(view, "Battery", "Charge", "Unavailable");
        AssertParsedField(view, "Driver / Safety Status", "Kernel driver loaded", "No");
        Assert.Contains("\"reportSchemaVersion\"", view.RawPreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportDetailReader_MissingFileFailsGracefully()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-missing.json");

        var view = new DrForgeReportDetailReader(temp.Path).Read(report);

        Assert.False(view.PreviewAvailable);
        Assert.Equal("Preview unavailable", view.StatusText);
        Assert.Contains("file was not found", view.PreviewText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportDetailReader_InaccessibleFileFailsGracefully()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-blocked.json");
        var reader = new DrForgeReportDetailReader(
            temp.Path,
            fileExists: _ => true,
            directoryExists: _ => false,
            getLength: _ => throw new UnauthorizedAccessException("blocked"),
            getLastWriteTimeUtc: _ => DateTimeOffset.UtcNow,
            readAllText: _ => throw new UnauthorizedAccessException("blocked"));

        var view = reader.Read(report);

        Assert.False(view.PreviewAvailable);
        Assert.Contains("could not be read or parsed", view.PreviewText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preview status: Preview unavailable", view.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportDetailReader_CorruptJsonFailsGracefully()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-corrupt.json");
        File.WriteAllText(report, "{ not json");

        var view = new DrForgeReportDetailReader(temp.Path).Read(report);

        Assert.False(view.PreviewAvailable);
        Assert.Contains("could not be read or parsed", view.PreviewText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportDetailReader_UnknownJsonSchemaDoesNotInventReadings()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-future.json");
        File.WriteAllText(report,
            """
            {
              "schemaVersion": "future-drforge-report/9.9",
              "futureUnknownBlock": { "nested": [1, 2, 3] }
            }
            """);

        var view = new DrForgeReportDetailReader(temp.Path).Read(report);

        Assert.True(view.PreviewAvailable);
        Assert.Equal("future-drforge-report/9.9", view.ReportSchema);
        Assert.Null(view.AvailableReadingCount);
        Assert.Null(view.UnavailableReadingCount);
        Assert.Contains("Available readings: Unknown", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("No previewable Dr. Forge report sections were found.", view.PreviewText, StringComparison.Ordinal);
        Assert.Contains("futureUnknownBlock", view.RawPreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain(view.ParsedSections, section => section.Title == "CPU");
        Assert.Contains(view.ParsedSections, section => section.Title == "Report Metadata");
    }

    [Fact]
    public void ReportDetailReader_LargeJsonPreviewIsCapped()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-large-preview.json");
        var longFinding = new string('x', DrForgeReportDetailReader.MaxPreviewCharacters + 500) + "TAIL_MARKER";
        File.WriteAllText(report,
            $$"""
            {
              "reportSchemaVersion": "forge-hardware-intake-report/1.0",
              "summary": {
                "cpuLoadPercent": 1,
                "memoryUsedPercent": 2,
                "storageCapacityBytes": 3,
                "storageSmartHealth": "OK"
              },
              "findings": [{ "severity": "Info", "message": "{{longFinding}}" }]
            }
            """);

        var view = new DrForgeReportDetailReader(temp.Path).Read(report);

        Assert.True(view.PreviewAvailable);
        Assert.Contains("[Preview capped for safety.]", view.PreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("TAIL_MARKER", view.PreviewText, StringComparison.Ordinal);
        Assert.Contains("[Preview capped for safety.]", view.RawPreviewText, StringComparison.Ordinal);
        Assert.DoesNotContain("TAIL_MARKER", view.RawPreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportDetailReader_LargeJsonFileUsesMetadataOnly()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-huge.json");
        var reader = new DrForgeReportDetailReader(
            temp.Path,
            fileExists: _ => true,
            directoryExists: _ => false,
            getLength: _ => DrForgeReportDetailReader.MaxJsonParseBytes + 1,
            getLastWriteTimeUtc: _ => DateTimeOffset.UtcNow,
            readAllText: _ => throw new InvalidOperationException("JSON body should not be read past the safe parse limit."));

        var view = reader.Read(report);

        Assert.False(view.PreviewAvailable);
        Assert.Contains("larger than the safe preview parse limit", view.PreviewText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReportDetailReader_MarkdownPreviewIsPlainTextAndCapped()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-20260704-123456.md");
        File.WriteAllText(report, "# Local report" + Environment.NewLine + "<script>alert('x')</script>" + new string('m', DrForgeReportDetailReader.MaxPreviewCharacters + 500));

        var view = new DrForgeReportDetailReader(temp.Path).Read(report);

        Assert.True(view.PreviewAvailable);
        Assert.Equal("Markdown", view.ReportSchema);
        Assert.Contains("Markdown is shown as plain text.", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("<script>alert('x')</script>", view.PreviewText, StringComparison.Ordinal);
        Assert.Contains("[Preview capped for safety.]", view.PreviewText, StringComparison.Ordinal);
        Assert.Equal(view.PreviewText, view.RawPreviewText);
        AssertParsedField(view, "Report Metadata", "Preview limits", "JSON 512 KiB parse cap; Markdown 64 KiB read cap; preview 4000 characters");
    }

    [Fact]
    public void ReportDetailReader_ArchivePreviewDoesNotReadOrExtractContents()
    {
        using var temp = new TempDir();
        var archive = Path.Combine(temp.Path, "drforge-intake-archive-20260704-123456.zip");
        var reader = new DrForgeReportDetailReader(
            temp.Path,
            fileExists: _ => true,
            directoryExists: _ => false,
            getLength: _ => 128,
            getLastWriteTimeUtc: _ => DateTimeOffset.UtcNow,
            readAllText: _ => throw new InvalidOperationException("Archive contents should not be read."),
            readTextPrefix: (_, _) => throw new InvalidOperationException("Archive contents should not be read."));

        var view = reader.Read(archive);

        Assert.False(view.PreviewAvailable);
        Assert.Equal("Archive", view.Kind);
        Assert.Contains("metadata only", view.PreviewText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No archive contents were extracted", view.PreviewText, StringComparison.Ordinal);
        AssertParsedField(view, "Report Metadata", "Archive handling", "Metadata only; no extraction");
    }

    [Fact]
    public void ReportDetailReader_RejectsPathsOutsideManagedReportRootBeforeReading()
    {
        using var temp = new TempDir();
        var outside = Path.GetFullPath(Path.Combine(temp.Path, "..", "drforge-intake-report-outside.json"));
        var reader = new DrForgeReportDetailReader(
            temp.Path,
            fileExists: _ => throw new InvalidOperationException("Outside path should not be probed."),
            directoryExists: _ => throw new InvalidOperationException("Outside path should not be probed."),
            readAllText: _ => throw new InvalidOperationException("Outside path should not be read."));

        var view = reader.Read(outside);

        Assert.False(view.PreviewAvailable);
        Assert.Contains("outside the app-managed Dr. Forge report folder", view.PreviewText, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportDetailReader_FormatsSelectedReportSummaryForCopy()
    {
        using var temp = new TempDir();
        var report = Path.Combine(temp.Path, "drforge-intake-report-copy.json");
        File.WriteAllText(report, """{"reportSchemaVersion":"forge-hardware-intake-report/1.0","summary":{"cpuLoadPercent":null}}""");

        var view = new DrForgeReportDetailReader(temp.Path).Read(report);

        Assert.StartsWith("Local Dr. Forge report preview", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Report: drforge-intake-report-copy.json", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Local report path: app-managed Dr. Forge report root\\drforge-intake-report-copy.json", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Parsed report fields:", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("CPU / CPU load: Unavailable", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Preview is read-only.", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("Reports stay local unless you explicitly export or include them in a support bundle.", view.SummaryText, StringComparison.Ordinal);
        Assert.Contains("No driver install, start, load, or elevation action is performed.", view.SummaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("Raw Preview", view.SummaryText, StringComparison.Ordinal);
    }

    [Fact]
    public void UiCopyIsHonestAboutParityAndUnavailableReadings()
    {
        var xaml = File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("Unavailable readings are shown as unavailable, not zero.", xaml, StringComparison.Ordinal);
        Assert.Contains("does not claim full hardware-monitor parity", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("unavailable readings are zero", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BridgeDoesNotHardcodePrivateDeveloperPaths()
    {
        var text = File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "Services", "DrForgeCliBridge.cs")) +
                   File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "ViewModels", "MainViewModel.cs")) +
                   File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.DoesNotContain("Daddy_FDS", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Desktop\\ForgerDigitalSolutions\\Dr.Forge", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Dr.Forge\\release\\cli", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BridgeAddsNoNetworkTelemetryAccountActivationOrElevationBehavior()
    {
        var text = File.ReadAllText(RepoFile("src", "ForgerEMS.Wpf", "Services", "DrForgeCliBridge.cs"));

        Assert.DoesNotContain("HttpClient", text, StringComparison.Ordinal);
        Assert.DoesNotContain("WebClient", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Socket", text, StringComparison.Ordinal);
        Assert.DoesNotContain("UseShellExecute = true", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Verb = \"runas\"", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pkexec", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sudo", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("activation", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("account", text, StringComparison.OrdinalIgnoreCase);
    }

    private static void AssertParsedSection(DrForgeReportDetailView view, string title)
    {
        Assert.Contains(view.ParsedSections, section => string.Equals(section.Title, title, StringComparison.Ordinal));
    }

    private static void AssertParsedField(DrForgeReportDetailView view, string sectionTitle, string fieldName, string expectedValue)
    {
        var section = Assert.Single(view.ParsedSections, item => string.Equals(item.Title, sectionTitle, StringComparison.Ordinal));
        var field = Assert.Single(section.Fields, item => string.Equals(item.Name, fieldName, StringComparison.Ordinal));
        Assert.Equal(expectedValue, field.Value);
    }

    private static DrForgePackageTempDir CreatePackage(string executableContent)
    {
        var temp = new DrForgePackageTempDir();
        var packageDir = Path.Combine(temp.Path, "windows-x64");
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(temp.ExecutablePath, executableContent);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(executableContent))).ToLowerInvariant();
        File.WriteAllText(Path.Combine(packageDir, "SHA256SUMS.txt"), $"{hash}  drforge.exe{Environment.NewLine}");
        File.WriteAllText(Path.Combine(temp.Path, "drforge-cli-release-manifest.json"),
            """
            {
              "schema": "drforge-cli-release-manifest/1.0",
              "product": "Dr. Forge",
              "cliName": "drforge",
              "version": "0.8.0",
              "commit": "test",
              "safetyPolicy": { "mode": "user-mode", "summary": [] },
              "packages": [
                {
                  "platform": "windows-x64",
                  "status": "published",
                  "checksumFile": "windows-x64/SHA256SUMS.txt"
                }
              ]
            }
            """);
        return temp;
    }

    private const string CurrentNoDriverStatusJson = """
        {
          "schemaVersion": "forger-sensor-driver-preflight/1.1",
          "readiness": "NotImplementedInThisBuild",
          "driverSupportCompiledIn": false,
          "devContractPresent": true,
          "productionDriverShipped": false,
          "userModeFallbackActive": true,
          "absenceIsNormal": true,
          "futureUnknownBlock": { "ignored": true },
          "checks": [
            {
              "name": "driver installed",
              "outcome": "not-applicable",
              "detail": "No driver is installed. Dr. Forge never installs one automatically."
            },
            {
              "name": "driver running",
              "outcome": "info",
              "detail": "No driver is running. Dr. Forge never starts one."
            },
            {
              "name": "user-mode fallback active",
              "outcome": "pass",
              "detail": "Safe user-mode providers keep collecting every reading they can; driver-required readings stay honestly unavailable with reasons."
            }
          ],
          "wouldUnlock": [
            { "gapReadingId": "fans.rpm", "displayName": "Fan RPM (SuperIO)", "safetyTier": "read-only" },
            { "gapReadingId": "motherboard.voltages", "displayName": "Motherboard voltage rails", "safetyTier": "read-only" }
          ],
          "safetyNote": "This preflight is read-only status detection. Nothing was installed, started, stopped, loaded, registered, or modified, and no elevation was requested."
        }
        """;

    private static DrForgeCliProcessResult Success(string executablePath, IReadOnlyList<string> arguments, string stdout) =>
        new(executablePath, arguments, 0, false, stdout, "");

    private static string RepoFile(params string[] parts) =>
        Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray());

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgerEMS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    private sealed class FakeDrForgeProcessRunner : IDrForgeProcessRunner
    {
        private readonly Queue<DrForgeCliProcessResult> _results;
        private readonly Action<Call>? _onRun;

        public FakeDrForgeProcessRunner(params DrForgeCliProcessResult[] results)
            : this(null, results)
        {
        }

        public FakeDrForgeProcessRunner(Action<Call>? onRun, params DrForgeCliProcessResult[] results)
        {
            _onRun = onRun;
            _results = new Queue<DrForgeCliProcessResult>(results);
        }

        public List<Call> Calls { get; } = [];

        public Task<DrForgeCliProcessResult> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var call = new Call(executablePath, arguments.ToList(), timeout);
            Calls.Add(call);
            _onRun?.Invoke(call);
            return Task.FromResult(_results.Dequeue());
        }
    }

    private sealed record Call(string ExecutablePath, IReadOnlyList<string> Arguments, TimeSpan Timeout);

    private class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "forgerems-drforge-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }

    private sealed class DrForgePackageTempDir : TempDir
    {
        public string ExecutablePath => System.IO.Path.Combine(Path, "windows-x64", "drforge.exe");
    }
}
