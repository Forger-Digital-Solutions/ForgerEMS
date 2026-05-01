using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;

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
    public bool Succeeded { get; init; }

    public string Status { get; init; } = "Not tested";

    public string Summary { get; init; } = string.Empty;

    public string Details { get; init; } = string.Empty;

    public string WriteSpeedDisplay { get; init; } = "Not tested";

    public string ReadSpeedDisplay { get; init; } = "Not tested";

    public int TestSizeMb { get; init; }

    public DateTimeOffset? LastTestedAt { get; init; }

    public string Classification { get; init; } = string.Empty;
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
        if (!UsbTargetSafety.IsSafeForBenchmark(target, out var blockReason))
        {
            onOutput?.Invoke(new LogLine(DateTimeOffset.Now, $"[WARN] USB benchmark skipped: {blockReason}", LogSeverity.Warning));
            return new UsbBenchmarkResult
            {
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark skipped",
                Details = blockReason,
                ReadSpeedDisplay = "Skipped (unsafe)",
                WriteSpeedDisplay = "Skipped (unsafe)",
                LastTestedAt = DateTimeOffset.Now
            };
        }

        var testSizeMb = target.FreeBytes >= 512L * 1024 * 1024 ? 128 : 64;
        if (target.FreeBytes < (testSizeMb + 128L) * 1024 * 1024)
        {
            return new UsbBenchmarkResult
            {
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark failed",
                Details = $"The selected USB does not have enough free space for a {testSizeMb} MB sequential speed check plus safety margin.",
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed",
                TestSizeMb = testSizeMb,
                LastTestedAt = DateTimeOffset.Now
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
            return new UsbBenchmarkResult
            {
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark failed",
                Details = $"PowerShell exited with code {result.ExitCode}.",
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed",
                TestSizeMb = testSizeMb,
                LastTestedAt = DateTimeOffset.Now
            };
        }

        var jsonLine = result.StandardOutputText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.TrimStart().StartsWith('{'));

        if (string.IsNullOrWhiteSpace(jsonLine))
        {
            return new UsbBenchmarkResult
            {
                Succeeded = false,
                Status = "Failed",
                Summary = "Benchmark failed",
                Details = "Benchmark completed without returning a parseable result payload.",
                ReadSpeedDisplay = "Failed",
                WriteSpeedDisplay = "Failed",
                TestSizeMb = testSizeMb,
                LastTestedAt = DateTimeOffset.Now
            };
        }

        using var document = JsonDocument.Parse(jsonLine);
        var writeSpeed = document.RootElement.GetProperty("WriteMbps").GetDouble();
        var readSpeed = document.RootElement.GetProperty("ReadMbps").GetDouble();
        var classification = document.RootElement.GetProperty("Classification").GetString() ?? "Unknown";
        var finishedAt = DateTimeOffset.Now;

        return new UsbBenchmarkResult
        {
            Succeeded = true,
            Status = "Complete",
            Summary = $"USB benchmark complete: {classification}",
            Details = $"{testSizeMb} MB sequential file speed check. Write {writeSpeed:0.0} MB/s, read {readSpeed:0.0} MB/s.",
            WriteSpeedDisplay = $"{writeSpeed.ToString("0.0", CultureInfo.InvariantCulture)} MB/s",
            ReadSpeedDisplay = $"{readSpeed.ToString("0.0", CultureInfo.InvariantCulture)} MB/s",
            TestSizeMb = testSizeMb,
            LastTestedAt = finishedAt,
            Classification = classification
        };
    }

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
            try {
                Write-Host ('[INFO] USB benchmark writing temporary file: ' + $path)
                $writeWatch = [System.Diagnostics.Stopwatch]::StartNew()
                $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
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

                Write-Host ('[INFO] USB benchmark reading temporary file.')
                $readBuffer = New-Object byte[] (4MB)
                $readBytes = [int64]0
                $readWatch = [System.Diagnostics.Stopwatch]::StartNew()
                $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
                try {
                    while (($count = $stream.Read($readBuffer, 0, $readBuffer.Length)) -gt 0) {
                        $readBytes += $count
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
