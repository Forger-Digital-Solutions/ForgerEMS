using System.Globalization;
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

public static class UsbHtmlDocumentationGenerator
{
    public static IReadOnlyList<string> GenerateAll(UsbHtmlDocumentationRequest request)
    {
        var written = new List<string>();
        var root = request.UsbRoot;
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "_docs"));
        Directory.CreateDirectory(Path.Combine(root, "_logs"));
        Directory.CreateDirectory(Path.Combine(root, "_reports"));

        UsbRootPolisher.Polish(root);

        WriteFile(Path.Combine(root, "README.html"), BuildDashboardHtml(request), written);
        WriteFile(Path.Combine(root, "_docs", "start-here.html"), BuildStartHereHtml(request), written);
        WriteFile(Path.Combine(root, "_docs", "manual-media-guide.html"), BuildManualMediaGuideHtml(request), written);
        WriteFile(Path.Combine(root, "_docs", "latest-updates.html"), BuildLatestUpdatesHtml(root), written);
        WriteFile(Path.Combine(root, "_reports", "index.html"), BuildReportsIndexHtml(root), written);
        WriteFile(Path.Combine(root, "_logs", "index.html"), BuildLogsIndexHtml(root), written);

        UsbRootPolisher.Polish(root);

        return written;
    }

    private static void WriteFile(string path, string html, List<string> written)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, html, Encoding.UTF8);
        written.Add(path);
    }

    private static void AppendInvariant(StringBuilder sb, FormattableString line) =>
        sb.AppendLine(line.ToString(CultureInfo.InvariantCulture));

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
        sb.AppendLine("<p class=\"lead\">Open this page first — your technician dashboard for this USB.</p>");
        AppendInvariant(sb, $"<p class=\"muted\">Generated {UsbHtmlEscaper.Escape(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))} · App {UsbHtmlEscaper.Escape(request.AppVersion)}</p>");
        sb.AppendLine("</header>");

        sb.AppendLine("<section><h2>Profile summary</h2>");
        AppendInvariant(sb, $"<p>This USB profile includes: <strong>{UsbHtmlEscaper.Escape(packs)}</strong>.</p>");
        AppendInvariant(sb, $"<p>Estimated space: <strong>{UsbHtmlEscaper.Escape(totals.TypicalRangeDisplay)}</strong> typical ({UsbHtmlEscaper.Escape(totals.MinimumDisplay)} minimum) before user-supplied media.</p>");
        if (request.UsbFreeBytes is > 0)
        {
            AppendInvariant(sb, $"<p>USB free space at generation: <strong>{UsbHtmlEscaper.Escape(UsbTargetInfo.FormatBytes(request.UsbFreeBytes.Value))}</strong>.</p>");
        }

        AppendInvariant(sb, $"<p>{totals.UserSuppliedPackCount} pack(s) need user-supplied or guided official downloads. {totals.AutoOrGuidedPackCount} pack(s) can auto-download or use guided official sources.</p>");
        sb.AppendLine("</section>");

        sb.AppendLine("<section><h2>Quick links</h2><ul class=\"links\">");
        AppendLink(sb, "ForgerEMS Portable App", "_apps/ForgerEMS/ForgerEMS.exe");
        AppendLink(sb, "ForgerEMS legal/help docs", "_docs/ForgerEMS/");
        AppendLink(sb, "Start here checklist", "_docs/start-here.html");
        AppendLink(sb, "Manual media guide", "_docs/manual-media-guide.html");
        AppendLink(sb, "Latest updates", "_docs/latest-updates.html");
        AppendLink(sb, "ISO folder", "ISO/");
        AppendLink(sb, "Tools", "Tools/");
        AppendLink(sb, "Drivers", "Drivers/");
        AppendLink(sb, "Logs dashboard", "_logs/index.html");
        AppendLink(sb, "Reports dashboard", "_reports/index.html");
        sb.AppendLine("</ul></section>");

        sb.AppendLine("<section><h2>Selected packs</h2><table><thead><tr><th>Pack</th><th>Status</th><th>Space</th></tr></thead><tbody>");
        foreach (var option in included)
        {
            sb.AppendLine("<tr>");
            AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(option.DisplayName)}</td>");
            AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(option.StatusChipText)}</td>");
            AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(option.SpaceChipText)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table></section>");

        if (userSupplied.Count > 0)
        {
            sb.AppendLine("<section><h2>User-supplied media checklist</h2><ul>");
            foreach (var option in userSupplied)
            {
                AppendInvariant(sb, $"<li><strong>{UsbHtmlEscaper.Escape(option.DisplayName)}</strong> — {UsbHtmlEscaper.Escape(option.ManualMediaExplanation)}</li>");
            }

            sb.AppendLine("</ul><p>See <a href=\"_docs/manual-media-guide.html\">manual media guide</a> for folder destinations.</p></section>");
        }

        sb.AppendLine("<section class=\"safety\"><h2>Safety notes</h2>");
        sb.AppendLine("<ul><li>Verify device model, backups, and licensing before imaging or flashing.</li>");
        sb.AppendLine("<li>Review ForgerEMS Terms, Privacy/Data Handling, Legal Notices, and third-party notices before using or sharing the portable app.</li>");
        sb.AppendLine("<li>ForgerEMS does not redistribute macOS, iOS IPSW, legacy Windows, or OEM firmware images.</li>");
        sb.AppendLine("<li>Use official vendor sources only. Do not use gray-market mirrors.</li></ul></section>");

        AppendInvariant(sb, $"<footer><p>Support: <a href=\"mailto:{UsbHtmlEscaper.EscapeAttribute(request.SupportEmail)}\">{UsbHtmlEscaper.Escape(request.SupportEmail)}</a></p>");
        AppendInvariant(sb, $"<p class=\"muted\">{UsbHtmlEscaper.Escape(BetaSupportInfo.DoNotEmailSecretsWarning)}</p></footer>");
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
        AppendInvariant(sb, $"<p>Selected packs: {UsbHtmlEscaper.Escape(string.Join(", ", request.ProfileOptions.Where(o => o.IsIncluded).Select(o => o.DisplayName)))}</p>");
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

        AppendGuideSection(sb, "ForgerEMS Portable App", "forgerems-portable", null, new[]
        {
            ("Portable app", "_apps\\ForgerEMS\\", "Runs ForgerEMS from the USB without installer registration when packaged files are present."),
            ("Legal/help docs", "_docs\\ForgerEMS\\", "Terms, Privacy/Data Handling, Legal Notices, FAQ, About, and third-party notices."),
            ("Support folders", "_logs\\ForgerEMS\\", "Local support/log folder. No automatic upload or sync is added.")
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
        AppendInvariant(sb, $"<section><h2>{UsbHtmlEscaper.Escape(title)}</h2>");
        sb.AppendLine("<table><thead><tr><th>Workflow</th><th>Folder</th><th>Notes</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.AppendLine("<tr>");
            AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(row.Label)}</td>");
            AppendInvariant(sb, $"<td><code>{UsbHtmlEscaper.Escape(row.Folder)}</code></td>");
            AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(row.Notes)}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody></table>");

        if (UsbBuilderProfileCatalog.TryGet(primaryCategory, out var definition))
        {
            AppendInvariant(sb, $"<p class=\"note\"><strong>What ForgerEMS can do:</strong> {UsbHtmlEscaper.Escape(UsbBuilderProfileStatusResolver.ToAcquisitionChip(definition.DownloadMode))}. {UsbHtmlEscaper.Escape(definition.ManualMediaExplanation)}</p>");
        }

        sb.AppendLine("</section>");
    }

    private static string BuildLatestUpdatesHtml(string usbRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Latest Updates"));
        sb.AppendLine("<h1>Latest updates</h1>");

        var managed = UsbInternalLayout.ResolveManagedDownloadResultPath(usbRoot);
        if (File.Exists(managed))
        {
            AppendManagedDownloadSummary(sb, managed, "../_forgerems/metadata/" + Path.GetFileName(managed));
        }
        else
        {
            sb.AppendLine("<p>No managed download result file found yet. Run Update USB from ForgerEMS.</p>");
        }

        var manifest = UsbInternalLayout.ResolveManifestPath(usbRoot);
        if (File.Exists(manifest))
        {
            sb.AppendLine("<h2>Manifest</h2>");
            AppendManifestSummary(sb, manifest);
        }

        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static void AppendManagedDownloadSummary(StringBuilder sb, string jsonPath, string relativeLink)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var root = doc.RootElement;
            sb.AppendLine("<table><tbody>");
            AppendSummaryRow(sb, "Readiness", GetJsonString(root, "readiness"));
            AppendSummaryRow(sb, "Started", GetJsonString(root, "startedAt"));
            AppendSummaryRow(sb, "Completed", GetJsonString(root, "completedAt"));
            AppendSummaryRow(sb, "Managed completed", GetJsonNumber(root, "managedCompleted"));
            AppendSummaryRow(sb, "Managed failed", GetJsonNumber(root, "managedFailed"));
            AppendSummaryRow(sb, "Manual/info shortcuts", GetJsonNumber(root, "manualInfoShortcuts"));
            sb.AppendLine("</tbody></table>");

            if (root.TryGetProperty("failedItems", out var failedItems) &&
                failedItems.ValueKind == JsonValueKind.Array &&
                failedItems.GetArrayLength() > 0)
            {
                sb.AppendLine("<h2>Failed managed items</h2><ul>");
                foreach (var item in failedItems.EnumerateArray())
                {
                    var name = GetJsonString(item, "name");
                    var reason = GetJsonString(item, "safeReason");
                    AppendInvariant(sb, $"<li><strong>{UsbHtmlEscaper.Escape(name)}</strong> — {UsbHtmlEscaper.Escape(reason)}</li>");
                }

                sb.AppendLine("</ul>");
            }

            AppendInvariant(sb, $"<p class=\"muted\">Support raw JSON: <a href=\"{UsbHtmlEscaper.EscapeAttribute(relativeLink)}\">{UsbHtmlEscaper.Escape(Path.GetFileName(jsonPath))}</a></p>");
        }
        catch
        {
            sb.AppendLine("<p>Managed download result file is present but could not be parsed for display.</p>");
        }
    }

    private static void AppendManifestSummary(StringBuilder sb, string manifestPath)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = doc.RootElement;
            sb.AppendLine("<table><tbody>");
            AppendSummaryRow(sb, "Core version", GetJsonString(root, "coreVersion"));
            AppendSummaryRow(sb, "Build", GetJsonString(root, "buildTimestampUtc"));
            AppendSummaryRow(sb, "Release", GetJsonString(root, "releaseType"));
            if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                AppendSummaryRow(sb, "Manifest items", items.GetArrayLength().ToString(CultureInfo.InvariantCulture));
            }

            sb.AppendLine("</tbody></table>");
        }
        catch
        {
            sb.AppendLine("<p>Manifest file is present but could not be parsed for display.</p>");
        }
    }

    private static void AppendSummaryRow(StringBuilder sb, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sb.AppendLine("<tr>");
        AppendInvariant(sb, $"<th>{UsbHtmlEscaper.Escape(label)}</th>");
        AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(value)}</td>");
        sb.AppendLine("</tr>");
    }

    private static string GetJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : string.Empty;

    private static string GetJsonNumber(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Number
            ? property.GetRawText()
            : string.Empty;

    private static string BuildLogsIndexHtml(string usbRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Logs"));
        sb.AppendLine("<h1>Logs</h1>");
        sb.AppendLine("<p class=\"note\">Logs may contain device paths and system details. They should not contain API keys or passwords. Review before sharing.</p>");

        AppendLatestRunSummary(sb, usbRoot);

        var internalLogs = UsbInternalLayout.RawLogsDirectory(usbRoot);
        sb.AppendLine("<h2>Recent setup/update logs</h2>");
        AppendInternalFileListing(sb, internalLogs, "../_forgerems/logs/");

        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static void AppendLatestRunSummary(StringBuilder sb, string usbRoot)
    {
        var internalLogs = UsbInternalLayout.RawLogsDirectory(usbRoot);
        var latestSetup = FindLatestMatchingLog(internalLogs, "setup_");
        var latestUpdate = FindLatestMatchingLog(internalLogs, "update_");

        if (latestSetup is null && latestUpdate is null)
        {
            sb.AppendLine("<p>No setup or update logs found yet.</p>");
            return;
        }

        sb.AppendLine("<table><thead><tr><th>Run</th><th>File</th><th>Updated</th></tr></thead><tbody>");
        if (latestSetup is not null)
        {
            AppendLogSummaryRow(sb, "Latest setup", latestSetup, "../_forgerems/logs/");
        }

        if (latestUpdate is not null)
        {
            AppendLogSummaryRow(sb, "Latest update", latestUpdate, "../_forgerems/logs/");
        }

        sb.AppendLine("</tbody></table>");
    }

    private static FileInfo? FindLatestMatchingLog(string directory, string prefix)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        return Directory.EnumerateFiles(directory, prefix + "*.log")
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static void AppendLogSummaryRow(StringBuilder sb, string label, FileInfo file, string linkPrefix)
    {
        sb.AppendLine("<tr>");
        AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(label)}</td>");
        AppendInvariant(sb, $"<td><a href=\"{UsbHtmlEscaper.EscapeAttribute(linkPrefix + file.Name)}\">{UsbHtmlEscaper.Escape(file.Name)}</a></td>");
        AppendInvariant(sb, $"<td>{UsbHtmlEscaper.Escape(file.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))} UTC</td>");
        sb.AppendLine("</tr>");
    }

    private static string BuildReportsIndexHtml(string usbRoot)
    {
        var sb = new StringBuilder();
        sb.AppendLine(HtmlDocument.Open("ForgerEMS USB — Reports"));
        sb.AppendLine("<h1>Reports</h1>");
        sb.AppendLine("<p>Support exports and raw report files are kept behind this dashboard. Open text or JSON files only when troubleshooting.</p>");

        var internalReports = UsbInternalLayout.RawReportsDirectory(usbRoot);
        AppendInternalFileListing(sb, internalReports, "../_forgerems/reports/");

        sb.AppendLine(HtmlDocument.Close());
        return sb.ToString();
    }

    private static void AppendInternalFileListing(StringBuilder sb, string directory, string linkPrefix)
    {
        if (!Directory.Exists(directory))
        {
            sb.AppendLine("<p>No files yet.</p>");
            return;
        }

        var files = Directory.EnumerateFiles(directory)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ToList();

        if (files.Count == 0)
        {
            sb.AppendLine("<p>No files yet.</p>");
            return;
        }

        sb.AppendLine("<ul>");
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            AppendInvariant(sb, $"<li><a href=\"{UsbHtmlEscaper.EscapeAttribute(linkPrefix + name)}\">{UsbHtmlEscaper.Escape(name)}</a></li>");
        }

        sb.AppendLine("</ul>");
    }

    private static void AppendLink(StringBuilder sb, string label, string href) =>
        AppendInvariant(sb, $"<li><a href=\"{UsbHtmlEscaper.EscapeAttribute(href)}\">{UsbHtmlEscaper.Escape(label)}</a></li>");

    private static class HtmlDocument
    {
        public static string Open(string title) =>
            "<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\"/>\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>\n" +
            FormattableString.Invariant($"<title>{UsbHtmlEscaper.Escape(title)}</title>\n<style>\n{EmbeddedCss}\n</style>\n</head>\n<body>\n");

        public static string Close() => "</body>\n</html>\n";

        private const string EmbeddedCss = """
            :root { color-scheme: light dark; font-family: Segoe UI, system-ui, sans-serif; line-height: 1.5; }
            body { margin: 0 auto; max-width: 960px; padding: 1.25rem 1.5rem 2rem; background: #0f172a; color: #e2e8f0; }
            a { color: #7dd3fc; }
            h1, h2 { color: #f8fafc; }
            .hero { border-bottom: 1px solid #334155; margin-bottom: 1.5rem; padding-bottom: 1rem; }
            .lead { font-size: 1.05rem; color: #cbd5e1; }
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
