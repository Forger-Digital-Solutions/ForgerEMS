using System;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class InfoDocumentTextsTests
{
    [Fact]
    public void BuildAbout_ContainsProductIdentityAndNoCopilotLabel()
    {
        var text = InfoDocumentTexts.BuildAbout("1.2.0-preview.1", "ForgerEMS v1.2.0 Public Preview", "test-fe", "test-be");
        Assert.Contains("Forger Engineering Maintenance Suite", text, StringComparison.Ordinal);
        Assert.Contains("Forger Digital Solutions", text, StringComparison.Ordinal);
        Assert.Contains("docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md", text, StringComparison.Ordinal);
        Assert.Contains("docs/ENVIRONMENT.md", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Copilot", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CopilotProviderEnvUx_mentions_kyra_setup_doc()
    {
        Assert.Contains("KYRA_PROVIDER_ENVIRONMENT_SETUP.md", CopilotProviderEnvironmentVariableNames.UxHowToConfigure, StringComparison.Ordinal);
        Assert.Contains("Offline Local", CopilotProviderEnvironmentVariableNames.UxHowToConfigure, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildFaq_BuildLegal_BuildPrivacy_AvoidCopilotBranding()
    {
        Assert.DoesNotContain("Copilot", InfoDocumentTexts.BuildFaq(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Copilot", InfoDocumentTexts.BuildLegal(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Copilot", InfoDocumentTexts.BuildPrivacy(), StringComparison.OrdinalIgnoreCase);
    }
}
