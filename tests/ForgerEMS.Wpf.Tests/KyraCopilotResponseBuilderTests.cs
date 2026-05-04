using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraCopilotResponseBuilderTests
{
    [Fact]
    public void Build_OnlineEnhancementUsed_SetsFooterFlag()
    {
        var online = new TestProvider(CopilotProviderType.GroqApi, online: true);
        var r = KyraCopilotResponseBuilder.Build(
            new CopilotProviderResult { Succeeded = true, UsedOnlineData = true, UserMessage = "Hello" },
            online,
            ["Gemini: OK"],
            "Hybrid",
            onlineEnhancementApplied: true);

        Assert.True(r.OnlineEnhancementApplied);
        var formatted = KyraResponseComposer.AppendEnhancementFooter(r.Text, r.OnlineEnhancementApplied);
        Assert.Contains("Enhanced with online", formatted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_LocalOnly_DoesNotSetFooterFlag()
    {
        var local = new TestProvider(CopilotProviderType.LocalOffline, online: false);
        var r = KyraCopilotResponseBuilder.Build(
            new CopilotProviderResult { Succeeded = true, UsedOnlineData = false, UserMessage = "Local" },
            local,
            [],
            "Offline",
            onlineEnhancementApplied: false);

        Assert.False(r.OnlineEnhancementApplied);
        Assert.Equal(KyraResponseComposer.AppendEnhancementFooter(r.Text, false), r.Text);
    }

    [Fact]
    public void Build_OnlineFlagButLocalProvider_NoFooter()
    {
        var local = new TestProvider(CopilotProviderType.LocalOffline, online: false);
        var r = KyraCopilotResponseBuilder.Build(
            new CopilotProviderResult { Succeeded = true, UsedOnlineData = false, UserMessage = "x" },
            local,
            [],
            "status",
            onlineEnhancementApplied: true);

        Assert.False(r.OnlineEnhancementApplied);
    }

    [Fact]
    public void Build_FallbackNotesStripFooterEvenIfCallerPassesTrue()
    {
        var local = new TestProvider(CopilotProviderType.LocalOffline, online: false);
        var r = KyraCopilotResponseBuilder.Build(
            new CopilotProviderResult { Succeeded = true, UsedOnlineData = false, UserMessage = "fallback text" },
            local,
            ["Gemini: failed timeout"],
            "status",
            onlineEnhancementApplied: true);

        Assert.True(r.FallbackUsed);
        Assert.False(r.OnlineEnhancementApplied);
    }

    private sealed class TestProvider : ICopilotProvider
    {
        public TestProvider(CopilotProviderType type, bool online)
        {
            ProviderType = type;
            IsOnlineProvider = online;
        }

        public string Id => "test";
        public string DisplayName => "Test";
        public string Category => "t";
        public CopilotProviderType ProviderType { get; }
        public bool IsOnlineProvider { get; }
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => true;
        public string DefaultBaseUrl => "";
        public string DefaultModelName => "";
        public string DefaultApiKeyEnvironmentVariable => "";
        public string StatusText => "";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => true;
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult());
    }
}
