using VentoyToolkitSetup.Wpf.Services;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public static class KyraProviderCapabilityCatalog
{
    public static KyraProviderCapabilities AggregateForProviders(IReadOnlyList<ICopilotProvider> providers)
    {
        KyraProviderCapabilities acc = KyraProviderCapabilities.None;
        foreach (var p in providers)
        {
            acc |= KyraProviderCapabilityCatalog.ForProvider(p);
        }

        return acc;
    }

    public static KyraProviderCapabilities ForProvider(ICopilotProvider provider)
    {
        if (!provider.IsOnlineProvider)
        {
            return KyraProviderCapabilities.SupportsChat |
                   KyraProviderCapabilities.SupportsLocalOnly |
                   KyraProviderCapabilities.SupportsSummarization |
                   KyraProviderCapabilities.SupportsSystemEnhancement;
        }

        KyraProviderCapabilities caps = KyraProviderCapabilities.SupportsChat |
                                        KyraProviderCapabilities.SupportsStreaming |
                                        KyraProviderCapabilities.SupportsGeneralKnowledge |
                                        KyraProviderCapabilities.SupportsSummarization |
                                        KyraProviderCapabilities.SupportsCodeHelp |
                                        KyraProviderCapabilities.RequiresNetwork;

        if (provider.IsPaidProvider)
        {
            caps |= KyraProviderCapabilities.RequiresSecret;
        }
        else
        {
            caps |= KyraProviderCapabilities.RequiresSecret;
        }

        caps |= KyraProviderCapabilities.SupportsSystemEnhancement;
        return caps;
    }
}

public static class KyraProviderRouter
{
    public static bool ShouldUseOnline(CopilotContext context, CopilotSettings settings)
    {
        if (settings.Mode is CopilotMode.OfflineOnly or CopilotMode.AskFirst)
        {
            return false;
        }

        if (KyraToolRouter.ShouldStayLocal(context.Intent, context.UserQuestion, settings))
        {
            return false;
        }

        return context.Intent is KyraIntent.LiveOnlineQuestion
                or KyraIntent.Weather
                or KyraIntent.News
                or KyraIntent.CryptoPrice
                or KyraIntent.StockPrice
                or KyraIntent.Sports
                or KyraIntent.CodeAssist
                or KyraIntent.ResaleValue
                or KyraIntent.UpgradeAdvice
                or KyraIntent.PerformanceLag
                or KyraIntent.AppFreezing
                or KyraIntent.SlowBoot
                or KyraIntent.GPUQuestion
                or KyraIntent.StorageIssue
                or KyraIntent.MemoryIssue
                or KyraIntent.DriverIssue
                or KyraIntent.OSRecommendation
                or KyraIntent.GeneralTechQuestion
                or KyraIntent.Unknown
            || context.UserQuestion.Contains("research", StringComparison.OrdinalIgnoreCase)
            || context.UserQuestion.Contains("lookup", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<KyraProviderScore> ScoreProviders(
        IReadOnlyList<ICopilotProvider> providers,
        CopilotRequest request,
        CopilotSettings settings,
        CopilotContext context,
        Func<ICopilotProvider, CopilotProviderConfiguration> configResolver)
    {
        var scores = new List<KyraProviderScore>();
        foreach (var provider in providers)
        {
            if (provider.ProviderType == CopilotProviderType.LocalOffline)
            {
                continue;
            }

            var config = configResolver(provider);
            if (!config.IsEnabled)
            {
                continue;
            }

            if (!settings.EnableByokProviders && provider.IsPaidProvider)
            {
                continue;
            }

            if (!settings.EnableFreeProviderPool && !provider.IsPaidProvider)
            {
                continue;
            }

            if (provider.Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase) &&
                ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source == KyraCredentialSource.None)
            {
                continue;
            }

            if (!provider.CanHandle(new CopilotProviderRequest
                {
                    Prompt = request.Prompt,
                    Context = context,
                    Settings = settings,
                    ProviderConfiguration = config
                }))
            {
                continue;
            }

            var deprioritizeLocalAi = !KyraToolRouter.ShouldStayLocal(context.Intent, request.Prompt, settings);
            var score = ScoreProvider(provider, context.Intent, deprioritizeLocalAi);
            scores.Add(new KyraProviderScore { Provider = provider, Score = score });
        }

        return scores
            .OrderByDescending(item => item.Score)
            .Take(Math.Max(1, settings.MaxProviderFallbacksPerMessage))
            .ToArray();
    }

    private static int ScoreProvider(ICopilotProvider provider, KyraIntent intent, bool deprioritizeLocalAi)
    {
        var capability = GetCapability(provider, intent);
        var baseScore = capability switch
        {
            KyraModelCapability.FastChat => 100,
            KyraModelCapability.DeepReasoning => 90,
            KyraModelCapability.CodeHelp => 88,
            KyraModelCapability.WritingPolish => 92,
            _ => 70
        };

        var bonus = provider.Id switch
        {
            "gemini-free" => 7,
            "openrouter-free" => 6,
            "github-models" => 5,
            "groq-free" => 4,
            "cerebras-free" => 3,
            _ => 0
        };

        var score = baseScore + bonus;

        if (deprioritizeLocalAi &&
            provider.ProviderType is CopilotProviderType.OllamaLocal or CopilotProviderType.LmStudioLocal)
        {
            score -= 28;
        }

        return score;
    }

    private static KyraModelCapability GetCapability(ICopilotProvider provider, KyraIntent intent)
    {
        if (intent is KyraIntent.GeneralTechQuestion or KyraIntent.LiveOnlineQuestion or KyraIntent.Weather
            or KyraIntent.News or KyraIntent.CryptoPrice or KyraIntent.StockPrice or KyraIntent.Sports)
        {
            return provider.Id switch
            {
                "gemini-free" or "groq-free" or "cerebras-free" or "openrouter-free" => KyraModelCapability.FastChat,
                "github-models" or "mistral-free" => KyraModelCapability.DeepReasoning,
                _ => KyraModelCapability.FastChat
            };
        }

        if (intent is KyraIntent.CodeAssist or KyraIntent.ForgerEMSQuestion or KyraIntent.DriverIssue)
        {
            return KyraModelCapability.CodeHelp;
        }

        if (intent is KyraIntent.ResaleValue or KyraIntent.UpgradeAdvice)
        {
            return KyraModelCapability.DeepReasoning;
        }

        return KyraModelCapability.WritingPolish;
    }
}
