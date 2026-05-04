using System;
using System.IO;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>USB-specific Kyra answers using only hashed/safe fields from local reports.</summary>
public static class KyraUsbAnswerBuilder
{
    public static string? TryBuildAnswer(string userQuestion)
    {
        var lowerEarly = userQuestion.Trim().ToLowerInvariant();
        if (ContainsAny(
                lowerEarly,
                "can't pick c",
                "cannot pick c",
                "why can't i pick c",
                "why cant i pick c",
                "c: as the target",
                "c: as target",
                "target c:",
                "pick c:",
                "why is c: hidden",
                "why is c hidden"))
        {
            return UsbTargetSafety.WindowsOsDriveBlockedExplanation + Environment.NewLine + Environment.NewLine +
                   "Kyra only lists removable USB volumes and the large Ventoy data partition for destructive actions — never the internal Windows volume.";
        }

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ForgerEMS",
            "Runtime",
            "reports",
            "usb-intelligence-latest.json");

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return TryBuildAnswerFromJson(userQuestion, File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Test hook: build from in-memory JSON (same shape as usb-intelligence-latest.json).</summary>
    internal static string? TryBuildAnswerFromJson(string userQuestion, string usbIntelligenceJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(usbIntelligenceJson);
            var root = doc.RootElement;
            var lower = userQuestion.Trim().ToLowerInvariant();
            var portLabel = string.Empty;
            if (root.TryGetProperty("selectedTargetPortUserLabel", out var pl) && pl.ValueKind == JsonValueKind.String)
            {
                portLabel = pl.GetString()?.Trim() ?? string.Empty;
            }

            string narrativeBlock;
            if (root.TryGetProperty("kyraUsbNarrative", out var kn) && kn.ValueKind == JsonValueKind.Object)
            {
                var sa = kn.TryGetProperty("shortAnswer", out var a) ? a.GetString() ?? "" : "";
                var lc = kn.TryGetProperty("likelyCause", out var b) ? b.GetString() ?? "" : "";
                var ns = kn.TryGetProperty("nextStep", out var c) ? c.GetString() ?? "" : "";
                narrativeBlock =
                    $"{sa}{Environment.NewLine}{Environment.NewLine}Likely cause:{Environment.NewLine}{lc}{Environment.NewLine}{Environment.NewLine}Recommended next step:{Environment.NewLine}{ns}";
            }
            else
            {
                narrativeBlock =
                    "Short answer: USB Intelligence has not written a narrative for this stick yet." +
                    Environment.NewLine + Environment.NewLine +
                    "Likely cause: no recent topology merge or benchmark on this target." +
                    Environment.NewLine + Environment.NewLine +
                    "Recommended next step: Run USB Benchmark, then refresh USB Intelligence.";
            }

            var extra = string.Empty;
            if (ContainsAny(lower, "ventoy", "boot", "iso image", "good for ventoy"))
            {
                extra +=
                    $"{Environment.NewLine}{Environment.NewLine}Ventoy note: target the large data partition; the small EFI/VTOYEFI partition is boot metadata only. USB Intelligence + benchmark help confirm the port is fast enough.";
            }

            if (ContainsAny(lower, "map usb", "mapping", "map port", "label port", "how do i map", "usb mapping"))
            {
                extra +=
                    $"{Environment.NewLine}{Environment.NewLine}USB port mapping: open the USB Port Mapping Wizard (USB Intelligence panel). Select your toolkit USB, capture the current port, move the stick, detect the change, then save a label like “Rear Blue USB 3.” Optional: run USB Benchmark first for speed context.";
            }

            if (ContainsAny(lower, "which port", "what port", "best port", "where to plug"))
            {
                if (!string.IsNullOrWhiteSpace(portLabel))
                {
                    extra +=
                        $"{Environment.NewLine}{Environment.NewLine}Port pick: you mapped this drive to “{portLabel}”—use that physical port when you need the fastest path you verified.";
                }
                else
                {
                    extra +=
                        $"{Environment.NewLine}{Environment.NewLine}Port pick: Run USB Benchmark first, then I can compare ports. Meanwhile prefer USB 3.x (often blue) or USB-C on the motherboard, not through an unpowered hub.";
                }
            }

            if (ContainsAny(lower, "wrong port", "plugged wrong"))
            {
                extra +=
                    $"{Environment.NewLine}{Environment.NewLine}If you used the wrong port, move the stick, wait a few seconds, and watch the topology summary update on the next scan.";
            }

            if (ContainsAny(lower, "why did speed", "speed change", "slower than before", "why is my usb slow", "usb slow"))
            {
                extra +=
                    $"{Environment.NewLine}{Environment.NewLine}Slow USB usually means a different port (USB 2 path vs USB 3), a weak hub/cable, background I/O, or thermal limits. Run USB Benchmark on the same stick in each port you care about, then compare read/write in USB Intelligence.";
            }

            if (root.TryGetProperty("selectedTargetBenchmark", out var sb) && sb.ValueKind == JsonValueKind.Object &&
                sb.TryGetProperty("succeeded", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var w = sb.TryGetProperty("writeSpeedMBps", out var ww) ? ww.GetDouble() : 0;
                var r = sb.TryGetProperty("readSpeedMBps", out var rr) ? rr.GetDouble() : 0;
                var cls = sb.TryGetProperty("classification", out var cc) && cc.ValueKind == JsonValueKind.String
                    ? cc.GetString() ?? ""
                    : "";
                if (!string.IsNullOrWhiteSpace(portLabel))
                {
                    extra +=
                        $"{Environment.NewLine}{Environment.NewLine}Mapped port: {portLabel}. Latest benchmark: ~{w:0.0} MB/s write, ~{r:0.0} MB/s read ({cls}).";
                }
                else
                {
                    extra +=
                        $"{Environment.NewLine}{Environment.NewLine}Latest benchmark on this target: ~{w:0.0} MB/s write, ~{r:0.0} MB/s read ({cls}).";
                }
            }
            else if (!string.IsNullOrWhiteSpace(portLabel))
            {
                extra +=
                    $"{Environment.NewLine}{Environment.NewLine}Mapped port label for this stick: {portLabel}. Run a benchmark from USB Builder for read/write numbers.";
            }

            if (root.TryGetProperty("combinedConfidenceReason", out var ccr) && ccr.ValueKind == JsonValueKind.String)
            {
                var t = ccr.GetString();
                if (!string.IsNullOrWhiteSpace(t))
                {
                    extra += $"{Environment.NewLine}{Environment.NewLine}Confidence mix: {t}";
                }
            }

            return narrativeBlock.Trim() + extra;
        }
        catch
        {
            return null;
        }
    }

    private static bool ContainsAny(string lower, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (lower.Contains(n, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
