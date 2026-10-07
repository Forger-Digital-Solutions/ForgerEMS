using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

/// <summary>
/// Phase 2 release-packaging closure: shipped Markdown doc parity across the
/// three staging paths (csproj publish, build-release Copy-ReleaseDocs, Inno
/// [Files]), packaged relative-link resolution, obsolete checklist exclusion,
/// LGPL/licensing file presence, canonical manifest URLs, and the signing
/// helper / installer-mode fail-closed contracts.
/// </summary>
public sealed class ReleasePackagingClosureTests
{
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "ForgerEMS.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException("Could not locate ForgerEMS.sln from test base directory.");
        }
    }

    // The current shipped-doc set. RELEASE_NOTES_v<version>.md is versioned and
    // normalized separately. marketing/PUBLIC-FAQ.md preserves its subfolder.
    private static readonly string[] ExpectedDocs =
    {
        "ABOUT_FORGEREMS.md",
        "BETA_ISSUE_REPORT_TEMPLATE.md",
        "BETA_TESTER_QUICKSTART.md",
        "DOWNLOAD_TROUBLESHOOTING.md",
        "ENVIRONMENT.md",
        "FAQ.md",
        "FIRST_TESTER_DOWNLOAD_FLOW.md",
        "FORGER-DEEP-SENSOR-DRIVER-ROADMAP.md",
        "FORGER-SENSOR-STACK.md",
        "LEGAL.md",
        "LEGAL_NOTICES.md",
        "LINUX-WINE-COMPATIBILITY.md",
        "PRIVACY_AND_DATA_HANDLING.md",
        "SENSOR-LIMITATIONS.md",
        "TERMS_OF_USE.md",
        "THIRD-PARTY-SENSOR-NOTICES.md",
        "THIRD_PARTY_NOTICES.md",
        "UPDATE_SYSTEM.md",
        "USER_CONSENT_FLOW.md",
        "marketing/PUBLIC-FAQ.md",
    };

    private const string ObsoleteChecklist = "PUBLIC_PREVIEW_MANUAL_QA_v1.2.0-preview.1.md";

    private static string NormalizeDocName(string name)
    {
        var normalized = name.Replace('\\', '/');
        if (Regex.IsMatch(normalized, @"^RELEASE_NOTES_v", RegexOptions.IgnoreCase))
        {
            return "RELEASE_NOTES_v{VERSION}.md";
        }

        return normalized;
    }

    private static string[] ParseDocListFromBuildScript()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        var blockMatch = Regex.Match(
            text,
            @"\$docs\s*=\s*@\((?<body>.*?)\)",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);
        Assert.True(blockMatch.Success, "Could not locate the $docs list in build-release.ps1.");

        var entries = Regex.Matches(blockMatch.Groups["body"].Value, "\"([^\"]+\\.md)\"")
            .Select(m => m.Groups[1].Value)
            .Select(NormalizeDocName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // The marketing subfolder copy is separate from the $docs loop.
        if (Regex.IsMatch(text, @"docs\\marketing\\PUBLIC-FAQ\.md|docs/marketing/PUBLIC-FAQ\.md"))
        {
            entries = entries.Append("marketing/PUBLIC-FAQ.md")
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        return entries;
    }

    private static string[] ParseDocsFromCsproj()
    {
        var csproj = Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj");
        var xml = XDocument.Load(csproj);
        var ns = xml.Root?.GetDefaultNamespace() ?? XNamespace.None;
        return xml.Descendants(ns + "Content")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(v => v.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .Select(v => v.Replace("..\\", "").Replace("../", ""))
            .Select(v => v.StartsWith("docs\\", StringComparison.OrdinalIgnoreCase)
                ? v.Substring(5).Replace('\\', '/')
                : v.Replace('\\', '/'))
            .Where(v => !v.Equals("SECURITY.md", StringComparison.OrdinalIgnoreCase))
            .Select(NormalizeDocName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ParseDocsFromIss()
    {
        var iss = File.ReadAllText(Path.Combine(RepoRoot, "installer", "ForgerEMS.iss"));
        var entries = new List<string>();
        foreach (Match m in Regex.Matches(iss,
            @"Source:\s*""\.\.\\(?<src>[^""]+\.md)""\s*;\s*DestDir:\s*""(?<dest>[^""]+)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var src = m.Groups["src"].Value.Replace('\\', '/');
            var dest = m.Groups["dest"].Value;
            // Only docs-folder entries belong to the shipped doc set; root-level
            // SECURITY.md/README.md are checked separately.
            if (!dest.Equals("{app}\\docs", StringComparison.OrdinalIgnoreCase)
                && !dest.Equals("{app}\\docs\\marketing", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = Path.GetFileName(src.Replace('/', Path.DirectorySeparatorChar));
            entries.Add(dest.EndsWith("marketing", StringComparison.OrdinalIgnoreCase)
                ? "marketing/" + fileName
                : fileName);
        }

        return entries
            .Select(NormalizeDocName)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [Fact]
    public void ShippedDocSet_IsIdenticalAcrossCsprojBuildScriptAndIss()
    {
        var expected = ExpectedDocs
            .Append("RELEASE_NOTES_v{VERSION}.md")
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var fromCsproj = ParseDocsFromCsproj();
        var fromBuild = ParseDocListFromBuildScript();
        var fromIss = ParseDocsFromIss();

        Assert.Equal(expected, fromCsproj);
        Assert.Equal(expected, fromBuild);
        Assert.Equal(expected, fromIss);
    }

    [Fact]
    public void RootPackageDoc_Security_ShipsInAllPaths()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));
        var build = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        var iss = File.ReadAllText(Path.Combine(RepoRoot, "installer", "ForgerEMS.iss"));

        foreach (var rootDoc in new[] { "SECURITY.md" })
        {
            Assert.Contains(rootDoc, csproj, StringComparison.Ordinal);
            Assert.Contains(rootDoc, build, StringComparison.Ordinal);
            Assert.Contains(rootDoc, iss, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ObsoleteKyraQaChecklist_IsNotShipped()
    {
        var csproj = File.ReadAllText(Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "ForgerEMS.Wpf.csproj"));
        var build = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));
        var iss = File.ReadAllText(Path.Combine(RepoRoot, "installer", "ForgerEMS.iss"));
        var quickstart = File.ReadAllText(Path.Combine(RepoRoot, "docs", "BETA_TESTER_QUICKSTART.md"));

        Assert.DoesNotContain(ObsoleteChecklist, csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ObsoleteChecklist, build, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ObsoleteChecklist, iss, StringComparison.OrdinalIgnoreCase);
        // The only previous packaged reference was the quickstart link.
        Assert.DoesNotContain(ObsoleteChecklist, quickstart, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagedMarkdownLinks_AllRelativeTargetsResolve()
    {
        // Build the packaged file set: docs/<name> + root SECURITY.md/README.md.
        var packaged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in ExpectedDocs.Append("RELEASE_NOTES_v1.2.4.md"))
        {
            packaged.Add("docs/" + doc);
        }

        packaged.Add("SECURITY.md");

        var linkPattern = new Regex(@"\]\((?<target>[^)#\s]+)(#[^)]*)?\)", RegexOptions.CultureInvariant);
        var failures = new List<string>();

        foreach (var packagedPath in packaged)
        {
            var sourcePath = packagedPath.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(RepoRoot, packagedPath.Replace('/', Path.DirectorySeparatorChar))
                : Path.Combine(RepoRoot, packagedPath);
            if (!File.Exists(sourcePath))
            {
                failures.Add($"shipped doc missing from repo: {packagedPath}");
                continue;
            }

            var text = File.ReadAllText(sourcePath);
            var sourceDirInPackage = Path.GetDirectoryName(packagedPath)?.Replace('\\', '/') ?? string.Empty;

            foreach (Match match in linkPattern.Matches(text))
            {
                var target = match.Groups["target"].Value;
                if (target.Contains(":", StringComparison.Ordinal)
                    || target.StartsWith("//", StringComparison.Ordinal))
                {
                    continue; // absolute URI (https:, mailto:, etc.)
                }

                if (!target.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    continue; // only markdown targets are part of the packaged set
                }

                var resolved = string.IsNullOrEmpty(sourceDirInPackage)
                    ? target
                    : (sourceDirInPackage + "/" + target);
                // Normalize ../ segments.
                var segments = new Stack<string>();
                foreach (var seg in resolved.Split('/'))
                {
                    if (seg == ".." && segments.Count > 0) { segments.Pop(); }
                    else if (seg != "." && seg != "..") { segments.Push(seg); }
                }

                var normalized = string.Join("/", segments.Reverse());
                if (!packaged.Contains(normalized))
                {
                    failures.Add($"{packagedPath} -> {target} resolves to {normalized} which is not packaged");
                }
            }
        }

        Assert.True(failures.Count == 0, "Broken packaged markdown links: " + string.Join("; ", failures));
    }

    [Fact]
    public void LgplReplacementDoc_ExistsAndIsLinkedFromNotices()
    {
        var replacementDoc = Path.Combine(RepoRoot, "providers", "sensors", "REPLACING-LGPL-COMPONENTS.md");
        Assert.True(File.Exists(replacementDoc), "providers/sensors/REPLACING-LGPL-COMPONENTS.md must ship.");

        var text = File.ReadAllText(replacementDoc);
        Assert.Contains("3d331e3370efb858411f19511373eff65a218701", text, StringComparison.Ordinal);
        Assert.Contains("LibreHardwareMonitorLib.dll", text, StringComparison.Ordinal);
        Assert.Contains("providers\\sensors", text.Replace('/', '\\'), StringComparison.Ordinal);

        var notices = File.ReadAllText(Path.Combine(RepoRoot, "providers", "sensors", "THIRD-PARTY-NOTICES.txt"));
        Assert.Contains("REPLACING-LGPL-COMPONENTS.md", notices, StringComparison.Ordinal);

        var sensorNotices = File.ReadAllText(Path.Combine(RepoRoot, "docs", "THIRD-PARTY-SENSOR-NOTICES.md"));
        Assert.Contains("REPLACING-LGPL-COMPONENTS.md", sensorNotices, StringComparison.Ordinal);
    }

    [Fact]
    public void LicensingArtifacts_ArePresent()
    {
        var sensorsDir = Path.Combine(RepoRoot, "providers", "sensors");
        Assert.True(File.Exists(Path.Combine(sensorsDir, "LICENSES", "Mono-project-license.txt")),
            "Upstream Mono project license capture must ship.");
        Assert.True(File.Exists(Path.Combine(sensorsDir, "LICENSES", "Microsoft-Windows-SDK-license.rtf")),
            "The official Windows SDK license RTF must ship (authoritative RTF bytes, not the superseded HTML).");
        Assert.True(File.Exists(Path.Combine(sensorsDir, "REPLACING-LGPL-COMPONENTS.md")));
    }

    [Fact]
    public void ProductionManifest_UsesCanonicalReplacementUrls()
    {
        var manifest = File.ReadAllText(Path.Combine(RepoRoot, "manifests", "ForgerEMS.updates.json"));
        using var doc = JsonDocument.Parse(manifest);

        // Settled canonical replacements must be present.
        foreach (var canonical in new[]
                 {
                     "https://endeavouros.com/",
                     "https://www.emsisoft.com/en/emergency-kit/",
                     "https://learn.microsoft.com/en-us/surface/manage-surface-driver-and-firmware-updates",
                     "https://learn.microsoft.com/en-us/lifecycle/products/",
                     "https://www.sandisk.com/support",
                     "https://www.truenas.com/download/",
                     "https://docs.slackware.com/slackware:install",
                     "https://www.wagnardsoft.com/display-driver-uninstaller-ddu",
                     "https://www.msi.com/support"
                 })
        {
            Assert.Contains($"\"url\": \"{canonical}\"", manifest, StringComparison.Ordinal);
        }

        // The obsolete production URLs must be gone.
        foreach (var obsolete in new[]
                 {
                     "endeavouros.com/latest-release",
                     "emsisoft.com/en/home/emergency-kit",
                     "learn.microsoft.com/en-us/surface/surface-models-msi",
                     "learn.microsoft.com/lifecycle/products/windows-2000",
                     "westerndigital.com/support/sandisk",
                     "truenas.com/download-truenas-scale",
                     "truenas.com/download-truenas-community-edition",
                     "slackware.com/getslack",
                     "guru3d.com/download/display-driver-uninstaller-download",
                     "msi.com/support/Laptops",
                     "msi.com/support/Motherboards",
                     "msi.com/support/Graphics-Cards"
                 })
        {
            Assert.DoesNotContain(obsolete, manifest, StringComparison.OrdinalIgnoreCase);
        }

        // Manual/unsupported flags preserved on the fixed rows.
        var items = doc.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var slackware = items.Single(e => e.GetProperty("name").GetString() == "Slackware Download Page");
        Assert.True(slackware.GetProperty("manualOnly").GetBoolean());
        Assert.Equal("OfficialDownloadPage", slackware.GetProperty("downloadMode").GetString());

        var truenas = items.Single(e => e.GetProperty("name").GetString() == "TrueNAS SCALE Download Page");
        Assert.True(truenas.GetProperty("manualOnly").GetBoolean());

        var ddu = items.Single(e => e.GetProperty("name").GetString() == "DDU Download Page");
        Assert.True(ddu.GetProperty("manualOnly").GetBoolean());

        foreach (var msi in items.Where(e => e.GetProperty("name").GetString()?.StartsWith("MSI ", StringComparison.Ordinal) == true))
        {
            Assert.True(msi.GetProperty("manualOnly").GetBoolean());
        }
    }

    [Fact]
    public void ResourcePolicy_TrueNasSource_UsesCanonicalPage()
    {
        var policy = File.ReadAllText(Path.Combine(RepoRoot, "manifests", "resource-policy.json"));
        Assert.Contains("https://www.truenas.com/download/", policy, StringComparison.Ordinal);
        Assert.DoesNotContain("download-truenas-scale", policy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("download-truenas-community-edition", policy, StringComparison.OrdinalIgnoreCase);
    }

    // ---- signing infrastructure ----

    [Fact]
    public void SignReleaseArtifactHelper_HasRequiredContract()
    {
        var helper = File.ReadAllText(Path.Combine(RepoRoot, "tools", "sign-release-artifact.ps1"));

        Assert.Contains("^[0-9A-Fa-f]{40}$", helper, StringComparison.Ordinal);
        Assert.Contains("ValidateSet('CurrentUser', 'LocalMachine')", helper, StringComparison.Ordinal);
        Assert.Contains("'/sm'", helper, StringComparison.Ordinal);
        Assert.Contains("CertificateStore -eq 'LocalMachine'", helper, StringComparison.Ordinal);
        Assert.Contains("'/pa'", helper, StringComparison.Ordinal);
        Assert.Contains("'/tr'", helper, StringComparison.Ordinal); // RFC3161 timestamp
        Assert.Contains("'/fd'", helper, StringComparison.Ordinal);
        Assert.Contains("TimeStamperCertificate", helper, StringComparison.Ordinal);
        Assert.Contains("HasPrivateKey", helper, StringComparison.Ordinal);
        Assert.Contains("1.3.6.1.5.5.7.3.3", helper, StringComparison.Ordinal); // code-signing EKU
        Assert.Contains("OrdinalIgnoreCase", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("Export-PfxCertificate", helper, StringComparison.Ordinal); // never expose keys
    }

    [Fact]
    public void BuildRelease_RoutesSigningThroughHelper_AndGatesProduction()
    {
        var build = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));

        Assert.Contains("sign-release-artifact.ps1", build, StringComparison.Ordinal);
        Assert.Contains("ForgerEMSCertificateStore", build, StringComparison.Ordinal);
        Assert.Contains("/DRequireSigning=1", build, StringComparison.Ordinal);
        Assert.Contains("/DUnsignedCandidate=1", build, StringComparison.Ordinal);
        Assert.Contains("/DSignedUninstallerDir=", build, StringComparison.Ordinal);
        Assert.Contains("/SForgerEMSRelease=", build, StringComparison.Ordinal);
        Assert.Contains("-VerifyOnly", build, StringComparison.Ordinal);
        Assert.Contains("$dirtyCount -eq 0", build, StringComparison.Ordinal);
        Assert.Contains("requires a clean source tree", build, StringComparison.OrdinalIgnoreCase);
        // The build must not invoke signtool directly — every sign/verify call
        // goes through the helper so /sm, /pa, publisher, and timestamp gates
        // apply uniformly.
        Assert.DoesNotContain("& $signtool sign", build, StringComparison.Ordinal);
        Assert.DoesNotContain("& $signtool verify", build, StringComparison.Ordinal);
    }

    [Fact]
    public void SignToolCallback_PassesFileAsPath_WithoutDoubleQuoting()
    {
        var build = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));

        // Inno's $f expands to an already-quoted file name per the official
        // SignTool docs — wrapping it in $q would produce doubled quotes.
        Assert.Contains("-Path $f ", build, StringComparison.Ordinal);
        Assert.DoesNotContain("$q$f$q", build, StringComparison.Ordinal);
        Assert.DoesNotContain("$f$q", build, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedUninstallerCapture_ExactFilenames_NoOverwrite_NoDatEnumeration()
    {
        var helper = File.ReadAllText(Path.Combine(RepoRoot, "tools", "sign-release-artifact.ps1"));
        var build = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));

        // Inno 6.x signs uninst.e32.tmp/uninst.e64.tmp in SignedUninstallerDir
        // then deletes them — the helper captures a verified copy, which is the
        // only durable uninstaller artifact for our callback flow.
        Assert.Contains("uninst.e32.tmp", helper, StringComparison.Ordinal);
        Assert.Contains("uninst.e64.tmp", helper, StringComparison.Ordinal);
        Assert.Contains("VerifiedUninstallerPath", helper, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::Copy($artifact, $VerifiedUninstallerPath, $false)", helper, StringComparison.Ordinal);
        Assert.Contains("already exists - refusing to overwrite", helper, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("parent directory does not exist", helper, StringComparison.OrdinalIgnoreCase);

        // The callback passes the capture path through (quoted), and the
        // post-compile gate requires that exact file and verify-only checks it.
        Assert.Contains("-VerifiedUninstallerPath $q", build, StringComparison.Ordinal);
        Assert.Contains("verified-uninstaller.exe", build, StringComparison.Ordinal);
        Assert.Contains("Test-Path -LiteralPath $verifiedUninstallerPath", build, StringComparison.Ordinal);
        Assert.Contains("Invoke-ForgerEMSSign -Path $verifiedUninstallerPath -VerifyOnly", build, StringComparison.Ordinal);

        // The old *.dat cached-uninstaller enumeration can never succeed in
        // real signed mode — it must be gone entirely.
        Assert.DoesNotContain("*.dat", build, StringComparison.Ordinal);
        Assert.DoesNotContain("cachedUninstallers", build, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedUninstallerDir_NeverRemovesPreexistingDirectory()
    {
        var build = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));

        // The cached-uninstaller staging directory may be reused only when it is
        // absent or already empty — never wiped.
        Assert.DoesNotContain("Remove-Item -LiteralPath $signedUninstallerDir", build, StringComparison.Ordinal);
        Assert.Contains("already exists and is not empty", build, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildRelease_GitProvenance_FailsOnGitErrors()
    {
        var build = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-release.ps1"));

        // A failed git invocation must never look like a clean tree or a valid
        // HEAD — provenance metadata depends on it.
        Assert.Contains("^[0-9a-fA-F]{40}$", build, StringComparison.Ordinal);
        Assert.Contains("git rev-parse HEAD failed", build, StringComparison.Ordinal);
        Assert.Contains("git status failed", build, StringComparison.Ordinal);
        Assert.Contains("refusing to treat an unreadable source tree as clean", build, StringComparison.OrdinalIgnoreCase);
    }

    // ---- sidebar navigation accessibility ----

    private static readonly (string Name, string AutomationName, string Tag)[] ExpectedNavButtons =
    {
        ("NavUsbButton", "USB Builder", "0"),
        ("NavPortUsbIntelligenceButton", "Port / USB Intelligence", "1"),
        ("NavToolkitButton", "Toolkit Manager", "2"),
        ("NavDriverHubButton", "Driver Hub", "3"),
        ("NavSettingsButton", "Settings", "4"),
    };

    [Fact]
    public void SidebarNavStyle_IsKeyboardFocusable_WithVisibleFocusCue()
    {
        var xamlPath = Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var doc = XDocument.Load(xamlPath);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var style = doc.Descendants(wpf + "Style")
            .Single(s => (string?)s.Attribute(x + "Key") == "SidebarNavButtonStyle");

        var setters = style.Elements(wpf + "Setter").ToList();
        Assert.Contains(setters, s =>
            (string?)s.Attribute("Property") == "Focusable" &&
            (string?)s.Attribute("Value") == "True");
        Assert.Contains(setters, s =>
            (string?)s.Attribute("Property") == "FocusVisualStyle" &&
            (string?)s.Attribute("Value") == "{x:Null}");
        Assert.DoesNotContain(setters, s =>
            (string?)s.Attribute("Property") == "Focusable" &&
            (string?)s.Attribute("Value") == "False");

        var trigger = style.Descendants(wpf + "Trigger")
            .Single(t => (string?)t.Attribute("Property") == "IsKeyboardFocused" &&
                         (string?)t.Attribute("Value") == "True");
        var triggerSetters = trigger.Elements(wpf + "Setter").ToList();
        Assert.Contains(triggerSetters, s =>
            (string?)s.Attribute("TargetName") == "NavBorder" &&
            (string?)s.Attribute("Property") == "BorderThickness" &&
            (string?)s.Attribute("Value") == "2");
        Assert.Contains(triggerSetters, s =>
            (string?)s.Attribute("TargetName") == "NavBorder" &&
            (string?)s.Attribute("Property") == "BorderBrush" &&
            (string?)s.Attribute("Value") == "#FF57C7E8");
    }

    [Fact]
    public void SidebarNavButtons_HaveAutomationNames_AndSequentialTags()
    {
        var xamlPath = Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var doc = XDocument.Load(xamlPath);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var buttons = doc.Descendants(wpf + "Button")
            .Where(b => (string?)b.Attribute("Style") == "{StaticResource SidebarNavButtonStyle}")
            .ToList();
        Assert.Equal(5, buttons.Count);

        foreach (var (name, automationName, tag) in ExpectedNavButtons)
        {
            var button = buttons.Single(b => (string?)b.Attribute(x + "Name") == name);
            Assert.Equal(tag, (string?)button.Attribute("Tag"));
            Assert.Equal(automationName, (string?)button.Attribute("AutomationProperties.Name"));
        }

        // Tags must be sequential 0..4 for predictable navigation ordering.
        var tags = buttons.Select(b => (string?)b.Attribute("Tag")).OrderBy(t => t).ToArray();
        Assert.Equal(new[] { "0", "1", "2", "3", "4" }, tags);
    }

    [Fact]
    public void DriverHubOverflowToggles_HaveReadableAutomationName()
    {
        // The two icon-only '?' toggles inherit their accessible name from the style.
        var xamlPath = Path.Combine(RepoRoot, "src", "ForgerEMS.Wpf", "MainWindow.xaml");
        var doc = XDocument.Load(xamlPath);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var style = doc.Descendants(wpf + "Style")
            .Single(s => (string?)s.Attribute(x + "Key") == "DriverHubOverflowToggleStyle");
        Assert.Contains(style.Elements(wpf + "Setter"), s =>
            (string?)s.Attribute("Property") == "AutomationProperties.Name" &&
            (string?)s.Attribute("Value") == "More driver tool actions");

        var toggles = doc.Descendants(wpf + "ToggleButton")
            .Where(t => (string?)t.Attribute("Style") == "{StaticResource DriverHubOverflowToggleStyle}")
            .ToList();
        Assert.Equal(2, toggles.Count);
        Assert.All(toggles, t => Assert.Null(t.Attribute("AutomationProperties.Name"))); // inherit from style
    }

    [Fact]
    public void InnoScript_RequiresExactlyOneSigningMode()
    {
        var iss = File.ReadAllText(Path.Combine(RepoRoot, "installer", "ForgerEMS.iss"));

        Assert.Contains("defined(RequireSigning) && defined(UnsignedCandidate)", iss, StringComparison.Ordinal);
        Assert.Contains("!defined(RequireSigning) && !defined(UnsignedCandidate)", iss, StringComparison.Ordinal);
        Assert.Contains("SignTool=ForgerEMSRelease", iss, StringComparison.Ordinal);
        Assert.Contains("SignedUninstaller=yes", iss, StringComparison.Ordinal);
        Assert.Contains("SignedUninstallerDir=\"{#SignedUninstallerDir}\"", iss, StringComparison.Ordinal);
    }

    [Fact]
    public void AdjunctInstallerScript_FailsClosedWithoutUnsignedCandidate()
    {
        var adjunct = File.ReadAllText(Path.Combine(RepoRoot, "tools", "build-forgerems-installer.ps1"));

        Assert.Contains("-UnsignedCandidate", adjunct, StringComparison.Ordinal);
        Assert.Contains("/DUnsignedCandidate=1", adjunct, StringComparison.Ordinal);
        Assert.Contains("candidate.json", adjunct, StringComparison.Ordinal);
        Assert.Contains("build-release.ps1", adjunct, StringComparison.Ordinal);

        // Fail closed before any publish/staging when the flag is missing.
        var result = RunPowerShellRaw(
            $"& '{Path.Combine(RepoRoot, "tools", "build-forgerems-installer.ps1")}' -SkipPublish",
            expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("UnsignedCandidate", result.Error + result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRelease_FailsClosedWithoutUnsignedCandidateOrCredentials()
    {
        var result = RunPowerShellRaw(
            $"& '{Path.Combine(RepoRoot, "tools", "build-release.ps1")}' -DryRun",
            expectSuccess: false);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("fail closed", result.Error + result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StagePrerelease_ForwardsUnsignedCandidate_AndRefusesNonemptyDestination()
    {
        var stage = File.ReadAllText(Path.Combine(RepoRoot, "tools", "stage-prerelease.ps1"));

        Assert.Contains("-UnsignedCandidate:$UnsignedCandidate", stage, StringComparison.Ordinal);
        Assert.Contains("already exists and is not empty", stage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Remove-Item -Recurse -Force", stage, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $OutputRoot", stage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tools/build-release.ps1")]
    [InlineData("tools/build-forgerems-installer.ps1")]
    [InlineData("tools/stage-prerelease.ps1")]
    [InlineData("tools/sign-release-artifact.ps1")]
    [InlineData("tools/Initialize-ReleaseSigningCertificate.ps1")]
    [InlineData("tools/Test-ForgerEMSInstallerLifecycle.ps1")]
    public void TouchedPowerShellScripts_ParseCleanly(string relativePath)
    {
        var path = Path.Combine(RepoRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var result = RunPowerShellRaw(
            "$errors = $null; " +
            "$null = [System.Management.Automation.PSParser]::Tokenize(" +
            $"(Get-Content -LiteralPath '{PsQuote(path)}' -Raw), [ref]$errors); " +
            "if ($errors.Count -gt 0) { $errors | ForEach-Object { Write-Error $_.Message }; exit 1 }",
            expectSuccess: false);
        Assert.Equal(0, result.ExitCode);
    }

    [Fact]
    public void SigningInitializer_RefusesBeforeAnyCertificateWorkOutsideGithubHostedRunner()
    {
        var thumbprint = new string('0', 40);
        var result = RunPowerShellFile(
            Path.Combine(RepoRoot, "tools", "Initialize-ReleaseSigningCertificate.ps1"),
            new[] { "-CertificateThumbprint", thumbprint },
            new Dictionary<string, string?>
            {
                ["GITHUB_ACTIONS"] = "false",
                ["RUNNER_ENVIRONMENT"] = "self-hosted",
                ["FORGEREMS_SIGNING_PFX_BASE64"] = null,
                ["FORGEREMS_SIGNING_PFX_PASSWORD"] = null
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("restricted to a disposable GitHub-hosted", result.Output + result.Error, StringComparison.OrdinalIgnoreCase);

        var probe = RunPowerShellRaw(
            $"if (Test-Path 'Cert:\\CurrentUser\\My\\{thumbprint}') {{ 'present' }} else {{ 'absent' }}",
            expectSuccess: false);
        Assert.Contains("absent", probe.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void InstallerLifecycleHarness_RefusesBeforeCreatingEvidenceOutsideDisposableGuest()
    {
        var evidenceRoot = Path.Combine(Path.GetTempPath(), "forgerems-lifecycle-" + Guid.NewGuid().ToString("N"));
        var zero64 = new string('0', 64);
        var result = RunPowerShellFile(
            Path.Combine(RepoRoot, "tools", "Test-ForgerEMSInstallerLifecycle.ps1"),
            new[]
            {
                "-Phase", "CleanInstall",
                "-DisposableVmId", Guid.Empty.ToString(),
                "-CandidateInstaller", Path.Combine(Path.GetTempPath(), "nonexistent-candidate.exe"),
                "-CandidateSha256", zero64,
                "-PreviousInstaller", Path.Combine(Path.GetTempPath(), "nonexistent-previous.exe"),
                "-PreviousSha256", zero64,
                "-SourceHead", new string('0', 40),
                "-IsolationKind", "VirtualBox",
                "-EvidenceRoot", evidenceRoot
            },
            new Dictionary<string, string?>
            {
                ["GITHUB_ACTIONS"] = "false",
                ["RUNNER_ENVIRONMENT"] = "self-hosted"
            });

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Refusing installer execution outside the identified disposable Windows guest", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(evidenceRoot));
    }

    [Fact]
    public void InstallerLifecycleWorkflow_IsDispatchOnlyValidationWithNoPublishing()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "installer-lifecycle.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("push:", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("action-gh-release", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("upload-release-asset", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("releases/upload", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseWorkflow_ProvisionsAuthorizedIdentityInMemoryBeforeSignedBuild()
    {
        var workflow = File.ReadAllText(Path.Combine(RepoRoot, ".github", "workflows", "release.yml"));

        Assert.Contains("Initialize-ReleaseSigningCertificate.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: production-release", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.FORGEREMS_SIGNING_CERT_THUMBPRINT", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.FORGEREMS_SIGNING_PFX_BASE64", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.FORGEREMS_SIGNING_PFX_PASSWORD", workflow, StringComparison.Ordinal);
        Assert.Contains("-RequireSigning", workflow, StringComparison.Ordinal);
        var provisionIndex = workflow.IndexOf("Initialize-ReleaseSigningCertificate.ps1", StringComparison.Ordinal);
        var buildIndex = workflow.IndexOf("-RequireSigning", StringComparison.Ordinal);
        Assert.True(provisionIndex < buildIndex, "Identity provisioning must precede the signed build step.");
    }

    [Fact]
    public void SigningInitializer_ValidatesIdentityInMemoryAndNeverWritesKeyMaterial()
    {
        var script = File.ReadAllText(Path.Combine(RepoRoot, "tools", "Initialize-ReleaseSigningCertificate.ps1"));

        Assert.Contains("EphemeralKeySet", script, StringComparison.Ordinal);
        Assert.Contains("1.3.6.1.5.5.7.3.3", script, StringComparison.Ordinal); // Code Signing EKU
        Assert.Contains("NotBefore", script, StringComparison.Ordinal);
        Assert.Contains("NotAfter", script, StringComparison.Ordinal);
        Assert.Contains("ExpectedPublisher", script, StringComparison.Ordinal);
        Assert.Contains("[Array]::Clear($bytes", script, StringComparison.Ordinal);
        Assert.DoesNotContain("New-SelfSignedCertificate", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Export-PfxCertificate", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Export-Certificate", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Set-Content", script, StringComparison.Ordinal);
        Assert.DoesNotContain("Out-File", script, StringComparison.Ordinal);
    }

    private static (int ExitCode, string Output, string Error) RunPowerShellFile(
        string scriptPath,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?> environment)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = ResolvePowerShellExe(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        if (Path.GetFileName(startInfo.FileName).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var pair in environment)
        {
            if (pair.Value is null)
            {
                startInfo.Environment.Remove(pair.Key);
            }
            else
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000))
        {
            try { process.Kill(entireProcessTree: false); } catch { /* already exited */ }
            throw new TimeoutException($"PowerShell did not exit within 60 seconds: {scriptPath}");
        }

        return (process.ExitCode, outputTask.GetAwaiter().GetResult(), errorTask.GetAwaiter().GetResult());
    }

    private static (int ExitCode, string Output, string Error) RunPowerShellRaw(string command, bool expectSuccess)
    {
        var exe = ResolvePowerShellExe();
        var startInfo = new ProcessStartInfo
        {
            FileName = exe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-NoProfile");
        if (Path.GetFileName(exe).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }

        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start PowerShell.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(60000);

        if (expectSuccess && process.ExitCode != 0)
        {
            throw new InvalidOperationException($"PowerShell failed with exit {process.ExitCode}: {error}{output}");
        }

        return (process.ExitCode, output, error);
    }

    private static string ResolvePowerShellExe()
    {
        var psHome = Environment.GetEnvironmentVariable("PSHOME");
        if (!string.IsNullOrWhiteSpace(psHome))
        {
            var candidate = Path.Combine(psHome, "powershell.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "powershell.exe";
    }

    private static string PsQuote(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
