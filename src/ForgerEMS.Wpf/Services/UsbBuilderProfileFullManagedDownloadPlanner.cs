using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VentoyToolkitSetup.Wpf.Services;

// Mirrors backend/Update-ForgerEMS.ps1's Get-UsbBuilderCategoryIdForPath +
// Get-UsbBuilderCategoryIdForManifestEntry so the WPF profile summary can answer
// "for this profile selection, which managed entries are eligible?" without
// shelling out. Backend remains the source of truth at run time; this front-end
// classifier is exercised by tests that pin every managed file in the catalog
// against the same rules.
public static class UsbBuilderProfileFullManagedDownloadPlanner
{
    public static UsbBuilderProfileFullManagedDownloadPlan Calculate(
        string manifestPath,
        IReadOnlySet<string> includedCategoryIds,
        string? usbRootPath = null,
        IEnumerable<string>? includedProfileItems = null)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);
        ArgumentNullException.ThrowIfNull(includedCategoryIds);

        if (!File.Exists(manifestPath))
        {
            return UsbBuilderProfileFullManagedDownloadPlan.Empty;
        }

        var normalizedCategories = NormalizeIncludedCategoryIds(includedCategoryIds);
        var itemFilter = UsbBuilderProfileItemFilter.FromSelectors(includedProfileItems);

        var rootObject = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
        if (rootObject is null || rootObject["items"] is not JsonArray items)
        {
            return UsbBuilderProfileFullManagedDownloadPlan.Empty;
        }

        var eligibleNames = new List<string>();
        var alreadyPresentNames = new List<string>();
        var missingNames = new List<string>();
        var excludedManualOrVendor = 0;
        var excludedBySafety = 0;
        var excludedByProfile = 0;
        long estimatedDownloadBytes = 0;
        var unknownSizeCount = 0;

        foreach (var node in items)
        {
            if (node is not JsonObject item)
            {
                continue;
            }

            var name = GetJsonString(item, "name");
            var type = GetJsonString(item, "type");
            var dest = GetJsonString(item, "dest");
            var manualOnly = GetJsonBool(item, "manualOnly");
            var enabled = !item.TryGetPropertyValue("enabled", out var enabledNode) ||
                          enabledNode is null ||
                          enabledNode.GetValueKind() != JsonValueKind.False;

            var categoryId = ClassifyCategoryId(item);
            if (!normalizedCategories.Contains(categoryId) || !itemFilter.Includes(item, categoryId))
            {
                excludedByProfile++;
                continue;
            }

            // Manual / vendor landing pages: everything that is not type=file or is explicitly manualOnly.
            if (!string.Equals(type, "file", StringComparison.OrdinalIgnoreCase) || manualOnly)
            {
                excludedManualOrVendor++;
                continue;
            }

            if (!enabled)
            {
                excludedBySafety++;
                continue;
            }

            // Checksum coverage is mandatory under require-for-release; an entry that loses it
            // counts as safety-excluded rather than eligible.
            var hasChecksum = !string.IsNullOrWhiteSpace(GetJsonString(item, "sha256")) ||
                              !string.IsNullOrWhiteSpace(GetJsonString(item, "sha256Url")) ||
                              !string.IsNullOrWhiteSpace(GetJsonString(item, "sha512")) ||
                              !string.IsNullOrWhiteSpace(GetJsonString(item, "sha512Url"));
            if (!hasChecksum)
            {
                excludedBySafety++;
                continue;
            }

            eligibleNames.Add(name);

            var alreadyPresent = FileExistsAtUsbDestination(usbRootPath, dest);
            if (alreadyPresent)
            {
                alreadyPresentNames.Add(name);
            }
            else
            {
                missingNames.Add(name);

                var size = ReadEstimatedSizeBytes(item);
                if (size.HasValue)
                {
                    estimatedDownloadBytes += size.Value;
                }
                else
                {
                    unknownSizeCount++;
                }
            }
        }

        return new UsbBuilderProfileFullManagedDownloadPlan
        {
            ManifestPath = manifestPath,
            EligibleManagedCount = eligibleNames.Count,
            AlreadyPresentCount = alreadyPresentNames.Count,
            MissingCount = missingNames.Count,
            ExcludedManualOrVendorCount = excludedManualOrVendor,
            ExcludedBySafetyCount = excludedBySafety,
            ExcludedByProfileCount = excludedByProfile,
            EstimatedDownloadBytes = estimatedDownloadBytes,
            UnknownSizeCount = unknownSizeCount,
            EligibleNames = eligibleNames,
            AlreadyPresentNames = alreadyPresentNames,
            MissingNames = missingNames,
            HasUsbRoot = !string.IsNullOrWhiteSpace(usbRootPath)
        };
    }

    public static string ClassifyCategoryId(JsonObject item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var explicitId = GetJsonString(item, "categoryId");
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId.Trim().ToLowerInvariant();
        }

        var dest = GetJsonString(item, "dest");
        var name = GetJsonString(item, "name");
        var family = GetJsonString(item, "family");
        return ClassifyCategoryIdForPath(dest, name, family);
    }

    public static string ClassifyCategoryIdForPath(string relativePath, string name, string family)
    {
        var path = (relativePath ?? string.Empty).Trim().Replace('/', '\\').ToLowerInvariant();
        var nameText = (name ?? string.Empty).Trim().ToLowerInvariant();
        var familyText = (family ?? string.Empty).Trim().ToLowerInvariant();

        if (path.Contains("ventoy") || nameText.Contains("ventoy")) { return "core"; }
        if (path.StartsWith("_apps\\forgerems", StringComparison.Ordinal) ||
            path.StartsWith("_docs\\forgerems", StringComparison.Ordinal) ||
            path.StartsWith("_logs\\forgerems", StringComparison.Ordinal) ||
            nameText.Contains("forgerems portable"))
        {
            return "forgerems-portable";
        }

        if (path.StartsWith("iso\\macos\\", StringComparison.Ordinal)) { return "macos"; }
        if (path.StartsWith("iso\\android\\", StringComparison.Ordinal) || path.StartsWith("tools\\android\\", StringComparison.Ordinal)) { return "android"; }
        if (path.StartsWith("iso\\ios-ipados\\", StringComparison.Ordinal) || path.StartsWith("tools\\apple-mobile\\", StringComparison.Ordinal)) { return "ios-ipados"; }
        if (path.StartsWith("drivers\\", StringComparison.Ordinal)) { return "oem-tools"; }
        if (path.StartsWith("iso\\windows-legacy\\", StringComparison.Ordinal)) { return "legacy-windows"; }
        if (path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows 8.1", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows 8", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows 7", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows vista", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows xp", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows 2000", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows me", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows 98", StringComparison.Ordinal) ||
            path.StartsWith("iso\\windows\\windows-manual-iso-drop\\windows 95", StringComparison.Ordinal))
        {
            return "legacy-windows";
        }
        if (path.StartsWith("iso\\windows\\", StringComparison.Ordinal) || familyText == "windows") { return "windows"; }
        // BSD/Other-Unix have no dedicated profile category; the backend routes them through
        // linux-rescue alongside Linux family entries.
        if (path.StartsWith("iso\\linux\\", StringComparison.Ordinal) || path.StartsWith("iso\\bsd\\", StringComparison.Ordinal) ||
            familyText == "linux" || familyText == "bsd") { return "linux-rescue"; }
        if (path.StartsWith("iso\\tools\\", StringComparison.Ordinal) ||
            path.StartsWith("tools\\portable\\", StringComparison.Ordinal) ||
            path.StartsWith("medicat.usb\\", StringComparison.Ordinal))
        {
            return "diagnostics";
        }

        return "diagnostics";
    }

    private static HashSet<string> NormalizeIncludedCategoryIds(IReadOnlySet<string> includedCategoryIds)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in includedCategoryIds)
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                set.Add(raw.Trim().ToLowerInvariant());
            }
        }

        // Core is always included by backend policy; mirror that here so a deselected core
        // doesn't quietly drop the Ventoy fallback from the eligible set.
        set.Add("core");
        return set;
    }

    private static bool FileExistsAtUsbDestination(string? usbRootPath, string destinationRelative)
    {
        if (string.IsNullOrWhiteSpace(usbRootPath) || string.IsNullOrWhiteSpace(destinationRelative))
        {
            return false;
        }

        try
        {
            var combined = Path.Combine(usbRootPath, destinationRelative.Replace('/', Path.DirectorySeparatorChar));
            return File.Exists(combined);
        }
        catch
        {
            return false;
        }
    }

    private static long? ReadEstimatedSizeBytes(JsonObject item)
    {
        // Catalog entries may publish an estimated size hint via "estimatedSizeBytes" or
        // similar; if absent, we report unknown rather than guess. Today the catalog does
        // not pin sizes, so this returns null almost always.
        if (item.TryGetPropertyValue("estimatedSizeBytes", out var node) && node is not null &&
            node.GetValueKind() == JsonValueKind.Number)
        {
            try
            {
                return node.GetValue<long>();
            }
            catch
            {
                return null;
            }
        }

        return null;
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
            _ => false
        };
    }

    private sealed class UsbBuilderProfileItemFilter
    {
        private readonly string[] _nameSelectors;
        private readonly string[] _destSelectors;

        private UsbBuilderProfileItemFilter(string[] nameSelectors, string[] destSelectors)
        {
            _nameSelectors = nameSelectors;
            _destSelectors = destSelectors;
        }

        private bool HasSelectors => _nameSelectors.Length > 0 || _destSelectors.Length > 0;

        public static UsbBuilderProfileItemFilter FromSelectors(IEnumerable<string>? selectors)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var raw in selectors ?? [])
            {
                foreach (var token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var trimmed = token.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    var separator = trimmed.IndexOf(':');
                    var prefix = separator > 0 ? trimmed[..separator].Trim() : "name";
                    var value = separator > 0 ? trimmed[(separator + 1)..].Trim() : trimmed;
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (prefix.Equals("dest", StringComparison.OrdinalIgnoreCase) ||
                        prefix.Equals("path", StringComparison.OrdinalIgnoreCase))
                    {
                        dests.Add(NormalizeSelectorPath(value));
                    }
                    else
                    {
                        names.Add(value.ToLowerInvariant());
                    }
                }
            }

            return new UsbBuilderProfileItemFilter(names.ToArray(), dests.ToArray());
        }

        public bool Includes(JsonObject item, string categoryId)
        {
            if (!HasSelectors || string.Equals(categoryId, "core", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var name = GetJsonString(item, "name").ToLowerInvariant();
            if (_nameSelectors.Any(selector =>
                    string.Equals(name, selector, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            var dest = NormalizeSelectorPath(GetJsonString(item, "dest"));
            return _destSelectors.Any(selector =>
                string.Equals(dest, selector, StringComparison.OrdinalIgnoreCase) ||
                dest.StartsWith(selector + "\\", StringComparison.OrdinalIgnoreCase) ||
                selector.StartsWith(dest + "\\", StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeSelectorPath(string value) =>
            value.Trim().Replace('/', '\\').Trim('\\').ToLowerInvariant();
    }
}
