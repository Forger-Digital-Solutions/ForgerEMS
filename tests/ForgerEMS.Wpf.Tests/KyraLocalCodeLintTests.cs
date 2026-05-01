using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraLocalCodeLintTests
{
    [Fact]
    public void JsonMissingComma_Reported()
    {
        var issues = KyraLocalCodeLint.AnalyzeSnippet("{ \"a\": 1 \"b\": 2 }", "json");
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void UnbalancedBraces()
    {
        var issues = KyraLocalCodeLint.AnalyzeSnippet("function x() { return 1; ", "csharp");
        Assert.Contains(issues, i => i.Contains("Brace", StringComparison.OrdinalIgnoreCase));
    }
}
