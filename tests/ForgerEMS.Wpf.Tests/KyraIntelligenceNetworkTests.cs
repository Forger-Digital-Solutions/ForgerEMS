using System.Text.Json;
using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraIntelligenceNetworkTests
{
    [Fact]
    public void LocalRepairMemory_DefaultsOn_CommunityDefaultsOff()
    {
        var settings = new CopilotSettings();

        Assert.True(settings.KyraLocalRepairMemoryEnabled);
        Assert.False(settings.KyraCommunitySharingEnabled);
        Assert.False(settings.KyraShareResolvedIssueFixPatterns);
        Assert.False(settings.KyraShareHardwareCompatibilityPerformancePatterns);
        Assert.False(settings.KyraShareCrashErrorDiagnostics);
    }

    [Fact]
    public void DisabledLocalRepairMemory_PreventsNewWrites()
    {
        var path = TempJsonPath();
        var store = new KyraMachineMemoryStore(path);
        var wrote = store.TryAppend(new KyraMemoryEntry { IssueCategory = "USB target not detected" }, new KyraMemorySettings { LocalRepairMemoryEnabled = false });

        Assert.False(wrote);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteMemory_RemovesStoredEntries()
    {
        var path = TempJsonPath();
        var store = new KyraMachineMemoryStore(path);
        Assert.True(store.TryAppend(new KyraMemoryEntry { IssueCategory = "Battery health" }, new KyraMemorySettings()));

        Assert.True(File.Exists(path));
        store.Delete();
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ExportMemory_ContainsSanitizedEntriesOnly()
    {
        var path = TempJsonPath();
        var store = new KyraMachineMemoryStore(path);
        store.TryAppend(new KyraMemoryEntry
        {
            IssueCategory = "serial ABCDEFGH123456 and email test@example.com",
            SuggestedFix = "check C:\\Users\\Owner\\secret.txt and token sk-testtoken1234567890"
        }, new KyraMemorySettings());

        var json = store.ExportSanitized();

        Assert.Contains("[email redacted]", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path redacted", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("redacted", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("test@example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\Owner", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-testtoken", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Contact bob@example.com")]
    [InlineData("Path C:\\Users\\Alice\\Documents\\private.txt")]
    [InlineData("IP 192.168.1.50")]
    [InlineData("Key AAAAA-BBBBB-CCCCC-DDDDD-EEEEE")]
    [InlineData("token sk-abcdefghijklmnopqrstuvwxyz123456")]
    public void Sanitizer_RedactsSensitivePatterns(string input)
    {
        var sanitized = KyraMemorySanitizer.SanitizeText(input);

        Assert.DoesNotContain("bob@example.com", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\Alice", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.50", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AAAAA-BBBBB", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", sanitized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CodeFixPrompts_DoNotPullMachineMemory()
    {
        var profile = new KyraMachineMemoryProfile
        {
            Entries =
            [
                new KyraMemoryEntry
                {
                    IssueCategory = "Performance lag",
                    SuggestedFix = "Check battery and TPM"
                }
            ]
        };

        var summary = KyraMemorySummaryBuilder.BuildForPrompt(profile, """
            Fix this small code snippet:
            public int Add(int a, int b)
            {
                return a - b;
            }
            """);

        Assert.Null(summary);
    }

    [Fact]
    public void MachineQuestions_CanUseLocalRepairMemory()
    {
        var profile = new KyraMachineMemoryProfile
        {
            Entries =
            [
                new KyraMemoryEntry
                {
                    MachineClass = "Mobile Workstation",
                    HealthScoreBand = "Watch",
                    IssueCategory = "USB target not detected",
                    SuggestedFix = "Replug USB, wait for mount, select large removable data partition"
                }
            ]
        };

        var summary = KyraMemorySummaryBuilder.BuildForPrompt(profile, "What device are we working on?");

        Assert.NotNull(summary);
        Assert.Contains("USB target not detected", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local repair memory", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeclinedConsent_PreventsCommunityClientCalls()
    {
        var client = new DisabledKyraCommunityIntelligenceClient();
        var consent = new KyraCommunityConsentService();

        var submitted = await consent.TrySubmitAsync(
            client,
            new KyraCommunityDiagnosticEvent { IssueCategory = "USB target not detected" },
            new KyraMemorySettings { CommunitySharingEnabled = false },
            default);

        Assert.False(submitted);
        Assert.False(client.SubmitAttempted);
    }

    [Fact]
    public void ViewSharedPreview_UsesSanitizedPayloadOnly()
    {
        var profile = new KyraMachineMemoryProfile
        {
            Entries =
            [
                new KyraMemoryEntry
                {
                    HardwareCategorySummary = "C:\\Users\\Tech\\private.txt",
                    IssueCategory = "email user@example.com",
                    SuggestedFix = "token sk-abcdefghijklmnopqrstuvwxyz123456"
                }
            ]
        };

        var preview = KyraCommunityPayloadPreviewBuilder.BuildPreview(
            profile,
            new KyraMemorySettings { CommunitySharingEnabled = true },
            "1.0.0-beta",
            "beta");

        Assert.DoesNotContain("user@example.com", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\Tech", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", preview, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preview only", preview, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("How's BTC doing today?")]
    [InlineData("What is the current NVDA stock price?")]
    [InlineData("What's the latest Ventoy version?")]
    [InlineData("Any current Windows issues or CVEs today?")]
    public void ResearchMode_CurrentPrompts_RouteToLiveOnlineIntent(string prompt)
    {
        var intent = KyraIntentRouter.DetectIntent(prompt);
        Assert.Contains(intent, new[]
        {
            KyraIntent.LiveOnlineQuestion,
            KyraIntent.CryptoPrice,
            KyraIntent.StockPrice,
            KyraIntent.Weather,
            KyraIntent.News,
            KyraIntent.Sports
        });
    }

    [Fact]
    public void SettingsUi_ContainsKyraIntelligenceControls()
    {
        var xaml = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ForgerEMS.Wpf", "MainWindow.xaml"));

        Assert.Contains("Header=\"Kyra Assistant (Beta)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("KyraLocalRepairMemoryEnabled", xaml, StringComparison.Ordinal);
        Assert.Contains("KyraCommunitySharingEnabled", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewKyraCommunityPayloadPreviewCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ExportKyraIntelligenceMemoryCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("DeleteKyraIntelligenceMemoryCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Docs_ContainRequiredKyraIntelligencePrivacyWording()
    {
        var root = FindRepoRoot();
        var required = "ForgerEMS does not sell user data. Local Kyra Memory stays on this PC unless the user explicitly enables a future sharing option. Realtime Kyra Gateway sends only sanitized request context needed to answer current-data questions. Provider API keys are stored server-side and are not included in the desktop app. Anonymous Community Intelligence sharing is optional and off by default.";
        var docs = new[]
        {
            File.ReadAllText(Path.Combine(root, "README.md")),
            File.ReadAllText(Path.Combine(root, "docs", "PRIVACY.md")),
            File.ReadAllText(Path.Combine(root, "docs", "LEGAL.md")),
            File.ReadAllText(Path.Combine(root, "docs", "FAQ.md")),
            File.ReadAllText(Path.Combine(root, "docs", "ABOUT_FORGEREMS.md")),
            File.ReadAllText(Path.Combine(root, "docs", "KYRA_PROVIDER_ENVIRONMENT_SETUP.md")),
            File.ReadAllText(Path.Combine(root, "docs", "BETA_TESTER_QUICKSTART.md")),
            File.ReadAllText(Path.Combine(root, "KYRA_BEHAVIOR_SPEC.md")),
            File.ReadAllText(Path.Combine(root, "src", "ForgerEMS.Wpf", "Infrastructure", "InfoDocumentTexts.cs"))
        };

        foreach (var doc in docs)
        {
            Assert.Contains("Kyra Intelligence Network", doc, StringComparison.Ordinal);
            Assert.Contains(required, doc, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PreviewJson_DoesNotContainPrivateDocumentContent()
    {
        var profile = new KyraMachineMemoryProfile
        {
            Entries =
            [
                new KyraMemoryEntry
                {
                    IssueCategory = "private document says payroll secret",
                    SuggestedFix = "raw logs containing secrets"
                }
            ]
        };

        var preview = KyraCommunityPayloadPreviewBuilder.BuildPreview(profile, new KyraMemorySettings(), "1", "beta");
        using var doc = JsonDocument.Parse(preview);

        Assert.Equal("Local Only - community sharing is off.", doc.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("payroll", preview, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("raw logs", preview, StringComparison.OrdinalIgnoreCase);
    }

    private static string TempJsonPath() =>
        Path.Combine(Path.GetTempPath(), $"kyra-intelligence-{Guid.NewGuid():N}.json");

    public static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ForgerEMS.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? AppContext.BaseDirectory;
    }
}
