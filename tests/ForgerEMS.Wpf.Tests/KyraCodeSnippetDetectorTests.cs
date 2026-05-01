using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraCodeSnippetDetectorTests
{
    [Fact]
    public void GuessLanguageHint_JsonFence()
    {
        var p = "```json\n{}\n```";
        Assert.Equal("JSON", KyraCodeSnippetDetector.GuessLanguageHint(p));
    }

    [Fact]
    public void LooksLikeCodeSnippet_PowerShellCmdlet()
    {
        Assert.True(KyraCodeSnippetDetector.LooksLikeCodeSnippet("Get-ChildItem -Path C:\\temp"));
    }
}
