using System.Collections.Generic;
using System.IO;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services;

public enum LinkSafetyBand
{
    LowConcern,
    Caution,
    HighRisk,
    Unknown
}

/// <summary>
/// Local URL heuristics only — not a malware verdict and not a substitute for antivirus or judgment.
/// </summary>
public static class LinkSafetyAnalyzer
{
    private static readonly HashSet<string> ShortenerHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "bit.ly", "tinyurl.com", "t.co", "goo.gl", "ow.ly", "buff.ly", "is.gd", "adf.ly", "cutt.ly", "rebrand.ly", "rb.gy"
    };

    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".msi", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jar", ".scr", ".com", ".pif", ".dll", ".app", ".deb", ".rpm", ".dmg", ".pkg"
    };

    public static LinkSafetyReport Analyze(string? rawInput)
    {
        var notes = new List<string>();
        var worst = LinkSafetyBand.LowConcern;

        void bump(LinkSafetyBand level, string note)
        {
            notes.Add(note);
            worst = MaxBand(worst, level);
        }

        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return new LinkSafetyReport(LinkSafetyBand.Unknown, ["Paste an http(s) URL to analyze."]);
        }

        var trimmed = rawInput.Trim();
        if (trimmed.Length > 4000)
        {
            return new LinkSafetyReport(LinkSafetyBand.Unknown, ["URL is too long to analyze safely in the UI."]);
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return new LinkSafetyReport(LinkSafetyBand.Unknown, ["Could not parse as an absolute URL. Include https:// or http://."]);
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            bump(LinkSafetyBand.HighRisk, $"Scheme \"{uri.Scheme}\" is not http/https. Treat as high risk.");
            return new LinkSafetyReport(worst, notes);
        }

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            bump(LinkSafetyBand.Caution, "HTTP (not HTTPS): traffic can be modified in transit. Prefer HTTPS downloads from the vendor.");
        }

        var host = uri.IdnHost;
        if (string.IsNullOrEmpty(host))
        {
            return new LinkSafetyReport(LinkSafetyBand.Unknown, ["Host name is missing."]);
        }

        if (host.StartsWith("xn--", StringComparison.OrdinalIgnoreCase) || host.Contains(".xn--", StringComparison.OrdinalIgnoreCase))
        {
            bump(LinkSafetyBand.Caution, "Punycode / IDN host: visually similar domains are sometimes used in phishing. Verify the spelling carefully.");
        }

        if (Uri.CheckHostName(host) == UriHostNameType.IPv4 || Uri.CheckHostName(host) == UriHostNameType.IPv6)
        {
            bump(LinkSafetyBand.Caution, "Numeric IP address instead of a normal hostname — sometimes used to bypass simple blocklists.");
        }

        var hostKey = TrimWww(host);
        if (ShortenerHosts.Contains(hostKey))
        {
            bump(LinkSafetyBand.Caution, "Known URL shortener: final destination is hidden until you follow the redirect. Prefer the vendor’s direct download page.");
        }

        var path = uri.AbsolutePath;
        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && ExecutableExtensions.Contains(ext))
        {
            bump(LinkSafetyBand.HighRisk, $"Path ends with executable-related extension \"{ext}\". Do not run unknown installers on your main machine.");
        }

        if (uri.Query.Length > 180)
        {
            bump(LinkSafetyBand.Caution, "Very long query string: can hide tokens or tracking parameters. Inspect before sharing.");
        }

        if (host.EndsWith(".ru", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".tk", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".ml", StringComparison.OrdinalIgnoreCase))
        {
            bump(LinkSafetyBand.Caution, "TLD is sometimes used by throwaway or unofficial mirrors — confirm you intended this domain.");
        }

        bump(LinkSafetyBand.LowConcern, "ForgerEMS can flag obvious risks, but it cannot guarantee a download is safe. Do not run unknown tools on your main machine.");

        return new LinkSafetyReport(worst, notes);
    }

    public static string FormatReport(LinkSafetyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Assessment: " + BandLabel(report.Band));
        sb.AppendLine();
        foreach (var line in report.Notes)
        {
            sb.AppendLine("• " + line);
        }

        return sb.ToString().TrimEnd();
    }

    public static string BandLabel(LinkSafetyBand band) => band switch
    {
        LinkSafetyBand.LowConcern => "Low concern (still verify manually)",
        LinkSafetyBand.Caution => "Caution",
        LinkSafetyBand.HighRisk => "High risk indicators",
        LinkSafetyBand.Unknown => "Unknown / could not classify",
        _ => "Unknown"
    };

    private static LinkSafetyBand MaxBand(LinkSafetyBand a, LinkSafetyBand b)
    {
        if (a == LinkSafetyBand.HighRisk || b == LinkSafetyBand.HighRisk)
        {
            return LinkSafetyBand.HighRisk;
        }

        if (a == LinkSafetyBand.Caution || b == LinkSafetyBand.Caution)
        {
            return LinkSafetyBand.Caution;
        }

        if (a == LinkSafetyBand.Unknown || b == LinkSafetyBand.Unknown)
        {
            return LinkSafetyBand.Unknown;
        }

        return LinkSafetyBand.LowConcern;
    }

    private static string TrimWww(string host)
    {
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}

public sealed class LinkSafetyReport
{
    public LinkSafetyReport(LinkSafetyBand band, IReadOnlyList<string> notes)
    {
        Band = band;
        Notes = notes;
    }

    public LinkSafetyBand Band { get; }

    public IReadOnlyList<string> Notes { get; }
}
