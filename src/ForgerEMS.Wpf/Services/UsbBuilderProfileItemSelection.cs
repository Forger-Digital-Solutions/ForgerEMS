using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public static class UsbBuilderProfileItemSelection
{
    public static string[] BuildSelectedManifestSelectors(IEnumerable<UsbBuilderProfileOption> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var selectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in options.Where(o => o.IsIncluded))
        {
            foreach (var item in option.Items.Where(i => i.IsSelected))
            {
                AddSelector(selectors, "name", item.ManifestEntryName);
                AddSelector(selectors, "dest", item.UsbRelativePath);
            }
        }

        return selectors
            .OrderBy(selector => selector, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddSelector(HashSet<string> selectors, string prefix, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        selectors.Add($"{prefix}:{value.Trim()}");
    }
}
