using System.IO;
using System.Text;
using System.Text.Json;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class UsbHtmlDocumentationRequest
{
    public required string UsbRoot { get; init; }

    public required IReadOnlyList<UsbBuilderProfileOption> ProfileOptions { get; init; }

    public long? UsbFreeBytes { get; init; }

    public string AppVersion { get; init; } = AppReleaseInfo.DisplayVersion;

    public string SupportEmail { get; init; } = BetaSupportInfo.SupportEmail;
}

public sealed class UsbHtmlDocumentationGenerator
{
    public IReadOnlyList<string> GenerateAll(UsbHtmlDocumentationRequest request)
    {
        var written = new List<string>();
        var root = request.UsbRoot;
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "_docs"));
        Directory.CreateDirectory(Path.Combine(root, "_logs"));
        Directory.CreateDirectory(Path.Combine(root, "_reports"));

        WriteFile(Path.Combine(root, "README.html"), BuildDashboardHtml(request), written);
        WriteFile(
            Path.Combine(root, "START-HERE.html"),
            "<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><meta http-equiv=\"refresh\" content=\"0; url=README.html\"/><title>ForgerEMS USB</title></head><body><p><a href=\"README.html\">Open ForgerEMS USB dashboard</a></p></body></html>",
            written);
        WriteFile(Path.Combine(root, "_docs", "start-here.html"), BuildStartHereHtml(request), written);
        WriteFile(Path.Combine(root, "_docs", "manual-media-guide.html"), BuildManualMediaGuideHtml(request), written);
        WriteFile(Path.Combine(root, "_docs", "latest-updates.html"), BuildLatestUpdatesHtml(root), written);
        WriteFile(Path.Combine(root, "_reports", "index.html"), BuildReportsIndexHtml(root), written);
        WriteFile(Path.Combine(root, "_logs", "index.html"), BuildLogsIndexHtml(root), written);
        WriteFile(Path.Combine(root, "_docs", "forgerems-usb-dashboard.html"), BuildDashboardHtml(request), written);

        return written;
    }

    private static void WriteFile(string path, string html, ICollection<string> written)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, html, Encoding.UTF8);
        written.Add(path);
    }

    private static string BuildDashboardHtml(UsbHtmlDocumentationRequest request)
    {
        var totals = UsbBuilderProfileEstimateCalculator.CalculateTotals(request.ProfileOptions, request.UsbFreeBytes);
        var included = request.ProfileOptions.Where(o => o.IsIncluded).ToList();
        var packs = string.Join(", ", included.Select(o => UsbBuilderProfileCatalog.GetSummaryLabel(o.CategoryId)));
        var userSupplied = included.Where(o => o.RequiresManualMedia || o.PackStatus is UsbBuilderProfilePackStatus.UserSuppliedMedia or UsbBuilderProfilePackStatus.GuidedOfficialDownload).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB Dashboard"));
        sb.AppendLine("<header class=\"hero\">");
        sb.AppendLine("<h1>ForgerEMS Technician USB</h1>");
        sb.AppendLine($"<p class=\"muted\">Generated {UsbHtmlEscaper.Escape(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))} · App {UsbHtmlEscaper.Escape(request.AppVersion)}</p>");
        sb.AppendLine("</header>");

        sb.AppendLine("<section><h2>Profile summary</h2>");
        sb.AppendLine($"<p>This USB profile includes: <strong>{UsbHtmlEscaper.Escape(packs)}</strong>.</p>");
        sb.AppendLine($"<p>Estimated space: <strong>{UsbHtmlEscaper.Escape(totals.TypicalRangeDisplay)}</strong> typical ({UsbHtmlEscaper.Escape(totals.MinimumDisplay)} minimum) before user-supplied media.</p>");
        if (request.UsbFreeBytes is > 0)
        {
            sb.AppendLine($"<p>USB free space at generation: <strong>{UsbHtmlEscaper.Escape(UsbTargetInfo.FormatBytes(request.UsbFreeBytes.Value))}</strong>.</p>");
        }

        sb.AppendLine($"<p>{totals.UserSuppliedPackCount} pack(s) need user-supplied or guided official downloads. {totals.AutoOrGuidedPackCount} pack(s) can auto-download or use guided official sources.</p>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Quick links</h2><ul class=\"links\">");
        AppendLink(sb, "Start here", "_docs/start-here.html");
        AppendLink(sb, "Manual media guide", "_docs/manual-media-guide.html");
        AppendLink(sb, "Latest updates", "_docs/latest-updates.html");
        AppendLink(sb, "ISO folder", "ISO/");
        AppendLink(sb, "Tools", "Tools/");
        AppendLink(sb, "Drivers", "Drivers/");
        AppendLink(sb, "Docs", "_docs/");
        AppendLink(sb, "Logs", "_logs/index.html");
        AppendLink(sb, "Reports", "_reports/index.html");
        AppendLink(sb, "Markdown README", "README.md");
        sb.AppendLine("</ul></section>");

        sb.AppendLine("<section><h2>Selected packs</h2><table><thead><tr><th>Pack</th><th>Status</th><th>Space</th></tr></thead><tbody>");
        foreach (var option in included)
        {
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{UsbHtmlEscaper.Escape(option.DisplayName)}</td>");
            sb.AppendLine($"<td>{UsbHtmlEscaper.Escape(option.StatusChipText)}</td>");
            sb.AppendLine($"<td>{UsbHtmlEscaper.Escape(option.SpaceChipText)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table></section>");

        if (userSupplied.Count > 0)
        {
            sb.AppendLine("<section><h2>User-supplied media checklist</h2><ul>");
            foreach (var option in userSupplied)
            {
                sb.AppendLine($"<li><strong>{UsbHtmlEscaper.Escape(option.DisplayName)}</strong> — {UsbHtmlEscaper.Escape(option.ManualMediaExplanation)}</li>");
            }

            sb.AppendLine("</ul><p>See <a href=\"_docs/manual-media-guide.html\">manual media guide</a> for folder destinations.</p></section>");
        }

        sb.AppendLine("<section class=\"safety\"><h2>Safety notes</h2>");
        sb.AppendLine("<ul><li>Verify device model, backups, and licensing before imaging or flashing.</li>");
        sb.AppendLine("<li>ForgerEMS does not redistribute macOS, iOS IPSW, legacy Windows, or OEM firmware images.</li>");
        sb.AppendLine("<li>Use official vendor sources only. Do not use gray-market mirrors.</li></ul></section>");

        sb.AppendLine($"<footer><p>Support: <a href=\"mailto:{UsbHtmlEscaper.EscapeAttribute(request.SupportEmail)}\">{UsbHtmlEscaper.Escape(request.SupportEmail)}</a></p>");
        sb.AppendLine($"<p class=\"muted\">{UsbHtmlEscaper.Escape(BetaSupportInfo.DoNotEmailSecretsWarning)}</p></footer>");
        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static string BuildStartHereHtml(UsbHtmlDocumentationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Start Here"));
        sb.AppendLine("<h1>Start here</h1>");
        sb.AppendLine("<ol>");
        sb.AppendLine("<li>Open <a href=\"../README.html\">README.html</a> for the USB dashboard and profile summary.</li>");
        sb.AppendLine("<li>Review <a href=\"manual-media-guide.html\">manual media guide</a> for anything you must supply from official sources.</li>");
        sb.AppendLine("<li>Place user ISOs, IPSW, macOS installers, and OEM firmware only in the prepared drop folders.</li>");
        sb.AppendLine("<li>Run managed updates from ForgerEMS on a trusted PC to refresh catalog downloads.</li>");
        sb.AppendLine("<li>Check <a href=\"../_logs/index.html\">logs</a> and <a href=\"../_reports/index.html\">reports</a> after updates.</li>");
        sb.AppendLine("</ol>");
        sb.AppendLine($"<p>Selected packs: {UsbHtmlEscaper.Escape(string.Join(", ", request.ProfileOptions.Where(o => o.IsIncluded).Select(o => o.DisplayName)))}</p>");
        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static string BuildManualMediaGuideHtml(UsbHtmlDocumentationRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Manual Media Guide"));
        sb.AppendLine("<h1>Manual &amp; guided media guide</h1>");
        sb.AppendLine("<p>ForgerEMS prepares folders, official links, and HTML guides. Items that cannot be legally redistributed must be supplied by you from official sources.</p>");

        AppendGuideSection(sb, "Windows", "windows", "legacy-windows", new[]
        {
            ("Managed / current", "ISO\\Windows\\", "Official Microsoft download pages and catalog-managed items."),
            ("Legacy user-supplied", "ISO\\Windows\\Windows-Manual-ISO-Drop\\", "Windows 8.1 and older — supply licensed installation media only.")
        });

        AppendGuideSection(sb, "macOS", "macos", null, new[]
        {
            ("User-supplied installers", "ISO\\macOS\\macOS-Manual-Installer-Drop\\", "DMG, PKG, or app-created media from Apple workflows. Example: Sequoia\\Install macOS Sequoia.app")
        });

        AppendGuideSection(sb, "iOS / iPadOS", "ios-ipados", null, new[]
        {
            ("IPSW restore files", "ISO\\iOS-iPadOS\\iOS-Manual-IPSW-Drop\\iPhone\\", "Signed IPSW from official Apple restore workflows. Example: iPhone15,2_18.0_22A3354_Restore.ipsw")
        });

        AppendGuideSection(sb, "Android", "android", null, new[]
        {
            ("Platform tools", "Tools\\Android\\", "Official platform-tools when guided by catalog."),
            ("OEM firmware", "ISO\\Android\\Android-Manual-Firmware-Drop\\", "Verify model, bootloader, and carrier. Example: Google Pixel\\image.zip")
        });

        AppendGuideSection(sb, "OEM recovery", "oem-tools", null, new[]
        {
            ("Vendor links", "Tools\\Portable\\ and catalog shortcuts", "Dell, HP, Lenovo, ASUS, Acer, MSI, Microsoft Surface — use vendor support portals only.")
        });

        AppendGuideSection(sb, "Linux", "linux-rescue", null, new[]
        {
            ("Rescue ISOs", "ISO\\Linux\\ and ISO\\Tools\\", "Catalog-managed downloads from official projects when enabled.")
        });

        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static void AppendGuideSection(
        StringBuilder sb,
        string title,
        string primaryCategory,
        string? secondaryCategory,
        (string Label, string Folder, string Notes)[] rows)
    {
        sb.AppendLine($"<section><h2>{UsbHtmlEscaper.Escape(title)}</h2>");
        sb.AppendLine("<table><thead><tr><th>Workflow</th><th>Folder</th><th>Notes</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{UsbHtmlEscaper.Escape(row.Label)}</td>");
            sb.AppendLine($"<td><code>{UsbHtmlEscaper.Escape(row.Folder)}</code></td>");
            sb.AppendLine($"<td>{UsbHtmlEscaper.Escape(row.Notes)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");

        if (UsbBuilderProfileCatalog.TryGet(primaryCategory, out var definition))
        {
            sb.AppendLine($"<p class=\"note\"><strong>What ForgerEMS can do:</strong> {UsbHtmlEscaper.Escape(UsbBuilderProfileStatusResolver.ToAcquisitionChip(definition.DownloadMode))}. {UsbHtmlEscaper.Escape(definition.ManualMediaExplanation)}</p>");
        }

        sb.AppendLine("</section>");
    }

    private static string BuildLatestUpdatesHtml(string usbRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Latest Updates"));
        sb.AppendLine("<h1>Latest updates</h1>");

        var managed = Path.Combine(usbRoot, "ForgerEMS-managed-download-result.json");
        if (File.Exists(managed))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(managed));
                sb.AppendLine("<pre class=\"code\">");
                sb.AppendLine(UsbHtmlEscaper.Escape(JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true })));
                sb.AppendLine("</pre>");
                sb.AppendLine($"<p><a href=\"../ForgerEMS-managed-download-result.json\">Raw JSON</a></p>");
            }
            catch
            {
                sb.AppendLine("<p>Managed download result file is present but could not be parsed for display.</p>");
            }
        }
        else
        {
            sb.AppendLine("<p>No managed download result file found yet. Run Update USB from ForgerEMS.</p>");
        }

        var manifest = Path.Combine(usbRoot, "ForgerEMS.updates");
        if (File.Exists(manifest))
        {
            sb.AppendLine($"<p><a href=\"../ForgerEMS.updates\">Manifest</a> (ForgerEMS.updates)</p>");
        }

        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static string BuildLogsIndexHtml(string usbRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Logs"));
        sb.AppendLine("<h1>Logs</h1>");
        sb.AppendLine("<p class=\"note\">Logs may contain device paths and system details. They should not contain API keys or passwords. Review before sharing.</p>");

        AppendFileListing(sb, Path.Combine(usbRoot, "_logs"), "../_logs/");
        var managed = Path.Combine(usbRoot, "ForgerEMS-managed-download-result.json");
        if (File.Exists(managed))
        {
            sb.AppendLine("<h2>Managed download summary</h2>");
            sb.AppendLine($"<p><a href=\"../ForgerEMS-managed-download-result.json\">ForgerEMS-managed-download-result.json</a></p>");
        }

        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static string BuildReportsIndexHtml(string usbRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Reports"));
        sb.AppendLine("<h1>Reports</h1>");
        sb.AppendLine("<p>Generated reports and raw exports. Open Markdown or JSON files in a text editor when needed.</p>");
        AppendFileListing(sb, Path.Combine(usbRoot, "_reports"), "../_reports/");
        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static void AppendFileListing(StringBuilder sb, string directory, string linkPrefix)
    {
        if (!Directory.Exists(directory))
        {
            sb.AppendLine("<p>No files yet.</p>");
            return;
        }

        sb.AppendLine("<ul>");
        foreach (var file in Directory.EnumerateFiles(directory).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (string.Equals(name, "index.html", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            sb.AppendLine($"<li><a href=\"{UsbHtmlEscaper.EscapeAttribute(linkPrefix + name)}\">{UsbHtmlEscaper.Escape(name)}</a></li>");
        }

        sb.AppendLine("</ul>");
    }

    private static void AppendLink(StringBuilder sb, string label, string href) =>
        sb.AppendLine($"<li><a href=\"{UsbHtmlEscaper.EscapeAttribute(href)}\">{UsbHtmlEscaper.Escape(label)}</a></li>");

    private static class HtmlDocument
    {
        public static string Open(string title) =>
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\"/>\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>\n" +
            $"<title>{UsbHtmlEscaper.Escape(title)}</title>\n<style>\n{EmbeddedCss}\n</style>\n</head>\n<body>\n";

        public static string Close() => "</body>\n</html>\n";

        private const string EmbeddedCss = """
            :root { color-scheme: light dark; font-family: Segoe UI, system-ui, sans-serif; line-height: 1.5; }
            body { margin: 0 auto; max-width: 960px; padding: 1.25rem 1.5rem 2rem; background: #0f172a; color: #e2e8f0; }
            a { color: #7dd3fc; }
            h1, h2 { color: #f8fafc; }
            .hero { border-bottom: 1px solid #334155; margin-bottom: 1.5rem; padding-bottom: 1rem; }
            .muted { color: #94a3b8; font-size: 0.9rem; }
            .note { background: #1e293b; border-left: 4px solid #38bdf8; padding: 0.75rem 1rem; border-radius: 6px; }
            table { width: 100%; border-collapse: collapse; margin: 0.75rem 0; }
            th, td { border: 1px solid #334155; padding: 0.5rem 0.65rem; text-align: left; vertical-align: top; }
            th { background: #1e293b; }
            code { background: #1e293b; padding: 0.1rem 0.35rem; border-radius: 4px; }
            ul.links { columns: 2; gap: 1rem; }
            .safety { margin-top: 1.5rem; }
            pre.code { overflow: auto; background: #020617; padding: 1rem; border-radius: 8px; font-size: 0.85rem; }
            footer { margin-top: 2rem; border-top: 1px solid #334155; padding-top: 1rem; font-size: 0.9rem; }
            """;
    }
}
