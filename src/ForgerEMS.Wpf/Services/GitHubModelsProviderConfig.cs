using System;
using System.Collections.Generic;
using System.Linq;

namespace VentoyToolkitSetup.Wpf.Services;

public enum GitHubModelRoute
{
    Default,
    Fast,
    Alt,
    Fallback
}

public sealed class GitHubModelChoice
{
    public GitHubModelRoute Route { get; init; }

    public string ModelId { get; init; } = string.Empty;

    public bool IsFallback { get; init; }
}

public sealed class GitHubModelsProviderConfig
{
    public const string BuiltInFallbackModel = "gpt-4o-mini";

    public string DefaultModel { get; init; } = string.Empty;

    public string FastModel { get; init; } = string.Empty;

    public string AltModel { get; init; } = string.Empty;

    public string FallbackModel { get; init; } = BuiltInFallbackModel;

    public IReadOnlyList<GitHubModelChoice> ConfiguredModels =>
        BuildChoices(includeFallback: false);

    public int ConfiguredModelsCount => ConfiguredModels.Count;

    public string PrimaryModel =>
        FirstNonPlaceholder(DefaultModel, FastModel, AltModel, FallbackModel);

    public static GitHubModelsProviderConfig FromEnvironment() => new()
    {
        DefaultModel = KyraProviderConfigResolver.ResolveNamedValue(CopilotProviderEnvironmentVariableNames.GitHubModelsDefaultModel),
        FastModel = KyraProviderConfigResolver.ResolveNamedValue(CopilotProviderEnvironmentVariableNames.GitHubModelsFastModel),
        AltModel = KyraProviderConfigResolver.ResolveNamedValue(CopilotProviderEnvironmentVariableNames.GitHubModelsAltModel),
        FallbackModel = BuiltInFallbackModel
    };

    public string GetModel(GitHubModelRoute route) =>
        route switch
        {
            GitHubModelRoute.Default => DefaultModel,
            GitHubModelRoute.Fast => FastModel,
            GitHubModelRoute.Alt => AltModel,
            GitHubModelRoute.Fallback => FallbackModel,
            _ => string.Empty
        };

    private IReadOnlyList<GitHubModelChoice> BuildChoices(bool includeFallback)
    {
        var choices = new[]
            {
                new GitHubModelChoice { Route = GitHubModelRoute.Default, ModelId = DefaultModel },
                new GitHubModelChoice { Route = GitHubModelRoute.Fast, ModelId = FastModel },
                new GitHubModelChoice { Route = GitHubModelRoute.Alt, ModelId = AltModel },
                new GitHubModelChoice { Route = GitHubModelRoute.Fallback, ModelId = FallbackModel, IsFallback = true }
            }
            .Where(choice => includeFallback || !choice.IsFallback)
            .Where(choice => !KyraProviderConfigResolver.IsMissingOrPlaceholder(choice.ModelId))
            .ToArray();

        return choices;
    }

    private static string FirstNonPlaceholder(params string?[] values)
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
}

public static class GitHubModelsRouteSelector
{
    private static readonly string[] AltKeywords =
    [
        "compare",
        "second opinion",
        "summarize",
        "summarise",
        "review this",
        "long context",
        "huge",
        "large log",
        "full log",
        "document",
        "transcript",
        "architecture review",
        "pull request review",
        "code review"
    ];

    private static readonly string[] DefaultKeywords =
    [
        "forgerems",
        "kyra",
        "code",
        "build",
        "compile",
        "exception",
        "stack trace",
        "diagnostic",
        "troubleshoot",
        "planning",
        "architecture",
        "powershell",
        "xaml",
        ".net",
        "wpf",
        "system intelligence"
    ];

