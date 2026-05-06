using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;
using Xunit;

namespace ForgerEMS.Wpf.Tests;

public sealed class KyraSoloUpgradeTests
{
    [Fact]
    public void Memory_DefaultKeepsOneHundredTurns()
    {
        var memory = new KyraConversationMemory();
        for (var i = 0; i < 125; i++)
        {
            memory.AddTurn($"question {i}", $"answer {i}", KyraIntent.GeneralTechQuestion, new SystemContext());
        }

        Assert.Equal(100, memory.Snapshot().Count);
        Assert.Equal(200, memory.ToChatMessages().Length);
    }

    [Fact]
    public void Memory_ClampAllowsOneToTwoHundredTurns()
    {
        var tiny = new KyraConversationMemory(0);
        tiny.AddTurn("one", "one", KyraIntent.Unknown, new SystemContext());
        tiny.AddTurn("two", "two", KyraIntent.Unknown, new SystemContext());
        Assert.Single(tiny.Snapshot());

        var large = new KyraConversationMemory(500);
        for (var i = 0; i < 225; i++)
        {
            large.AddTurn($"q{i}", $"a{i}", KyraIntent.Unknown, new SystemContext());
        }

        Assert.Equal(200, large.Snapshot().Count);
    }

    [Fact]
    public void ProviderRouter_SkipsMissingKeysAndHonorsPriority()
    {
        var originals = CaptureEnv(
            "FAKE_GROQ_KEY",
            "FAKE_GEMINI_KEY",
            "GROQ_API_KEY",
            "GEMINI_API_KEY",
            "OPENROUTER_API_KEY",
            "FORGEREMS_OPENAI_API_KEY",
            "OPENAI_API_KEY",
            "CEREBRAS_API_KEY",
            "MISTRAL_API_KEY",
            "GITHUB_MODELS_TOKEN",
            "CLOUDFLARE_API_KEY");
        KyraApiKeyStore.SetSessionKey("groq-free", "groq-test-key");
        KyraApiKeyStore.SetSessionKey("gemini-free", "gemini-test-key");
        Environment.SetEnvironmentVariable("FAKE_GROQ_KEY", "groq-test-key", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("FAKE_GEMINI_KEY", "gemini-test-key", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("GROQ_API_KEY", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("GEMINI_API_KEY", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("OPENROUTER_API_KEY", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("FORGEREMS_OPENAI_API_KEY", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("CEREBRAS_API_KEY", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("MISTRAL_API_KEY", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("GITHUB_MODELS_TOKEN", null, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("CLOUDFLARE_API_KEY", null, EnvironmentVariableTarget.Process);
        try
        {
            var registry = new CopilotProviderRegistry();
            var settings = new CopilotSettings
            {
                Mode = CopilotMode.HybridAuto,
                EnableFreeProviderPool = true,
                EnableByokProviders = true,
                MaxProviderFallbacksPerMessage = 3,
                ProviderPriorityCsv = "groq,gemini,openrouter,offline"
            };
            var configs = registry.Providers.ToDictionary(
                p => p.Id,
                p => new CopilotProviderConfiguration
                {
                    IsEnabled = p.Id is "groq-free" or "gemini-free" or "openrouter-free",
                    BaseUrl = p.DefaultBaseUrl,
                    ModelName = p.DefaultModelName,
                    ApiKeyEnvironmentVariable = p.Id switch
                    {
                        "groq-free" => "FORGEREMS_FAKE_EMPTY_GROQ_KEY",
                        "gemini-free" => "FORGEREMS_FAKE_EMPTY_GEMINI_KEY",
                        _ => "FORGEREMS_FAKE_EMPTY_PROVIDER_KEY"
                    }
                },
                StringComparer.OrdinalIgnoreCase);

            var scored = KyraProviderRouter.ScoreProviders(
                registry.Providers,
                new CopilotRequest { Prompt = "general chat" },
                settings,
                new CopilotContext { UserQuestion = "general chat", Intent = KyraIntent.GeneralTechQuestion },
                provider => configs[provider.Id]);

            Assert.Equal("groq-free", scored[0].Provider.Id);
            Assert.Contains(scored, p => p.Provider.Id == "gemini-free");
            Assert.DoesNotContain(scored, p => p.Provider.Id == "openrouter-free");
        }
        finally
        {
            KyraApiKeyStore.ClearSessionKey("groq-free");
            KyraApiKeyStore.ClearSessionKey("gemini-free");
            RestoreEnv(originals);
        }
    }

    [Fact]
    public void Registry_IncludesCustomOpenAiCompatibleProvider()
    {
        var provider = new CopilotProviderRegistry().FindById("custom-openai-compatible");

        Assert.NotNull(provider);
        Assert.Equal(CopilotProviderType.CustomOpenAICompatible, provider!.ProviderType);
        Assert.Equal("FORGEREMS_CUSTOM_PROVIDER_API_KEY", provider.DefaultApiKeyEnvironmentVariable);
    }

    [Fact]
    public void ApiKeyResolver_OpenAiCompatibleAcceptsForgeremsKeyFallback()
    {
        var originals = CaptureEnv("FORGEREMS_OPENAI_API_KEY", "OPENAI_API_KEY");
        Environment.SetEnvironmentVariable("FORGEREMS_OPENAI_API_KEY", "forgerems-openai-test-key", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("OPENAI_API_KEY", null, EnvironmentVariableTarget.Process);
        try
        {
            var config = new CopilotProviderConfiguration { ApiKeyEnvironmentVariable = "FORGEREMS_FAKE_EMPTY_OPENAI_KEY" };
            var key = KyraApiKeyStore.ResolveApiKey("openai-compatible", config);
            Assert.False(string.IsNullOrWhiteSpace(key));
        }
        finally
        {
            RestoreEnv(originals);
        }
    }

    [Fact]
    public void ProviderBaseUrlSafety_RejectsEmbeddedCredentials()
    {
        Assert.False(KyraProviderUrlSafety.IsSafeBaseUrl("https://user:pass@example.com/v1"));
        Assert.True(KyraProviderUrlSafety.IsSafeBaseUrl("https://api.example.com/v1"));
    }

    [Fact]
    public void ToolRegistry_IncludesLocalUtilityAndCurrentDataShells()
    {
        var names = new KyraToolRegistry().Tools.Select(t => t.Name).ToArray();

        Assert.Contains("Calculator", names);
        Assert.Contains("Date / Time", names);
        Assert.Contains("Finance", names);
        Assert.Contains("Stats / Economic Data", names);
    }

    [Fact]
    public void LocalRules_CalculatorUsesDeterministicLocalAnswer()
    {
        var answer = LocalRulesCopilotEngine.GenerateReply(
            "calculate 12 * (3 + 2)",
            new CopilotContext { Intent = KyraIntent.GeneralTechQuestion });

        Assert.Contains("60", answer, StringComparison.Ordinal);
        Assert.Contains("local calculator", answer, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string?> CaptureEnv(params string[] names)
    {
        return names.ToDictionary(
            name => name,
            name => Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Process),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void RestoreEnv(Dictionary<string, string?> values)
    {
        foreach (var pair in values)
        {
            Environment.SetEnvironmentVariable(pair.Key, pair.Value, EnvironmentVariableTarget.Process);
        }
    }
}
