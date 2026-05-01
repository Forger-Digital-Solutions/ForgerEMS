using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class DownloadedFileSafetyReport
{
    public required string FileName { get; init; }

    public required string DisplayPath { get; init; }

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
}

public static class DownloadedFileSafetyAnalyzer
{
    private static readonly string[] HighRiskExtensions =
    [
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".scr", ".dll", ".com", ".pif", ".reg", ".hta"
    ];

    public static string GetQuarantineRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ForgerEMS", "Quarantine");

    public static DownloadedFileSafetyReport? Analyze(string? filePath, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            errorMessage = "No file path was provided.";
            return null;
        }

        try
        {
            filePath = Path.GetFullPath(filePath);
        }
        catch (Exception ex)
        {
            errorMessage = "Invalid path: " + ex.Message;
            return null;
        }

        if (!File.Exists(filePath))
        {
            errorMessage = "File was not found.";
            return null;
        }

        try
        {
            var info = new FileInfo(filePath);
            var ext = info.Extension;
            if (string.IsNullOrEmpty(ext))
            {
                ext = "(none)";
            }

            var flags = new List<string>();
            var extLower = ext.ToLowerInvariant();
            if (HighRiskExtensions.Contains(extLower, StringComparer.OrdinalIgnoreCase))
            {
                flags.Add($"Extension {ext} is often executable or installer-related — treat as higher risk.");
            }

            var doubleExt = DetectDoubleExtension(info.Name);
            if (doubleExt is not null)
            {
                flags.Add(doubleExt);
            }

            var downloadsNote = TryBuildDownloadsMotwNote(filePath);

            var motw = TryReadMarkOfTheWeb(filePath);
            if (motw is not null)
            {
                flags.Add("Mark-of-the-Web (Zone.Identifier) present — file likely came from the internet or email.");
            }

            var ageHours = (DateTime.UtcNow - info.LastWriteTimeUtc).TotalHours;
            if (ageHours < 6)
            {
                flags.Add("File is very new (modified within the last few hours) — extra caution.");
            }

            var kind = ClassifyFileKind(extLower);
            if (kind.Contains("Executable", StringComparison.OrdinalIgnoreCase) ||
                kind.Contains("Script", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("Treat as executable/script surface — do not run on your main machine unless you trust the source.");
            }

            var sha = ComputeSha256Hex(filePath);
            var auth = TryGetAuthenticodeStatus(filePath);
            if (auth is not null && auth.Contains("NotSigned", StringComparison.OrdinalIgnoreCase))
            {
                flags.Add("Authenticode: file appears unsigned (or signature could not be read).");
            }
            else if (auth is not null && (auth.Contains("HashMismatch", StringComparison.OrdinalIgnoreCase) ||
                                          auth.Contains("UnknownError", StringComparison.OrdinalIgnoreCase)))
            {
                flags.Add("Authenticode: signature state is ambiguous or invalid — verify manually if needed.");
            }

            return new DownloadedFileSafetyReport
            {
                FileName = info.Name,
                DisplayPath = MinimizePathForDisplay(filePath),
                Extension = ext,
                SizeBytes = info.Length,
                Sha256Hex = sha,
                CreationTimeUtc = info.CreationTimeUtc,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                FileKind = kind,
                RiskFlags = flags,
                AuthenticodeSummary = auth,
                MarkOfTheWebSummary = motw,
                DownloadsFolderNote = downloadsNote
            };
        }
        catch (Exception ex)
        {
            errorMessage = "Analysis failed: " + ex.Message;
            return null;
        }
    }

    public static string FormatReport(DownloadedFileSafetyReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Downloaded file safety check (read-only — file was not executed)");
        builder.AppendLine("ForgerEMS can flag obvious risks and compute hashes, but it cannot guarantee a file is safe. Do not run unknown tools on your main machine.");
        builder.AppendLine();
        builder.AppendLine("File name: " + report.FileName);
        builder.AppendLine("Path (minimized): " + report.DisplayPath);
        builder.AppendLine("Extension: " + report.Extension);
        builder.AppendLine("Size: " + report.SizeBytes.ToString("N0", CultureInfo.CurrentCulture) + " bytes");
        builder.AppendLine("SHA256: " + report.Sha256Hex);
        builder.AppendLine("Created (UTC): " + report.CreationTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendLine("Modified (UTC): " + report.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendLine("Kind: " + report.FileKind);
        if (!string.IsNullOrWhiteSpace(report.AuthenticodeSummary))
        {
            builder.AppendLine("Authenticode: " + report.AuthenticodeSummary);
        }

        if (!string.IsNullOrWhiteSpace(report.MarkOfTheWebSummary))
        {
            builder.AppendLine("Mark-of-the-Web: " + report.MarkOfTheWebSummary);
        }

        if (!string.IsNullOrWhiteSpace(report.DownloadsFolderNote))
        {
            builder.AppendLine(report.DownloadsFolderNote);
        }

        builder.AppendLine();
        if (report.RiskFlags.Count == 0)
        {
            builder.AppendLine("Heuristic flags: none beyond standard caution for unknown binaries.");
        }
        else
        {
            builder.AppendLine("Heuristic flags:");
            foreach (var flag in report.RiskFlags)
            {
                builder.AppendLine("- " + flag);
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static void CopyToQuarantine(string sourcePath, string quarantineRoot, out string destinationPath, out string? errorMessage)
    {
        destinationPath = string.Empty;
        errorMessage = null;
        try
        {
            Directory.CreateDirectory(quarantineRoot);
            var name = Path.GetFileName(sourcePath);
            destinationPath = Path.Combine(quarantineRoot, $"{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{name}");
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
        }
    }

    private static string ComputeSha256Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash);
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

        if (nameWithoutLast.Contains('.', StringComparison.Ordinal))
        {
            return "Possible double extension (example pattern: document.pdf.exe) — verify the true file type.";
        }

        return null;
    }

    private static string ClassifyFileKind(string extLower)
    {
        if (extLower is ".zip" or ".7z" or ".rar" or ".cab" or ".iso")
        {
            return "Archive / disk image";
        }

        if (extLower is ".msi" or ".msix" or ".appx")
        {
            return "Installer package";
        }

        if (extLower is ".bat" or ".cmd" or ".ps1" or ".vbs" or ".js" or ".hta")
        {
            return "Script";
        }

        if (extLower is ".exe" or ".scr" or ".com" or ".dll" or ".pif")
        {
            return "Executable / loadable module";
        }

        return "Other / data";
    }

    private static string? TryReadMarkOfTheWeb(string filePath)
    {
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

            return "Path is under the user Downloads folder — treat as untrusted until you verify the source.";
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetAuthenticodeStatus(string filePath)
    {
        try
        {
            var escaped = filePath.Replace("'", "''", StringComparison.Ordinal);
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments =
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"(Get-AuthenticodeSignature -LiteralPath '" +
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
}
