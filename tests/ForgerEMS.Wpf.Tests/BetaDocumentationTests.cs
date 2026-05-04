using System;
using System.IO;

namespace ForgerEMS.Wpf.Tests;

public sealed class BetaDocumentationTests
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

    [Fact]
    public void BetaDocs_v1_1_11_Exist()
    {
        var root = RepoRoot;
        Assert.True(File.Exists(Path.Combine(root, "docs", "BETA_HUMAN_TESTING_CHECKLIST_v1.1.11.md")));
        Assert.True(File.Exists(Path.Combine(root, "docs", "MISSING_BEFORE_HUMAN_TESTING_v1.1.11.md")));
        Assert.True(File.Exists(Path.Combine(root, "docs", "BETA_TESTER_QUICKSTART.md")));
        Assert.True(File.Exists(Path.Combine(root, "docs", "BETA_ISSUE_REPORT_TEMPLATE.md")));
    }

    [Fact]
    public void LegalBundleDocs_Exist()
    {
        var root = RepoRoot;
        foreach (var name in new[]
                 {
                     "FAQ.md", "LEGAL.md", "PRIVACY.md", "THIRD_PARTY_NOTICES.md", "ABOUT_FORGEREMS.md"
                 })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", name)), $"Missing docs/{name}");
        }
    }

    [Fact]
    public void Faq_HasExpectedSections()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "docs", "FAQ.md"));
        Assert.Contains("SmartScreen", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CHECKSUMS", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Not measured", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USB mapping", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upload", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kyra", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Manual Required", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LOCALAPPDATA", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DOWNLOAD_TROUBLESHOOTING.md", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("FIRST_TESTER_DOWNLOAD_FLOW.md", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Readme_MentionsCurrentBetaAndFaq()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "README.md"));
        Assert.Contains("1.1.12-rc.3", text, StringComparison.Ordinal);
        Assert.Contains("docs/DOWNLOAD_TROUBLESHOOTING.md", text, StringComparison.Ordinal);
        Assert.Contains("docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md", text, StringComparison.Ordinal);
        Assert.Contains("docs/FAQ.md", text, StringComparison.Ordinal);
    }

    [Fact]
    public void MechanicalRcDocs_v1_1_12_Exist()
    {
        var root = RepoRoot;
        foreach (var name in new[]
                 {
                     "DOWNLOAD_TROUBLESHOOTING.md",
                     "FIRST_TESTER_DOWNLOAD_FLOW.md",
                     "KYRA_PROVIDER_ENVIRONMENT_SETUP.md",
                     "RELEASE_NOTES_v1.1.12-rc.1.md",
                     "RELEASE_NOTES_v1.1.12-rc.2.md",
                     "SCRIPT_AUDIT_v1.1.12-rc.1.md",
                     "UI_DUPLICATE_AUDIT_v1.1.12-rc.1.md",
                     "MECHANICAL_SMOKE_TEST_MATRIX_v1.1.12-rc.1.md",
                     "TOOLKIT_CONTENT_AUDIT_v1.1.12-rc.1.md",
                     "PERFORMANCE_AUDIT_v1.1.12-rc.1.md",
                     "SECURITY_PRIVACY_AUDIT_v1.1.12-rc.1.md",
                     "CI_RELEASE_AUDIT_v1.1.12-rc.1.md",
                     "BETA_RC_GO_NO_GO_v1.1.12-rc.1.md",
                     "BETA_RC_GO_NO_GO_v1.1.12-rc.2.md",
                     "GIT_CLEANLINESS_AUDIT_v1.1.12-rc.1.md"
                 })
        {
            Assert.True(File.Exists(Path.Combine(root, "docs", name)), $"Missing docs/{name}");
        }
    }

    [Fact]
    public void ReleaseNotes_v1_1_12_Rc2_RecommendsZipFirst()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "docs", "RELEASE_NOTES_v1.1.12-rc.2.md"));
        Assert.Contains("ForgerEMS-v1.1.12-rc.2.zip", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ForgerEMS-Beta-v1.1.12-rc.2.zip", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("START_HERE.bat", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Advanced", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraProvider_Setup_doc_covers_local_and_remote_topics()
    {
        var text = File.ReadAllText(Path.Combine(RepoRoot, "docs", "KYRA_PROVIDER_ENVIRONMENT_SETUP.md"));
        Assert.Contains("Offline", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LM Studio", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ollama", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OpenAI-compatible", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("setx", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Read-Host", text, StringComparison.OrdinalIgnoreCase);
    }
}
