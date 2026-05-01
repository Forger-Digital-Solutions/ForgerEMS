using System;

namespace VentoyToolkitSetup.Wpf.Services;

public static class CopilotProviderStatusFormatter
{
    public static bool IsPlaceholderProvider(ICopilotProvider provider)
    {
        return provider.StatusText.Contains("placeholder", StringComparison.OrdinalIgnoreCase) ||
               provider.StatusText.Contains("shell", StringComparison.OrdinalIgnoreCase) ||
               provider.StatusText.Contains("future", StringComparison.OrdinalIgnoreCase) ||
               provider.Id.Contains("forgerems-cloud", StringComparison.OrdinalIgnoreCase);
    }

    public static string BuildCredentialSourceLine(ICopilotProvider provider, CopilotProviderConfiguration providerConfig)
    {
        var envVar = string.IsNullOrWhiteSpace(providerConfig.ApiKeyEnvironmentVariable)
            ? provider.DefaultApiKeyEnvironmentVariable
            : providerConfig.ApiKeyEnvironmentVariable;

        var resolution = ProviderEnvironmentResolver.ResolveApiCredential(provider.Id, envVar);
        if (resolution.Source == KyraCredentialSource.None)
        {
            return "Key source: not configured";
        }

        return $"Key source — {resolution.DescribeUx()}";
    }

    public static string BuildStatusLabel(ICopilotProvider provider, CopilotProviderConfiguration providerConfig)
    {
        if (!providerConfig.IsEnabled)
        {
            return "Disabled";
        }

        if (provider.Id.Equals("anthropic-claude", StringComparison.OrdinalIgnoreCase))
        {
            var anthropicKey = ProviderEnvironmentResolver.ResolveApiCredential(provider.Id, providerConfig.ApiKeyEnvironmentVariable);
            if (anthropicKey.Source == KyraCredentialSource.None)
            {
                return "Not configured — set ANTHROPIC_API_KEY (process/user/machine env) or a session key.";
            }

            return "Anthropic: adapter shell only in this build — key detected but live Claude API calls are not enabled yet.";
        }

        if (provider.Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase))
        {
            var account = ProviderEnvironmentResolver.ResolveCloudflareAccountId();
            var keyResolution = ProviderEnvironmentResolver.ResolveApiCredential(provider.Id, providerConfig.ApiKeyEnvironmentVariable);
            if (keyResolution.Source == KyraCredentialSource.None)
            {
                return $"Not configured — set {CopilotProviderEnvironmentVariableNames.CloudflareWorkersAi} (process/user/machine env) or a session key.";
            }

            if (account.Source == KyraCredentialSource.None)
            {
                return $"Not usable — {CopilotProviderEnvironmentVariableNames.CloudflareAccountId} is missing. Add it to user or machine environment, then Refresh Provider Status.";
            }

            return $"Ready — API key via {DescribeEnvTier(keyResolution.Source)}; account ID via {DescribeEnvTier(account.Source)}.";
        }

        if (IsPlaceholderProvider(provider))
        {
            return "Placeholder / future — not wired for live API in this build.";
        }

        if (!provider.IsOnlineProvider)
        {
            return provider.IsConfigured(providerConfig) ? "Ready (local / offline)" : "Not configured";
        }

        var envVarName = string.IsNullOrWhiteSpace(providerConfig.ApiKeyEnvironmentVariable)
            ? provider.DefaultApiKeyEnvironmentVariable
            : providerConfig.ApiKeyEnvironmentVariable;

        var res = ProviderEnvironmentResolver.ResolveApiCredential(provider.Id, envVarName);

        if (provider.IsConfigured(providerConfig))
        {
            if (res.Source == KyraCredentialSource.Session)
            {
                var envOnly = ProviderEnvironmentResolver.ResolveFromEnvironmentVariable(envVarName ?? string.Empty);
                if (envOnly.Source != KyraCredentialSource.None)
                {
                    return $"Configured: session key active (overrides {DescribeEnvTier(envOnly.Source).ToLowerInvariant()}).";
                }

                return "Configured via session key (not saved to disk).";
            }

            return $"Configured: {res.DescribeUx().Replace("Configured via ", "", StringComparison.Ordinal)}.";
        }

        if (string.IsNullOrWhiteSpace(envVarName))
        {
            return $"{provider.DisplayName} is not configured (no API key environment variable is defined for this provider). Paste a session key or adjust Base URL / Model.";
        }

        return $"{provider.DisplayName} key not found. Enter a session API key or set {envVarName} for process, user, or machine scope, then tap Refresh Provider Status.";
    }

    private static string DescribeEnvTier(KyraCredentialSource source)
    {
        return source switch
        {
            KyraCredentialSource.ProcessEnvironment => "process env",
            KyraCredentialSource.UserEnvironment => "user env",
            KyraCredentialSource.MachineEnvironment => "machine env",
            KyraCredentialSource.Session => "session key",
            _ => "environment"
        };
    }
}
