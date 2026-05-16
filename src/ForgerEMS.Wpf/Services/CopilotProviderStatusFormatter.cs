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
        var label = provider.Id.Equals(KyraGatewayProvider.ProviderId, StringComparison.OrdinalIgnoreCase)
            ? "Gateway token source"
            : "Key source";
        if (resolution == KyraProviderCredentialState.Missing)
        {
            return $"{label}: not configured";
        }

        if (resolution == KyraProviderCredentialState.Placeholder)
        {
            return $"{label}: placeholder ignored";
        }

        return $"{label} — {DescribeCredentialState(resolution)}";
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

        if (provider.Id.Equals("github-models", StringComparison.OrdinalIgnoreCase))
        {
            var token = KyraProviderConfigResolver.ResolveCredentialState(provider.Id, providerConfig.ApiKeyEnvironmentVariable, providerConfig.BaseUrl);
            var models = GitHubModelsProviderConfig.FromEnvironment();
            var tokenText = token is KyraProviderCredentialState.Missing or KyraProviderCredentialState.Placeholder
                ? token == KyraProviderCredentialState.Placeholder ? "placeholder ignored" : "missing"
                : "configured";

            return $"GitHub Models — Token: {tokenText}; Default model: {SlotState(models.DefaultModel)}; Fast model: {SlotState(models.FastModel)}; Alt model: {SlotState(models.AltModel)}; Active routing: enabled.";
        }

        if (provider.Id.Equals(KyraGatewayProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
        {
            var config = KyraGatewayProviderConfig.FromProviderConfiguration(providerConfig);
            var tokenText = config.TokenState switch
            {
                KyraProviderCredentialState.Missing => "MISSING",
                KyraProviderCredentialState.Placeholder => "PLACEHOLDER",
                _ => "SET"
            };
            var host = string.IsNullOrWhiteSpace(config.GatewayHost) ? "not set" : config.GatewayHost;
            var context = config.ShareSystemContext ? "on" : "off";
            if (config.UrlState != KyraProviderEndpointState.Ready)
            {
                return $"ForgerEMS Gateway — URL: {DescribeUrlState(config.UrlState)}; Beta token: {tokenText}; Context sharing: {context}; Active routing: enabled.";
            }

            return $"ForgerEMS Gateway — Host: {host}; Beta token: {tokenText}; Context sharing: {context}; Active routing: enabled.";
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
            if (provider.IsOnlineProvider &&
                resolved.CredentialState == KyraProviderCredentialState.Missing &&
                !string.IsNullOrWhiteSpace(envVarName))
            {
                return $"{provider.DisplayName} key not found. Enter a session API key or set {envVarName} for process, user, or machine scope, then tap Refresh Provider Status.";
            }

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

    private static string SlotState(string value) =>
        KyraProviderConfigResolver.IsMissingOrPlaceholder(value) ? "missing" : "configured";

    private static string DescribeUrlState(KyraProviderEndpointState state) =>
        state switch
        {
            KyraProviderEndpointState.Missing => "MISSING",
            KyraProviderEndpointState.Placeholder => "PLACEHOLDER",
            KyraProviderEndpointState.InvalidUrl => "INVALID",
            KyraProviderEndpointState.EmbeddedCredentials => "INVALID",
            KyraProviderEndpointState.Ready => "ready",
            _ => "not set"
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
