namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// User-facing status lines for Settings → App updates (kept testable without loading MainViewModel).
/// </summary>
public static class UpdateCheckDisplay
{
    public static string FormatInstalledAlreadyLatest(string installedNormalizedLabel)
    {
        var v = FormatVersionPrefix(installedNormalizedLabel);
        return $"Already up to date. Installed version {v} is the latest available release.";
    }

    public static string FormatInstalledNewerThanPublic(string installedNormalizedLabel, string latestNormalizedLabel)
    {
        var i = FormatVersionPrefix(installedNormalizedLabel);
        var l = FormatVersionPrefix(latestNormalizedLabel);
        return $"You are running {i}, which is newer than the latest public release {l}.";
    }

    public static string FormatIgnoredVersion(string ignoredNormalizedLabel)
    {
        var v = FormatVersionPrefix(ignoredNormalizedLabel);
        return $"Updates to {v} are ignored. Clear the ignored version below to see prompts for it again.";
    }

    private static string FormatVersionPrefix(string normalizedLabel)
        => string.IsNullOrWhiteSpace(normalizedLabel) ? "v?" : $"v{normalizedLabel.Trim()}";
}
