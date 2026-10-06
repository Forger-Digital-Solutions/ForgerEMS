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
            var metadata = s[(plus + 1)..];
            if (s.IndexOf('+', plus + 1) >= 0 ||
                !IsValidIdentifierList(metadata, rejectNumericLeadingZeros: false))
            {
                return false;
            }

            s = s[..plus];
        }

        string? prerelease = null;
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        string core;
        if (dash >= 0)
        {
            core = s[..dash];
            prerelease = s[(dash + 1)..];
            if (!IsValidIdentifierList(prerelease, rejectNumericLeadingZeros: true))
            {
                return false;
            }
        }
        else
        {
            core = s;
        }

        var parts = core.Split('.');
        if (parts.Length is < 3 or > 4)
        {
            return false;
        }

        if (!TryParseCoreNumber(parts[0], out var maj) ||
            !TryParseCoreNumber(parts[1], out var min) ||
            !TryParseCoreNumber(parts[2], out var pat))
        {
            return false;
        }

        var rev = 0;
        if (parts.Length == 4 && !TryParseCoreNumber(parts[3], out rev))
        {
            return false;
        }

        version = new AppSemanticVersion(maj, min, pat, rev, prerelease);
        return true;
    }

    /// <summary>Core numeric segment: ASCII digits only — no sign, whitespace, or non-ASCII digits.</summary>
    private static bool TryParseCoreNumber(string part, out int value)
    {
        value = 0;
        return IsAllDigits(part) &&
            int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>Dot-separated identifiers, each nonempty ASCII alnum/hyphen; optionally rejects numeric identifiers with leading zeros.</summary>
    private static bool IsValidIdentifierList(string value, bool rejectNumericLeadingZeros)
    {
        if (value.Length == 0)
        {
            return false;
        }

        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0)
            {
                return false;
            }

            var allDigits = true;
            foreach (var ch in identifier)
            {
                if (ch is >= '0' and <= '9')
                {
                    continue;
                }

                allDigits = false;
                if (ch is not ((>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or '-'))
                {
                    return false;
                }
            }

            if (rejectNumericLeadingZeros && allDigits && identifier.Length > 1 && identifier[0] == '0')
            {
                return false;
            }
        }

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
                var cmp = CompareNumericIdentifiers(ac, bc);
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

    /// <summary>Numeric order for digit strings of arbitrary length: normalized digit count first, then ordinal.</summary>
    private static int CompareNumericIdentifiers(string a, string b)
    {
        var an = a.TrimStart('0');
        var bn = b.TrimStart('0');
        var cmp = an.Length.CompareTo(bn.Length);
        return cmp != 0 ? cmp : string.CompareOrdinal(an, bn);
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

    public override bool Equals(object? obj) => obj is AppSemanticVersion other && CompareTo(other) == 0;
    public bool Equals(AppSemanticVersion other) => CompareTo(other) == 0;
    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Revision, NormalizedPrereleaseForHash());

    /// <summary>Prerelease canonicalized the way <see cref="CompareTo"/> sees it: empty identifiers dropped and numeric identifiers zero-stripped.</summary>
    private string? NormalizedPrereleaseForHash()
    {
        if (Prerelease is null)
        {
            return null;
        }

        var identifiers = Prerelease.Split('.', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < identifiers.Length; i++)
        {
            if (IsAllDigits(identifiers[i]))
            {
                identifiers[i] = identifiers[i].TrimStart('0');
            }
        }

        return string.Join('.', identifiers);
    }

    public static bool operator ==(AppSemanticVersion left, AppSemanticVersion right) => left.CompareTo(right) == 0;
    public static bool operator !=(AppSemanticVersion left, AppSemanticVersion right) => left.CompareTo(right) != 0;
    public static bool operator <(AppSemanticVersion left, AppSemanticVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(AppSemanticVersion left, AppSemanticVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(AppSemanticVersion left, AppSemanticVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AppSemanticVersion left, AppSemanticVersion right) => left.CompareTo(right) >= 0;
}