    public static GitHubModelRoute SelectRoute(CopilotProviderRequest request)
    {
        var prompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? request.Context.UserQuestion
            : request.Prompt;
        var combinedLength = prompt.Length +
                             (request.Context.ContextText?.Length ?? 0) +
                             (request.Context.ProviderRealtimeAugmentation?.Length ?? 0);
        var lower = prompt.ToLowerInvariant();

        if (combinedLength > 6000 ||
            prompt.Length > 2000 ||
            ContainsAny(lower, AltKeywords))
        {
            return GitHubModelRoute.Alt;
        }

        if (IsDefaultIntent(request.Context.Intent) ||
            ContainsAny(lower, DefaultKeywords))
        {
            return GitHubModelRoute.Default;
        }

        return IsShortSimplePrompt(prompt)
            ? GitHubModelRoute.Fast
            : GitHubModelRoute.Default;
    }

    public static IReadOnlyList<GitHubModelChoice> BuildAttemptPlan(
        GitHubModelsProviderConfig config,
        GitHubModelRoute selectedRoute)
    {
        var selectedModelAvailable = !KyraProviderConfigResolver.IsMissingOrPlaceholder(config.GetModel(selectedRoute));
        var orderedRoutes = selectedRoute == GitHubModelRoute.Default && selectedModelAvailable
            ? [GitHubModelRoute.Default, GitHubModelRoute.Alt, GitHubModelRoute.Fast, GitHubModelRoute.Fallback]
            : new[] { selectedRoute, GitHubModelRoute.Default, GitHubModelRoute.Fast, GitHubModelRoute.Alt, GitHubModelRoute.Fallback };
        var seenModels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var choices = new List<GitHubModelChoice>();

        foreach (var route in orderedRoutes)
        {
            var model = config.GetModel(route);
            if (KyraProviderConfigResolver.IsMissingOrPlaceholder(model) ||
                !seenModels.Add(model.Trim()))
            {
                continue;
            }

            choices.Add(new GitHubModelChoice
            {
                Route = route,
                ModelId = model.Trim(),
                IsFallback = route == GitHubModelRoute.Fallback
            });
        }

        return choices;
    }

    public static string BuildSafeDiagnostic(
        GitHubModelRoute route,
        string modelId,
        bool fallbackUsed,
        int configuredModelsCount) =>
        $"provider=github-models route={RouteName(route)} model={modelId} fallbackUsed={fallbackUsed.ToString().ToLowerInvariant()} configuredModelsCount={configuredModelsCount}";

    public static string RouteName(GitHubModelRoute route) =>
        route.ToString().ToLowerInvariant();

    public static bool ShouldRetryWithNextModel(CopilotProviderResult result) =>
        !result.Succeeded &&
        (result.IsTransientFailure ||
         result.FailureReason is KyraProviderFailureReason.AuthFailed
             or KyraProviderFailureReason.RateLimited
             or KyraProviderFailureReason.Timeout
             or KyraProviderFailureReason.ModelUnavailable
             or KyraProviderFailureReason.ServiceUnavailable
             or KyraProviderFailureReason.NetworkError);

    private static bool IsDefaultIntent(KyraIntent intent) =>
        intent is KyraIntent.PerformanceLag
            or KyraIntent.AppFreezing
            or KyraIntent.SlowBoot
            or KyraIntent.UpgradeAdvice
            or KyraIntent.USBBuilderHelp
            or KyraIntent.ToolkitManagerHelp
            or KyraIntent.SystemHealthSummary
            or KyraIntent.DriverIssue
            or KyraIntent.StorageIssue
            or KyraIntent.MemoryIssue
            or KyraIntent.GPUQuestion
            or KyraIntent.OSRecommendation
            or KyraIntent.ForgerEMSQuestion
            or KyraIntent.CodeAssist;

    private static bool IsShortSimplePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 160)
        {
            return false;
        }

        var words = prompt.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= 24 &&
               !ContainsAny(prompt.ToLowerInvariant(), AltKeywords) &&
               !ContainsAny(prompt.ToLowerInvariant(), DefaultKeywords);
    }

    private static bool ContainsAny(string text, IEnumerable<string> needles) =>
        needles.Any(needle => text.Contains(needle, StringComparison.OrdinalIgnoreCase));
}
