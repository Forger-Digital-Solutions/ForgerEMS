namespace VentoyToolkitSetup.Wpf.Services.Kyra;

public interface IKyraGatewayResearchClient
{
    Task<KyraGatewayResearchResponseDto> SendResearchAsync(
        string researchEndpoint,
        string bearerToken,
        KyraGatewayResearchRequestDto body,
        string appVersion,
        string releaseChannel,
        int timeoutSeconds,
        CancellationToken cancellationToken);
}
