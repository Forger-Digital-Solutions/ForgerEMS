using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public interface IKyraMemoryStore
{
    KyraMachineMemoryProfile Load();

    void Save(KyraMachineMemoryProfile profile);

    bool TryAppend(KyraMemoryEntry entry, KyraMemorySettings settings);

    void Delete();

    string ExportSanitized();
}

public sealed class KyraMemorySettings
{
    public bool LocalRepairMemoryEnabled { get; set; } = true;

    public bool CommunitySharingEnabled { get; set; }

    public bool ShareResolvedIssueFixPatterns { get; set; }

    public bool ShareHardwareCompatibilityPerformancePatterns { get; set; }

    public bool ShareCrashErrorDiagnostics { get; set; }
}

public sealed class KyraMemoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);

    public DateTimeOffset ScanTimestamp { get; set; } = DateTimeOffset.UtcNow;

    public string MachineClass { get; set; } = "Unknown";

    public string HardwareCategorySummary { get; set; } = "Unknown";

    public string HealthScoreBand { get; set; } = "Unknown";

    public string IssueCategory { get; set; } = "General diagnostic";

    public string WarningCategory { get; set; } = "None";

    public string SuggestedFix { get; set; } = "Unknown";

    public string UserConfirmedFix { get; set; } = "unknown";

    public string UsbBenchmarkSummary { get; set; } = "Unknown";

    public string UsbTargetSafetyResult { get; set; } = "Unknown";

    public string BestUseRecommendationCategory { get; set; } = "Unknown";

    public string ResalePrepNoteCategory { get; set; } = "Unknown";

    public string ConfidenceLevel { get; set; } = "Medium";

    public string AnonymizedModelFamily { get; set; } = "Unknown";

    /// <summary>Learning-event metadata (sanitized; no secrets).</summary>
    public string AppVersion { get; set; } = "unknown";

    public string ReleaseChannel { get; set; } = "unknown";

    /// <summary>e.g. local-only, community-preview</summary>
    public string PrivacyMode { get; set; } = "local-only";

    public string ToolArea { get; set; } = "Kyra";

    public string UserIntentCategory { get; set; } = "General";

    public string KyraActionCategory { get; set; } = "assist";

    public string OutcomeCategory { get; set; } = "unknown";

    public string SanitizedNotes { get; set; } = "None";
}

public sealed class KyraMachineMemoryProfile
{
    public string LocalMachineProfileId { get; set; } = KyraMemorySanitizer.CreateLocalMachineProfileId();

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<KyraMemoryEntry> Entries { get; set; } = new();
}

public sealed class KyraMachineMemoryStore : IKyraMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly KyraMemoryRetentionPolicy _retentionPolicy;

    public KyraMachineMemoryStore(string path, KyraMemoryRetentionPolicy? retentionPolicy = null)
    {
        _path = path;
        _retentionPolicy = retentionPolicy ?? new KyraMemoryRetentionPolicy();
    }

    public KyraMachineMemoryProfile Load()
    {
        try
        {
            var profile = File.Exists(_path)
                ? JsonSerializer.Deserialize<KyraMachineMemoryProfile>(File.ReadAllText(_path)) ?? new KyraMachineMemoryProfile()
                : new KyraMachineMemoryProfile();
            KyraMemorySanitizer.SanitizeInPlace(profile);
            return _retentionPolicy.Apply(profile);
        }
        catch
        {
            return new KyraMachineMemoryProfile();
        }
    }

    public void Save(KyraMachineMemoryProfile profile)
    {
        KyraMemorySanitizer.SanitizeInPlace(profile);
        profile = _retentionPolicy.Apply(profile);
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(profile, JsonOptions));
    }

    public bool TryAppend(KyraMemoryEntry entry, KyraMemorySettings settings)
    {
        if (settings.LocalRepairMemoryEnabled is false)
        {
            return false;
        }

        KyraMemorySanitizer.SanitizeInPlace(entry);
        if (KyraMemorySanitizer.IsEmptyOrSensitive(entry))
        {
            return false;
        }

        var profile = Load();
        profile.Entries.Add(entry);
        Save(profile);
        return true;
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    public string ExportSanitized()
    {
        var profile = Load();
        KyraMemorySanitizer.SanitizeInPlace(profile);
        return JsonSerializer.Serialize(profile, JsonOptions);
    }
}

