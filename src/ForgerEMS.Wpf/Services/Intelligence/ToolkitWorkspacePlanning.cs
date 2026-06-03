using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public sealed class CatalogSelectionGroup
{
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string FilterToken { get; init; } = string.Empty;
    public IReadOnlyList<string> SelectedEntryIds { get; init; } = [];
}

public sealed class DownloadQueueItem
{
    public string EntryId { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string Tool { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public string BootMode { get; init; } = string.Empty;
    public string SourceTrust { get; init; } = string.Empty;
    public string PlanSectionLabel { get; init; } = string.Empty;
    public string RequirementLabel { get; init; } = string.Empty;
    public string ChecksumLabel { get; init; } = string.Empty;
    public string FreshnessLabel { get; init; } = string.Empty;
    public string StorageLabel { get; init; } = string.Empty;
    public string VentoyLabel { get; init; } = string.Empty;
    public long? EstimatedSizeBytes { get; init; }
    public bool IsManaged { get; init; }
    public bool IsManualOnly { get; init; }
}

public sealed class EstimatedStorageUsage
{
    public long KnownBytes { get; init; }
    public int KnownItemCount { get; init; }
    public int UnknownItemCount { get; init; }
    public string TotalDisplay { get; init; } = "0 B";
    public string CapacityWarningText { get; init; } = "Plan is empty.";
}

public sealed class DownloadPlan
{
    public IReadOnlyList<DownloadQueueItem> Items { get; init; } = [];
    public EstimatedStorageUsage Storage { get; init; } = new();
    public int ManagedCount { get; init; }
    public int ManualCount { get; init; }
    public int ChecksumAvailableCount { get; init; }
    public string SourceTrustSummary { get; init; } = "Source trust: none";
    public string ArchitectureSummary { get; init; } = "Architecture mix: none";
    public string VentoyCompatibilitySummary { get; init; } = "Ventoy: no planned items";
    public string ManualRequirementSummary { get; init; } = "Manual requirements: none";
    public string ValidationSummary { get; init; } = "Plan is empty.";
}

public sealed class PlannedUsbLoadout
{
    public string Name { get; init; } = "Technician USB Loadout";
    public DownloadPlan Plan { get; init; } = new();
    public IReadOnlyList<string> TechnicianLabels { get; init; } = [];
}

public enum SelectedManagedDownloadQueueStatus
{
    Pending,
    Downloading,
    VerifyingChecksum,
    Completed,
    Failed,
    AlreadyPresent,
    SkippedManualOnly,
    SkippedDisabled,
    SkippedMissingChecksum,
    SkippedBlocked,
    Canceled
}

public sealed class SelectedManagedDownloadQueueItem
{
    public string EntryId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Destination { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string StatusText { get; init; } = "Pending";
    public SelectedManagedDownloadQueueStatus Status { get; init; } = SelectedManagedDownloadQueueStatus.Pending;
}

public sealed class SelectedManagedDownloadManifestResult
{
    public string ManifestPath { get; init; } = string.Empty;
    public IReadOnlyList<SelectedManagedDownloadQueueItem> QueueItems { get; init; } = [];
    public IReadOnlyList<SelectedManagedDownloadQueueItem> ManualItems { get; init; } = [];
    public IReadOnlyList<SelectedManagedDownloadQueueItem> BlockedItems { get; init; } = [];
    public int ReadyCount => QueueItems.Count;
    public int ManualCount => ManualItems.Count;
    public int BlockedCount => BlockedItems.Count;
    public int UnknownSizeCount { get; init; }
    public long KnownBytes { get; init; }
    public string KnownSizeDisplay { get; init; } = "estimate unavailable";
    public string SummaryText { get; init; } = string.Empty;
}

public sealed class UsbWorkspaceProfile
{
    public int SchemaVersion { get; init; } = ToolkitWorkspaceProfileStore.CurrentSchemaVersion;
    public string ProfileId { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Notes { get; init; } = string.Empty;
    public IReadOnlyList<string> TechnicianLabels { get; init; } = [];
    public IReadOnlyList<string> CategoryPreferences { get; init; } = [];
    public IReadOnlyList<string> SelectedEntries { get; init; } = [];
    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}

public static class ToolkitWorkspacePlanner
{
    private static readonly (long Bytes, string Label)[] UsbThresholds =
    [
        (16L * 1024 * 1024 * 1024, "16GB"),
        (32L * 1024 * 1024 * 1024, "32GB"),
        (64L * 1024 * 1024 * 1024, "64GB"),
        (128L * 1024 * 1024 * 1024, "128GB"),
        (256L * 1024 * 1024 * 1024, "256GB")
    ];

    public static string GetSelectionId(ToolkitHealthItemView item)
    {
        var stablePath = FirstNonBlank(item.ExpectedPath, item.ResolvedExpectedPath, item.MatchedPath);
        if (!string.IsNullOrWhiteSpace(stablePath))
        {
            return NormalizeId(stablePath);
        }

        return NormalizeId($"{item.Tool}|{item.Category}|{item.Url}");
    }

    public static DownloadPlan BuildPlan(IEnumerable<ToolkitHealthItemView> allItems)
    {
        var selected = allItems
            .Where(static item => item.SelectedForDownload)
            .GroupBy(GetSelectionId, StringComparer.OrdinalIgnoreCase)
            .Select(static group => group.First())
            .ToList();

        var queue = selected
            .Select((item, index) => ToQueueItem(item, index + 1))
            .ToList();

        var storage = EstimateStorage(queue);
        var manual = queue.Where(static item => item.IsManualOnly).ToList();
        var managedCount = queue.Count(static item => item.IsManaged);
        var checksumCount = queue.Count(static item => item.ChecksumLabel != "Checksum unavailable");

        return new DownloadPlan
        {
            Items = queue,
            Storage = storage,
            ManagedCount = managedCount,
            ManualCount = manual.Count,
            ChecksumAvailableCount = checksumCount,
            SourceTrustSummary = BuildGroupedSummary("Source trust", queue.Select(static item => BlankAsUnknown(item.SourceTrust))),
            ArchitectureSummary = BuildGroupedSummary("Architecture mix", queue.SelectMany(static item => SplitTokens(item.Architecture))),
            VentoyCompatibilitySummary = BuildVentoySummary(queue),
            ManualRequirementSummary = manual.Count == 0
                ? "Manual requirements: none"
                : $"Manual requirements: {manual.Count} item(s) need vendor/community/manual source review",
            ValidationSummary = BuildValidationSummary(queue, storage)
        };
    }

    public static void ApplyProfile(IEnumerable<ToolkitHealthItemView> allItems, UsbWorkspaceProfile profile)
    {
        var selectedIds = new HashSet<string>(MigrateSelectedEntries(profile), StringComparer.OrdinalIgnoreCase);
        foreach (var item in allItems)
        {
            item.SelectedForDownload = selectedIds.Contains(GetSelectionId(item)) ||
                                       selectedIds.Contains(item.Tool);
        }
    }

    public static UsbWorkspaceProfile BuildProfile(
        string name,
        string notes,
        IEnumerable<ToolkitHealthItemView> allItems,
        IEnumerable<string>? categoryPreferences = null,
        IEnumerable<string>? technicianLabels = null)
    {
        var selectedEntries = allItems
            .Where(static item => item.SelectedForDownload)
            .Select(GetSelectionId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new UsbWorkspaceProfile
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Technician USB Profile" : name.Trim(),
            Notes = notes.Trim(),
            SelectedEntries = selectedEntries,
            CategoryPreferences = (categoryPreferences ?? [])
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            TechnicianLabels = (technicianLabels ?? [])
                .Where(static x => !string.IsNullOrWhiteSpace(x))
                .Select(static x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            CreatedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    public static SelectedManagedDownloadManifestResult BuildSelectedManagedManifest(
        string sourceManifestPath,
        string outputManifestPath,
        IEnumerable<ToolkitHealthItemView> allItems)
    {
        if (string.IsNullOrWhiteSpace(sourceManifestPath) || !File.Exists(sourceManifestPath))
        {
            throw new FileNotFoundException("Source manifest was not found.", sourceManifestPath);
        }

        var selectedById = allItems
            .Where(static item => item.SelectedForDownload)
            .GroupBy(GetSelectionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.OrdinalIgnoreCase);

        var sourceNode = JsonNode.Parse(File.ReadAllText(sourceManifestPath)) as JsonObject
            ?? throw new InvalidDataException("Source manifest root must be a JSON object.");
        var sourceItems = sourceNode["items"] as JsonArray
            ?? throw new InvalidDataException("Source manifest root must contain an items array.");

        var selectedItems = new JsonArray();
        var queue = new List<SelectedManagedDownloadQueueItem>();
        var manual = new List<SelectedManagedDownloadQueueItem>();
        var blocked = new List<SelectedManagedDownloadQueueItem>();
        var knownBytes = 0L;
        var unknownSizeCount = 0;

        foreach (var node in sourceItems)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            var dest = GetJsonString(item, "dest");
            if (string.IsNullOrWhiteSpace(dest))
            {
                continue;
            }

            var id = NormalizeId(dest);
            if (!selectedById.TryGetValue(id, out var selectedView))
            {
                continue;
            }

            var decision = ClassifySelectedManifestItem(item, selectedView);
            if (decision.Status == SelectedManagedDownloadQueueStatus.Pending)
            {
                selectedItems.Add(item.DeepClone());
                queue.Add(decision);
                var size = selectedView.EstimatedSizeBytes ?? (selectedView.SizeBytes > 0 ? selectedView.SizeBytes : null);
                if (size.HasValue)
                {
                    knownBytes += size.Value;
                }
                else
                {
                    unknownSizeCount++;
                }
            }
            else if (decision.Status is SelectedManagedDownloadQueueStatus.SkippedManualOnly)
            {
                manual.Add(decision);
            }
            else
            {
                blocked.Add(decision);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputManifestPath)) ?? ".");
        var output = BuildSubsetManifest(sourceNode, selectedItems);
        File.WriteAllText(outputManifestPath, output.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        return new SelectedManagedDownloadManifestResult
        {
            ManifestPath = outputManifestPath,
            QueueItems = queue,
            ManualItems = manual,
            BlockedItems = blocked,
            KnownBytes = knownBytes,
            UnknownSizeCount = unknownSizeCount,
            KnownSizeDisplay = knownBytes > 0 ? $"~{FormatBytes(knownBytes)} known" : "estimate unavailable",
            SummaryText = $"Ready {queue.Count} | manual {manual.Count} | blocked {blocked.Count} | unknown size {unknownSizeCount}"
        };
    }

    public static IReadOnlyList<string> MigrateSelectedEntries(UsbWorkspaceProfile profile)
    {
        if (profile.SelectedEntries.Count > 0)
        {
            return profile.SelectedEntries;
        }

        if (profile.ExtensionData is not null &&
            profile.ExtensionData.TryGetValue("selectedTools", out var legacyTools) &&
            legacyTools.ValueKind == JsonValueKind.Array)
        {
            return legacyTools.EnumerateArray()
                .Where(static element => element.ValueKind == JsonValueKind.String)
                .Select(static element => element.GetString() ?? string.Empty)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
        }

        return [];
    }

    private static JsonObject BuildSubsetManifest(JsonObject sourceNode, JsonArray selectedItems)
    {
        var output = new JsonObject();
        foreach (var property in sourceNode)
        {
            if (property.Key.Equals("items", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            output[property.Key] = property.Value?.DeepClone();
        }

        output["items"] = selectedItems;
        output["selectionMode"] = "selected-managed-downloads";
        output["selectionGeneratedUtc"] = DateTimeOffset.UtcNow.ToString("o");
        return output;
    }

    private static SelectedManagedDownloadQueueItem ClassifySelectedManifestItem(JsonObject manifestItem, ToolkitHealthItemView selectedView)
    {
        var name = FirstNonBlank(GetJsonString(manifestItem, "name"), selectedView.Tool);
        var dest = FirstNonBlank(GetJsonString(manifestItem, "dest"), selectedView.ExpectedPath);
        var url = GetJsonString(manifestItem, "url");
        var type = FirstNonBlank(GetJsonString(manifestItem, "type"), "file");
        var enabled = !manifestItem.TryGetPropertyValue("enabled", out var enabledNode) ||
                      enabledNode is null ||
                      enabledNode.GetValueKind() != JsonValueKind.False;
        var manualOnly = GetJsonBool(manifestItem, "manualOnly") || selectedView.ManualOnly;
        var legacyWarning = FirstNonBlank(GetJsonString(manifestItem, "legacyWarning"), selectedView.LegacyWarning);
        var family = FirstNonBlank(GetJsonString(manifestItem, "family"), selectedView.Family);
        var osCategory = FirstNonBlank(GetJsonString(manifestItem, "osCategory"), selectedView.OsCategory);
        var licenseNote = FirstNonBlank(GetJsonString(manifestItem, "licenseNote"), selectedView.LicenseNote);
        var hasChecksum = !string.IsNullOrWhiteSpace(GetJsonString(manifestItem, "sha256")) ||
                          !string.IsNullOrWhiteSpace(GetJsonString(manifestItem, "sha256Url")) ||
                          !string.IsNullOrWhiteSpace(GetJsonString(manifestItem, "sha512")) ||
                          !string.IsNullOrWhiteSpace(GetJsonString(manifestItem, "sha512Url"));

        if (!enabled)
        {
            return QueueDecision(name, dest, url, SelectedManagedDownloadQueueStatus.SkippedDisabled, "Blocked / needs attention: disabled manifest candidate is excluded from automation.");
        }

        if (!type.Equals("file", StringComparison.OrdinalIgnoreCase) || manualOnly)
        {
            return QueueDecision(name, dest, url, SelectedManagedDownloadQueueStatus.SkippedManualOnly, "Manual required: " + BuildManualReason(selectedView, licenseNote, legacyWarning));
        }

        if (!string.IsNullOrWhiteSpace(legacyWarning) ||
            family.Equals("Windows", StringComparison.OrdinalIgnoreCase) &&
            osCategory.Equals("Legacy", StringComparison.OrdinalIgnoreCase))
        {
            return QueueDecision(name, dest, url, SelectedManagedDownloadQueueStatus.SkippedBlocked, "Blocked / needs attention: legacy/lab-only item cannot be automated.");
        }

        if (licenseNote.Contains("paid", StringComparison.OrdinalIgnoreCase))
        {
            return QueueDecision(name, dest, url, SelectedManagedDownloadQueueStatus.SkippedBlocked, "Blocked / needs attention: paid/manual licensing blocks automation.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return QueueDecision(name, dest, url, SelectedManagedDownloadQueueStatus.SkippedBlocked, "Blocked / needs attention: automation requires an absolute HTTPS source URL.");
        }

        if (!hasChecksum)
        {
            return QueueDecision(name, dest, url, SelectedManagedDownloadQueueStatus.SkippedMissingChecksum, "Blocked / needs attention: checksum metadata is required before automation.");
        }

        return QueueDecision(name, dest, url, SelectedManagedDownloadQueueStatus.Pending, "Ready to download: managed HTTPS source with checksum metadata.");
    }

    private static SelectedManagedDownloadQueueItem QueueDecision(
        string name,
        string dest,
        string url,
        SelectedManagedDownloadQueueStatus status,
        string reason)
    {
        return new SelectedManagedDownloadQueueItem
        {
            EntryId = NormalizeId(dest),
            Name = name,
            Destination = dest,
            Url = url,
            Status = status,
            StatusText = StatusToText(status),
            Reason = reason
        };
    }

    private static string StatusToText(SelectedManagedDownloadQueueStatus status) => status switch
    {
        SelectedManagedDownloadQueueStatus.Pending => "Pending",
        SelectedManagedDownloadQueueStatus.Downloading => "Downloading",
        SelectedManagedDownloadQueueStatus.VerifyingChecksum => "Verifying checksum",
        SelectedManagedDownloadQueueStatus.Completed => "Completed",
        SelectedManagedDownloadQueueStatus.Failed => "Failed",
        SelectedManagedDownloadQueueStatus.AlreadyPresent => "Already present",
        SelectedManagedDownloadQueueStatus.SkippedManualOnly => "Skipped manual-only",
        SelectedManagedDownloadQueueStatus.SkippedDisabled => "Skipped disabled",
        SelectedManagedDownloadQueueStatus.SkippedMissingChecksum => "Skipped missing checksum",
        SelectedManagedDownloadQueueStatus.SkippedBlocked => "Skipped blocked",
        SelectedManagedDownloadQueueStatus.Canceled => "Canceled",
        _ => "Unknown"
    };

    private static string BuildManualReason(ToolkitHealthItemView item, string licenseNote, string legacyWarning)
    {
        if (!string.IsNullOrWhiteSpace(legacyWarning))
        {
            return "Legacy/lab-only unsupported; user-provided media only.";
        }

        if (licenseNote.Contains("paid", StringComparison.OrdinalIgnoreCase))
        {
            return "Paid vendor license required.";
        }

        if (item.SourceTrust.Equals("community", StringComparison.OrdinalIgnoreCase))
        {
            return "Community source; review manually before use.";
        }

        if (item.SourceTrust.Equals("official", StringComparison.OrdinalIgnoreCase))
        {
            return "Official vendor source required.";
        }

        return "Manual ISO/tool required.";
    }

    private static string GetJsonString(JsonObject item, string propertyName)
    {
        if (!item.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return string.Empty;
        }

        return node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToString();
    }

    private static bool GetJsonBool(JsonObject item, string propertyName)
    {
        if (!item.TryGetPropertyValue(propertyName, out var node) || node is null)
        {
            return false;
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(node.GetValue<string>(), out var parsed) && parsed,
            _ => false
        };
    }

    private static DownloadQueueItem ToQueueItem(ToolkitHealthItemView item, int priority)
    {
        var manual = item.ManualOnly ||
                     item.TypeDisplay.Equals("Manual", StringComparison.OrdinalIgnoreCase) ||
                     item.Status.Equals("MANUAL_REQUIRED", StringComparison.OrdinalIgnoreCase);
        long? size = item.EstimatedSizeBytes.HasValue && item.EstimatedSizeBytes.Value > 0
            ? item.EstimatedSizeBytes.Value
            : item.SizeBytes > 0 ? item.SizeBytes : null;

        return new DownloadQueueItem
        {
            EntryId = GetSelectionId(item),
            Priority = priority,
            Tool = item.Tool,
            Category = item.Category,
            Family = item.Family,
            Architecture = item.Architecture,
            BootMode = item.BootMode,
            SourceTrust = item.SourceTrust,
            PlanSectionLabel = BuildPlanSectionLabel(item, manual),
            RequirementLabel = manual ? BuildManualRequirementLabel(item) : "Managed download candidate",
            ChecksumLabel = BuildChecksumLabel(item, manual),
            FreshnessLabel = string.IsNullOrWhiteSpace(item.FreshnessBadgeDisplay) ? "Freshness not tracked" : item.FreshnessBadgeDisplay,
            StorageLabel = size.HasValue ? $"~{FormatBytes(size.Value)}" : "estimate unavailable",
            VentoyLabel = string.IsNullOrWhiteSpace(item.VentoyNotes) ? InferVentoyLabel(item) : item.VentoyNotes,
            EstimatedSizeBytes = size,
            IsManaged = !manual,
            IsManualOnly = manual
        };
    }

    private static EstimatedStorageUsage EstimateStorage(IReadOnlyList<DownloadQueueItem> queue)
    {
        var known = queue.Where(static item => item.EstimatedSizeBytes.HasValue).ToList();
        var knownBytes = known.Sum(static item => item.EstimatedSizeBytes!.Value);
        var unknown = queue.Count - known.Count;
        var warning = BuildCapacityWarning(knownBytes, unknown, queue.Count);

        return new EstimatedStorageUsage
        {
            KnownBytes = knownBytes,
            KnownItemCount = known.Count,
            UnknownItemCount = unknown,
            TotalDisplay = knownBytes > 0 ? $"~{FormatBytes(knownBytes)} known" : "estimate unavailable",
            CapacityWarningText = warning
        };
    }

    private static string BuildCapacityWarning(long knownBytes, int unknownCount, int totalCount)
    {
        if (totalCount == 0)
        {
            return "Plan is empty.";
        }

        var suffix = unknownCount > 0 ? $" ({unknownCount} unknown item(s) not included)" : string.Empty;
        if (knownBytes <= 0)
        {
            return "Storage estimate unavailable" + suffix;
        }

        foreach (var threshold in UsbThresholds)
        {
            if (knownBytes <= threshold.Bytes)
            {
                return $"Known size fits within {threshold.Label}{suffix}";
            }
        }

        return "Known size exceeds 256GB" + suffix;
    }

    private static string BuildValidationSummary(IReadOnlyList<DownloadQueueItem> queue, EstimatedStorageUsage storage)
    {
        if (queue.Count == 0)
        {
            return "Plan is empty.";
        }

        var parts = new List<string>
        {
            $"Plan contains {queue.Count} item(s)",
            $"{queue.Count(static item => item.IsManaged)} managed",
            $"{queue.Count(static item => item.IsManualOnly)} manual",
            $"{storage.UnknownItemCount} unknown-size",
            $"Ready to download: {queue.Count(static item => item.PlanSectionLabel == "Ready to download")}",
            $"Manual required: {queue.Count(static item => item.PlanSectionLabel == "Manual required")}",
            $"Blocked / needs attention: {queue.Count(static item => item.PlanSectionLabel == "Blocked / needs attention")}",
            $"Already present: {queue.Count(static item => item.PlanSectionLabel == "Already present")}",
            $"Failed: {queue.Count(static item => item.PlanSectionLabel == "Failed")}",
            $"Update available: {queue.Count(static item => item.FreshnessLabel == "Update available")}"
        };

        if (queue.Any(static item => item.ChecksumLabel == "Checksum unavailable"))
        {
            parts.Add("checksum gaps require review");
        }

        parts.Add("planning only; no downloads or USB writes are executed by planning");
        return string.Join(" | ", parts);
    }

    private static string BuildVentoySummary(IReadOnlyList<DownloadQueueItem> queue)
    {
        if (queue.Count == 0)
        {
            return "Ventoy: no planned items";
        }

        var bios = queue.Count(static item => TokenContains(item.BootMode, "bios"));
        var secureBoot = queue.Count(static item => TokenContains(item.BootMode, "secure-boot"));
        var uefi = queue.Count(static item => TokenContains(item.BootMode, "uefi"));
        return $"Ventoy: BIOS {bios}, UEFI {uefi}, Secure Boot {secureBoot}";
    }

    private static string BuildGroupedSummary(string label, IEnumerable<string> values)
    {
        var groups = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(static value => value.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count())
            .ThenBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static group => $"{group.Key}: {group.Count()}")
            .ToArray();

        return groups.Length == 0 ? $"{label}: none" : $"{label}: {string.Join(", ", groups)}";
    }

    private static bool HasChecksum(ToolkitHealthItemView item)
    {
        var text = $"{item.ChecksumStatus} {item.Verification}".Trim();
        return !string.IsNullOrWhiteSpace(text) &&
               !text.Equals("unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("checksum unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildChecksumLabel(ToolkitHealthItemView item, bool manual)
    {
        var badge = item.ChecksumBadgeDisplay;
        if (!string.IsNullOrWhiteSpace(badge))
        {
            return badge;
        }

        if (manual)
        {
            return "Checksum manual";
        }

        return HasChecksum(item) ? "Checksum verified" : "Checksum unavailable";
    }

    private static string BuildPlanSectionLabel(ToolkitHealthItemView item, bool manual)
    {
        if (item.Status.Equals("INSTALLED", StringComparison.OrdinalIgnoreCase) ||
            item.Status.Equals("COVERED_BY_MANAGED", StringComparison.OrdinalIgnoreCase))
        {
            return "Already present";
        }

        if (item.Status.Contains("FAILED", StringComparison.OrdinalIgnoreCase) ||
            item.Status.Contains("MISMATCH", StringComparison.OrdinalIgnoreCase))
        {
            return "Failed";
        }

        if (manual)
        {
            return "Manual required";
        }

        if (!HasChecksum(item))
        {
            return "Blocked / needs attention";
        }

        return "Ready to download";
    }

    private static string BuildManualRequirementLabel(ToolkitHealthItemView item)
    {
        if (!string.IsNullOrWhiteSpace(item.LegacyWarning))
        {
            return "Legacy/lab only";
        }

        if (!string.IsNullOrWhiteSpace(item.LicenseNote) &&
            item.LicenseNote.Contains("paid", StringComparison.OrdinalIgnoreCase))
        {
            return "Paid/manual";
        }

        if (item.SourceTrust.Equals("community", StringComparison.OrdinalIgnoreCase))
        {
            return "Community/manual source";
        }

        if (item.SourceTrust.Equals("official", StringComparison.OrdinalIgnoreCase))
        {
            return "Official vendor source required";
        }

        return "Manual ISO required";
    }

    private static string InferVentoyLabel(ToolkitHealthItemView item)
    {
        if (!string.IsNullOrWhiteSpace(item.BootMode))
        {
            return $"Boot modes: {item.BootMode}";
        }

        return "Ventoy compatibility: review catalog notes";
    }

    private static string[] SplitTokens(string value)
    {
        return value.Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool TokenContains(string value, string token)
    {
        return SplitTokens(value).Any(part => part.Equals(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string BlankAsUnknown(string value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Trim();

    private static string FirstNonBlank(params string[] values) =>
        values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string NormalizeId(string value) => value.Trim().Replace('/', '\\').ToUpperInvariant();

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var size = (double)bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit >= 3 ? $"{size:0.#} {units[unit]}" : $"{size:0} {units[unit]}";
    }
}

public sealed class ToolkitWorkspaceProfileStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ToolkitWorkspaceProfileStore(string? profilesRoot = null)
    {
        ProfilesRoot = string.IsNullOrWhiteSpace(profilesRoot) ? GetDefaultProfilesRoot() : profilesRoot;
    }

    public string ProfilesRoot { get; }

    public static IReadOnlyList<UsbWorkspaceProfile> GetBuiltInProfiles()
    {
        return
        [
            BuiltIn("Windows Recovery USB", "windows-recovery", ["Windows", "Recovery"], ["Windows", "Recovery", "Secure Boot"]),
            BuiltIn("Retro Repair Toolkit", "retro-repair", ["Legacy", "Hobby", "BIOS"], ["Legacy", "Hobby"]),
            BuiltIn("Linux Admin Pack", "linux-admin", ["Linux", "Server", "Recovery"], ["Linux", "Server", "Recovery"]),
            BuiltIn("Network Diagnostics Kit", "network-diagnostics", ["Network", "BSD"], ["Network-Appliance", "Network"]),
            BuiltIn("Malware Cleanup USB", "malware-cleanup", ["Security", "Recovery"], ["Security", "Malware", "Recovery"]),
            BuiltIn("VM/Sandbox Toolkit", "vm-sandbox", ["Hypervisor", "Sandbox"], ["Hypervisor", "Server", "Desktop"]),
            BuiltIn("Portable Tech Bench", "portable-tech-bench", ["Portable", "Diagnostics"], ["Disk", "Hardware", "System", "USB"])
        ];
    }

    public IReadOnlyList<string> ListProfileNames()
    {
        Directory.CreateDirectory(ProfilesRoot);
        return Directory.EnumerateFiles(ProfilesRoot, "*.json", SearchOption.TopDirectoryOnly)
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public string Save(UsbWorkspaceProfile profile)
    {
        Directory.CreateDirectory(ProfilesRoot);
        var safeName = MakeSafeFileName(profile.Name);
        var path = Path.Combine(ProfilesRoot, safeName + ".json");
        var normalized = new UsbWorkspaceProfile
        {
            SchemaVersion = CurrentSchemaVersion,
            ProfileId = string.IsNullOrWhiteSpace(profile.ProfileId) ? Guid.NewGuid().ToString("N") : profile.ProfileId,
            Name = profile.Name,
            Notes = profile.Notes,
            TechnicianLabels = profile.TechnicianLabels,
            CategoryPreferences = profile.CategoryPreferences,
            SelectedEntries = profile.SelectedEntries,
            CreatedUtc = profile.CreatedUtc == default ? DateTimeOffset.UtcNow : profile.CreatedUtc,
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        File.WriteAllText(path, JsonSerializer.Serialize(normalized, JsonOptions));
        return path;
    }

    public bool TryLoad(string nameOrPath, out UsbWorkspaceProfile profile)
    {
        profile = new UsbWorkspaceProfile();
        var path = File.Exists(nameOrPath)
            ? nameOrPath
            : Path.Combine(ProfilesRoot, MakeSafeFileName(nameOrPath) + ".json");
        if (!File.Exists(path))
        {
            return false;
        }

        var loaded = JsonSerializer.Deserialize<UsbWorkspaceProfile>(File.ReadAllText(path), JsonOptions);
        if (loaded is null)
        {
            return false;
        }

        profile = loaded;
        return true;
    }

    public static string Export(UsbWorkspaceProfile profile, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, JsonSerializer.Serialize(profile, JsonOptions));
        return fullPath;
    }

    public UsbWorkspaceProfile Import(string path)
    {
        if (!TryLoad(path, out var profile))
        {
            throw new InvalidDataException("Workspace profile could not be loaded.");
        }

        Save(profile);
        return profile;
    }

    private static UsbWorkspaceProfile BuiltIn(string name, string id, IReadOnlyList<string> labels, IReadOnlyList<string> categories)
    {
        return new UsbWorkspaceProfile
        {
            ProfileId = id,
            Name = name,
            Notes = "Built-in starter profile. Apply it after loading a catalog, then tune selections before downloading.",
            TechnicianLabels = labels,
            CategoryPreferences = categories,
            SelectedEntries = []
        };
    }

    private static string GetDefaultProfilesRoot()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.GetTempPath();
        }

        return Path.Combine(localAppData, "ForgerEMS", "profiles");
    }

    private static string MakeSafeFileName(string name)
    {
        var cleaned = string.IsNullOrWhiteSpace(name) ? "Technician USB Profile" : name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalid, '-');
        }

        return cleaned;
    }
}
