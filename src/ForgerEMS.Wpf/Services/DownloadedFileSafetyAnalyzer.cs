using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services;

public enum LocalFileSafetyOutcome
{
    InvalidInput,
    LocalFileNotFound,
    Directory,
    LocalFileReadBlocked,
    Analyzed
}

public sealed class DownloadedFileSafetyReport
{
    public LocalFileSafetyOutcome Outcome { get; init; } = LocalFileSafetyOutcome.Analyzed;

    public SafetyCheckSeverity Severity { get; init; } = SafetyCheckSeverity.UnknownManualReview;

    public IReadOnlyList<SafetyCheckSeverity> States { get; init; } = [];

    public required string Verdict { get; init; }

    public required string FileName { get; init; }

    public required string DisplayPath { get; init; }

    public required string FullPath { get; init; }

    public required string Extension { get; init; }

    public long SizeBytes { get; init; }

    public required string Sha256Hex { get; init; }

    public DateTime CreationTimeUtc { get; init; }

    public DateTime LastWriteTimeUtc { get; init; }

    public required string FileKind { get; init; }

    public required IReadOnlyList<string> RiskFlags { get; init; }

    public string? AuthenticodeSummary { get; init; }

    public string? MarkOfTheWebSummary { get; init; }

    public string? DownloadsFolderNote { get; init; }

    public KnownSecurityTestFixture Fixture { get; init; } = KnownSecurityTestFixture.None;

    public bool NoExecutionPerformed { get; init; } = true;

    public string? ErrorCategory { get; init; }

    public string? ErrorMessage { get; init; }
}

public static class DownloadedFileSafetyAnalyzer
{
    private static readonly JsonSerializerOptions MetadataJsonOptions = new() { WriteIndented = true };

    private static readonly string[] HighRiskExtensions =
    [
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr", ".dll", ".com", ".pif", ".reg", ".hta"
    ];

    public static string GetQuarantineRoot() => QuarantineDownloadService.GetDefaultQuarantineRoot();

    public static DownloadedFileSafetyReport? Analyze(
        string? filePath,
        out string? errorMessage,
        Func<string, Stream>? openReadStream = null)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return BuildNonReadableReport(
                string.Empty,
                LocalFileSafetyOutcome.InvalidInput,
                SafetyCheckSeverity.InvalidInput,
                "Invalid input: no file path was provided.",
                "InvalidInput",
                "No file path was provided.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath.Trim());
        }
        catch (Exception ex)
        {
            return BuildNonReadableReport(
                filePath,
                LocalFileSafetyOutcome.InvalidInput,
                SafetyCheckSeverity.InvalidInput,
                "Invalid input: path could not be resolved.",
                ex.GetType().Name,
                SafeExceptionSummary(ex));
        }

        if (Directory.Exists(fullPath))
        {
            return BuildNonReadableReport(
                fullPath,
                LocalFileSafetyOutcome.Directory,
                SafetyCheckSeverity.NotExecutable,
                "Directory selected. No file hash was computed.",
                null,
                null,
                "Directory");
        }

        if (!File.Exists(fullPath))
        {
            return BuildNonReadableReport(
                fullPath,
                LocalFileSafetyOutcome.LocalFileNotFound,
                SafetyCheckSeverity.LocalFileNotFound,
                "Local file not found. It may have been moved, deleted, or intercepted by external security software.",
                "LocalFileNotFound",
                "File was not found.");
        }

