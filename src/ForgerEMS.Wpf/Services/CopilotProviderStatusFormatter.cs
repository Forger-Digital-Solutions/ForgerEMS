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

        var resolution = KyraProviderConfigResolver.ResolveCredentialState(provider.Id, envVar, providerConfig.BaseUrl);
        if (resolution == KyraProviderCredentialState.Missing)
        {
            return "Key source: not configured";
        }

        if (resolution == KyraProviderCredentialState.Placeholder)
        {
            return "Key source: placeholder ignored";
        }

        return $"Key source — {DescribeCredentialState(resolution)}";
    }

    public static string BuildStatusLabel(ICopilotProvider provider, CopilotProviderConfiguration providerConfig)
    {
        if (!providerConfig.IsEnabled)
        {
            return "Disabled";
        }

        if (provider.Id.Equals("anthropic-claude", StringComparison.OrdinalIgnoreCase))
        {
            var anthropicKey = KyraProviderConfigResolver.ResolveCredentialState(provider.Id, providerConfig.ApiKeyEnvironmentVariable, providerConfig.BaseUrl);
            if (anthropicKey == KyraProviderCredentialState.Missing)
            {
                return "Not configured — set ANTHROPIC_API_KEY (process/user/machine env) or a session key.";
            }
            if (anthropicKey == KyraProviderCredentialState.Placeholder)
            {
                return "Not configured — Anthropic key is a placeholder and will be ignored.";
            }

            return "Anthropic: adapter shell only in this build — key detected but live Claude API calls are not enabled yet.";
        }

        if (provider.Id.Equals("cloudflare-workers-ai", StringComparison.OrdinalIgnoreCase))
        {
            var account = KyraProviderConfigResolver.ResolveNamedCredential(CopilotProviderEnvironmentVariableNames.CloudflareAccountId);
            var keyResolution = KyraProviderConfigResolver.ResolveCredentialState(provider.Id, providerConfig.ApiKeyEnvironmentVariable, providerConfig.BaseUrl);
            if (keyResolution == KyraProviderCredentialState.Missing)
            {
                return $"Not configured — set {CopilotProviderEnvironmentVariableNames.CloudflareWorkersAi} (process/user/machine env) or a session key.";
            }

            if (keyResolution == KyraProviderCredentialState.Placeholder)
            {
                return "Not configured — Cloudflare API key is a placeholder and will be ignored.";
            }

            if (account == KyraProviderCredentialState.Missing)
            {
                return $"Not usable — {CopilotProviderEnvironmentVariableNames.CloudflareAccountId} is missing. Add it to user or machine environment, then Refresh Provider Status.";
            }

            if (account == KyraProviderCredentialState.Placeholder)
            {
                return $"Not usable — {CopilotProviderEnvironmentVariableNames.CloudflareAccountId} is a placeholder and will be ignored.";
            }

            return $"Ready — API key via {DescribeCredentialState(keyResolution)}; account ID via {DescribeCredentialState(account)}.";
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

        var resolved = KyraProviderConfigResolver.ResolveProvider(provider, providerConfig);

        if (resolved.IsReady && provider.IsConfigured(providerConfig))
        {
            if (resolved.CredentialState == KyraProviderCredentialState.FromSession)
            {
                var envOnly = ProviderEnvironmentResolver.ResolveFromEnvironmentVariable(envVarName ?? string.Empty);
                if (envOnly.Source != KyraCredentialSource.None)
                {
                    return $"Configured: session key active (overrides {DescribeEnvTier(envOnly.Source).ToLowerInvariant()}).";
                }

                return "Configured via session key (not saved to disk).";
            }

            return $"Configured: {DescribeCredentialState(resolved.CredentialState)}.";
        }

        if (!resolved.IsReady)
        {
            return $"{provider.DisplayName} is not ready: {resolved.SafeSkipReason}.";
        }

        if (string.IsNullOrWhiteSpace(envVarName))
        {
            return $"{provider.DisplayName} is not configured (no API key environment variable is defined for this provider). Paste a session key or adjust Base URL / Model.";
        }

        return $"{provider.DisplayName} key not found. Enter a session API key or set {envVarName} for process, user, or machine scope, then tap Refresh Provider Status.";
    }

    private static string DescribeCredentialState(KyraProviderCredentialState state) =>
        state switch
        {
            KyraProviderCredentialState.FromSession => "session key",
            KyraProviderCredentialState.FromUserEnv => "user env",
            KyraProviderCredentialState.FromProcessEnv => "process env",
            KyraProviderCredentialState.FromSettings => "settings",
            KyraProviderCredentialState.Present => "environment",
            KyraProviderCredentialState.InvalidFormatMaybe => "present (format may be invalid)",
            _ => "not configured"
        };

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
