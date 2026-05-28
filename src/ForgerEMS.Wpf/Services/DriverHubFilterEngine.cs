using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public static class DriverHubFilterEngine
{
    public static IReadOnlyList<DriverHubEntryView> Filter(
        IEnumerable<DriverHubEntryView> entries,
        string? filter,
        string? searchText)
    {
        var normalizedFilter = string.IsNullOrWhiteSpace(filter) ? "All" : filter.Trim();
        var normalizedSearch = searchText?.Trim() ?? string.Empty;

        return entries
            .Where(entry => MatchesFilter(entry, normalizedFilter))
            .Where(entry => MatchesSearch(entry, normalizedSearch))
            .OrderByDescending(entry => entry.IsRecommended)
            .ThenBy(entry => DriverHubDisplay.FormatCategory(entry.Category), StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool MatchesFilter(DriverHubEntryView entry, string filter)
    {
        if (string.Equals(filter, "All", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(filter, "Recommended", StringComparison.OrdinalIgnoreCase))
        {
            return entry.IsRecommended;
        }

        if (string.Equals(filter, "GPU", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Category == DriverHubCategory.Gpu ||
                   HasPlatform(entry, DriverHubPlatform.Gpu) ||
                   HasTag(entry, "gpu") ||
                   HasTag(entry, "graphics");
        }

        if (string.Equals(filter, "OEM", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Category == DriverHubCategory.OemSupport ||
                   HasPlatform(entry, DriverHubPlatform.Oem) ||
                   HasTag(entry, "oem");
        }

        if (string.Equals(filter, "Network", StringComparison.OrdinalIgnoreCase))
        {
            return HasPlatform(entry, DriverHubPlatform.Network) ||
                   HasTag(entry, "network") ||
                   HasTag(entry, "wireless") ||
                   HasTag(entry, "ethernet");
        }

        if (string.Equals(filter, "Chipset", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Category == DriverHubCategory.ChipsetStorageNetwork ||
                   HasPlatform(entry, DriverHubPlatform.Chipset) ||
                   HasPlatform(entry, DriverHubPlatform.Storage) ||
                   HasTag(entry, "chipset") ||
                   HasTag(entry, "storage");
        }

        if (string.Equals(filter, "BIOS/Firmware", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Entry.IsFirmwareRelated ||
                   entry.Category == DriverHubCategory.BiosFirmware ||
                   HasPlatform(entry, DriverHubPlatform.BiosFirmware) ||
                   HasTag(entry, "bios") ||
                   HasTag(entry, "firmware");
        }

        if (string.Equals(filter, "Linux", StringComparison.OrdinalIgnoreCase))
        {
            return entry.Entry.IsLinuxGuidance ||
                   HasPlatform(entry, DriverHubPlatform.Linux) ||
                   HasTag(entry, "linux");
        }

        if (string.Equals(filter, "Windows", StringComparison.OrdinalIgnoreCase))
        {
            return HasPlatform(entry, DriverHubPlatform.Windows) ||
                   HasTag(entry, "windows");
        }

        return true;
    }

    private static bool MatchesSearch(DriverHubEntryView entry, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var haystack = string.Join(
            " ",
            entry.Name,
            entry.Vendor,
            entry.CategoryDisplayName,
            entry.Description,
            entry.PlatformBadgesText,
            entry.TagsText,
            entry.StatusLine);

        return haystack.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPlatform(DriverHubEntryView entry, DriverHubPlatform platform) =>
        entry.Entry.Platforms.Contains(platform);

    private static bool HasTag(DriverHubEntryView entry, string tag) =>
        entry.Entry.Tags.Any(item => item.Contains(tag, StringComparison.OrdinalIgnoreCase));
}
