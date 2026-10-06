using System;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class InfoDocumentTextsTests
{
    [Fact]
    public void BuildAbout_ContainsProductIdentityAndNoCopilotLabel()
    {
        var text = InfoDocumentTexts.BuildAbout("1.2.4", "ForgerEMS v1.2.4", "test-fe", "test-be");
        Assert.Contains("Forger Engineering Maintenance Suite", text, StringComparison.Ordinal);
        Assert.Contains("Forger Digital Solutions", text, StringComparison.Ordinal);
        Assert.Contains("docs/ENVIRONMENT.md", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Copilot", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kyra", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildFaq_BuildLegal_BuildPrivacy_AvoidCopilotBranding()
    {
        Assert.DoesNotContain("Copilot", InfoDocumentTexts.BuildFaq(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Copilot", InfoDocumentTexts.BuildLegal(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Copilot", InfoDocumentTexts.BuildPrivacy(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildLegal_DiscloseTechnicianAssistScopeAndNoGuaranteedOutcomes()
    {
        var legal = InfoDocumentTexts.BuildLegal();
        Assert.Contains("technician-assist", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a replacement", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guaranteed repair", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guaranteed data recovery", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guaranteed malware removal", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("guaranteed hardware diagnosis", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Unknown", legal, StringComparison.Ordinal);
        Assert.Contains("NotExposed", legal, StringComparison.Ordinal);
        Assert.Contains("review every bundle", legal, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not automatically upload", legal, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildPrivacy_ExcludesAssistantSharingCopy()
    {
        var privacy = InfoDocumentTexts.BuildPrivacy();
        Assert.DoesNotContain("Kyra", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gateway", privacy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AI provider", privacy, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildTermsOfService_IsPlainEnglishAndDoesNotOverpromise()
    {
        var terms = InfoDocumentTexts.BuildTermsOfService();
        Assert.Contains("utility", terms, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("responsible for", terms, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not guarantee", terms, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("diagnostics accuracy", terms, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data recovery", terms, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("driver safety", terms, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kyra", terms, StringComparison.Ordinal);
        Assert.DoesNotContain("Copilot", terms, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guaranteed", terms, StringComparison.OrdinalIgnoreCase);
    }
}
