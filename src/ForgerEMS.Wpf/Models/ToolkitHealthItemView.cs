using System.ComponentModel;
using System.Runtime.CompilerServices;
using VentoyToolkitSetup.Wpf.Services.Intelligence;

namespace VentoyToolkitSetup.Wpf.Models;

public sealed class ToolkitHealthItemView : INotifyPropertyChanged
{
    private bool _selectedForDownload;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Tool { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public string Type { get; init; } = string.Empty;

    public string ExpectedPath { get; init; } = string.Empty;

    public string ResolvedExpectedPath { get; init; } = string.Empty;

    public string ExpectedFoundPath { get; init; } = string.Empty;

    public string MatchedPath { get; init; } = string.Empty;

    public bool Exists { get; init; }

    public long SizeBytes { get; init; }

    public long? EstimatedSizeBytes { get; init; }

    public string Url { get; init; } = string.Empty;

    public string Purpose { get; init; } = string.Empty;

    public string OfficialUrl { get; init; } = string.Empty;

    public string LicenseRedistributionNote { get; init; } = string.Empty;

    public string DownloadStatus { get; init; } = string.Empty;

    public string ChecksumStatus { get; init; } = string.Empty;

    public string DistributionModel { get; init; } = string.Empty;

    public string BetaSafetyRating { get; init; } = string.Empty;

    public string ClassificationReason { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Verification { get; init; } = string.Empty;

    /// <summary>
    /// Backend-emitted hint about how the most recent checksum verdict was
    /// produced: "fresh" (hashed this run), "cached" (reused from a previous
    /// verified scan), or empty (no checksum verification took place — manual
    /// shortcuts, optional items, missing files, or legacy reports).
    /// </summary>
    public string VerificationMode { get; init; } = string.Empty;

    public string Recommendation { get; init; } = string.Empty;

    public string NormalizedCategoryLabel { get; init; } = string.Empty;

    /// <summary>Catalog kind from the manifest: "os", "tool", "driver-shortcut", "runtime", "browser". Empty when legacy.</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>OS / tool family from the manifest: "Windows", "Linux", "BSD", "Hobby", "DOS", etc.</summary>
    public string Family { get; init; } = string.Empty;

    /// <summary>OS-only sub-category from the manifest: "Desktop", "Server", "Recovery", "Security", "Legacy", "Hobby", "Hypervisor", "Network-Appliance".</summary>
    public string OsCategory { get; init; } = string.Empty;

    /// <summary>Architecture string, comma-separated when the manifest declares multiple.</summary>
    public string Architecture { get; init; } = string.Empty;

    /// <summary>Boot firmware support string, comma-separated when the manifest declares multiple.</summary>
    public string BootMode { get; init; } = string.Empty;

    /// <summary>Short technician-facing line describing when to reach for this entry.</summary>
    public string RecommendedUse { get; init; } = string.Empty;

    /// <summary>Longer technician note (gotchas, install caveats).</summary>
    public string TechnicianNotes { get; init; } = string.Empty;

    /// <summary>Plain-English license note from the manifest.</summary>
    public string LicenseNote { get; init; } = string.Empty;

    /// <summary>True when the manifest explicitly flagged the entry as manualOnly.</summary>
    public bool ManualOnly { get; init; }

    /// <summary>Plain-English warning shown for unsupported / EOL / hobby / lab-only entries.</summary>
    public string LegacyWarning { get; init; } = string.Empty;

    /// <summary>Ventoy-specific compatibility note.</summary>
    public string VentoyNotes { get; init; } = string.Empty;

    /// <summary>Secure Boot status note.</summary>
    public string SecureBootNote { get; init; } = string.Empty;

    /// <summary>Trust level of the upstream URL: "official", "community", or "manual".</summary>
    public string SourceTrust { get; init; } = string.Empty;

    /// <summary>First-class catalog action mode: ManagedDownload, OfficialDownloadPage, ManualMediaRequired, and related policy modes.</summary>
    public string DownloadMode { get; init; } = string.Empty;

    /// <summary>Optional manifest-supplied primary action label. When absent, it is derived from DownloadMode.</summary>
    public string ActionLabel { get; init; } = string.Empty;

    public string SecondaryActionLabel { get; init; } = string.Empty;

    public string ActionReason { get; init; } = string.Empty;

    public string PromotionStatus { get; init; } = string.Empty;

    public string PromotionEvidence { get; init; } = string.Empty;

    public string LegalRisk { get; init; } = string.Empty;

    public string ChecksumRequirement { get; init; } = string.Empty;

    public bool ManagedPromotionCandidate { get; init; }

    public string CurrentPinnedVersion { get; init; } = string.Empty;

    public string LatestKnownStableVersion { get; init; } = string.Empty;

    public string FreshnessStatus { get; init; } = string.Empty;

    public string LastFreshnessAuditUtc { get; init; } = string.Empty;

    public string ChecksumVerificationMode { get; init; } = string.Empty;

    public string UpdateRecommendation { get; init; } = string.Empty;

    public bool SelectedForDownload
    {
        get => _selectedForDownload;
        set
        {
            if (_selectedForDownload == value)
            {
                return;
            }

            _selectedForDownload = value;
            OnPropertyChanged();
        }
    }

    public string TypeDisplay => Type.Trim().ToUpperInvariant() switch
    {
        "MANUALDOWNLOAD" => "Manual",
        "MANUAL" => "Manual",
        _ => "Managed"
    };

    /// <summary>Grid/UI label — manual-required items are never shown as generic “missing”.</summary>
    public string StatusDisplayUi => Status.Trim().ToUpperInvariant() switch
    {
        "MISSING_REQUIRED" => "Missing required file",
        "MISSING" => "Missing required file",
        "INSTALLED" => "Installed",
        "UPDATE_AVAILABLE" => "Update available",
        "HASH_FAILED" => "Verification issue",
        "VERIFICATION_PENDING" => "Present / verification pending",
        "COVERED_BY_MANAGED" => "Covered by managed download",
        "MANUAL_REQUIRED" => string.IsNullOrWhiteSpace(MatchedPath)
            ? "Manual shortcut missing"
            : "Manual shortcut present",
        "PLACEHOLDER" => "Placeholder",
        "SKIPPED" => "Skipped",
        _ => Status
    };

    public string LocationDisplay
    {
        get
        {
            var compactExpected = CompactPath(ExpectedPath);
            if (Exists)
            {
                var size = SizeBytes > 0 ? $", {FormatSize(SizeBytes)}" : string.Empty;
                return $"Present{size} | {compactExpected}";
            }

            if (Status.Equals("VERIFICATION_PENDING", StringComparison.OrdinalIgnoreCase))
            {
                return $"Verification pending | {compactExpected}";
            }

            if (Status.Equals("HASH_FAILED", StringComparison.OrdinalIgnoreCase))
            {
                return $"Checksum mismatch | {compactExpected}";
            }

            if (Status.Equals("COVERED_BY_MANAGED", StringComparison.OrdinalIgnoreCase))
            {
                return $"Covered by managed download | {compactExpected}";
            }

            return $"Missing | {compactExpected}";
        }
    }

    public string VerificationDisplay => Status.Trim().ToUpperInvariant() switch
    {
        "INSTALLED" => InstalledVerificationLabel(),
        "VERIFICATION_PENDING" => "Pending",
        "COVERED_BY_MANAGED" => "Covered",
        "HASH_FAILED" => "Checksum mismatch",
        "MISSING_REQUIRED" => "Not present",
        "MANUAL_REQUIRED" => "Manual",
        _ => string.IsNullOrWhiteSpace(Verification) ? "Unknown" : Verification
    };

    /// <summary>
    /// Short, technician-facing tag that distinguishes fresh vs cached vs
    /// shortcut. Wording is intentionally honest — cached entries are NEVER
    /// labeled "fresh verified."
    /// </summary>
    public string VerificationModeBadge => Status.Trim().ToUpperInvariant() switch
    {
        "INSTALLED" => VerificationMode.Trim().ToLowerInvariant() switch
        {
            "cached" => "Cached match",
            "fresh" => "Fresh match",
            _ => "Verified"
        },
        "VERIFICATION_PENDING" => "Pending",
        "COVERED_BY_MANAGED" => "Covered shortcut",
        "HASH_FAILED" => "Checksum mismatch",
        "MISSING_REQUIRED" => "Not present",
        "MANUAL_REQUIRED" => "Manual shortcut",
        _ => string.Empty
    };

    /// <summary>
    /// Longer tooltip-style explanation that matches the backend honesty
    /// rules. Cached items advertise themselves as unchanged since the prior
    /// verified scan, not as a fresh re-hash this run.
    /// </summary>
    public string VerificationModeTooltip => Status.Trim().ToUpperInvariant() switch
    {
        "INSTALLED" => VerificationMode.Trim().ToLowerInvariant() switch
        {
            "cached" => "Cached match: unchanged since previous verified scan. Re-hash by clicking Full Verify.",
            "fresh" => "Fresh match: re-hashed this run.",
            _ => "Verified."
        },
        "MANUAL_REQUIRED" => "Manual shortcut — no checksum verification expected.",
        "COVERED_BY_MANAGED" => "Covered by a verified managed download; this shortcut is intentionally suppressed.",
        "VERIFICATION_PENDING" => "File present, checksum not yet verified.",
        "HASH_FAILED" => "File hash did not match the manifest's expected checksum.",
        "MISSING_REQUIRED" => "File is not present on the target.",
        _ => string.Empty
    };

    private static string InstalledVerificationLabel()
    {
        // Keep the existing one-word grid column behaviour ("Verified") so
        // existing UI layout/grid widths are unchanged. The cached vs fresh
        // distinction lives in VerificationModeBadge / VerificationModeTooltip
        // / DetailText.
        return "Verified";
    }

    public string EffectiveDownloadMode =>
        ManifestPromotionPolicy.InferDownloadMode(
            DownloadMode,
            Type,
            ManualOnly,
            Kind,
            SourceTrust,
            string.Join(" ", Recommendation, TechnicianNotes, ActionReason),
            LegacyWarning,
            LicenseNote,
            ExpectedPath,
            Family);

    public string DownloadActionLabel =>
        !string.IsNullOrWhiteSpace(ActionLabel)
            ? ActionLabel.Trim()
            : ManifestPromotionPolicy.GetPrimaryActionLabel(EffectiveDownloadMode);

    public string ActionHelperText =>
        !string.IsNullOrWhiteSpace(ActionReason)
            ? ActionReason.Trim()
            : ManifestPromotionPolicy.GetHelperText(EffectiveDownloadMode);

    private bool HasDownloadActionMetadata =>
        !string.IsNullOrWhiteSpace(DownloadMode) ||
        !string.IsNullOrWhiteSpace(ActionLabel) ||
        !string.IsNullOrWhiteSpace(ActionReason) ||
        ManualOnly ||
        string.Equals(Type, "manualDownload", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "managedAutoDownload", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase);

    public string ActionDisplay
    {
        get
        {
            var status = Status.Trim().ToUpperInvariant();
            return status switch
            {
                "INSTALLED" => "No action needed",
                "VERIFICATION_PENDING" => "Revalidate",
                "HASH_FAILED" => "Checksum issue",
                "UPDATE_AVAILABLE" => "Review update",
                "MISSING_REQUIRED" => HasDownloadActionMetadata ? DownloadActionLabel : "Run Setup USB Toolkit",
                "COVERED_BY_MANAGED" => "No action needed",
                "MANUAL_REQUIRED" => string.IsNullOrWhiteSpace(MatchedPath) ? DownloadActionLabel : "No action needed",
                _ => !string.IsNullOrWhiteSpace(DownloadMode) || ManualOnly || string.Equals(Type, "manualDownload", StringComparison.OrdinalIgnoreCase)
                    ? DownloadActionLabel
                    : string.IsNullOrWhiteSpace(Recommendation) ? "Review detail" : TruncateSingleLine(Recommendation, 44)
            };
        }
    }

    public string RecommendationShort => TruncateSingleLine(Recommendation, 72);

    public string StorageEstimateDisplay => EstimatedSizeBytes.HasValue && EstimatedSizeBytes.Value > 0
        ? $"~{FormatSize(EstimatedSizeBytes.Value)}"
        : Exists && SizeBytes > 0
            ? FormatSize(SizeBytes)
            : "estimate unavailable";

    public string DetailText =>
        $"{Tool} ({Category}){Environment.NewLine}" +
        $"Purpose: {(string.IsNullOrWhiteSpace(Purpose) ? "Not provided in current report." : Purpose)}{Environment.NewLine}" +
        $"Official URL: {(string.IsNullOrWhiteSpace(OfficialUrl) ? (string.IsNullOrWhiteSpace(Url) ? "Not provided." : Url) : OfficialUrl)}{Environment.NewLine}" +
        $"License / redistribution: {(string.IsNullOrWhiteSpace(LicenseRedistributionNote) ? "Check vendor terms before bundling." : LicenseRedistributionNote)}{Environment.NewLine}" +
        $"Distribution model: {(string.IsNullOrWhiteSpace(DistributionModel) ? TypeDisplay : DistributionModel)}{Environment.NewLine}" +
        $"Beta safety rating: {(string.IsNullOrWhiteSpace(BetaSafetyRating) ? "Needs review" : BetaSafetyRating)}{Environment.NewLine}" +
        $"Download status: {(string.IsNullOrWhiteSpace(DownloadStatus) ? StatusDisplayUi : DownloadStatus)}{Environment.NewLine}" +
        $"Checksum status: {(string.IsNullOrWhiteSpace(ChecksumStatus) ? VerificationDisplay : ChecksumStatus)}{Environment.NewLine}" +
        OptionalDownloadModeLines() +
        OptionalFreshnessMetadataLines() +
        $"Classification: {NormalizedCategoryLabel}{Environment.NewLine}" +
        $"Status: {StatusDisplayUi}{Environment.NewLine}" +
        $"Type: {TypeDisplay}{Environment.NewLine}" +
        $"Expected path: {(string.IsNullOrWhiteSpace(ResolvedExpectedPath) ? ExpectedPath : ResolvedExpectedPath)}{Environment.NewLine}" +
        $"Found path: {(string.IsNullOrWhiteSpace(MatchedPath) ? "UNKNOWN" : MatchedPath)}{Environment.NewLine}" +
        $"Size: {(SizeBytes > 0 ? FormatSize(SizeBytes) : "unknown")}{Environment.NewLine}" +
        $"Verification: {VerificationDisplay}{Environment.NewLine}" +
        $"Verification mode: {VerificationModeBadge}{(string.IsNullOrWhiteSpace(VerificationModeTooltip) ? string.Empty : " — " + VerificationModeTooltip)}{Environment.NewLine}" +
        $"Reason: {(string.IsNullOrWhiteSpace(ClassificationReason) ? "Report did not include a classification reason." : ClassificationReason)}{Environment.NewLine}" +
        OptionalCatalogMetadataLines() +
        $"Next step: {Recommendation}";

    /// <summary>One-line badge string used by the grid (family · category · arch · bootMode). Empty when no catalog metadata is set.</summary>
    public string CatalogBadgesDisplay
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(6);
            if (!string.IsNullOrWhiteSpace(Family)) { parts.Add(Family.Trim()); }
            if (!string.IsNullOrWhiteSpace(OsCategory)) { parts.Add(OsCategory.Trim()); }
            if (!string.IsNullOrWhiteSpace(Architecture)) { parts.Add(Architecture.Trim()); }
            if (!string.IsNullOrWhiteSpace(BootMode)) { parts.Add(BootMode.Trim()); }
            return string.Join(" · ", parts);
        }
    }

