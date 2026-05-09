#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: No ForgerEMS-specific coupling; eligible for Kyra.Core in Phase 3.
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

public sealed class KyraResponseCache
{
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string key, out string value) => _cache.TryGetValue(key, out value!);

    public void Store(string key, string value)
    {
        _cache[key] = value;
    }

    public static bool IsCacheablePrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var text = prompt.ToLowerInvariant();
        return !(text.Contains("current ") ||
                 text.Contains("latest ") ||
                 text.Contains("today") ||
                 text.Contains("right now") ||
                 text.Contains("password") ||
                 text.Contains("serial") ||
                 text.Contains("license"));
    }
}

public sealed class KyraProviderUsageTracker
{
    private readonly Dictionary<string, KyraProviderQuotaState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public KyraProviderQuotaState GetOrCreate(string providerId)
    {
        lock (_sync)
        {
            if (!_states.TryGetValue(providerId, out var state))
            {
                state = new KyraProviderQuotaState();
                _states[providerId] = state;
            }

            return state;
        }
    }
}

public static class KyraApiKeyStore
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, string> SessionKeys = new(StringComparer.OrdinalIgnoreCase);

    public static void SetSessionKey(string providerId, string apiKey)
    {
        lock (Sync)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                SessionKeys.Remove(providerId);
                return;
            }

            SessionKeys[providerId] = apiKey.Trim();
        }
    }

    public static void ClearSessionKey(string providerId)
    {
        lock (Sync)
        {
            SessionKeys.Remove(providerId);
        }
    }

    public static string GetSessionKey(string providerId)
    {
        lock (Sync)
        {
            return SessionKeys.TryGetValue(providerId, out var value) ? value : string.Empty;
        }
    }

    public static string ResolveApiKey(string providerId, CopilotProviderConfiguration configuration)
    {
        return KyraProviderConfigResolver.ResolveApiKeyValue(providerId, configuration.ApiKeyEnvironmentVariable, configuration.BaseUrl);
    }

    public static string Mask(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return string.Empty;
        }

        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 8)
        {
            return "****";
        }

        return $"{trimmed[..4]}...{trimmed[^4..]}";
    }
}

public static class KyraProviderUrlSafety
{
    public static bool IsSafeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme is "https" or "http" && string.IsNullOrWhiteSpace(uri.UserInfo);
    }
}
