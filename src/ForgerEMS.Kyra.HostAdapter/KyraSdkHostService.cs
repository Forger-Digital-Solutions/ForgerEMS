using Kyra.Sdk;

namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>SDK-backed host service (active only when FORGEREMS_KYRA_SDK_ENABLED=true).</summary>
public sealed class KyraSdkHostService : IKyraHostService, IDisposable
{
    private readonly KyraSdkClient _client;

    public KyraSdkHostService(KyraSdkOptions? options = null) =>
        _client = new KyraSdkClient(options ?? new KyraSdkOptions
        {
            GatewayBaseUrl = null,
            Privacy = KyraSdkPrivacyOptions.SafeDefaults,
            DefaultMode = KyraSdkMode.LocalOnly,
        });

    public async Task<KyraHostResponse> ProcessAsync(KyraHostRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var mode = request.Mode ?? KyraHostMode.LocalOnly;
        var gateway = request.GatewayBaseUrl?.Trim();

        if (mode == KyraHostMode.WorkerOnly && string.IsNullOrWhiteSpace(gateway))
        {
            return KyraHostRequestMapper.NotConfigured(
                mode,
                "Worker mode requires a gateway URL. Configure gateway before enabling Worker-only SDK calls.");
        }

        var sdkRequest = KyraHostRequestMapper.ToSdkRequest(request);
        var sdkResponse = await _client.ProcessAsync(sdkRequest, cancellationToken).ConfigureAwait(false);
        return KyraHostRequestMapper.ToHostResponse(sdkResponse);
    }

    public void Dispose() => _client.Dispose();
}
