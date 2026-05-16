using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>GET /v1/kyra/status — safe provider readiness flags (no secret values).</summary>
public static class KyraGatewayStatusClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string BuildStatusEndpoint(string gatewayBaseUrl)
    {
        var b = (gatewayBaseUrl ?? string.Empty).Trim().TrimEnd('/');
        if (b.Length == 0)
        {
            return string.Empty;
        }

        if (b.EndsWith("/v1/kyra/status", StringComparison.OrdinalIgnoreCase))
        {
            return b;
        }

        return b + "/v1/kyra/status";
    }

    public static async Task<KyraGatewayStatusResponseDto> FetchAsync(
        string statusEndpoint,
        string bearerToken,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(statusEndpoint))
        {
            return new KyraGatewayStatusResponseDto { Ok = false, ErrorCode = "no_endpoint" };
        }

        using var http = new HttpClient();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 3, 60)));

        using var req = new HttpRequestMessage(HttpMethod.Get, statusEndpoint);
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearerToken.Trim());

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new KyraGatewayStatusResponseDto { Ok = false, ErrorCode = "timeout" };
        }
        catch (HttpRequestException)
        {
            return new KyraGatewayStatusResponseDto { Ok = false, ErrorCode = "network" };
        }

        var raw = await resp.Content.ReadAsStringAsync(linked.Token).ConfigureAwait(false);
        KyraGatewayStatusResponseDto? parsed = null;
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                parsed = JsonSerializer.Deserialize<KyraGatewayStatusResponseDto>(raw, JsonOptions);
            }
            catch (JsonException)
            {
                parsed = null;
            }
        }

        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            return parsed ?? new KyraGatewayStatusResponseDto { Ok = false, ErrorCode = "unauthorized" };
        }

        if (resp.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return parsed ?? new KyraGatewayStatusResponseDto { Ok = false, ErrorCode = "rate_limited" };
        }

        if (!resp.IsSuccessStatusCode)
        {
            return parsed ?? new KyraGatewayStatusResponseDto { Ok = false, ErrorCode = $"http_{(int)resp.StatusCode}" };
        }

        return parsed ?? new KyraGatewayStatusResponseDto { Ok = false, ErrorCode = "bad_response" };
    }
}
