using System.Collections.Concurrent;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

internal static class KyraLiveToolsRedactor
{
    private static readonly Regex KeyLike = new(@"(api[_-]?key|token|secret|password)\s*[=:]\s*[\w\-]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Strip common secret patterns from strings that might be logged or shown.</summary>
    public static string SanitizeForDisplay(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var t = KeyLike.Replace(text, "$1=(redacted)");
        t = Regex.Replace(t, @"sk-[a-zA-Z0-9]{10,}", "(redacted)", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"ghp_[a-zA-Z0-9]{10,}", "(redacted)", RegexOptions.IgnoreCase);
        return t;
    }
}

internal sealed class KyraLiveToolTelemetry
{
    private static readonly ConcurrentDictionary<string, KyraToolRunSnapshot> Snapshots = new(StringComparer.OrdinalIgnoreCase);

    public static void Record(string toolName, KyraToolOperationalStatus status, string providerLabel, bool fromCache, string? notes = null)
    {
        Snapshots[toolName] = new KyraToolRunSnapshot
        {
            LastUtc = DateTimeOffset.UtcNow,
            Status = status,
            ProviderLabel = providerLabel,
            FromCache = fromCache,
            Notes = notes ?? string.Empty
        };
    }

    public static KyraToolRunSnapshot? Get(string toolName) =>
        Snapshots.TryGetValue(toolName, out var s) ? s : null;

    public static string FormatLastCheckedCell(string toolName)
    {
        if (!Snapshots.TryGetValue(toolName, out var s))
        {
            return "—";
        }

        var age = DateTimeOffset.UtcNow - s.LastUtc;
        if (s.FromCache)
        {
            return $"cached {FormatAge(age)}";
        }

        return $"{s.LastUtc:HH:mm:ss} UTC";
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalMinutes < 1)
        {
            return "just now";
        }

        if (age.TotalHours < 1)
        {
            return $"{(int)age.TotalMinutes}m ago";
        }

        if (age.TotalDays < 1)
        {
            return $"{(int)age.TotalHours}h ago";
        }

        return $"{(int)age.TotalDays}d ago";
    }
}

internal readonly struct KyraToolRunSnapshot
{
    public DateTimeOffset LastUtc { get; init; }

    public KyraToolOperationalStatus Status { get; init; }

    public string ProviderLabel { get; init; }

    public bool FromCache { get; init; }

    public string Notes { get; init; }
}

internal sealed class KyraLiveToolCache
{
    private sealed class Entry
    {
        public KyraToolResult Result { get; init; } = null!;

        public DateTimeOffset StoredUtc { get; init; }
    }

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public bool TryGet(string key, TimeSpan ttl, out KyraToolResult result)
    {
        if (_entries.TryGetValue(key, out var e) && DateTimeOffset.UtcNow - e.StoredUtc <= ttl)
        {
            result = CloneAsCached(e.Result);
            return true;
        }

        result = null!;
        return false;
    }

    public void Set(string key, KyraToolResult result) =>
        _entries[key] = new Entry { Result = result, StoredUtc = DateTimeOffset.UtcNow };

    private static KyraToolResult CloneAsCached(KyraToolResult r) =>
        new()
        {
            Success = r.Success,
            Status = "Cached result used",
            ToolName = r.ToolName,
            ProviderName = r.ProviderName,
            TimestampUtc = r.TimestampUtc,
            UserFacingSummary = r.UserFacingSummary,
            ProviderAugmentation = r.ProviderAugmentation,
            Sources = r.Sources,
            ErrorKind = r.ErrorKind,
            SafeErrorMessage = r.SafeErrorMessage,
            IsRealtime = r.IsRealtime,
            Disclaimer = r.Disclaimer,
            FromCache = true,
            AugmentsProviderPrompt = r.AugmentsProviderPrompt
        };

    public static string MakeKey(string toolId, string provider, string normalizedInput)
    {
        var raw = $"{toolId}|{provider}|{normalizedInput}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash);
    }
}

internal static class KyraLiveToolsSharedHttp
{
    private static HttpClient? _prod;

    public static HttpClient Create(HttpMessageHandler? testHandler)
    {
        if (testHandler is not null)
        {
            return new HttpClient(testHandler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        return _prod ??= new HttpClient(new HttpClientHandler(), disposeHandler: false)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
    }
}

internal static class KyraLiveToolHttp
{
    public static int EffectiveTimeoutSeconds(CopilotSettings settings)
    {
        var t = settings.LiveTools?.TimeoutSeconds ?? 0;
        if (t <= 0)
        {
            t = settings.TimeoutSeconds <= 0 ? 12 : settings.TimeoutSeconds;
        }

        return Math.Clamp(t, 3, 120);
    }

    public static TimeSpan CacheTtl(CopilotSettings settings)
    {
        var m = settings.LiveTools?.CacheMinutes ?? 10;
        if (m <= 0)
        {
            m = 10;
        }

        return TimeSpan.FromMinutes(Math.Clamp(m, 1, 120));
    }

    public static async Task<(bool Ok, bool TimedOut, string Body, int Code)> GetStringAsync(
        HttpClient http,
        string urlForRequest,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        try
        {
            using var response = await http.GetAsync(urlForRequest, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return (response.IsSuccessStatusCode, false, body, (int)response.StatusCode);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, true, string.Empty, 0);
        }
        catch
        {
            return (false, false, string.Empty, 0);
        }
    }

    public static JsonDocument? TryParseJson(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }
}