public sealed class KyraMemoryRetentionPolicy
{
    public int MaxEntries { get; init; } = 250;

    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(365);

    public KyraMachineMemoryProfile Apply(KyraMachineMemoryProfile profile)
    {
        var cutoff = DateTimeOffset.UtcNow.Subtract(MaxAge);
        profile.Entries = profile.Entries
            .Where(e => e.ScanTimestamp >= cutoff)
            .OrderByDescending(e => e.ScanTimestamp)
            .Take(Math.Max(1, MaxEntries))
            .OrderBy(e => e.ScanTimestamp)
            .ToList();
        return profile;
    }
}

public static partial class KyraMemorySanitizer
{
    private static readonly string[] SecretMarkers =
    {
        "api key", "apikey", "api_key", "token", "secret", "password", "passwd", "bearer ",
        "product key", "license key", "serial", "private file", "private document"
    };

    public static string CreateLocalMachineProfileId()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return "kyra-local-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static KyraMemorySettings FromCopilotSettings(CopilotSettings? settings) => new()
    {
        LocalRepairMemoryEnabled = settings?.KyraLocalRepairMemoryEnabled ?? true,
        CommunitySharingEnabled = settings?.KyraCommunitySharingEnabled ?? false,
        ShareResolvedIssueFixPatterns = settings?.KyraShareResolvedIssueFixPatterns ?? false,
        ShareHardwareCompatibilityPerformancePatterns = settings?.KyraShareHardwareCompatibilityPerformancePatterns ?? false,
        ShareCrashErrorDiagnostics = settings?.KyraShareCrashErrorDiagnostics ?? false
    };

    public static KyraMemoryEntry BuildEntryFromPrompt(
        string prompt,
        string response,
        SystemProfile? profile,
        SystemHealthEvaluation? health,
        KyraIntent intent = KyraIntent.Unknown,
        string appVersion = "unknown",
        string releaseChannel = "unknown",
        string privacyMode = "local-only",
        string? kyraActionCategory = null,
        string? outcomeCategory = null,
        string? sanitizedNotesOverride = null,
        string? userConfirmedFix = null)
    {
        var entry = new KyraMemoryEntry
        {
            MachineClass = ClassifyMachine(profile),
            HardwareCategorySummary = BuildHardwareCategorySummary(profile),
            HealthScoreBand = BuildHealthScoreBand(health?.HealthScore),
            IssueCategory = ClassifyIssue(prompt, response),
            WarningCategory = ClassifyWarning(prompt, response),
            SuggestedFix = SummarizeSuggestedFix(prompt, response),
            UsbBenchmarkSummary = ClassifyUsbBenchmark(prompt, response),
            UsbTargetSafetyResult = ClassifyUsbTargetSafety(prompt, response),
            BestUseRecommendationCategory = ClassifyBestUse(prompt, response),
            ResalePrepNoteCategory = ClassifyResalePrep(prompt, response),
            ConfidenceLevel = BuildConfidence(health?.ConfidenceScore),
            AnonymizedModelFamily = BuildSafeModelFamily(profile),
            AppVersion = appVersion,
            ReleaseChannel = releaseChannel,
            PrivacyMode = privacyMode,
            ToolArea = MapToolArea(intent),
            UserIntentCategory = MapUserIntentCategory(intent),
            KyraActionCategory = string.IsNullOrWhiteSpace(kyraActionCategory) ? "assist" : kyraActionCategory!,
            OutcomeCategory = string.IsNullOrWhiteSpace(outcomeCategory) ? "unknown" : outcomeCategory!,
            SanitizedNotes = string.IsNullOrWhiteSpace(sanitizedNotesOverride)
                ? "None"
                : sanitizedNotesOverride!
        };

        if (!string.IsNullOrWhiteSpace(userConfirmedFix))
        {
            entry.UserConfirmedFix = userConfirmedFix!;
        }

        SanitizeInPlace(entry);
        return entry;
    }

