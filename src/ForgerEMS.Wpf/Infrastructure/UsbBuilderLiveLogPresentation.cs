using System;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>Maps verbose backend output to concise Live Logs lines while full logs stay detailed.</summary>
public static class UsbBuilderLiveLogPresentation
{
    public static bool TryGetConciseSidebarLine(LogLine line, bool verboseLiveLogs, out string sidebarDisplayText)
    {
        sidebarDisplayText = line.DisplayText;

        if (verboseLiveLogs)
        {
            return true;
        }

        if (line.Channel is LiveLogChannel.KyraDetail or LiveLogChannel.Diagnostics)
        {
            sidebarDisplayText = string.Empty;
            return false;
        }

        if (line.Severity is LogSeverity.Error or LogSeverity.Warning)
        {
            sidebarDisplayText = line.DisplayText;
            return true;
        }

        if (line.Severity == LogSeverity.Success)
        {
            var mapped = MapInfoOrSuccessToCompact(line.Text);
            if (mapped is null)
            {
                sidebarDisplayText = string.Empty;
                return false;
            }

            sidebarDisplayText = $"[{line.Timestamp:HH:mm:ss}] {mapped}";
            return true;
        }

        if (line.Severity == LogSeverity.Info)
        {
            var mapped = MapInfoOrSuccessToCompact(line.Text);
            if (mapped is not null)
            {
                sidebarDisplayText = $"[{line.Timestamp:HH:mm:ss}] {mapped}";
                return true;
            }

            sidebarDisplayText = string.Empty;
            return false;
        }

        sidebarDisplayText = string.Empty;
        return false;
    }

