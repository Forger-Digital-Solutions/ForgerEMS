namespace ForgerEMS.Wpf.Tests;

public sealed class KyraEnvConfigDetectorTests
{
    private static readonly string[] Aliases = ["kyra", "forgerems", "forger ems", "for you", "gateway", "provider", "beta"];

    [Fact]
    public void NullPrompt_ReturnsFalse()
    {
        Assert.False(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(null, Aliases));
    }

    [Fact]
    public void EmptyPrompt_ReturnsFalse()
    {
        Assert.False(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion("  ", Aliases));
    }

    [Fact]
    public void EnvVarPhraseWithAlias_ReturnsTrue()
    {
        Assert.True(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(
            "How do I set environment variables for kyra?", Aliases));
    }

    [Fact]
    public void EnvironmentVariablesWithGatewayAlias_ReturnsTrue()
    {
        Assert.True(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(
            "what environment variables do I need for the gateway?", Aliases));
    }

    [Fact]
    public void EnvVarAbbreviationWithAlias_ReturnsTrue()
    {
        Assert.True(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(
            "where do I put the env variable for the beta endpoint?", Aliases));
    }

    [Fact]
    public void EnvVarPhraseWithNoAliasMatch_ReturnsFalse()
    {
        Assert.False(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(
            "how do I set environment variables for git?", Aliases));
    }

    [Fact]
    public void NoEnvVarCue_ReturnsFalse()
    {
        Assert.False(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(
            "how do I configure the gateway url?", Aliases));
    }

    [Fact]
    public void EmptyAliasList_ReturnsFalse_EvenWithEnvVarCue()
    {
        Assert.False(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(
            "set environment variable for kyra", []));
    }

    [Fact]
    public void ProviderAlias_EnvVarContext_ReturnsTrue()
    {
        Assert.True(KyraEnvConfigDetector.LooksLikeWindowsEnvConfigurationQuestion(
            "which env var does the provider need?", Aliases));
    }
}
