using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public sealed class KyraGatewayResearchClient : IKyraGatewayResearchClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public KyraGatewayResearchClient(HttpClient? httpClient = null) =>
        _http = httpClient ?? new HttpClient();

    public async Task<KyraGatewayResearchResponseDto> SendResearchAsync(
        string researchEndpoint,
        string bearerToken,
        KyraGatewayResearchRequestDto body,
        string appVersion,
        string releaseChannel,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 3, 120)));

        using var req = new HttpRequestMessage(HttpMethod.Post, researchEndpoint);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerToken.Trim());
        req.Headers.TryAddWithoutValidation("X-ForgerEMS-Version", SanitizeHeader(appVersion));
        req.Headers.TryAddWithoutValidation("X-ForgerEMS-Channel", SanitizeHeader(releaseChannel));
        req.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

        using var resp = await _http.SendAsync(req, linked.Token).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        KyraGatewayResearchResponseDto? parsed = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                parsed = JsonSerializer.Deserialize<KyraGatewayResearchResponseDto>(raw, JsonOptions);
            }
            catch (JsonException)
            {
                parsed = null;
            }
        }

        if (resp.IsSuccessStatusCode && parsed is { Ok: true } && !string.IsNullOrWhiteSpace(parsed.Answer))
        {
            return parsed;
        }

        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return parsed ?? new KyraGatewayResearchResponseDto
            {
                Ok = false,
                ErrorCode = "rate_limited",
                SafeMessage =
                    "The realtime gateway is rate-limited right now. Try again in a minute or use local Kyra."
            };
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new KyraGatewayResearchResponseDto
            {
                Ok = false,
                ErrorCode = "unauthorized",
                SafeMessage =
                    "Realtime gateway access was denied. Check your gateway token configuration."
            };
        }

        return parsed ?? new KyraGatewayResearchResponseDto
        {
            Ok = false,
            ErrorCode = $"http_{(int)resp.StatusCode}",
            SafeMessage =
                "I couldn’t reach the realtime research gateway. Local Kyra is still available."
        };
    }

    private static string SanitizeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var v = value.Trim();
        return v.Length > 64 ? v[..64] : v;
    }

    public static string BuildResearchEndpoint(string gatewayBaseUrl)
    {
        var b = (gatewayBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (b.Length == 0)
        {
            return string.Empty;
        }

        if (b.EndsWith("/v1/kyra/research", StringComparison.OrdinalIgnoreCase))
        {
            return b;
        }

        return b + "/v1/kyra/research";
    }
}