        try
        {
            var info = new FileInfo(fullPath);
            var ext = string.IsNullOrEmpty(info.Extension) ? "(none)" : info.Extension;
            var extLower = info.Extension.ToLowerInvariant();
            var fixture = KnownSecurityTestFixtureRecognizer.RecognizeLocalFile(fullPath);
            var states = new List<SafetyCheckSeverity>();
            foreach (var state in fixture.Classifications)
            {
                AddState(states, state);
            }

            var flags = new List<string>();
            if (HighRiskExtensions.Contains(extLower, StringComparer.OrdinalIgnoreCase))
            {
                flags.Add($"Extension {ext} is often executable or installer-related - treat as higher risk.");
            }

            var doubleExt = DetectDoubleExtension(info.Name);
            if (doubleExt is not null)
            {
                flags.Add(doubleExt);
            }

            var read = ComputeSha256AndHeader(fullPath, openReadStream);
            AddState(states, SafetyCheckSeverity.HashComputed);

            var kind = DetectFileKind(info.Name, extLower, read.Header);
            var isExecutable = kind.Contains("PE executable", StringComparison.OrdinalIgnoreCase) ||
                               kind.Contains("Executable", StringComparison.OrdinalIgnoreCase) ||
                               HighRiskExtensions.Contains(extLower, StringComparer.OrdinalIgnoreCase);
            if (isExecutable)
            {
                AddState(states, SafetyCheckSeverity.ExecutableMetadataOnly);
                flags.Add("Executable metadata only. ForgerEMS did not run, shell-open, or load this file.");
            }
            else
            {
                AddState(states, SafetyCheckSeverity.NotExecutable);
            }

            if (kind.Contains("Archive", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("Archive detected. ForgerEMS did not extract or preview archive contents.");
            }

            var downloadsNote = TryBuildDownloadsMotwNote(fullPath);
            var motw = TryReadMarkOfTheWeb(fullPath);
            if (motw is not null)
            {
                flags.Add("Mark-of-the-Web (Zone.Identifier) present - file likely came from the internet or email.");
            }

            var ageHours = (DateTime.UtcNow - info.LastWriteTimeUtc).TotalHours;
            if (ageHours < 6)
            {
                flags.Add("File is very new (modified within the last few hours) - extra caution.");
            }

            string? auth = null;
            if (isExecutable)
            {
                auth = TryGetAuthenticodeStatus(fullPath);
                AddState(states, SafetyCheckSeverity.SignatureChecked);
                if (auth is not null && auth.Contains("NotSigned", StringComparison.OrdinalIgnoreCase))
                {
                    flags.Add("Authenticode: file appears unsigned (or signature could not be read).");
                }
                else if (auth is not null && (auth.Contains("HashMismatch", StringComparison.OrdinalIgnoreCase) ||
                                              auth.Contains("UnknownError", StringComparison.OrdinalIgnoreCase)))
                {
                    flags.Add("Authenticode: signature state is ambiguous or invalid - verify manually if needed.");
                }
            }

            if (fixture.IsKnown)
            {
                flags.Add("Known safe security test fixture: " + fixture.Description);
            }

            if (states.Count == 0)
            {
                AddState(states, SafetyCheckSeverity.CleanOrLowConcern);
            }

            return new DownloadedFileSafetyReport
            {
                Outcome = LocalFileSafetyOutcome.Analyzed,
                Severity = ResolveSeverity(states, fixture, isExecutable),
                States = states,
                Verdict = BuildVerdict(fixture, isExecutable),
                FileName = info.Name,
                DisplayPath = MinimizePathForDisplay(fullPath),
                FullPath = fullPath,
                Extension = ext,
                SizeBytes = info.Length,
                Sha256Hex = read.Sha256Hex,
                CreationTimeUtc = info.CreationTimeUtc,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                FileKind = kind,
                RiskFlags = flags,
                AuthenticodeSummary = auth,
                MarkOfTheWebSummary = motw,
                DownloadsFolderNote = downloadsNote,
                Fixture = fixture
            };
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return BuildNonReadableReport(
                fullPath,
                LocalFileSafetyOutcome.LocalFileReadBlocked,
                SafetyCheckSeverity.LocalFileReadBlocked,
                "Local file read blocked. External AV/security may have intercepted or locked the file.",
                ex.GetType().Name,
                SafeExceptionSummary(ex));
        }
        catch (Exception ex)
        {
            return BuildNonReadableReport(
                fullPath,
                LocalFileSafetyOutcome.InvalidInput,
                SafetyCheckSeverity.InvalidInput,
                "Analysis failed before file metadata could be read.",
                ex.GetType().Name,
                SafeExceptionSummary(ex));
        }
    }

