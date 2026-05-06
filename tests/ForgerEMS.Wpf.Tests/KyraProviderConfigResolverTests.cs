using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraProviderConfigResolverTests
{
    [Theory]
    [InlineData("REPLACE_ME")]
    [InlineData("YOUR_OPENAI_API_KEY")]
    [InlineData("REPLACE_MODEL_NAME")]
    [InlineData("local-model-name")]
    [InlineData("model-name")]
    [InlineData("https://example.local/v1")]
    [InlineData("sk-REPLACE_ME")]
    [InlineData("changeme")]
    [InlineData("TODO")]
    public void PlaceholderValues_DoNotCountAsConfigured(string value)
    {
        Assert.True(KyraProviderConfigResolver.IsPlaceholderSecretOrValue(value));
    }

    [Fact]
    public void PlaceholderKey_IsSkippedByRouter()
    {
        var provider = new FakeProvider("fake-free", "Fake Free", CopilotProviderType.GroqApi, "FAKE_PLACEHOLDER_KEY");
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            BaseUrl = "https://example.test",
            ModelName = "fake-model",
            ApiKeyEnvironmentVariable = "FAKE_PLACEHOLDER_KEY"
        };

        try
        {
            Environment.SetEnvironmentVariable("FAKE_PLACEHOLDER_KEY", "YOUR_GROQ_API_KEY", EnvironmentVariableTarget.Process);
            var scored = KyraProviderRouter.ScoreProviders(
                [provider],
                new CopilotRequest { Prompt = "hello", Settings = settings },
                settings,
                new CopilotContext { Intent = KyraIntent.GeneralTechQuestion, UserQuestion = "hello" },
                p => settings.Providers[p.Id]);

            Assert.Empty(scored);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FAKE_PLACEHOLDER_KEY", null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void EmbeddedCredentialsInBaseUrl_AreRejected()
    {
        var provider = new FakeProvider("fake-free", "Fake Free", CopilotProviderType.GroqApi, "FAKE_KEY");
        var cfg = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            BaseUrl = "https://user:pass@example.com/v1",
            ModelName = "fake-model",
            ApiKeyEnvironmentVariable = "FAKE_KEY"
        };

        var resolved = KyraProviderConfigResolver.ResolveProvider(provider, cfg);

        Assert.Equal(KyraProviderEndpointState.EmbeddedCredentials, resolved.EndpointState);
        Assert.False(resolved.IsReady);
    }

    [Fact]
    public void CustomOpenRouter_CanUseOpenRouterGenericKeyFallback()
    {
        var provider = new OpenAiStyleCopilotProvider(
            "custom-openai-compatible",
            "Custom OpenAI-Compatible",
            CopilotProviderType.CustomOpenAICompatible,
            "Online/local AI",
            true,
            "https://openrouter.ai/api/v1",
            "openrouter/auto",
            "FORGEREMS_CUSTOM_PROVIDER_API_KEY",
            "custom");
        var cfg = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            BaseUrl = "https://openrouter.ai/api/v1",
            ModelName = "openrouter/auto",
            ApiKeyEnvironmentVariable = "FORGEREMS_CUSTOM_PROVIDER_API_KEY"
        };

        try
        {
            Environment.SetEnvironmentVariable("FORGEREMS_CUSTOM_PROVIDER_API_KEY", null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", "openrouter-test-key", EnvironmentVariableTarget.Process);

            var resolved = KyraProviderConfigResolver.ResolveProvider(provider, cfg);

            Assert.True(resolved.IsReady);
            Assert.False(string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(provider.Id, cfg)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("FORGEREMS_CUSTOM_PROVIDER_API_KEY", null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void CustomGroq_CanUseGroqGenericKeyFallback()
    {
        var cfg = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            BaseUrl = "https://api.groq.com/openai/v1",
            ModelName = "llama-3.1-8b-instant",
            ApiKeyEnvironmentVariable = "FORGEREMS_CUSTOM_PROVIDER_API_KEY"
        };

        try
        {
            Environment.SetEnvironmentVariable("FORGEREMS_CUSTOM_PROVIDER_API_KEY", null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("GROQ_API_KEY", "groq-test-key", EnvironmentVariableTarget.Process);

            Assert.False(string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey("custom-openai-compatible", cfg)));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GROQ_API_KEY", null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("FORGEREMS_CUSTOM_PROVIDER_API_KEY", null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void CurrentDataStatus_ReportsImplementedAndShellToolsHonestly()
    {
        var settings = new CopilotSettings
        {
            LiveTools = new KyraLiveToolsSettings
            {
                WeatherProvider = "openmeteo",
                StocksProvider = "alphavantage",
                StocksApiKey = "av-test-key",
                CryptoProvider = "coingecko",
                StatsProvider = "fred"
            }
        };

        var statuses = new KyraToolRegistry().BuildCurrentDataStatus(settings);

        Assert.Contains(statuses, item => item.ToolName == "Weather" && item.Implemented && item.SupportsNoKey);
        Assert.Contains(statuses, item => item.ToolName == "Finance / Stocks" && item.Implemented && item.Configured);
        Assert.Contains(statuses, item => item.ToolName == "Crypto" && item.Implemented && item.SupportsNoKey);
        Assert.Contains(statuses, item => item.ToolName == "Stats / Economic Data" && !item.Implemented);
    }

    private sealed class FakeProvider(
        string id,
        string displayName,
        CopilotProviderType type,
        string envName) : ICopilotProvider
    {
        public string Id => id;
        public string DisplayName => displayName;
        public CopilotProviderType ProviderType => type;
        public string Category => "Test";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "https://example.test";
        public string DefaultModelName => "fake-model";
        public string DefaultApiKeyEnvironmentVariable => envName;
        public string StatusText => "fake";
        public bool IsConfigured(CopilotProviderConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult { Succeeded = true, UsedOnlineData = true, UserMessage = "ok" });
    }
}
