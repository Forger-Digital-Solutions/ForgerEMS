#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// Wires providers from ForgerEmsEnvironmentConfiguration; replace with a generic IProviderFactory.
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class CopilotProviderRegistry : ICopilotProviderRegistry
{
    public CopilotProviderRegistry()
    {
        Providers =
        [
            new LocalOfflineCopilotProvider(),
            new KyraGatewayProvider(),
            new GeminiCopilotProvider(),
            new OpenAiStyleCopilotProvider("groq-free", "Groq (Free Tier)", CopilotProviderType.GroqApi, "Free API pool", false, "https://api.groq.com/openai/v1", "llama-3.1-8b-instant", "GROQ_API_KEY", "Groq free-tier via OpenAI-compatible API."),
            new OpenAiStyleCopilotProvider("cerebras-free", "Cerebras (Free Tier)", CopilotProviderType.CerebrasApi, "Free API pool", false, "https://api.cerebras.ai/v1", "llama3.1-8b", "CEREBRAS_API_KEY", "Cerebras free inference via OpenAI-compatible API."),
            new OpenAiStyleCopilotProvider("openrouter-free", "OpenRouter Free", CopilotProviderType.OpenRouterFree, "Free API pool", false, "https://openrouter.ai/api/v1", "openrouter/auto", "OPENROUTER_API_KEY", "OpenRouter free model routing."),
            new OpenAiStyleCopilotProvider("mistral-free", "Mistral (Eval/BYOK)", CopilotProviderType.MistralApi, "Free API pool", false, "https://api.mistral.ai/v1", "mistral-small-latest", "MISTRAL_API_KEY", "Mistral API provider (free/eval depends on account plan)."),
            new OpenAiStyleCopilotProvider("github-models", "GitHub Models", CopilotProviderType.GitHubModels, "Free API pool", false, "https://models.inference.ai.azure.com", ForgerEmsEnvironmentConfiguration.GitHubModelsPrimaryModel, CopilotProviderEnvironmentVariableNames.GitHubModels, "GitHub Models endpoint provider with routed model slots. Optional model env vars: FORGEREMS_GITHUB_MODELS_DEFAULT_MODEL, FORGEREMS_GITHUB_MODELS_FAST_MODEL, FORGEREMS_GITHUB_MODELS_ALT_MODEL."),
            new OpenAiStyleCopilotProvider("cloudflare-workers-ai", "Cloudflare Workers AI", CopilotProviderType.CloudflareWorkersAi, "Free API pool", false, "https://api.cloudflare.com/client/v4/accounts", "@cf/meta/llama-3.1-8b-instruct", "CLOUDFLARE_API_KEY", "Cloudflare Workers AI (endpoint shape may require account-specific route)."),
            new StubCopilotProvider(CopilotProviderType.HuggingFaceInference, "huggingface-inference", "Hugging Face Inference Providers", "Free API pool", "Placeholder provider: endpoint/model compatibility varies by provider route."),
            new OpenAICompatibleCopilotProvider(),
            new OpenAiStyleCopilotProvider(
                "custom-openai-compatible",
                "Custom OpenAI-Compatible",
                CopilotProviderType.CustomOpenAICompatible,
                "Online/local AI",
                true,
                ForgerEmsEnvironmentConfiguration.CustomProviderBaseUrl,
                ForgerEmsEnvironmentConfiguration.CustomProviderModel,
                "FORGEREMS_CUSTOM_PROVIDER_API_KEY",
                "Operator-supplied OpenAI-compatible endpoint. Rejects base URLs with embedded credentials."),
            new AnthropicClaudeCopilotProvider(),
            new OllamaCopilotProvider(),
            new LmStudioCopilotProvider(),
            new StubCopilotProvider(CopilotProviderType.ForgerEmsCloud, "forgerems-cloud", "ForgerEMS Cloud (Future)", "Future", "Future ForgerEMS-hosted provider pool. Billing and broker routing intentionally not implemented in desktop app."),
            new StubCopilotProvider(CopilotProviderType.EbayPricing, "ebay-sold-listings", "eBay Sold Listings", "Pricing", "Provider hook ready; configure API access later for real sold-listing comps."),
            new StubCopilotProvider(CopilotProviderType.GitHubReleases, "github-releases", "GitHub Releases", "Toolkit updates", "Provider hook ready; public release lookup can be added without paid dependencies."),
            new StubCopilotProvider(CopilotProviderType.ManufacturerSupport, "manufacturer-support", "Manufacturer Support Lookup", "Drivers/BIOS", "Provider hook ready; future lookup must use sanitized model/manufacturer only."),
            new StubCopilotProvider(CopilotProviderType.MicrosoftDocs, "microsoft-support-docs", "Microsoft/Windows Support Docs", "Windows docs", "Provider hook ready; docs lookup should never send service tags or usernames."),
            new StubCopilotProvider(CopilotProviderType.LinuxReleaseInfo, "linux-release-info", "Ubuntu/Mint/Xubuntu Release Info", "Linux support", "Provider hook ready for public distro support-window checks.")
        ];
    }

    public IReadOnlyList<ICopilotProvider> Providers { get; }

    public ICopilotProvider? FindById(string id)
    {
        return Providers.FirstOrDefault(provider => string.Equals(provider.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public ICopilotProvider? FindByType(CopilotProviderType providerType)
    {
        return Providers.FirstOrDefault(provider => provider.ProviderType == providerType);
    }
}
