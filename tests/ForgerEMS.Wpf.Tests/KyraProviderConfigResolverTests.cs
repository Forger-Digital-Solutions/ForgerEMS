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
    [InlineData("PASTE_YOUR_REAL_GITHUB_MODELS_PAT")]
    [InlineData("sample")]
    [InlineData("example")]
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

    [Fact]
    public void GitHubModelsDefaultModel_ComesFromConfiguredEnvironment()
    {
        var previous = Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, EnvironmentVariableTarget.Process);
        try
        {
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, "openai/gpt-5-test", EnvironmentVariableTarget.Process);

            var expected = FirstConfiguredValue(
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, EnvironmentVariableTarget.User),
                "openai/gpt-5-test",
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, EnvironmentVariableTarget.Machine),
                "gpt-4o-mini");

            var provider = new CopilotProviderRegistry().FindById("github-models");

            Assert.NotNull(provider);
            Assert.Equal(expected, provider!.DefaultModelName);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, previous, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void GitHubModelsConfig_ReadsAllModelEnvironmentSlots()
    {
        var previousDefault = Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, EnvironmentVariableTarget.Process);
        var previousFast = Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsFastModel, EnvironmentVariableTarget.Process);
        var previousAlt = Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsAltModel, EnvironmentVariableTarget.Process);
        try
        {
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, "openai/gpt-5-test", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsFastModel, "deepseek/fast-test", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsAltModel, "meta/alt-test", EnvironmentVariableTarget.Process);

            var config = GitHubModelsProviderConfig.FromEnvironment();

            Assert.Equal(FirstConfiguredValue(
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, EnvironmentVariableTarget.User),
                "openai/gpt-5-test",
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, EnvironmentVariableTarget.Machine)), config.DefaultModel);
            Assert.Equal(FirstConfiguredValue(
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsFastModel, EnvironmentVariableTarget.User),
                "deepseek/fast-test",
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsFastModel, EnvironmentVariableTarget.Machine)), config.FastModel);
            Assert.Equal(FirstConfiguredValue(
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsAltModel, EnvironmentVariableTarget.User),
                "meta/alt-test",
                Environment.GetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsAltModel, EnvironmentVariableTarget.Machine)), config.AltModel);
        }
        finally
        {
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel, previousDefault, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsFastModel, previousFast, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable(CopilotProviderEnvironmentVariableNames.GitHubModelsAltModel, previousAlt, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void GitHubModelsConfig_DefaultDoesNotSquashFastOrAlt()
    {
        var config = new GitHubModelsProviderConfig
        {
            DefaultModel = "openai/gpt-5",
            FastModel = "deepseek/DeepSeek-V3-0324",
            AltModel = "meta/Llama-4-Scout-17B-16E-Instruct"
        };

        Assert.Equal(3, config.ConfiguredModelsCount);
        Assert.Contains(config.ConfiguredModels, model => model.Route == GitHubModelRoute.Fast);
        Assert.Contains(config.ConfiguredModels, model => model.Route == GitHubModelRoute.Alt);
    }

    [Fact]
    public void GitHubModelsConfig_IgnoresPlaceholderModelValues()
    {
        var config = new GitHubModelsProviderConfig
        {
            DefaultModel = "YOUR_GITHUB_MODEL",
            FastModel = "PASTE_FAST_MODEL",
            AltModel = "meta/real-alt"
        };

        Assert.Single(config.ConfiguredModels);
        Assert.Equal("meta/real-alt", config.ConfiguredModels[0].ModelId);
    }

    [Fact]
    public void GitHubModelsRouteSelector_PicksFastForShortSimplePrompts()
    {
        var route = GitHubModelsRouteSelector.SelectRoute(new CopilotProviderRequest
        {
            Prompt = "what is RAM?",
            Context = new CopilotContext { Intent = KyraIntent.GeneralTechQuestion }
        });

        Assert.Equal(GitHubModelRoute.Fast, route);
    }

    [Fact]
    public void GitHubModelsRouteSelector_PicksDefaultForForgerEmsCodingAndDiagnostics()
    {
        var route = GitHubModelsRouteSelector.SelectRoute(new CopilotProviderRequest
        {
            Prompt = "ForgerEMS build failed in WPF, can you inspect this diagnostic error?",
            Context = new CopilotContext { Intent = KyraIntent.CodeAssist }
        });

        Assert.Equal(GitHubModelRoute.Default, route);
    }

    [Fact]
    public void GitHubModelsRouteSelector_PicksAltForLongContextSummariesAndCompare()
    {
        var route = GitHubModelsRouteSelector.SelectRoute(new CopilotProviderRequest
        {
            Prompt = "Please compare these logs and give me a second opinion.",
            Context = new CopilotContext { Intent = KyraIntent.GeneralTechQuestion }
        });

        Assert.Equal(GitHubModelRoute.Alt, route);
    }

    [Fact]
    public void GitHubModelsAttemptPlan_FallsBackDeterministicallyWhenSelectedRouteMissing()
    {
        var config = new GitHubModelsProviderConfig
        {
            FastModel = "fast-model",
            AltModel = "alt-model"
        };

        var plan = GitHubModelsRouteSelector.BuildAttemptPlan(config, GitHubModelRoute.Default);

        Assert.Collection(
            plan,
            item => Assert.Equal(GitHubModelRoute.Fast, item.Route),
            item => Assert.Equal(GitHubModelRoute.Alt, item.Route),
            item => Assert.Equal(GitHubModelRoute.Fallback, item.Route));
    }

    [Fact]
    public void GitHubModelsAttemptPlan_DoesNotRetryDuplicateModelIds()
    {
        var config = new GitHubModelsProviderConfig
        {
            DefaultModel = "same-model",
            FastModel = "same-model",
            AltModel = "alt-model",
            FallbackModel = "same-model"
        };

        var plan = GitHubModelsRouteSelector.BuildAttemptPlan(config, GitHubModelRoute.Default);

        Assert.Equal(2, plan.Count);
        Assert.Equal(["same-model", "alt-model"], plan.Select(item => item.ModelId).ToArray());
    }

    [Fact]
    public void GitHubModelsDiagnostics_DoNotIncludeToken()
    {
        var diagnostic = GitHubModelsRouteSelector.BuildSafeDiagnostic(GitHubModelRoute.Fast, "openai/gpt-5", fallbackUsed: false, configuredModelsCount: 3);

        Assert.DoesNotContain("secret-token", diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(CopilotProviderEnvironmentVariableNames.GitHubModels, diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("provider=github-models", diagnostic);
        Assert.Contains("model=openai/gpt-5", diagnostic);
    }

    [Fact]
    public void GitHubModelsAttemptPlan_UsesOldFallbackWhenNoModelEnvConfigured()
    {
        var config = new GitHubModelsProviderConfig();

        var plan = GitHubModelsRouteSelector.BuildAttemptPlan(config, GitHubModelRoute.Default);

        var only = Assert.Single(plan);
        Assert.Equal(GitHubModelRoute.Fallback, only.Route);
        Assert.Equal("gpt-4o-mini", only.ModelId);
    }

    private static string FirstConfiguredValue(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!KyraProviderConfigResolver.IsMissingOrPlaceholder(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
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
