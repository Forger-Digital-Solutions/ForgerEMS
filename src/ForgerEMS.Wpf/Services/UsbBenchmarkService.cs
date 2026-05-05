using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.Services;

public interface IUsbBenchmarkService
{
    Task<UsbBenchmarkResult> RunSequentialBenchmarkAsync(
        UsbTargetInfo target,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default);
}

public sealed class UsbBenchmarkResult
{
    public Guid RunId { get; init; }

    public bool Succeeded { get; init; }

    public string Status { get; init; } = "Not tested";

    public string Summary { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public string WriteSpeedDisplay { get; init; } = "Not tested";

    public string ReadSpeedDisplay { get; init; } = "Not tested";

    public int TestSizeMb { get; init; }

    public DateTimeOffset? LastTestedAt { get; init; }

    public string Classification { get; init; } = string.Empty;

    /// <summary>Native/PowerShell measured write MB/s (for Intelligence profile sync).</summary>
    public double WriteSpeedMBps { get; init; }

    /// <summary>Native/PowerShell measured read MB/s.</summary>
    public double ReadSpeedMBps { get; init; }

    public int BenchmarkDurationMs { get; init; }

    /// <summary><see cref="UsbSpeedMeasurementClass"/> name for cache JSON.</summary>
    public string IntelligenceMeasurementClass { get; init; } = string.Empty;

    public int IntelligenceConfidenceScore { get; init; }

    public UsbBenchmarkResultKind ResultKind { get; init; } = UsbBenchmarkResultKind.NotStarted;

    public UsbBenchmarkCancellationSource CancellationSource { get; init; } = UsbBenchmarkCancellationSource.None;

    public DateTimeOffset? StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public long ActualBytesWritten { get; init; }

    public long ActualBytesRead { get; init; }

    public long WriteElapsedMs { get; init; }

    public long ReadElapsedMs { get; init; }

    public bool ReadLikelyCached { get; init; }

    public bool ReadIsEstimate { get; init; }

    public string BenchmarkConfidence { get; init; } = string.Empty;

    public string AccuracyWarning { get; init; } = string.Empty;

    public string TargetTopologyFingerprint { get; init; } = string.Empty;

    public string UiSummaryLine { get; init; } = string.Empty;

    public UsbBenchmarkResultKind GetEffectiveResultKind()
    {
        if (ResultKind != UsbBenchmarkResultKind.NotStarted)
        {
            return ResultKind;
        }

        if (Succeeded && WriteSpeedMBps > 0 && ReadSpeedMBps > 0 &&
            Status.Equals("Complete", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBenchmarkResultKind.Completed;
        }

        if (Status.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBenchmarkResultKind.BlockedBySafety;
        }

        if (Status.Equals("Device removed", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBenchmarkResultKind.DeviceRemoved;
        }

        if (Status.Equals("Target changed", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBenchmarkResultKind.TargetChanged;
        }

        if (Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBenchmarkResultKind.CancelledByUser;
        }

        if (Status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            return UsbBenchmarkResultKind.IoFailed;
        }

        return UsbBenchmarkResultKind.UnknownFailed;
    }

    /// <summary>History disk cache and Intelligence sync: successful completed runs only.</summary>
    public bool ShouldPersistSuccessfulHistory =>
        GetEffectiveResultKind() == UsbBenchmarkResultKind.Completed && Succeeded && WriteSpeedMBps > 0 && ReadSpeedMBps > 0;
}

public sealed class UsbBenchmarkService : IUsbBenchmarkService
{
    private readonly IPowerShellRunnerService _powerShellRunnerService;

    public UsbBenchmarkService(IPowerShellRunnerService powerShellRunnerService)
    {
        _powerShellRunnerService = powerShellRunnerService;
    }

    public async Task<UsbBenchmarkResult> RunSequentialBenchmarkAsync(
        UsbTargetInfo target,
        Action<LogLine>? onOutput = null,
        CancellationToken cancellationToken = default)
    {
        var runId = Guid.NewGuid();
        var identity = UsbTargetIdentitySnapshot.Capture(target);
        var startedAt = DateTimeOffset.UtcNow;
        if (!UsbTargetSafety.IsSafeForBenchmark(target, out var blockReason))
        {
            onOutput?.Invoke(new LogLine(DateTimeOffset.Now, $"[WARN] USB benchmark skipped: {blockReason}", LogSeverity.Warning));
            var now = DateTimeOffset.UtcNow;
            return new UsbBenchmarkResult
            {
                RunId = runId,
                Succeeded = false,
                Status = "Blocked",
                Summary = "Benchmark skipped",
                Details = blockReason,
                ReadSpeedDisplay = "Blocked",
                WriteSpeedDisplay = "Blocked",
                LastTestedAt = now,
                ResultKind = UsbBenchmarkResultKind.BlockedBySafety,
                CancellationSource = UsbBenchmarkCancellationSource.SafetyRevalidationBlocked,
                StartedAtUtc = startedAt,
                CompletedAtUtc = now,
                TargetTopologyFingerprint = identity.TopologyFingerprint,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.BlockedBySafety, 0, 0)
            };
        }

        var letter = string.IsNullOrWhiteSpace(target.DriveLetter) ? "?" : target.DriveLetter.TrimEnd('\\');
        var tokenAlreadyCancelled = cancellationToken.IsCancellationRequested;
        if (tokenAlreadyCancelled)
        {
            onOutput?.Invoke(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] USB benchmark received a pre-cancelled token before measurement start. runId={runId:N}; replacing with a clean benchmark token.",
                LogSeverity.Warning));
            cancellationToken = CancellationToken.None;
        }

        onOutput?.Invoke(new LogLine(
            DateTimeOffset.Now,
            $"[INFO] USB benchmark requested. runId={runId:N} drive={letter} label=\"{target.LabelDisplay}\" fs={target.FileSystem} capacity={target.DisplayTotalBytes} free={target.DisplayFreeBytes} safety={target.SafetyStatusText} tokenPreCancelled={(tokenAlreadyCancelled ? "replaced" : "no")}",
            LogSeverity.Info));
        onOutput?.Invoke(new LogLine(DateTimeOffset.Now, "[INFO] Running native USB file benchmark (measurement-based).", LogSeverity.Info));
        try
        {
            var native = await UsbFileBenchmarkEngine.RunAsync(target, null, cancellationToken).ConfigureAwait(false);
            if (native.Succeeded)
            {
                onOutput?.Invoke(new LogLine(
                    DateTimeOffset.Now,
                    $"[OK] USB benchmark completed. runId={runId:N} native: write {native.WriteSpeedMBps:0.0} MB/s, read {native.ReadSpeedMBps:0.0} MB/s ({native.Classification}; confidence={native.BenchmarkConfidence}).",
                    LogSeverity.Success));
                if (native.ReadLikelyCached || native.ReadIsEstimate)
                {
                    onOutput?.Invoke(new LogLine(
                        DateTimeOffset.Now,
                        $"[WARN] USB benchmark read speed may be cached. runId={runId:N} {native.AccuracyWarning}",
                        LogSeverity.Warning,
                        channel: LiveLogChannel.Diagnostics));
                }

                return MapNativeToLegacy(native, runId, startedAt, identity.TopologyFingerprint);
            }

            if ((native.EndKind == UsbNativeBenchmarkEndKind.OperationCanceled ||
                 native.EndKind == UsbNativeBenchmarkEndKind.None) &&
                cancellationToken.IsCancellationRequested)
            {
                onOutput?.Invoke(new LogLine(
                    DateTimeOffset.Now,
                    $"[INFO] USB benchmark cancelled by user or host. runId={runId:N} detail={native.SummaryLine}",
                    LogSeverity.Info));
                var nowC = DateTimeOffset.UtcNow;
                return new UsbBenchmarkResult
                {
                    RunId = runId,
                    Succeeded = false,
                    Status = "Cancelled",
                    Summary = "Benchmark cancelled",
                    Details = native.SummaryLine,
                    ReadSpeedDisplay = "Cancelled",
                    WriteSpeedDisplay = "Cancelled",
                    LastTestedAt = nowC,
                    ResultKind = UsbBenchmarkResultKind.CancelledByUser,
                    CancellationSource = UsbBenchmarkCancellationSource.OperationCanceledUnknown,
                    StartedAtUtc = startedAt,
                    CompletedAtUtc = nowC,
                    TargetTopologyFingerprint = identity.TopologyFingerprint,
                    UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.CancelledByUser, 0, 0)
                };
            }

            if (native.EndKind == UsbNativeBenchmarkEndKind.OperationCanceled)
            {
                onOutput?.Invoke(new LogLine(
                    DateTimeOffset.Now,
                    $"[WARN] Native benchmark reported cancellation without an operator cancel request; falling back to PowerShell. runId={runId:N} detail={native.SummaryLine}",
                    LogSeverity.Warning));
            }

            if (native.EndKind == UsbNativeBenchmarkEndKind.ValidationBlocked)
            {
                onOutput?.Invoke(new LogLine(
                    DateTimeOffset.Now,
                    $"[WARN] USB benchmark blocked. runId={runId:N} {native.SummaryLine}",
                    LogSeverity.Warning));
                var nowV = DateTimeOffset.UtcNow;
                return new UsbBenchmarkResult
                {
                    RunId = runId,
                    Succeeded = false,
                    Status = "Blocked",
                    Summary = "Benchmark blocked",
                    Details = native.SummaryLine,
                    ReadSpeedDisplay = "Blocked",
                    WriteSpeedDisplay = "Blocked",
                    LastTestedAt = nowV,
                    ResultKind = UsbBenchmarkResultKind.ValidationFailed,
                    StartedAtUtc = startedAt,
                    CompletedAtUtc = nowV,
                    TargetTopologyFingerprint = identity.TopologyFingerprint,
                    UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(
                        UsbBenchmarkResultKind.ValidationFailed,
                        0,
                        0,
                        native.SummaryLine)
                };
            }

            onOutput?.Invoke(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] Native benchmark failed ({native.SummaryLine}); falling back to PowerShell. runId={runId:N}",
                LogSeverity.Warning));
        }
        catch (OperationCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                onOutput?.Invoke(new LogLine(
                    DateTimeOffset.Now,
                    $"[WARN] Native benchmark raised cancellation without an operator cancel request; falling back to PowerShell. runId={runId:N}",
                    LogSeverity.Warning));
                goto PowerShellFallback;
            }

            onOutput?.Invoke(new LogLine(DateTimeOffset.Now, $"[INFO] USB benchmark cancelled. runId={runId:N}", LogSeverity.Info));
            var nowX = DateTimeOffset.UtcNow;
            return new UsbBenchmarkResult
            {
                RunId = runId,
                Succeeded = false,
                Status = "Cancelled",
                Summary = "Benchmark cancelled",
                Details = "The benchmark was cancelled.",
                ReadSpeedDisplay = "Cancelled",
                WriteSpeedDisplay = "Cancelled",
                LastTestedAt = nowX,
                ResultKind = UsbBenchmarkResultKind.CancelledByUser,
                CancellationSource = UsbBenchmarkCancellationSource.OperationCanceledUnknown,
                StartedAtUtc = startedAt,
                CompletedAtUtc = nowX,
                TargetTopologyFingerprint = identity.TopologyFingerprint,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.CancelledByUser, 0, 0)
            };
        }
        catch (Exception ex)
        {
            onOutput?.Invoke(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] Native benchmark error: {ex.Message}; falling back to PowerShell.",
                LogSeverity.Warning));
        }