    public static string MapToolArea(KyraIntent intent) =>
        intent switch
        {
            KyraIntent.USBBuilderHelp => "USB Builder",
            KyraIntent.SystemHealthSummary => "System Intelligence",
            KyraIntent.ToolkitManagerHelp => "Toolkit Manager",
            KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot or KyraIntent.StorageIssue
                or KyraIntent.MemoryIssue or KyraIntent.DriverIssue or KyraIntent.GPUQuestion or KyraIntent.UpgradeAdvice
                => "Diagnostics",
            _ => "Kyra"
        };

    public static string MapUserIntentCategory(KyraIntent intent) =>
        intent switch
        {
            KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot => "Performance",
            KyraIntent.SystemHealthSummary => "System health",
            KyraIntent.USBBuilderHelp => "USB",
            KyraIntent.ToolkitManagerHelp => "Toolkit",
            KyraIntent.ResaleValue => "Resale",
            KyraIntent.ForgerEMSQuestion => "In-app help",
            _ => "General"
        };

    public static bool ShouldOfferFixFeedback(KyraIntent intent, string userPrompt, string responseText)
    {
        if (string.IsNullOrWhiteSpace(userPrompt) || KyraCodeSnippetDetector.LooksLikeCodeSnippet(userPrompt))
        {
            return false;
        }

        if (KyraSimpleMathEvaluator.LooksLikeSimpleArithmeticQuestion(userPrompt))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(responseText) || responseText.Trim().Length < 72)
        {
            return false;
        }

        if (responseText.Contains("Confirm before buying:", StringComparison.Ordinal) &&
            responseText.Contains("What I know (local scan):", StringComparison.Ordinal))
        {
            return false;
        }

        if (responseText.Contains("Tiny upgrade goblin", StringComparison.Ordinal) ||
            responseText.Contains("Repair gremlin note", StringComparison.Ordinal))
        {
            return false;
        }

        var p = userPrompt.ToLowerInvariant();
        if (ContainsAny(p, "price of", "btc", "bitcoin", "stock market", "weather today", "forecast") ||
            KyraPromptIsolation.LooksLikeKyraWindowsEnvConfigurationQuestion(userPrompt))
        {
            return false;
        }

        if (ContainsAny(
                p,
                "no likely usb",
                "explain this warning",
                "what changed since last scan",
                "gateway unauthorized",
                "provider missing",
                "provider unavailable",
                "live api disabled",
                "rate limit",
                "rate-limited",
                "local fallback active",
                "web research unavailable"))
        {
            return true;
        }