    public static string FormatReport(DownloadedFileSafetyReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Verdict: " + report.Verdict);
        builder.AppendLine("Classification: " + string.Join(", ", report.States.Select(static s => s.ToString())));
        if (report.Fixture.IsKnown)
        {
            builder.AppendLine("Known safe security test fixture: " + report.Fixture.Description);
        }

        builder.AppendLine("No execution performed.");
        builder.AppendLine("ForgerEMS only read file metadata/header bytes and a SHA256 stream when allowed.");
        builder.AppendLine();
        builder.AppendLine("File path: " + report.FullPath);
        builder.AppendLine("File name: " + report.FileName);
        builder.AppendLine("Path (minimized): " + report.DisplayPath);
        builder.AppendLine("Extension: " + report.Extension);
        builder.AppendLine("Size: " + report.SizeBytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes");
        builder.AppendLine("SHA256: " + report.Sha256Hex);
        builder.AppendLine("Created (UTC): " + FormatOptionalUtc(report.CreationTimeUtc));
        builder.AppendLine("Modified (UTC): " + FormatOptionalUtc(report.LastWriteTimeUtc));
        builder.AppendLine("Detected type: " + report.FileKind);
        if (!string.IsNullOrWhiteSpace(report.AuthenticodeSummary))
        {
            builder.AppendLine("Signature status: " + report.AuthenticodeSummary);
        }
        else
        {
            builder.AppendLine("Signature status: not checked or unavailable.");
        }

        if (!string.IsNullOrWhiteSpace(report.MarkOfTheWebSummary))
        {
            builder.AppendLine("Mark-of-the-Web: " + report.MarkOfTheWebSummary);
        }

        if (!string.IsNullOrWhiteSpace(report.DownloadsFolderNote))
        {
            builder.AppendLine(report.DownloadsFolderNote);
        }

        if (!string.IsNullOrWhiteSpace(report.ErrorCategory) || !string.IsNullOrWhiteSpace(report.ErrorMessage))
        {
            builder.AppendLine("Error: " + (report.ErrorCategory ?? "Error") + " - " + (report.ErrorMessage ?? "(no detail)"));
        }

        builder.AppendLine();
        if (report.RiskFlags.Count == 0)
        {
            builder.AppendLine("Evidence flags: none beyond standard caution for unknown downloads.");
        }
        else
        {
            builder.AppendLine("Evidence flags:");
            foreach (var flag in report.RiskFlags)
            {
                builder.AppendLine("- " + flag);
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static void CopyToQuarantine(string sourcePath, string quarantineRoot, out string destinationPath, out string? errorMessage, out string? metadataPath)
    {
        destinationPath = string.Empty;
        metadataPath = null;
        errorMessage = null;

        try
        {
            sourcePath = Path.GetFullPath(sourcePath);
            quarantineRoot = Path.GetFullPath(quarantineRoot);
            if (!File.Exists(sourcePath))
            {
                errorMessage = "Source file was not found.";
                return;
            }

            var timestamp = DateTimeOffset.UtcNow;
            var folder = Path.Combine(quarantineRoot, timestamp.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            destinationPath = Path.Combine(folder, "payload.forgerq");
            metadataPath = Path.Combine(folder, "quarantine.json");
            EnsureInsideRoot(quarantineRoot, destinationPath);
            EnsureInsideRoot(quarantineRoot, metadataPath);

            var bytesWritten = 0L;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, buffer.Length, FileOptions.SequentialScan))
            using (var dest = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, FileOptions.SequentialScan))
            {
                while (true)
                {
                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    hash.AppendData(buffer.AsSpan(0, read));
                    dest.Write(buffer, 0, read);
                    bytesWritten += read;
                }
            }

            var sha = Convert.ToHexString(hash.GetHashAndReset());
            TryMarkReadOnly(destinationPath);
            if (!File.Exists(destinationPath))
            {
                errorMessage = "External AV/security intercepted the file before ForgerEMS could retain it.";
                return;
            }

            var metadata = new
            {
                originalPath = sourcePath,
                finalUrl = (string?)null,
                utcTimestamp = timestamp,
                bytesAttempted = bytesWritten,
                bytesWritten,
                sha256 = sha,
                outcome = QuarantineOutcome.Quarantined.ToString(),
                finalFileExists = true,
                avExternalSecurityLikelyIntercepted = false,
                note = "Local read-only copy to ForgerEMS quarantine. Payload saved with neutral extension and not executed."
            };
            File.WriteAllText(metadataPath, JsonSerializer.Serialize(metadata, MetadataJsonOptions), Encoding.UTF8);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            errorMessage = "External AV/security intercepted or blocked the file before ForgerEMS could retain it: " + SafeExceptionSummary(ex);
        }
        catch (Exception ex)
        {
            errorMessage = SafeExceptionSummary(ex);
        }
    }

    private sealed class ShaHeaderRead
    {
        public required string Sha256Hex { get; init; }

        public required byte[] Header { get; init; }
    }

    private static ShaHeaderRead ComputeSha256AndHeader(string path, Func<string, Stream>? openReadStream)
    {
        using var stream = openReadStream?.Invoke(path) ?? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        var header = new byte[512];
        var headerLength = 0;

        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            if (headerLength < header.Length)
            {
                var copy = Math.Min(read, header.Length - headerLength);
                Array.Copy(buffer, 0, header, headerLength, copy);
                headerLength += copy;
            }

            hash.AppendData(buffer.AsSpan(0, read));
        }

        Array.Resize(ref header, headerLength);
        return new ShaHeaderRead
        {
            Sha256Hex = Convert.ToHexString(hash.GetHashAndReset()),
            Header = header
        };
    }

    private static DownloadedFileSafetyReport BuildNonReadableReport(
        string path,
        LocalFileSafetyOutcome outcome,
        SafetyCheckSeverity severity,
        string verdict,
        string? errorCategory,
        string? errorMessage,
        string fileKind = "Not available")
    {
        var fullPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path;
        return new DownloadedFileSafetyReport
        {
            Outcome = outcome,
            Severity = severity,
            States = [severity],
            Verdict = verdict,
            FileName = string.IsNullOrWhiteSpace(fullPath) ? "(none)" : Path.GetFileName(fullPath),
            DisplayPath = string.IsNullOrWhiteSpace(fullPath) ? "(none)" : MinimizePathForDisplay(fullPath),
            FullPath = fullPath,
            Extension = string.IsNullOrWhiteSpace(fullPath) ? "(none)" : (Path.GetExtension(fullPath) is { Length: > 0 } ext ? ext : "(none)"),
            SizeBytes = 0,
            Sha256Hex = "(not computed)",
            CreationTimeUtc = default,
            LastWriteTimeUtc = default,
            FileKind = fileKind,
            RiskFlags = [verdict, "No execution performed."],
            ErrorCategory = errorCategory,
            ErrorMessage = errorMessage,
            Fixture = KnownSecurityTestFixtureRecognizer.RecognizeLocalFile(fullPath)
        };
    }

    private static string DetectFileKind(string fileName, string extLower, byte[] header)
    {
        if (IsPeExecutable(header))
        {
            return "PE executable (MZ/PE header)";
        }

        if (HasMzHeader(header))
        {
            return "PE executable-like (MZ header; PE signature not present in preview)";
        }

        if (IsZip(header) || extLower is ".zip")
        {
            return "Archive (ZIP) - not extracted";
        }

        if (extLower is ".7z" or ".rar" or ".cab" or ".iso")
        {
            return "Archive / disk image - not extracted";
        }

        if (IsLikelyPlainText(header))
        {
            return "Plain text";
        }

        if (HighRiskExtensions.Contains(extLower, StringComparer.OrdinalIgnoreCase))
        {
            return "Executable/script extension; header not recognized";
        }

        if (Path.GetExtension(fileName).Equals(".bin", StringComparison.OrdinalIgnoreCase) || header.Any(static b => b == 0))
        {
            return "Unknown binary";
        }

        return "Other / data";
    }

    private static bool HasMzHeader(byte[] header) => header.Length >= 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';

    private static bool IsPeExecutable(byte[] header)
    {
        if (!HasMzHeader(header) || header.Length < 0x40)
        {
            return false;
        }

        var peOffset = BitConverter.ToInt32(header, 0x3C);
        return peOffset >= 0 &&
               peOffset + 4 <= header.Length &&
               header[peOffset] == (byte)'P' &&
               header[peOffset + 1] == (byte)'E' &&
               header[peOffset + 2] == 0 &&
               header[peOffset + 3] == 0;
    }

    private static bool IsZip(byte[] header)
    {
        return header.Length >= 4 &&
               header[0] == 0x50 &&
               header[1] == 0x4B &&
               (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07) &&
               (header[3] == 0x04 || header[3] == 0x06 || header[3] == 0x08);
    }

    private static bool IsLikelyPlainText(byte[] header)
    {
        if (header.Length == 0)
        {
            return true;
        }

        var printable = 0;
        foreach (var b in header)
        {
            if (b == 0)
            {
                return false;
            }

            if (b is 9 or 10 or 13 || b is >= 32 and <= 126)
            {
                printable++;
            }
        }

        return printable >= header.Length * 0.85;
    }

    private static SafetyCheckSeverity ResolveSeverity(
        IReadOnlyList<SafetyCheckSeverity> states,
        KnownSecurityTestFixture fixture,
        bool isExecutable)
    {
        if (fixture.IsKnown)
        {
            return fixture.PrimarySeverity;
        }

        if (states.Contains(SafetyCheckSeverity.LocalFileReadBlocked))
        {
            return SafetyCheckSeverity.LocalFileReadBlocked;
        }

        return isExecutable ? SafetyCheckSeverity.ExecutableMetadataOnly : SafetyCheckSeverity.CleanOrLowConcern;
    }

    private static string BuildVerdict(KnownSecurityTestFixture fixture, bool isExecutable)
    {
        if (fixture.IsKnown)
        {
            return fixture.PrimarySeverity switch
            {
                SafetyCheckSeverity.SimulatedMalwareTestFixture => "Simulated malware test fixture - known safe security test. No execution performed.",
                SafetyCheckSeverity.SimulatedPhishingTestFixture => "Simulated phishing test fixture - known safe security test. No execution performed.",
                _ => "Known safe security test fixture. No execution performed."
            };
        }

        return isExecutable
            ? "Executable metadata only. Manual review still required. No execution performed."
            : "No obvious executable header detected. Manual review still required. No execution performed.";
    }

    private static string FormatOptionalUtc(DateTime value)
    {
        return value == default ? "(not available)" : value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static string MinimizePathForDisplay(string fullPath)
    {
        try
        {
            var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(user) &&
                fullPath.StartsWith(user, StringComparison.OrdinalIgnoreCase))
            {
                return "~" + fullPath[user.Length..].Replace(Path.DirectorySeparatorChar, '/');
            }
        }
        catch
        {
        }

        return Path.GetFileName(fullPath);
    }

    private static string? DetectDoubleExtension(string fileName)
    {
        var nameWithoutLast = Path.GetFileNameWithoutExtension(fileName);
        if (string.IsNullOrEmpty(nameWithoutLast))
        {
            return null;
        }

        return nameWithoutLast.Contains('.', StringComparison.Ordinal)
            ? "Possible double extension (example pattern: document.pdf.exe) - verify the true file type."
            : null;
    }

    private static string? TryReadMarkOfTheWeb(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var ads = filePath + ":Zone.Identifier";
            if (!File.Exists(ads))
            {
                return null;
            }

            var text = File.ReadAllText(ads);
            return string.IsNullOrWhiteSpace(text) ? "present (empty)" : text.Trim().Replace("\r", string.Empty, StringComparison.Ordinal);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryBuildDownloadsMotwNote(string filePath)
    {
        try
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
            if (!filePath.StartsWith(downloads, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return "Path is under the user Downloads folder - treat as untrusted until you verify the source.";
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetAuthenticodeStatus(string filePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return "Authenticode check unavailable on this platform.";
        }

        try
        {
            var escaped = filePath.Replace("'", "''", StringComparison.Ordinal);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -NonInteractive -Command \"(Get-AuthenticodeSignature -LiteralPath '" +
                    escaped +
                    "').Status.ToString()\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                return "Authenticode check skipped (could not start PowerShell).";
            }

            if (!process.WaitForExit(8000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }

                return "Authenticode check timed out.";
            }

            var stdout = process.StandardOutput.ReadToEnd().Trim();
            return string.IsNullOrWhiteSpace(stdout) ? "Unknown" : stdout;
        }
        catch
        {
            return "Authenticode check unavailable.";
        }
    }

    private static void EnsureInsideRoot(string root, string path)
    {
        var fullRoot = EnsureTrailingSeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resolved path escaped the ForgerEMS quarantine root.");
        }
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static void TryMarkReadOnly(string path)
    {
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        }
        catch
        {
        }
    }

    private static void AddState(List<SafetyCheckSeverity> states, SafetyCheckSeverity state)
    {
        if (!states.Contains(state))
        {
            states.Add(state);
        }
    }

    private static string SafeExceptionSummary(Exception ex)
    {
        return ex.GetType().Name + ": " + ex.Message;
    }
}