PowerShellFallback:
        var testSizeMb = UsbBenchmarkAccuracy.SelectTestSizeMb(target.FreeBytes);
        if (target.FreeBytes < (testSizeMb + 128L) * 1024 * 1024)
        {
            var nowF = DateTimeOffset.UtcNow;
            return new UsbBenchmarkResult
            {
                RunId = runId,
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark failed",
                Details = $"The selected USB does not have enough free space for a {testSizeMb} MB sequential speed check plus safety margin.",
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed",
                TestSizeMb = testSizeMb,
                LastTestedAt = nowF,
                ResultKind = UsbBenchmarkResultKind.ValidationFailed,
                StartedAtUtc = startedAt,
                CompletedAtUtc = nowF,
                TargetTopologyFingerprint = identity.TopologyFingerprint,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(
                    UsbBenchmarkResultKind.ValidationFailed,
                    0,
                    0,
                    "Not enough free space.")
            };
        }

        var request = new PowerShellRunRequest
        {
            DisplayName = "USB benchmark",
            WorkingDirectory = AppContext.BaseDirectory,
            InlineCommand = BuildBenchmarkCommand(target.RootPath, testSizeMb)
        };

        var result = await _powerShellRunnerService.RunAsync(request, onOutput, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutputText))
        {
            var nowPs = DateTimeOffset.UtcNow;
            return new UsbBenchmarkResult
            {
                RunId = runId,
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark failed",
                Details = $"PowerShell exited with code {result.ExitCode}.",
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed",
                TestSizeMb = testSizeMb,
                LastTestedAt = nowPs,
                ResultKind = UsbBenchmarkResultKind.IoFailed,
                StartedAtUtc = startedAt,
                CompletedAtUtc = nowPs,
                TargetTopologyFingerprint = identity.TopologyFingerprint,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.IoFailed, 0, 0, "PowerShell benchmark did not complete.")
            };
        }

        var jsonLine = result.StandardOutputText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.TrimStart().StartsWith('{'));

        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            var nowJ = DateTimeOffset.UtcNow;
            return new UsbBenchmarkResult
            {
                RunId = runId,
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark failed",
                Details = "Benchmark completed without returning a parseable result payload.",
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed",
                TestSizeMb = testSizeMb,
                LastTestedAt = nowJ,
                ResultKind = UsbBenchmarkResultKind.IoFailed,
                StartedAtUtc = startedAt,
                CompletedAtUtc = nowJ,
                TargetTopologyFingerprint = identity.TopologyFingerprint,
                UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(UsbBenchmarkResultKind.IoFailed, 0, 0, "Result payload missing.")
            };
        }

        using var document = JsonDocument.Parse(jsonLine);
        var writeSpeed = document.RootElement.GetProperty("WriteMbps").GetDouble();
        var readSpeed = document.RootElement.GetProperty("ReadMbps").GetDouble();
        var legacyTag = document.RootElement.GetProperty("Classification").GetString() ?? "Unknown";
        var finishedAt = DateTimeOffset.Now;
        var (measClass, conf, _) = UsbMeasurementClassifier.Classify(writeSpeed, readSpeed, null);
        var accuracy = UsbBenchmarkAccuracy.Assess(writeSpeed, readSpeed, null, target);
        var adjustedConfidence = Math.Clamp(conf - accuracy.ConfidencePenalty, 20, 95);
        var readDisplay = $"{readSpeed.ToString("0.0", CultureInfo.InvariantCulture)} MB/s{accuracy.ReadDisplaySuffix}";
        var summarySuffix = accuracy.ReadLikelyCached || accuracy.ReadIsEstimate
            ? " Read may be cached; treat read speed as an estimate."
            : string.Empty;

        onOutput?.Invoke(new LogLine(
            DateTimeOffset.Now,
            $"[OK] USB benchmark completed (PowerShell path). runId={runId:N} write={writeSpeed:0.0} MB/s read={readSpeed:0.0} MB/s size={testSizeMb} MB confidence={accuracy.ConfidenceLabel}",
            LogSeverity.Success));
        if (accuracy.ReadLikelyCached || accuracy.ReadIsEstimate)
        {
            onOutput?.Invoke(new LogLine(
                DateTimeOffset.Now,
                $"[WARN] USB benchmark read speed may be cached. runId={runId:N} {accuracy.Reason}",
                LogSeverity.Warning,
                channel: LiveLogChannel.Diagnostics));
        }

        var byteCount = (long)testSizeMb * 1024L * 1024L;
        return new UsbBenchmarkResult
        {
            RunId = runId,
            Succeeded = true,
            Status = "Complete",
            Summary =
                $"USB benchmark complete: {measClass} (legacy tag {legacyTag})",
            Details = $"{testSizeMb} MB file speed check. Write {writeSpeed:0.0} MB/s, read {readSpeed:0.0} MB/s. Confidence: {accuracy.ConfidenceLabel}.{summarySuffix}",
            WriteSpeedDisplay = $"{writeSpeed.ToString("0.0", CultureInfo.InvariantCulture)} MB/s",
            ReadSpeedDisplay = readDisplay,
            TestSizeMb = testSizeMb,
            LastTestedAt = finishedAt,
            Classification = legacyTag,
            WriteSpeedMBps = writeSpeed,
            ReadSpeedMBps = readSpeed,
            BenchmarkDurationMs = 0,
            IntelligenceMeasurementClass = measClass.ToString(),
            IntelligenceConfidenceScore = adjustedConfidence,
            ResultKind = UsbBenchmarkResultKind.Completed,
            StartedAtUtc = startedAt,
            CompletedAtUtc = finishedAt,
            ActualBytesWritten = byteCount,
            ActualBytesRead = byteCount,
            TargetTopologyFingerprint = identity.TopologyFingerprint,
            ReadLikelyCached = accuracy.ReadLikelyCached,
            ReadIsEstimate = accuracy.ReadIsEstimate,
            BenchmarkConfidence = accuracy.ConfidenceLabel,
            AccuracyWarning = accuracy.Reason,
            UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(
                UsbBenchmarkResultKind.Completed,
                readSpeed,
                writeSpeed,
                accuracy.ConfidenceLabel,
                accuracy.ReadLikelyCached || accuracy.ReadIsEstimate)
        };
    }

    private static UsbBenchmarkResult MapNativeToLegacy(
        UsbIntelligenceBenchmarkResult native,
        Guid runId,
        DateTimeOffset startedAt,
        string topologyFingerprint) =>
        new()
        {
            RunId = runId,
            Succeeded = true,
            Status = "Complete",
            Summary = $"USB benchmark complete: {native.Classification}",
            Details = native.SummaryLine,
            WriteSpeedDisplay = $"{native.WriteSpeedMBps.ToString("0.0", CultureInfo.InvariantCulture)} MB/s",
            ReadSpeedDisplay = $"{native.ReadSpeedMBps.ToString("0.0", CultureInfo.InvariantCulture)} MB/s{(native.ReadLikelyCached || native.ReadIsEstimate ? " (cache suspected)" : string.Empty)}",
            TestSizeMb = native.TestSizeMb,
            LastTestedAt = native.Timestamp,
            Classification = native.Classification.ToString(),
            WriteSpeedMBps = native.WriteSpeedMBps,
            ReadSpeedMBps = native.ReadSpeedMBps,
            BenchmarkDurationMs = native.DurationMs,
            IntelligenceMeasurementClass = native.Classification.ToString(),
            IntelligenceConfidenceScore = native.ConfidenceScore,
            ResultKind = UsbBenchmarkResultKind.Completed,
            StartedAtUtc = startedAt,
            CompletedAtUtc = native.Timestamp,
            ActualBytesWritten = native.ActualBytesWritten,
            ActualBytesRead = native.ActualBytesRead,
            WriteElapsedMs = native.WriteElapsedMs,
            ReadElapsedMs = native.ReadElapsedMs,
            ReadLikelyCached = native.ReadLikelyCached,
            ReadIsEstimate = native.ReadIsEstimate,
            BenchmarkConfidence = native.BenchmarkConfidence,
            AccuracyWarning = native.AccuracyWarning,
            TargetTopologyFingerprint = topologyFingerprint,
            UiSummaryLine = UsbBenchmarkUiMessages.BuildUiSummary(
                UsbBenchmarkResultKind.Completed,
                native.ReadSpeedMBps,
                native.WriteSpeedMBps,
                native.BenchmarkConfidence,
                native.ReadLikelyCached || native.ReadIsEstimate)
        };

    private static string BuildBenchmarkCommand(string rootPath, int testSizeMb)
    {
        return $$"""
            $ErrorActionPreference = 'Stop'
            $root = {{ToSingleQuotedPowerShellLiteral(rootPath)}}
            $sizeMb = {{testSizeMb}}
            $path = Join-Path $root ('.forgerems-benchmark-' + [guid]::NewGuid().ToString('N') + '.tmp')
            Write-Host ('[INFO] USB benchmark queued for ' + $root + ' using ' + $sizeMb + ' MB test file.')
            $buffer = New-Object byte[] (4MB)
            $rng = [System.Random]::new(9173)
            $rng.NextBytes($buffer)
            $targetBytes = [int64]$sizeMb * 1MB
            $written = [int64]0
            $writeOptions = [System.IO.FileOptions](([int][System.IO.FileOptions]::WriteThrough) -bor ([int][System.IO.FileOptions]::SequentialScan))
            try {
                Write-Host ('[INFO] USB benchmark writing temporary file: ' + $path)
                $writeWatch = [System.Diagnostics.Stopwatch]::StartNew()
                $stream = [System.IO.FileStream]::new(
                    $path,
                    [System.IO.FileMode]::CreateNew,
                    [System.IO.FileAccess]::Write,
                    [System.IO.FileShare]::None,
                    $buffer.Length,
                    $writeOptions)
                try {
                    while ($written -lt $targetBytes) {
                        $remaining = $targetBytes - $written
                        $count = [int][Math]::Min($buffer.Length, $remaining)
                        $stream.Write($buffer, 0, $count)
                        $written += $count
                    }
                    $stream.Flush($true)
                }
                finally {
                    $stream.Dispose()
                }
                $writeWatch.Stop()
                $writeMbps = [Math]::Round(($targetBytes / 1MB) / [Math]::Max($writeWatch.Elapsed.TotalSeconds, 0.001), 1)

                Write-Host ('[INFO] USB benchmark reading temporary file with randomized offsets.')
                $readBuffer = New-Object byte[] (4MB)
                $readBytes = [int64]0
                $blocks = [int][Math]::Max(1, [Math]::Floor($targetBytes / $readBuffer.Length))
                $offsets = New-Object int64[] $blocks
                for ($i = 0; $i -lt $blocks; $i++) { $offsets[$i] = [int64]$i * $readBuffer.Length }
                $shuffle = [System.Random]::new(31627)
                for ($i = $offsets.Length - 1; $i -gt 0; $i--) {
                    $j = $shuffle.Next($i + 1)
                    $tmp = $offsets[$i]
                    $offsets[$i] = $offsets[$j]
                    $offsets[$j] = $tmp
                }
                $readWatch = [System.Diagnostics.Stopwatch]::StartNew()
                $stream = [System.IO.FileStream]::new(
                    $path,
                    [System.IO.FileMode]::Open,
                    [System.IO.FileAccess]::Read,
                    [System.IO.FileShare]::Read,
                    $readBuffer.Length,
                    [System.IO.FileOptions]::RandomAccess)
                try {
                    foreach ($offset in $offsets) {
                        [void]$stream.Seek($offset, [System.IO.SeekOrigin]::Begin)
                        $remaining = [int64][Math]::Min($readBuffer.Length, $targetBytes - $offset)
                        while ($remaining -gt 0) {
                            $count = $stream.Read($readBuffer, 0, [int][Math]::Min($readBuffer.Length, $remaining))
                            if ($count -le 0) { break }
                            $readBytes += $count
                            $remaining -= $count
                        }
                    }
                }
                finally {
                    $stream.Dispose()
                }
                $readWatch.Stop()
                $readMbps = [Math]::Round(($readBytes / 1MB) / [Math]::Max($readWatch.Elapsed.TotalSeconds, 0.001), 1)
                $classification = if ($writeMbps -lt 20) { 'Slow' } elseif ($writeMbps -le 60) { 'Usable' } else { 'Fast' }
                Write-Host ('[OK] USB benchmark complete. Write ' + $writeMbps + ' MB/s, read ' + $readMbps + ' MB/s.')
                [pscustomobject]@{
                    WriteMbps = $writeMbps
                    ReadMbps = $readMbps
                    TestSizeMb = $sizeMb
                    Classification = $classification
                } | ConvertTo-Json -Compress
            }
            finally {
                Write-Host ('[INFO] Removing USB benchmark temporary file if present.')
                try {
                    if ([System.IO.File]::Exists($path)) {
                        [System.IO.File]::Delete($path)
                    }
                    Write-Host '[OK] USB benchmark temporary file removed.'
                }
                catch {
                    Write-Host ('[WARN] USB benchmark temporary file cleanup needs manual review: ' + $_.Exception.Message)
                }
            }
            exit 0
            """;
    }

    private static string ToSingleQuotedPowerShellLiteral(string value)
    {
        return "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
    }
}