    /// <summary>Short status tag suitable for a coloured chip in the UI. Returns null when no tag applies.</summary>
    public string? CatalogStatusTag
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(LegacyWarning))
            {
                return "Legacy / Lab Only";
            }

            if (!string.IsNullOrWhiteSpace(LicenseNote) &&
                LicenseNote.Contains("Paid", StringComparison.OrdinalIgnoreCase))
            {
                return "Paid - vendor licence";
            }

            if (ManualOnly || string.Equals(Type, "manualDownload", StringComparison.OrdinalIgnoreCase))
            {
                return "Manual ISO Required";
            }

            if (string.Equals(SourceTrust, "community", StringComparison.OrdinalIgnoreCase))
            {
                return "Community source";
            }

            if (string.Equals(SourceTrust, "official", StringComparison.OrdinalIgnoreCase))
            {
                return "Official source";
            }

            return null;
        }
    }

    public string SafetyBadgesDisplay
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(8)
            {
                ManualOnly || string.Equals(Type, "manualDownload", StringComparison.OrdinalIgnoreCase)
                    ? "Manual required"
                    : "Managed download"
            };

            if (string.Equals(SourceTrust, "official", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("Official source");
            }
            else if (string.Equals(SourceTrust, "community", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("Community source");
            }

            var checksum = ChecksumBadgeDisplay;
            if (!string.IsNullOrWhiteSpace(checksum))
            {
                parts.Add(checksum);
            }

            var freshness = FreshnessBadgeDisplay;
            if (!string.IsNullOrWhiteSpace(freshness))
            {
                parts.Add(freshness);
            }

            if (!string.IsNullOrWhiteSpace(PromotionBlockerDisplay))
            {
                parts.Add("Promotion blocked");
            }

            if (!string.IsNullOrWhiteSpace(LegacyWarning))
            {
                parts.Add("Legacy/lab only");
            }

            if (!string.IsNullOrWhiteSpace(LicenseNote) &&
                LicenseNote.Contains("paid", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add("Paid/manual");
            }

            return string.Join(" | ", parts);
        }
    }

    public string FreshnessBadgeDisplay
    {
        get
        {
            return FreshnessStatus.Trim() switch
            {
                "UpToDate" => "Up to date",
                "PatchUpdateAvailable" or "MinorUpdateAvailable" or "MajorUpdateAvailable" => "Update available",
                "ChecksumVerificationRequired" => "Checksum review",
                "SourceChanged" => "Source review",
                "UpdateUnsafe" => "Update unsafe",
                "LegacyPinned" => "Legacy pinned",
                "ManualReviewRequired" => "Manual review",
                _ => string.Empty
            };
        }
    }

    /// <summary>Explains why an available update is not eligible for automatic promotion.</summary>
    public string PromotionBlockerDisplay =>
        FreshnessStatus is "PatchUpdateAvailable" or "MinorUpdateAvailable" or "MajorUpdateAvailable" &&
        UpdateRecommendation.Contains("machine-readable checksum", StringComparison.OrdinalIgnoreCase)
            ? "Automatic promotion blocked: the vendor does not publish a verifiable checksum for the exact available artifact. The existing verified package remains safe and usable."
            : string.Empty;

    public string ChecksumBadgeDisplay
    {
        get
        {
            if (ChecksumVerificationMode.Contains("url", StringComparison.OrdinalIgnoreCase) ||
                ChecksumVerificationMode.Contains("digest", StringComparison.OrdinalIgnoreCase))
            {
                return "Checksum verified";
            }

            if (ChecksumVerificationMode.Contains("pinned", StringComparison.OrdinalIgnoreCase) ||
                ChecksumStatus.Contains("pinned", StringComparison.OrdinalIgnoreCase) ||
                Verification.Contains("pinned", StringComparison.OrdinalIgnoreCase))
            {
                return "Checksum limited";
            }

            if (ChecksumStatus.Contains("verified", StringComparison.OrdinalIgnoreCase) ||
                Verification.Contains("verified", StringComparison.OrdinalIgnoreCase))
            {
                return "Checksum verified";
            }

            if (ManualOnly || string.Equals(Type, "manualDownload", StringComparison.OrdinalIgnoreCase))
            {
                return "Checksum manual";
            }

            return string.Empty;
        }
    }

    public string FreshnessDetailDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FreshnessStatus) &&
                string.IsNullOrWhiteSpace(CurrentPinnedVersion) &&
                string.IsNullOrWhiteSpace(LatestKnownStableVersion))
            {
                return string.Empty;
            }

            var current = string.IsNullOrWhiteSpace(CurrentPinnedVersion) ? Version : CurrentPinnedVersion;
            var latest = string.IsNullOrWhiteSpace(LatestKnownStableVersion) ? "unknown" : LatestKnownStableVersion;
            var audit = string.IsNullOrWhiteSpace(LastFreshnessAuditUtc) ? "audit unknown" : LastFreshnessAuditUtc;
            var mode = string.IsNullOrWhiteSpace(ChecksumVerificationMode) ? "checksum mode unknown" : ChecksumVerificationMode;
            var recommendation = string.IsNullOrWhiteSpace(UpdateRecommendation) ? "Review upstream before changing pinned version." : UpdateRecommendation;
            var blocker = PromotionBlockerDisplay;
            return $"Pinned {current} | latest stable {latest} | {FreshnessBadgeDisplay} | audited {audit} | {mode}" +
                   (string.IsNullOrWhiteSpace(blocker) ? string.Empty : $" | {blocker}") +
                   $" | {recommendation}";
        }
    }

    private string OptionalCatalogMetadataLines()
    {
        // Only emit a block when at least one catalog metadata field is populated.
        // This keeps the existing DetailText shape stable for legacy items / older reports.
        if (string.IsNullOrWhiteSpace(Family) &&
            string.IsNullOrWhiteSpace(OsCategory) &&
            string.IsNullOrWhiteSpace(Architecture) &&
            string.IsNullOrWhiteSpace(BootMode) &&
            string.IsNullOrWhiteSpace(RecommendedUse) &&
            string.IsNullOrWhiteSpace(TechnicianNotes) &&
            string.IsNullOrWhiteSpace(LicenseNote) &&
            string.IsNullOrWhiteSpace(LegacyWarning) &&
            string.IsNullOrWhiteSpace(VentoyNotes) &&
            string.IsNullOrWhiteSpace(SecureBootNote) &&
            string.IsNullOrWhiteSpace(SourceTrust) &&
            string.IsNullOrWhiteSpace(DownloadMode) &&
            string.IsNullOrWhiteSpace(ActionLabel) &&
            string.IsNullOrWhiteSpace(ActionReason) &&
            string.IsNullOrWhiteSpace(PromotionStatus) &&
            string.IsNullOrWhiteSpace(PromotionEvidence) &&
            string.IsNullOrWhiteSpace(LegalRisk) &&
            string.IsNullOrWhiteSpace(ChecksumRequirement) &&
            !ManualOnly)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        AppendIfSet(sb, "Family", Family);
        AppendIfSet(sb, "OS category", OsCategory);
        AppendIfSet(sb, "Architecture", Architecture);
        AppendIfSet(sb, "Boot mode", BootMode);
        AppendIfSet(sb, "Recommended use", RecommendedUse);
        AppendIfSet(sb, "Technician notes", TechnicianNotes);
        AppendIfSet(sb, "License note", LicenseNote);
        if (ManualOnly) { sb.Append("Manual ISO Required: yes").Append(Environment.NewLine); }
        AppendIfSet(sb, "Legacy warning", LegacyWarning);
        AppendIfSet(sb, "Ventoy notes", VentoyNotes);
        AppendIfSet(sb, "Secure Boot", SecureBootNote);
        AppendIfSet(sb, "Source trust", SourceTrust);
        AppendIfSet(sb, "Download mode", EffectiveDownloadMode);
        AppendIfSet(sb, "Action", DownloadActionLabel);
        AppendIfSet(sb, "Action reason", ActionHelperText);
        AppendIfSet(sb, "Promotion status", PromotionStatus);
        AppendIfSet(sb, "Promotion evidence", PromotionEvidence);
        AppendIfSet(sb, "Legal risk", LegalRisk);
        AppendIfSet(sb, "Checksum requirement", ChecksumRequirement);
        if (ManagedPromotionCandidate) { sb.Append("Managed promotion candidate: yes").Append(Environment.NewLine); }
        return sb.ToString();
    }

    private string OptionalDownloadModeLines()
    {
        if (string.IsNullOrWhiteSpace(DownloadMode) &&
            string.IsNullOrWhiteSpace(ActionLabel) &&
            string.IsNullOrWhiteSpace(ActionReason) &&
            string.IsNullOrWhiteSpace(PromotionStatus) &&
            string.IsNullOrWhiteSpace(PromotionEvidence) &&
            string.IsNullOrWhiteSpace(LegalRisk) &&
            string.IsNullOrWhiteSpace(ChecksumRequirement) &&
            !ManagedPromotionCandidate)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        AppendIfSet(sb, "Download mode", EffectiveDownloadMode);
        AppendIfSet(sb, "Primary action", DownloadActionLabel);
        AppendIfSet(sb, "Action helper", ActionHelperText);
        AppendIfSet(sb, "Secondary action", SecondaryActionLabel);
        AppendIfSet(sb, "Promotion status", PromotionStatus);
        AppendIfSet(sb, "Promotion evidence", PromotionEvidence);
        AppendIfSet(sb, "Legal risk", LegalRisk);
        AppendIfSet(sb, "Checksum requirement", ChecksumRequirement);
        if (ManagedPromotionCandidate) { sb.Append("Managed promotion candidate: yes").Append(Environment.NewLine); }
        return sb.ToString();
    }

    private string OptionalFreshnessMetadataLines()
    {
        if (string.IsNullOrWhiteSpace(FreshnessStatus) &&
            string.IsNullOrWhiteSpace(CurrentPinnedVersion) &&
            string.IsNullOrWhiteSpace(LatestKnownStableVersion) &&
            string.IsNullOrWhiteSpace(ChecksumVerificationMode) &&
            string.IsNullOrWhiteSpace(UpdateRecommendation))
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        AppendIfSet(sb, "Pinned version", CurrentPinnedVersion);
        AppendIfSet(sb, "Latest stable known", LatestKnownStableVersion);
        AppendIfSet(sb, "Freshness status", FreshnessBadgeDisplay);
        AppendIfSet(sb, "Freshness audit", LastFreshnessAuditUtc);
        AppendIfSet(sb, "Checksum mode", ChecksumVerificationMode);
        AppendIfSet(sb, "Update recommendation", UpdateRecommendation);
        return sb.ToString();
    }

    private static void AppendIfSet(System.Text.StringBuilder sb, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sb.Append(label).Append(": ").Append(value.Trim()).Append(Environment.NewLine);
    }

    private static string CompactPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Path not reported";
        }

        var normalized = path.Replace('/', '\\').Trim();
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 3)
        {
            return normalized;
        }

        return $"{parts[0]}\\{parts[1]}\\...\\{parts[^1]}";
    }

    private static string FormatSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return "0 B";
        }

        var mb = sizeBytes / (1024d * 1024d);
        return mb >= 100d ? $"{Math.Round(mb):0} MB" : $"{mb:0.#} MB";
    }

    private static string TruncateSingleLine(string text, int maxLength)
    {
        var normalized = (text ?? string.Empty).Replace(Environment.NewLine, " ").Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..(maxLength - 3)] + "...";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
