using System;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public enum KyraProviderCredentialState
{
    Missing,
    Placeholder,
    Present,
    InvalidFormatMaybe,
    FromSession,
    FromUserEnv,
    FromProcessEnv,
    FromSettings
}

public enum KyraProviderEndpointState
{
    Missing,
    Placeholder,
    InvalidUrl,
    EmbeddedCredentials,
    Ready,
    NotRequired
}

public sealed class KyraProviderConfig
{
    public string ProviderId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string BaseUrl { get; init; } = string.Empty;

    public KyraProviderEndpointState EndpointState { get; init; }

    public string ModelName { get; init; } = string.Empty;

    public bool ModelIsPlaceholder { get; init; }

    public string ApiKeyEnvironmentVariable { get; init; } = string.Empty;

    public KyraProviderCredentialState CredentialState { get; init; }

    public string? SafeSkipReason { get; init; }

    public bool IsReady => SafeSkipReason is null;
}

public static class KyraProviderConfigResolver
{
    public static bool IsPlaceholderSecretOrValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var v = value.Trim();
        return v.Equals("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("REPLACE_WITH_BETA_ACCESS_TOKEN", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("REPLACE_MODEL_NAME", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("local-model-name", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("model-name", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("changeme", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("TODO", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("sk-REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("REPLACE_", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("PASTE_", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("sample", StringComparison.OrdinalIgnoreCase) ||
               v.Equals("example", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("sample-", StringComparison.OrdinalIgnoreCase) ||
               v.StartsWith("example-", StringComparison.OrdinalIgnoreCase) ||
               v.Contains("REPLACE_ME", StringComparison.OrdinalIgnoreCase) ||
               v.Contains("example.local", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMissingOrPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || IsPlaceholderSecretOrValue(value);

    public static KyraProviderConfig ResolveProvider(
        ICopilotProvider provider,
        CopilotProviderConfiguration configuration)
    {
        var baseUrl = FirstNonPlaceholder(configuration.BaseUrl, provider.DefaultBaseUrl);
        var model = FirstNonPlaceholder(configuration.ModelName, provider.DefaultModelName);
        var envName = string.IsNullOrWhiteSpace(configuration.ApiKeyEnvironmentVariable)
            ? provider.DefaultApiKeyEnvironmentVariable
            : configuration.ApiKeyEnvironmentVariable.Trim();

        var endpointState = ResolveEndpointState(provider, baseUrl);
        var credential = ResolveCredentialState(provider.Id, envName, baseUrl);
        var needsKey = provider.IsOnlineProvider && !string.IsNullOrWhiteSpace(envName);
        var modelIsPlaceholder = IsPlaceholderSecretOrValue(configuration.ModelName) ||
                                 IsPlaceholderSecretOrValue(provider.DefaultModelName) && string.IsNullOrWhiteSpace(configuration.ModelName);

        string? reason = null;
        if (endpointState == KyraProviderEndpointState.EmbeddedCredentials)
        {
            reason = "base URL contains embedded credentials";
        }
        else if (endpointState == KyraProviderEndpointState.Missing)
        {
            reason = "base URL is missing";
        }
        else if (endpointState == KyraProviderEndpointState.InvalidUrl)
        {
            reason = "base URL is invalid";
        }
        else if (endpointState == KyraProviderEndpointState.Placeholder)
        {
            reason = "base URL is a placeholder";
        }
        else if (provider.IsOnlineProvider && string.IsNullOrWhiteSpace(model))
        {
            reason = "model is missing";
        }
        else if (provider.IsOnlineProvider && modelIsPlaceholder)
        {
            reason = "model is a placeholder";
        }
        else if (needsKey && credential == KyraProviderCredentialState.Missing)
        {
            reason = "API key is missing";
        }
        else if (needsKey && credential == KyraProviderCredentialState.Placeholder)
        {
            reason = "API key is a placeholder";
        }
        else if (provider.Id.Equals("anthropic-claude", StringComparison.OrdinalIgnoreCase))
        {
            reason = "provider shell is not implemented";
        }
        else if (provider.Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase) &&
                 ResolveNamedCredential("CLOUDFLARE_ACCOUNT_ID") is KyraProviderCredentialState.Missing or KyraProviderCredentialState.Placeholder)
        {
            reason = "Cloudflare account id is missing";
        }

        return new KyraProviderConfig
        {
            ProviderId = provider.Id,
            DisplayName = provider.DisplayName,
            BaseUrl = baseUrl,
            EndpointState = endpointState,
            ModelName = model,
            ModelIsPlaceholder = modelIsPlaceholder,
            ApiKeyEnvironmentVariable = envName,
            CredentialState = credential,
            SafeSkipReason = reason
        };
    }

    public static KyraProviderCredentialState ResolveCredentialState(string providerId, string? apiKeyEnvironmentVariable, string? baseUrl = null)
    {
        var session = KyraApiKeyStore.GetSessionKey(providerId);
        if (!string.IsNullOrWhiteSpace(session))
        {
            return IsPlaceholderSecretOrValue(session)
                ? KyraProviderCredentialState.Placeholder
                : KyraProviderCredentialState.FromSession;
        }

        var encrypted = KyraProviderCredentialStore.Default.TryGetSecret(providerId);
        if (!string.IsNullOrWhiteSpace(encrypted))
        {
            return IsPlaceholderSecretOrValue(encrypted)
                ? KyraProviderCredentialState.Placeholder
                : KyraProviderCredentialState.FromSettings;
        }

        foreach (var name in ExpandCredentialNames(providerId, apiKeyEnvironmentVariable, baseUrl))
        {
            var state = ResolveNamedCredential(name);
            if (state != KyraProviderCredentialState.Missing)
            {
                return state;
            }
        }

        return KyraProviderCredentialState.Missing;
    }

    public static string ResolveApiKeyValue(string providerId, string? apiKeyEnvironmentVariable, string? baseUrl = null)
    {
        var session = KyraApiKeyStore.GetSessionKey(providerId);
        if (!IsMissingOrPlaceholder(session))
        {
            return session.Trim();
        }

        var encrypted = KyraProviderCredentialStore.Default.TryGetSecret(providerId);
        if (!IsMissingOrPlaceholder(encrypted))
        {
            return encrypted.Trim();
        }

        foreach (var name in ExpandCredentialNames(providerId, apiKeyEnvironmentVariable, baseUrl))
        {
            var process = SafeGet(name, EnvironmentVariableTarget.Process);
            if (!IsMissingOrPlaceholder(process))
            {
                return process!.Trim();
            }

            var user = SafeGet(name, EnvironmentVariableTarget.User);
            if (!IsMissingOrPlaceholder(user))
            {
                return user!.Trim();
            }

            var machine = SafeGet(name, EnvironmentVariableTarget.Machine);
            if (!IsMissingOrPlaceholder(machine))
            {
                return machine!.Trim();
            }
        }

        return string.Empty;
    }

    public static string ResolveNamedValue(string name, string defaultValue = "")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return defaultValue;
        }

        var process = SafeGet(name, EnvironmentVariableTarget.Process);
        if (!IsMissingOrPlaceholder(process))
        {
            return process!.Trim();
        }

        var user = SafeGet(name, EnvironmentVariableTarget.User);
        if (!IsMissingOrPlaceholder(user))
        {
            return user!.Trim();
        }

        var machine = SafeGet(name, EnvironmentVariableTarget.Machine);
        if (!IsMissingOrPlaceholder(machine))
        {
            return machine!.Trim();
        }

        return IsMissingOrPlaceholder(defaultValue) ? string.Empty : defaultValue.Trim();
    }

    public static KyraProviderCredentialState ResolveNamedCredential(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return KyraProviderCredentialState.Missing;
        }

        var process = SafeGet(name, EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(process))
        {
            return IsPlaceholderSecretOrValue(process)
                ? KyraProviderCredentialState.Placeholder
                : KyraProviderCredentialState.FromProcessEnv;
        }

        var user = SafeGet(name, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(user))
        {
            return IsPlaceholderSecretOrValue(user)
                ? KyraProviderCredentialState.Placeholder
                : KyraProviderCredentialState.FromUserEnv;
        }

        var machine = SafeGet(name, EnvironmentVariableTarget.Machine);
        if (!string.IsNullOrWhiteSpace(machine))
        {
            return IsPlaceholderSecretOrValue(machine)
                ? KyraProviderCredentialState.Placeholder
                : KyraProviderCredentialState.Present;
        }

        return KyraProviderCredentialState.Missing;
    }

    public static KyraProviderEndpointState ResolveEndpointState(ICopilotProvider provider, string? baseUrl)
    {
        if (!provider.IsOnlineProvider &&
            provider.ProviderType is CopilotProviderType.LocalOffline)
        {
            return KyraProviderEndpointState.NotRequired;
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return provider.IsOnlineProvider ? KyraProviderEndpointState.Missing : KyraProviderEndpointState.Ready;
        }

        if (IsPlaceholderSecretOrValue(baseUrl))
        {
            return KyraProviderEndpointState.Placeholder;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return KyraProviderEndpointState.InvalidUrl;
        }

        return string.IsNullOrEmpty(uri.UserInfo)
            ? KyraProviderEndpointState.Ready
            : KyraProviderEndpointState.EmbeddedCredentials;
    }

    public static void ApplyLiveToolEnvironmentDefaults(KyraLiveToolsSettings settings)
    {
        settings.WeatherProvider = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_WEATHER_PROVIDER"), settings.WeatherProvider, "openmeteo");
        settings.DefaultWeatherLocation = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_WEATHER_DEFAULT_LOCATION"), settings.DefaultWeatherLocation);
        settings.WeatherApiKey = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_WEATHER_API_KEY"), settings.WeatherApiKey);

        settings.NewsProvider = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_NEWS_PROVIDER"), settings.NewsProvider, "newsapi");
        settings.NewsApiKey = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_NEWS_API_KEY"), settings.NewsApiKey);

        settings.StocksProvider = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_FINANCE_PROVIDER"), settings.StocksProvider, "finnhub");
        settings.StocksApiKey = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_FINANCE_API_KEY"), settings.StocksApiKey);

        settings.CryptoProvider = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_CRYPTO_PROVIDER"), settings.CryptoProvider, "coingecko");
        settings.CryptoApiKey = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_CRYPTO_API_KEY"), settings.CryptoApiKey);

        settings.StatsProvider = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_STATS_PROVIDER"), settings.StatsProvider, "fred");
        settings.StatsApiKey = FirstNonPlaceholder(ReadUserThenProcess("FORGEREMS_STATS_API_KEY"), settings.StatsApiKey);

        var timeout = ForgerEmsEnvironmentConfiguration.KyraProviderTimeoutSeconds;
        if (settings.TimeoutSeconds <= 0)
        {
            settings.TimeoutSeconds = timeout;
        }
    }

    private static IEnumerable<string> ExpandCredentialNames(string providerId, string? configuredName, string? baseUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            yield return configuredName.Trim();
        }

        foreach (var fallback in providerId.ToLowerInvariant() switch
        {
            "openai-compatible" => ["FORGEREMS_OPENAI_API_KEY", "OPENAI_API_KEY"],
            "forgerems-gateway" => SingleDefaultFallback(configuredName, CopilotProviderEnvironmentVariableNames.KyraGatewayBetaToken),
            "custom-openai-compatible" => CustomCredentialFallbacks(baseUrl),
            "gemini-free" => ["FORGEREMS_GEMINI_API_KEY", "GEMINI_API_KEY"],
            "anthropic-claude" => ["FORGEREMS_ANTHROPIC_API_KEY", "ANTHROPIC_API_KEY"],
            "groq-free" => SingleDefaultFallback(configuredName, "GROQ_API_KEY"),
            "openrouter-free" => SingleDefaultFallback(configuredName, "OPENROUTER_API_KEY"),
            "mistral-free" => SingleDefaultFallback(configuredName, "MISTRAL_API_KEY"),
            "cerebras-free" => SingleDefaultFallback(configuredName, "CEREBRAS_API_KEY"),
            "github-models" => SingleDefaultFallback(configuredName, "GITHUB_MODELS_TOKEN"),
            "cloudflare-workers-ai" => SingleDefaultFallback(configuredName, "CLOUDFLARE_API_KEY"),
            _ => []
        })
        {
            if (string.IsNullOrWhiteSpace(configuredName) ||
                !fallback.Equals(configuredName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                yield return fallback;
            }
        }
    }

    private static string[] SingleDefaultFallback(string? configuredName, string defaultName)
    {
        return string.IsNullOrWhiteSpace(configuredName) ||
               configuredName.Trim().Equals(defaultName, StringComparison.OrdinalIgnoreCase)
            ? [defaultName]
            : [];
    }

    private static string[] CustomCredentialFallbacks(string? baseUrl)
    {
        var candidate = FirstNonPlaceholder(baseUrl, ForgerEmsEnvironmentConfiguration.CustomProviderBaseUrl);
        if (candidate.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
        {
            return ["FORGEREMS_CUSTOM_PROVIDER_API_KEY", "OPENROUTER_API_KEY"];
        }

        if (candidate.Contains("groq.com", StringComparison.OrdinalIgnoreCase))
        {
            return ["FORGEREMS_CUSTOM_PROVIDER_API_KEY", "GROQ_API_KEY"];
        }

        return ["FORGEREMS_CUSTOM_PROVIDER_API_KEY"];
    }

    private static string ReadUserThenProcess(string name)
    {
        var user = SafeGet(name, EnvironmentVariableTarget.User);
        if (!string.IsNullOrWhiteSpace(user))
        {
            return user;
        }

        var process = SafeGet(name, EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(process))
        {
            return process;
        }

        return string.Empty;
    }

    private static string FirstNonPlaceholder(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!IsMissingOrPlaceholder(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
    }

    private static string? SafeGet(string name, EnvironmentVariableTarget target)
    {
        try
        {
            return Environment.GetEnvironmentVariable(name, target);
        }
        catch
        {
            return null;
        }
    }
}
