using System;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Infrastructure;

/// <summary>
/// Detects and removes the backend PowerShell <c>[yyyy-MM-dd HH:mm:ss][LEVEL]</c> prefix
/// the Update-ForgerEMS / Setup-Toolkit scripts emit via <c>Write-Log</c>. We strip the
/// date portion at the WPF log-append seam so Full Logs / Live Logs / session log files
/// do not show two timestamps on the same row.
/// </summary>
public static class BackendLogPrefix
{
    private static readonly Regex DateTimeLevelPrefix = new(
        @"^\s*\[\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\](\[(?:INFO|OK|WARN|ERROR|ACTION|INIT|COMPLETE|PROGRESS|Info|Success|Warning|Error)\])?\s?",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns <paramref name="text"/> with the leading <c>[yyyy-MM-dd HH:mm:ss]</c>
    /// removed when present. Any trailing <c>[LEVEL]</c> bracket is preserved on the
    /// message so downstream readers see e.g. <c>[OK] Managed tools ready: 50</c>.
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var match = DateTimeLevelPrefix.Match(text);
        if (!match.Success)
        {
            return text;
        }

        var levelBracket = match.Groups[1].Success ? match.Groups[1].Value : string.Empty;
        var remainder = text[match.Length..];
        if (levelBracket.Length == 0)
        {
            return remainder;
        }

        return remainder.Length == 0 ? levelBracket : levelBracket + " " + remainder;
    }

    /// <summary>Returns <see langword="true"/> when <paramref name="text"/> begins with the backend date+time prefix.</summary>
    public static bool HasDateTimePrefix(string text)
    {
        return !string.IsNullOrEmpty(text) && DateTimeLevelPrefix.IsMatch(text);
    }
}
