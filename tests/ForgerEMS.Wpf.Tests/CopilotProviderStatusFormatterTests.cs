using System;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class CopilotProviderStatusFormatterTests
{
    private sealed class FakeProvider : ICopilotProvider
    {
        public FakeProvider(
            string id,
            string displayName,
            bool online,
            string statusText,
            string defaultEnv,
            Func<CopilotProviderConfiguration, bool> isConfigured)
        {
            Id = id;
            DisplayName = displayName;
            ProviderType = CopilotProviderType.GroqApi;
            Category = "Free API pool";
            IsOnlineProvider = online;
            IsPaidProvider = false;
            EnabledByDefault = false;
            DefaultBaseUrl = "https://example.invalid";
            DefaultModelName = "test-model";
            DefaultApiKeyEnvironmentVariable = defaultEnv;
            StatusText = statusText;
            _isConfigured = isConfigured;
        }

        private readonly Func<CopilotProviderConfiguration, bool> _isConfigured;

        public string Id { get; }
        public string DisplayName { get; }
        public CopilotProviderType ProviderType { get; }
        public string Category { get; }
        public bool IsOnlineProvider { get; }
        public bool IsPaidProvider { get; }
        public bool EnabledByDefault { get; }
        public string DefaultBaseUrl { get; }
        public string DefaultModelName { get; }
        public string DefaultApiKeyEnvironmentVariable { get; }
        public string StatusText { get; }

        public bool IsConfigured(CopilotProviderConfiguration configuration) => _isConfigured(configuration);

        public bool CanHandle(CopilotProviderRequest request) => true;

        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult());
    }

    [Fact]
    public void BuildStatusLabelMentionsEnvVarWhenMissingKey()
    {
        var provider = new FakeProvider("groq-free", "Groq", online: true, "Live API", "GROQ_API_KEY", _ => false);
        var cfg = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            BaseUrl = provider.DefaultBaseUrl,
            ModelName = provider.DefaultModelName,
            ApiKeyEnvironmentVariable = "GROQ_API_KEY"
        };

        var label = CopilotProviderStatusFormatter.BuildStatusLabel(provider, cfg);
        Assert.Contains("GROQ_API_KEY", label, StringComparison.Ordinal);
        Assert.Contains("key not found", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraGetOnlineSummaryAddsOptionalLineWhenNoOnline()
    {
        var text = KyraProviderStatusPresenter.GetOnlineSummary(false, false, false, false, false);
        Assert.Contains("Local Kyra is active", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("optional", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraGetOnlineSummaryOmitsOptionalLineWhenOnlineConfigured()
    {
        var text = KyraProviderStatusPresenter.GetOnlineSummary(false, false, false, false, true);
        Assert.DoesNotContain("Online providers are optional", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnthropicWithoutKey_ShowsNotConfigured()
    {
        var provider = new FakeProvider(
            "anthropic-claude",
            "Anthropic",
            online: true,
            "Adapter shell ready",
            "ANTHROPIC_API_KEY",
            _ => false);
        var cfg = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            ApiKeyEnvironmentVariable = "ANTHROPIC_API_KEY"
        };

        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null, EnvironmentVariableTarget.Process);
            KyraApiKeyStore.ClearSessionKey("anthropic-claude");
            var label = CopilotProviderStatusFormatter.BuildStatusLabel(provider, cfg);
            Assert.Contains("Not configured", label, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void CloudflareWithKeyButNoAccountId_ShowsNotUsableWhenAccountUnresolved()
    {
        if (ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source != KyraCredentialSource.None)
        {
            return;
        }

        var provider = new FakeProvider(
            "cloudflare-workers-ai",
            "Cloudflare Workers AI",
            online: true,
            "Cloudflare Workers AI",
            "CLOUDFLARE_API_KEY",
            _ => true);
        var cfg = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            ApiKeyEnvironmentVariable = "CLOUDFLARE_API_KEY"
        };

        try
        {
            Environment.SetEnvironmentVariable("CLOUDFLARE_API_KEY", "test-cloudflare-key", EnvironmentVariableTarget.Process);
            var label = CopilotProviderStatusFormatter.BuildStatusLabel(provider, cfg);
            Assert.Contains("Not usable", label, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("CLOUDFLARE_ACCOUNT_ID", label, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLOUDFLARE_API_KEY", null, EnvironmentVariableTarget.Process);
        }
    }
}
