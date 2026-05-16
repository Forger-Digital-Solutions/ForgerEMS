using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraProviderRoutingHardeningTests
{
    private sealed class FakeOnlineProvider(
        string id,
        string displayName,
        string envName) : ICopilotProvider
    {
        public string Id => id;
        public string DisplayName => displayName;
        public CopilotProviderType ProviderType => CopilotProviderType.GroqApi;
        public string Category => "Test";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "https://example.test";
        public string DefaultModelName => "fake";
        public string DefaultApiKeyEnvironmentVariable => envName;
        public string StatusText => "test";
        public bool IsConfigured(CopilotProviderConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult { Succeeded = true, UserMessage = "ok" });
    }

    private sealed class FakePaidProvider : ICopilotProvider
    {
        public string Id => "openai-compatible";
        public string DisplayName => "OpenAI Compatible (BYOK)";
        public CopilotProviderType ProviderType => CopilotProviderType.OpenAICompatible;
        public string Category => "BYOK";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => true;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "https://api.openai.com/v1";
        public string DefaultModelName => "gpt-4o-mini";
        public string DefaultApiKeyEnvironmentVariable => "OPENAI_API_KEY";
        public string StatusText => "test";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => false;
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult { Succeeded = true, UserMessage = "ok" });
    }

    private sealed class FakeLocalOfflineProvider : ICopilotProvider
    {
        public string Id => "local-offline";
        public string DisplayName => "Local Offline";
        public CopilotProviderType ProviderType => CopilotProviderType.LocalOffline;
        public string Category => "Local";
        public bool IsOnlineProvider => false;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => true;
        public string DefaultBaseUrl => string.Empty;
        public string DefaultModelName => string.Empty;
        public string DefaultApiKeyEnvironmentVariable => string.Empty;
        public string StatusText => "local";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => true;
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotProviderResult { Succeeded = true, UserMessage = "ok" });
    }

    [Fact]
    public void PaidProvider_HasRequiresSecretCapability()
    {
        var caps = KyraProviderCapabilityCatalog.ForProvider(new FakePaidProvider());
        Assert.True(caps.HasFlag(KyraProviderCapabilities.RequiresSecret));
    }

    [Fact]
    public void FreeOnlineProvider_DoesNotHaveRequiresSecretCapability()
    {
        var caps = KyraProviderCapabilityCatalog.ForProvider(new FakeOnlineProvider("groq-free", "Groq", "GROQ_API_KEY"));
        Assert.False(caps.HasFlag(KyraProviderCapabilities.RequiresSecret));
    }

    [Fact]
    public void LocalOfflineProvider_DoesNotHaveRequiresSecretCapability()
    {
        var caps = KyraProviderCapabilityCatalog.ForProvider(new FakeLocalOfflineProvider());
        Assert.False(caps.HasFlag(KyraProviderCapabilities.RequiresSecret));
    }

    [Fact]
    public void Gateway_CanHandle_False_ForGeneralChatWithoutTools()
    {
        var gateway = new KyraGatewayProvider();
        var ok = gateway.CanHandle(new CopilotProviderRequest
        {
            Prompt = "hello",
            Context = new CopilotContext { UserQuestion = "hello", Intent = KyraIntent.Unknown },
            Settings = new CopilotSettings(),
            ProviderConfiguration = new CopilotProviderConfiguration()
        });
        Assert.False(ok);
    }

    [Fact]
    public void Gateway_CanHandle_True_WhenCryptoIntent()
    {
        var gateway = new KyraGatewayProvider();
        var ok = gateway.CanHandle(new CopilotProviderRequest
        {
            Prompt = "btc",
            Context = new CopilotContext { UserQuestion = "btc", Intent = KyraIntent.CryptoPrice },
            Settings = new CopilotSettings(),
            ProviderConfiguration = new CopilotProviderConfiguration()
        });
        Assert.True(ok);
    }

    [Fact]
    public void ScoreProviders_SkipsProviderInCooldown()
    {
        const string env = "FORGEREMS_UT_KYRA_COOLDOWN_KEY";
        var tracker = new KyraProviderUsageTracker();
        var fake = new FakeOnlineProvider("groq-free", "Groq (Free Tier)", env);
        var settings = new CopilotSettings
        {
            Mode = CopilotMode.FreeApiPool,
            EnableFreeProviderPool = true,
            MaxProviderFallbacksPerMessage = 4
        };
        settings.Providers[fake.Id] = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            BaseUrl = fake.DefaultBaseUrl,
            ModelName = fake.DefaultModelName,
            ApiKeyEnvironmentVariable = env
        };

        try
        {
            Environment.SetEnvironmentVariable(env, "test-key-cooldown", EnvironmentVariableTarget.Process);
            var state = tracker.GetOrCreate(fake.Id);
            state.CooldownUntilUtc = DateTimeOffset.UtcNow.AddMinutes(2);

            var scored = KyraProviderRouter.ScoreProviders(
                [fake],
                new CopilotRequest { Prompt = "hi", Settings = settings },
                settings,
                new CopilotContext { UserQuestion = "hi", Intent = KyraIntent.GeneralTechQuestion },
                _ => settings.Providers[fake.Id],
                tracker);

            Assert.Empty(scored);

            var skipped = KyraProviderRouter.ExplainSkippedProviders(
                [fake],
                new CopilotRequest { Prompt = "hi", Settings = settings },
                settings,
                new CopilotContext { UserQuestion = "hi", Intent = KyraIntent.GeneralTechQuestion },
                _ => settings.Providers[fake.Id],
                tracker);

            Assert.Contains(skipped, s => s.Contains("cooling down", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Environment.SetEnvironmentVariable(env, null, EnvironmentVariableTarget.Process);
        }
    }
}
