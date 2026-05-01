using System;
using System.Globalization;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>
/// Semver-like version for update checks: supports v prefix, ForgerEMS- tag prefix,
/// optional 4th numeric segment (Windows file version), and prerelease identifiers.
/// </summary>
public readonly struct AppSemanticVersion : IComparable<AppSemanticVersion>
{
    public AppSemanticVersion(int major, int minor, int patch, int revision = 0, string? prerelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Revision = revision;
        Prerelease = prerelease;
    }

    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }
    public int Revision { get; }
    public string? Prerelease { get; }

    /// <summary>Core <see cref="Version"/> without prerelease (Revision included when &gt; 0).</summary>
    public Version ToLegacyVersion()
        => Revision > 0
            ? new Version(Major, Minor, Patch, Revision)
            : new Version(Major, Minor, Patch);

    public static bool TryParse(string? input, out AppSemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var s = input.Trim();
        while (s.StartsWith("ForgerEMS-", StringComparison.OrdinalIgnoreCase))
        {
            s = s["ForgerEMS-".Length..];
        }

        if (s.Length >= 1 && (s[0] == 'v' || s[0] == 'V'))
        {
            s = s[1..];
        }

        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            s = s[..plus];
        }

        string? prerelease = null;
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        string core;
        if (dash >= 0)
        {
            core = s[..dash];
            prerelease = s[(dash + 1)..];
            if (string.IsNullOrWhiteSpace(prerelease))
            {
                prerelease = null;
            }
        }
        else
        {
            core = s;
        }

        var parts = core.Split('.');
        if (parts.Length < 3)
        {
            return false;
        }

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var maj) ||
            !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var min) ||
            !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pat))
        {
            return false;
        }

        var rev = 0;
        if (parts.Length >= 4)
        {
            if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out rev))
            {
                return false;
            }
        }

        version = new AppSemanticVersion(maj, min, pat, rev, prerelease);
        return true;
    }

    public int CompareTo(AppSemanticVersion other)
    {
        var c = Major.CompareTo(other.Major);
        if (c != 0)
        {
            return c;
        }

        c = Minor.CompareTo(other.Minor);
        if (c != 0)
        {
            return c;
        }

        c = Patch.CompareTo(other.Patch);
        if (c != 0)
        {
            return c;
        }

        c = Revision.CompareTo(other.Revision);
        if (c != 0)
        {
            return c;
        }

        if (Prerelease is null && other.Prerelease is null)
        {
            return 0;
        }

        if (Prerelease is null)
        {
            return 1;
        }

        if (other.Prerelease is null)
        {
            return -1;
        }

        return ComparePrereleaseIdentifers(Prerelease, other.Prerelease);
    }

    private static int ComparePrereleaseIdentifers(string a, string b)
    {
        var ap = a.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var bp = b.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var len = Math.Max(ap.Length, bp.Length);
        for (var i = 0; i < len; i++)
        {
            if (i >= ap.Length)
            {
                return -1;
            }

            if (i >= bp.Length)
            {
                return 1;
            }

            var ac = ap[i];
            var bc = bp[i];
            var aNum = IsAllDigits(ac);
            var bNum = IsAllDigits(bc);
            if (aNum && bNum)
            {
                var an = int.Parse(ac, CultureInfo.InvariantCulture);
                var bn = int.Parse(bc, CultureInfo.InvariantCulture);
                var cmp = an.CompareTo(bn);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
            else if (aNum != bNum)
            {
                return aNum ? -1 : 1;
            }
            else
            {
                var cmp = string.CompareOrdinal(ac, bc);
                if (cmp != 0)
                {
                    return cmp;
                }
            }
        }

        return 0;
    }

    private static bool IsAllDigits(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return false;
        }

        foreach (var ch in s)
        {
            if (ch is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }
}