    /// <summary>For heartbeat / progress UI: infer managed-download phase from the latest script line.</summary>
    public static UsbManagedHeartbeatPhase InferHeartbeatPhase(string? logText)
    {
        if (string.IsNullOrWhiteSpace(logText))
        {
            return UsbManagedHeartbeatPhase.Unknown;
        }

        var t = logText.Trim();

        if (t.Contains("Final file written:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Final destination write result:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Item staging verdict: STAGED", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Updated:", StringComparison.OrdinalIgnoreCase))
        {
            return UsbManagedHeartbeatPhase.WritingFinal;
        }

        if (t.Contains("Download start:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Downloading ", StringComparison.OrdinalIgnoreCase) && ContainsDownloadProgress(t) ||
            t.Contains("Download attempt", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Download complete:", StringComparison.OrdinalIgnoreCase))
        {
            return UsbManagedHeartbeatPhase.Downloading;
        }

        if (t.Contains("Checksum expected vs actual", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Checksum verification passed", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Verified OK (sha256 match)", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Verify failed: sha256 mismatch", StringComparison.OrdinalIgnoreCase))
        {
            return UsbManagedHeartbeatPhase.VerifyingChecksum;
        }

        if (t.Contains("SHA256 hash provider:", StringComparison.OrdinalIgnoreCase))
        {
            return UsbManagedHeartbeatPhase.HashingLargeFile;
        }

        if (t.Contains("Up-to-date (sha256 match)", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("WhatIf: destination exists; would calculate SHA256", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Exists:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("destination missing", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Verify failed: destination missing", StringComparison.OrdinalIgnoreCase))
        {
            return UsbManagedHeartbeatPhase.CheckingExisting;
        }

        return UsbManagedHeartbeatPhase.Unknown;
    }

    private static bool ContainsDownloadProgress(string t) =>
        t.Contains('%') ||
        t.Contains(" MB / ", StringComparison.OrdinalIgnoreCase) ||
        t.Contains(" MB downloaded", StringComparison.OrdinalIgnoreCase);

    internal static string? MapInfoOrSuccessToCompact(string raw)
    {
        var t = StripLeadingLogPrefixes(raw).Trim();
        if (t.Length == 0)
        {
            return null;
        }

        if (t.StartsWith("[PASS]", StringComparison.OrdinalIgnoreCase) &&
            Regex.IsMatch(t, @"^\[PASS\]\s+\S", RegexOptions.IgnoreCase))
        {
            return null;
        }

        if (t.StartsWith("[RUN]", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (t.StartsWith("[INIT]", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Frontend version:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Backend version:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Backend compatibility:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Working directory:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Script:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Target drive:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (t.Contains("--- ACTION SUMMARY ---", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Items downloaded:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Items already up to date:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Shortcuts updated:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Failures:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Warnings:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("USB readiness:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Backend readiness:", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Checks passed:", StringComparison.OrdinalIgnoreCase))
        {
            return t;
        }

        if (t.Contains("Verification summary", StringComparison.OrdinalIgnoreCase))
        {
            return "Verification summary";
        }

        if (t.Contains("Ventoy core verification passed", StringComparison.OrdinalIgnoreCase))
        {
            return "Backend verification passed";
        }

        if (t.Contains("Ventoy core verification failed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (t.Contains("Managed download phase started", StringComparison.OrdinalIgnoreCase))
        {
            return "Managed downloads started";
        }

        if (t.Contains("USB readiness: READY", StringComparison.OrdinalIgnoreCase))
        {
            return "USB readiness: READY";
        }

        if (t.Contains("USB readiness:", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return "USB readiness: FAILED";
        }

        if (t.Contains("USB readiness:", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("PARTIALLY", StringComparison.OrdinalIgnoreCase))
        {
            return "USB readiness: PARTIALLY STAGED";
        }

        if (t.StartsWith("Backend readiness:", StringComparison.OrdinalIgnoreCase))
        {
            return t;
        }

        if (t.Contains("Download start:", StringComparison.OrdinalIgnoreCase) ||
            (t.Contains("Downloading ", StringComparison.OrdinalIgnoreCase) && ContainsDownloadProgress(t)))
        {
            return "Downloading…";
        }

        if (t.Contains("Checksum verification passed", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Checksum verified:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Verified OK (sha256 match)", StringComparison.OrdinalIgnoreCase))
        {
            return "Verifying checksum — OK";
        }

        if (t.Contains("Checksum expected vs actual", StringComparison.OrdinalIgnoreCase))
        {
            return "Verifying checksum…";
        }

        if (t.Contains("SHA256 hash provider:", StringComparison.OrdinalIgnoreCase))
        {
            return "Hashing / verifying large file…";
        }

        if (t.Contains("Up-to-date (sha256 match)", StringComparison.OrdinalIgnoreCase))
        {
            return "Already up to date";
        }

        if (t.StartsWith("Shortcut updated:", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("Shortcut updated:", StringComparison.OrdinalIgnoreCase))
        {
            return "Shortcut updated";
        }

        if (t.Contains("Item staging verdict: STAGED", StringComparison.OrdinalIgnoreCase) ||
            t.StartsWith("Updated:", StringComparison.OrdinalIgnoreCase))
        {
            return "Staged";
        }

        if (t.Contains("Final file written:", StringComparison.OrdinalIgnoreCase))
        {
            return "Writing final file — done";
        }

        if (t.Contains("[COMPLETE]", StringComparison.OrdinalIgnoreCase))
        {
            return t;
        }

        if (t.Contains("ForgerEMS USB Builder finished successfully", StringComparison.OrdinalIgnoreCase))
        {
            return "USB Builder finished successfully";
        }

        if (t.Contains("ForgerEMS USB Builder finished with warnings", StringComparison.OrdinalIgnoreCase))
        {
            return "USB Builder finished with warnings";
        }

        return null;
    }

    private static string StripLeadingLogPrefixes(string raw)
    {
        var t = raw.Trim();
        var bracketDepth = 0;
        var i = 0;
        while (i < t.Length)
        {
            if (t[i] == '[')
            {
                bracketDepth++;
                i++;
                while (i < t.Length && bracketDepth > 0)
                {
                    if (t[i] == '[')
                    {
                        bracketDepth++;
                    }
                    else if (t[i] == ']')
                    {
                        bracketDepth--;
                    }

                    i++;
                }

                while (i < t.Length && char.IsWhiteSpace(t[i]))
                {
                    i++;
                }

                continue;
            }

            break;
        }

        return i > 0 ? t[i..] : t;
    }
}

public enum UsbManagedHeartbeatPhase
{
    Unknown,
    CheckingExisting,
    Downloading,
    HashingLargeFile,
    VerifyingChecksum,
    WritingFinal
}
