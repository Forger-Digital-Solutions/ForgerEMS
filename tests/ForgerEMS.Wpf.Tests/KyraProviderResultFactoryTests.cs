using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraProviderResultFactoryTests
{
    [Fact]
    public void FromLegacy_Success_Normalizes()
    {
        var legacy = new CopilotProviderResult
        {
            Succeeded = true,
            UsedOnlineData = true,
            UserMessage = "Hello from engine"
        };

        var r = KyraProviderResultFactory.FromLegacy(
            "groq-free",
            legacy,
            latencyMs: 42,
            usedContext: true,
            enhancementApplied: true,
            wasDiscarded: false,
            discardReason: string.Empty,
            requiresFallback: false);

        Assert.Equal("groq-free", r.ProviderId);
        Assert.True(r.Success);
        Assert.True(r.UsedContext);
        Assert.True(r.EnhancementApplied);
        Assert.False(r.WasDiscarded);
        Assert.Equal("None", r.ErrorCategory);
    }

    [Fact]
    public void FromLegacy_Failure_Normalizes()
    {
        var legacy = new CopilotProviderResult
        {
            Succeeded = false,
            FailureReason = KyraProviderFailureReason.Timeout,
            UserMessage = "Timed out"
        };

        var r = KyraProviderResultFactory.FromLegacy(
            "fake",
            legacy,
            500,
            usedContext: false,
            enhancementApplied: false,
            wasDiscarded: false,
            string.Empty,
            requiresFallback: true);

        Assert.False(r.Success);
        Assert.Equal(nameof(KyraProviderFailureReason.Timeout), r.ErrorCategory);
        Assert.True(r.RequiresFallback);
    }

    [Fact]
    public void FromLegacy_Discarded_RecordsReason()
    {
        var legacy = new CopilotProviderResult { Succeeded = true, UserMessage = "ignored", UsedOnlineData = true };
        var r = KyraProviderResultFactory.FromLegacy(
            "online",
            legacy,
            10,
            true,
            false,
            wasDiscarded: true,
            discardReason: "truth_guard",
            false);

        Assert.True(r.WasDiscarded);
        Assert.Equal("truth_guard", r.DiscardReason);
    }

    [Fact]
    public void KyraResponseComposer_BuildChatSourceLabel_NeverUsesVendorName()
    {
        var p = new FakeNamedProvider();
        Assert.Equal(KyraResponseComposer.KyraEnhancedLabel, KyraResponseComposer.BuildChatSourceLabel(p, onlineEnhancementApplied: true));
        Assert.Equal(KyraResponseComposer.KyraLocalModeLabel, KyraResponseComposer.BuildChatSourceLabel(p, onlineEnhancementApplied: false));
    }

    [Fact]
    public void MainViewModelStyleFooter_OnlyWhenEnhancementFlag()
    {
        var body = "Answer text.";
        var withFooter = KyraResponseComposer.AppendEnhancementFooter(body, onlineEnhancementApplied: true);
        var noFooter = KyraResponseComposer.AppendEnhancementFooter(body, onlineEnhancementApplied: false);
        Assert.Contains("Enhanced with online", withFooter, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(body, noFooter);
    }

    private sealed class FakeNamedProvider : ICopilotProvider
    {
        public string Id => "groq-free";
        public string DisplayName => "Groq";
        public string Category => "test";
        public CopilotProviderType ProviderType => CopilotProviderType.GroqApi;
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => true;
        public string DefaultBaseUrl => string.Empty;
        public string DefaultModelName => string.Empty;
        public string DefaultApiKeyEnvironmentVariable => string.Empty;
        public bool IsConfigured(CopilotProviderConfiguration configuration) => true;
        public string StatusText => string.Empty;
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult { Succeeded = true, UserMessage = "x" });
    }
}
