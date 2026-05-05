using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

public static partial class UsbPortLabelNormalizer
{
    public static string NormalizeKey(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var normalized = label.Trim().ToLowerInvariant();
        normalized = SeparatorRegex().Replace(normalized, " ");
        normalized = SpaceRegex().Replace(normalized, " ");
        normalized = normalized.Replace("usb c", "usbc", StringComparison.Ordinal);
        normalized = normalized.Replace("usb a", "usba", StringComparison.Ordinal);
        normalized = normalized.Replace("usb b", "usbb", StringComparison.Ordinal);
        normalized = AlphaNumericOnlyRegex().Replace(normalized, string.Empty);
        return normalized;
    }

    public static string CanonicalizeDisplay(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        var cleaned = SeparatorRegex().Replace(label.Trim(), " ");
        cleaned = SpaceRegex().Replace(cleaned, " ");
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var output = new List<string>();

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            var lower = token.ToLowerInvariant();
            if (IsUsbToken(lower) && i + 1 < tokens.Length && IsUsbTypeToken(tokens[i + 1]))
            {
                output.Add("USB-" + tokens[i + 1].ToUpperInvariant());
                i++;
                continue;
            }

            output.Add(lower switch
            {
                "lt" => "LT",
                "rt" => "RT",
                "usb" => "USB",
                "usbc" => "USB-C",
                "usba" => "USB-A",
                "usbb" => "USB-B",
                _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower)
            });
        }

        return string.Join(' ', output);
    }

    public static bool NormalizeProfile(UsbMachineProfile profile)
    {
        var changed = false;
        foreach (var rec in profile.KnownPorts.Where(p => !string.IsNullOrWhiteSpace(p.UserLabel)))
        {
            if (string.IsNullOrWhiteSpace(rec.MappingId))
            {
                rec.MappingId = Guid.NewGuid().ToString("N");
                changed = true;
            }

            var key = NormalizeKey(rec.UserLabel);
            if (!string.Equals(rec.NormalizedLabelKey, key, StringComparison.Ordinal))
            {
                rec.NormalizedLabelKey = key;
                changed = true;
            }

            var display = CanonicalizeDisplay(rec.UserLabel);
            if (!string.IsNullOrWhiteSpace(display) &&
                !string.Equals(rec.UserLabel, display, StringComparison.Ordinal))
            {
                rec.UserLabel = display;
                changed = true;
            }
        }

        var duplicateGroups = profile.KnownPorts
            .Where(p => !string.IsNullOrWhiteSpace(p.NormalizedLabelKey))
            .GroupBy(p => p.NormalizedLabelKey, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var ordered = group
                .OrderByDescending(p => p.LastManualLabelConnectionEpoch)
                .ThenByDescending(p => p.UpdatedUtc ?? p.LastSeenUtc ?? p.CreatedUtc ?? DateTimeOffset.MinValue)
                .ToList();
            var primary = ordered[0];
            foreach (var duplicate in ordered.Skip(1))
            {
                MergeDuplicate(primary, duplicate);
                profile.KnownPorts.Remove(duplicate);
                changed = true;
            }

            IntelligenceLogWriter.Append(
                "usb-intelligence.log",
                $"usbPortLabelDuplicateMerged normalizedKeyHash={UsbIdentityHasher.ShortKey(UsbIdentityHasher.Sha256Hex(primary.NormalizedLabelKey))} label=\"{primary.UserLabel}\" mergedCount={ordered.Count - 1}");
        }

        return changed;
    }

    private static void MergeDuplicate(UsbKnownPortRecord primary, UsbKnownPortRecord duplicate)
    {
        primary.CreatedUtc = Earliest(primary.CreatedUtc, duplicate.CreatedUtc);
        primary.UpdatedUtc = Latest(primary.UpdatedUtc, duplicate.UpdatedUtc);
        primary.LastSeenUtc = Latest(primary.LastSeenUtc, duplicate.LastSeenUtc);
        primary.Confidence = Math.Max(primary.Confidence, duplicate.Confidence);
        primary.MappingConfidenceScore = Math.Max(primary.MappingConfidenceScore, duplicate.MappingConfidenceScore);

        if (primary.LastBenchmark is null ||
            duplicate.LastBenchmark?.WriteSpeedMBps > primary.LastBenchmark.WriteSpeedMBps)
        {
            primary.LastBenchmark = duplicate.LastBenchmark ?? primary.LastBenchmark;
        }

        if (string.IsNullOrWhiteSpace(primary.StablePortKey))
        {
            primary.StablePortKey = duplicate.StablePortKey;
        }

        if (string.IsNullOrWhiteSpace(primary.DeviceIdentityKey))
        {
            primary.DeviceIdentityKey = duplicate.DeviceIdentityKey;
        }

        if (string.IsNullOrWhiteSpace(primary.PortTopologyKey))
        {
            primary.PortTopologyKey = duplicate.PortTopologyKey;
        }

        primary.HasStrongPortTopologyEvidence |= duplicate.HasStrongPortTopologyEvidence;
    }

    private static DateTimeOffset? Earliest(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null ? first : first < second ? first : second;

    private static DateTimeOffset? Latest(DateTimeOffset? first, DateTimeOffset? second) =>
        first is null ? second : second is null ? first : first > second ? first : second;

    private static bool IsUsbToken(string lower) => lower is "usb";

    private static bool IsUsbTypeToken(string token) =>
        token.Length == 1 && token[0] is 'a' or 'A' or 'b' or 'B' or 'c' or 'C';

    [GeneratedRegex(@"[-_]+", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex AlphaNumericOnlyRegex();
}