        return intent switch
        {
            KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot or KyraIntent.SystemHealthSummary
                or KyraIntent.StorageIssue or KyraIntent.MemoryIssue or KyraIntent.DriverIssue or KyraIntent.GPUQuestion
                or KyraIntent.UpgradeAdvice or KyraIntent.USBBuilderHelp or KyraIntent.ToolkitManagerHelp
                or KyraIntent.ResaleValue => true,
            KyraIntent.ForgerEMSQuestion when p.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                                              p.Contains("explain this", StringComparison.OrdinalIgnoreCase) => true,
            _ => false
        };
    }

    public static void SanitizeInPlace(KyraMachineMemoryProfile profile)
    {
        profile.LocalMachineProfileId = string.IsNullOrWhiteSpace(profile.LocalMachineProfileId) ||
                                        LooksSensitive(profile.LocalMachineProfileId) ||
                                        !profile.LocalMachineProfileId.StartsWith("kyra-local-", StringComparison.OrdinalIgnoreCase)
            ? CreateLocalMachineProfileId()
            : profile.LocalMachineProfileId;
        foreach (var entry in profile.Entries)
        {
            SanitizeInPlace(entry);
        }
    }

    public static void SanitizeInPlace(KyraMemoryEntry entry)
    {
        entry.Id = string.IsNullOrWhiteSpace(entry.Id) || LooksSensitive(entry.Id)
            ? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)
            : entry.Id.Trim();
        entry.MachineClass = SanitizeText(entry.MachineClass, 80);
        entry.HardwareCategorySummary = SanitizeText(entry.HardwareCategorySummary, 160);
        entry.HealthScoreBand = SanitizeText(entry.HealthScoreBand, 60);
        entry.IssueCategory = SanitizeText(entry.IssueCategory, 120);
        entry.WarningCategory = SanitizeText(entry.WarningCategory, 120);
        entry.SuggestedFix = SanitizeText(entry.SuggestedFix, 220);
        entry.UserConfirmedFix = NormalizeFixOutcome(entry.UserConfirmedFix);
        entry.UsbBenchmarkSummary = SanitizeText(entry.UsbBenchmarkSummary, 160);
        entry.UsbTargetSafetyResult = SanitizeText(entry.UsbTargetSafetyResult, 120);
        entry.BestUseRecommendationCategory = SanitizeText(entry.BestUseRecommendationCategory, 120);
        entry.ResalePrepNoteCategory = SanitizeText(entry.ResalePrepNoteCategory, 120);
        entry.ConfidenceLevel = SanitizeText(entry.ConfidenceLevel, 40);
        entry.AnonymizedModelFamily = SanitizeText(entry.AnonymizedModelFamily, 120);
        entry.AppVersion = SanitizeText(entry.AppVersion, 48);
        entry.ReleaseChannel = SanitizeText(entry.ReleaseChannel, 48);
        entry.PrivacyMode = SanitizeText(entry.PrivacyMode, 48);
        entry.ToolArea = SanitizeText(entry.ToolArea, 80);
        entry.UserIntentCategory = SanitizeText(entry.UserIntentCategory, 120);
        entry.KyraActionCategory = SanitizeText(entry.KyraActionCategory, 80);
        entry.OutcomeCategory = SanitizeText(entry.OutcomeCategory, 120);
        entry.SanitizedNotes = SanitizeText(entry.SanitizedNotes, 400);
    }

    public static bool IsEmptyOrSensitive(KyraMemoryEntry entry)
    {
        var combined = string.Join(" ", entry.MachineClass, entry.HardwareCategorySummary, entry.IssueCategory, entry.WarningCategory, entry.SuggestedFix, entry.SanitizedNotes);
        return string.IsNullOrWhiteSpace(combined) || LooksSensitive(combined);
    }

    public static bool LooksSensitive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var t = value.Trim();
        return SecretMarkers.Any(marker => t.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
               EmailRegex().IsMatch(t) ||
               IpAddressRegex().IsMatch(t) ||
               WindowsPathRegex().IsMatch(t) ||
               ProductKeyRegex().IsMatch(t) ||
               TokenLikeRegex().IsMatch(t);
    }

    public static string SanitizeText(string? value, int maxLength = 240)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Unknown";
        }

        if (ContainsAny(value, "private document", "private file", "private files", "private file contents", "raw log", "raw logs", "raw provider response"))
        {
            return "[private content redacted]";
        }

        var text = KyraSystemContextSanitizer.SanitizeForExternalProviders(value);
        text = EmailRegex().Replace(text, "[email redacted]");
        text = IpAddressRegex().Replace(text, "[ip redacted]");
        text = WindowsPathRegex().Replace(text, "[local path redacted]");
        text = ProductKeyRegex().Replace(text, "[product key redacted]");
        text = TokenLikeRegex().Replace(text, "[token redacted]");
        foreach (var marker in SecretMarkers)
        {
            text = Regex.Replace(text, Regex.Escape(marker), "[sensitive label redacted]", RegexOptions.IgnoreCase);
        }

        text = Regex.Replace(text, @"\s+", " ").Trim();
        if (text.Length > maxLength)
        {
            text = text[..maxLength].TrimEnd() + "...";
        }

        return string.IsNullOrWhiteSpace(text) ? "Unknown" : text;
    }

    public static bool IsMachineScopedPrompt(string prompt)
    {
        if (KyraCodeSnippetDetector.LooksLikeCodeSnippet(prompt))
        {
            return false;
        }

        var text = prompt.ToLowerInvariant();
        return ContainsAny(text, "this pc", "this laptop", "this machine", "this computer", "device are we working on", "best use case", "diagnose", "system scan", "battery", "storage health", "usb", "warning", "resale", "repair", "fix this machine", "hardware");
    }

    private static string ClassifyMachine(SystemProfile? profile)
    {
        if (profile is null)
        {
            return "Unknown";
        }

        var line = $"{profile.Manufacturer} {profile.Model}".ToLowerInvariant();
        var hasBattery = profile.Batteries.Count > 0;
        var gpuText = string.Join(" ", profile.Gpus.Select(g => $"{g.Name} {g.GpuKind}")).ToLowerInvariant();
        if (line.Contains("precision") || gpuText.Contains("quadro") || gpuText.Contains("rtx a"))
        {
            return hasBattery ? "Mobile Workstation" : "Workstation";
        }

        if (gpuText.Contains("rtx") || gpuText.Contains("gtx") || gpuText.Contains("radeon rx"))
        {
            return hasBattery ? "Gaming / Creator Laptop" : "Gaming / Creator Desktop";
        }

        if (hasBattery || ContainsAny(line, "laptop", "notebook", "latitude", "thinkpad", "elitebook", "surface", "xps"))
        {
            return "Laptop";
        }

        return "Desktop / Mini PC";
    }

    private static string BuildHardwareCategorySummary(SystemProfile? profile)
    {
        if (profile is null)
        {
            return "Unknown hardware category";
        }

        var ramBand = profile.RamTotalGb switch
        {
            >= 64 => "64GB+ RAM",
            >= 32 => "32GB RAM class",
            >= 16 => "16GB RAM class",
            > 0 => "Entry RAM class",
            _ => "RAM unknown"
        };
        var storage = profile.Disks.Any(d => d.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                                             d.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
            ? "SSD/NVMe storage"
            : profile.Disks.Count > 0 ? "storage detected" : "storage unknown";
        var gpu = profile.Gpus.Any(g => g.GpuKind.Contains("dedicated", StringComparison.OrdinalIgnoreCase) ||
                                        Regex.IsMatch(g.Name, @"\b(rtx|gtx|quadro|radeon)\b", RegexOptions.IgnoreCase))
            ? "dedicated GPU"
            : "integrated/unknown GPU";

        return $"{ramBand}, {storage}, {gpu}";
    }

    private static string BuildHealthScoreBand(int? score) => score switch
    {
        >= 85 => "Excellent",
        >= 70 => "Good",
        >= 55 => "Watch",
        >= 1 => "Needs attention",
        _ => "Unknown"
    };

    private static string ClassifyIssue(string prompt, string response)
    {
        var t = $"{prompt} {response}".ToLowerInvariant();
        if (ContainsAny(t, "no likely usb targets", "usb target", "flash drive", "thumb drive")) return "USB target not detected";
        if (ContainsAny(t, "battery", "wear", "cycle")) return "Battery health";
        if (ContainsAny(t, "storage", "ssd", "nvme", "smart", "disk")) return "Storage health";
        if (ContainsAny(t, "lag", "slow", "stutter", "freezing")) return "Performance lag";
        if (ContainsAny(t, "driver", "bios", "chipset")) return "Driver or firmware";
        if (ContainsAny(t, "resale", "sell", "listing", "value")) return "Resale prep";
        return "General diagnostic";
    }

    private static string ClassifyWarning(string prompt, string response)
    {
        var t = $"{prompt} {response}".ToLowerInvariant();
        if (t.Contains("no likely usb targets", StringComparison.OrdinalIgnoreCase)) return "No likely USB targets";
        if (ContainsAny(t, "secure boot", "tpm")) return "Windows 11 readiness";
        if (ContainsAny(t, "battery wear", "battery health")) return "Battery wear";
        if (ContainsAny(t, "disk warning", "smart warning", "bad sector")) return "Storage warning";
        return "None";
    }

    private static string SummarizeSuggestedFix(string prompt, string response)
    {
        var t = $"{prompt} {response}".ToLowerInvariant();
        if (t.Contains("no likely usb targets", StringComparison.OrdinalIgnoreCase))
        {
            return "Replug USB, wait for mount, select large removable data partition";
        }

        if (ContainsAny(t, "battery", "wear"))
        {
            return "Run battery report or vendor diagnostics; disclose wear for resale";
        }

        if (ContainsAny(t, "storage", "smart", "disk"))
        {
            return "Back up data and verify storage health before repair or resale";
        }

        if (ContainsAny(t, "driver", "bios", "chipset"))
        {
            return "Check official vendor driver or BIOS support page";
        }

        return "Review latest System Intelligence scan and follow safe repair steps";
    }

    private static string ClassifyUsbBenchmark(string prompt, string response)
    {
        var t = $"{prompt} {response}".ToLowerInvariant();
        return ContainsAny(t, "benchmark", "faster port", "usb-c faster", "transfer speed")
            ? "USB performance pattern mentioned"
            : "Unknown";
    }

    private static string ClassifyUsbTargetSafety(string prompt, string response)
    {
        var t = $"{prompt} {response}".ToLowerInvariant();
        if (t.Contains("no likely usb targets", StringComparison.OrdinalIgnoreCase)) return "No safe USB target detected";
        if (ContainsAny(t, "safe target", "removable data partition")) return "Likely removable data target";
        if (ContainsAny(t, "do not select", "system drive", "internal drive")) return "Unsafe target avoided";
        return "Unknown";
    }

    private static string ClassifyBestUse(string prompt, string response)
    {
        var t = $"{prompt} {response}".ToLowerInvariant();
        if (ContainsAny(t, "developer", "creator", "workstation")) return "Developer / Creator Workstation";
        if (ContainsAny(t, "gaming", "light gaming")) return "Light Gaming";
        if (ContainsAny(t, "office", "school", "student")) return "Office / Student";
        if (ContainsAny(t, "homelab", "server")) return "Homelab";
        return "Unknown";
    }

    private static string ClassifyResalePrep(string prompt, string response)
    {
        var t = $"{prompt} {response}".ToLowerInvariant();
        if (ContainsAny(t, "disclose battery", "battery wear")) return "Disclose battery wear";
        if (ContainsAny(t, "storage warning", "smart")) return "Verify/disclose storage health";
        if (ContainsAny(t, "clean install", "wipe", "reset")) return "Prepare clean OS install";
        return "Unknown";
    }

    private static string BuildConfidence(int? score) => score switch
    {
        >= 80 => "High",
        >= 50 => "Medium",
        >= 1 => "Low",
        _ => "Medium"
    };

    private static string BuildSafeModelFamily(SystemProfile? profile)
    {
        if (profile is null)
        {
            return "Unknown";
        }

        var text = $"{profile.Manufacturer} {profile.Model}".Trim();
        if (string.IsNullOrWhiteSpace(text) || text.Equals("Unknown Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "Unknown";
        }

        text = SerialLikeRegex().Replace(text, string.Empty);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 2)
            .Take(3);
        return string.Join(' ', words);
    }

    private static string NormalizeFixOutcome(string value)
    {
        var t = value.Trim().ToLowerInvariant();
        return t switch
        {
            "yes" or "confirmed" or "true" or "fixed" => "yes",
            "no" or "false" or "still_broken" or "broken" => "no",
            "not_sure" or "unsure" or "maybe" => "unknown",
            _ => "unknown"
        };
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4]\d|1?\d?\d)\.){3}(?:25[0-5]|2[0-4]\d|1?\d?\d)\b", RegexOptions.Compiled)]
    private static partial Regex IpAddressRegex();

    [GeneratedRegex(@"[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]*", RegexOptions.Compiled)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"\b[A-Z0-9]{5}(?:-[A-Z0-9]{5}){4}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ProductKeyRegex();

    [GeneratedRegex(@"\b(?:sk|pk|ghp|github_pat|xox[baprs]|AIza)[A-Za-z0-9_\-]{16,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex TokenLikeRegex();

    [GeneratedRegex(@"\b[A-Z0-9]{8,}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SerialLikeRegex();
}

public static class KyraMemorySummaryBuilder
{
    public static string? BuildForPrompt(KyraMachineMemoryProfile profile, string prompt)
    {
        if (!KyraMemorySanitizer.IsMachineScopedPrompt(prompt) || profile.Entries.Count == 0)
        {
            return null;
        }

        var related = profile.Entries
            .OrderByDescending(e => e.ScanTimestamp)
            .Take(8)
            .ToArray();
        if (related.Length == 0)
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Kyra local repair memory (sanitized, machine-scoped, local only):");
        sb.AppendLine($"Local machine profile ID: {profile.LocalMachineProfileId}");
        foreach (var entry in related)
        {
            sb.AppendLine(
                $"- {entry.ScanTimestamp:yyyy-MM-dd}: machine={entry.MachineClass}; health={entry.HealthScoreBand}; issue={entry.IssueCategory}; warning={entry.WarningCategory}; fix={entry.SuggestedFix}; outcome={entry.UserConfirmedFix}; confidence={entry.ConfidenceLevel}");
        }

        return sb.ToString().TrimEnd();
    }
}
