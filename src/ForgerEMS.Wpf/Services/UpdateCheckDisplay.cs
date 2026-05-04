namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// User-facing status lines for Settings → App updates (kept testable without loading MainViewModel).
/// </summary>
public static class UpdateCheckDisplay
{
    public static string FormatInstalledAlreadyLatest(string installedNormalizedLabel)
    {
        var v = FormatVersionPrefix(installedNormalizedLabel);
        return $"You are on the latest selected channel release ({v}).";
    }

    public static string FormatInstalledNewerThanPublic(string installedNormalizedLabel, string latestNormalizedLabel)
    {
        var i = FormatVersionPrefix(installedNormalizedLabel);
        var l = FormatVersionPrefix(latestNormalizedLabel);
        return $"Installed build is newer than selected channel (installed {i}, latest on channel {l}).";
    }

    public static string FormatIgnoredVersion(string ignoredNormalizedLabel)
    {
        var v = FormatVersionPrefix(ignoredNormalizedLabel);
        return $"Updates to {v} are ignored. Clear the ignored version below to see prompts for it again.";
    }

    private static string FormatVersionPrefix(string normalizedLabel)
        => string.IsNullOrWhiteSpace(normalizedLabel) ? "v?" : $"v{normalizedLabel.Trim()}";
}
