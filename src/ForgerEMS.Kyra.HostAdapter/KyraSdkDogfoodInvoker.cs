namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>Hidden/dev Kyra.Sdk dogfood entry (requires FORGEREMS_KYRA_SDK_ENABLED=true).</summary>
public static class KyraSdkDogfoodInvoker
{
    public static async Task<KyraSdkDogfoodResult> InvokeAsync(
        KyraSdkDogfoodOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new KyraSdkDogfoodOptions();
        cancellationToken.ThrowIfCancellationRequested();

        var privacy = options.Privacy ?? KyraHostPrivacyOptions.SafeDefaults;
        var gatewayUrl = options.GatewayBaseUrl ?? KyraSdkDogfoodEnvironment.ReadGatewayUrl();
        var bearerToken = options.BearerToken ?? KyraSdkDogfoodEnvironment.ReadGatewayBetaToken();
        var sdkFlagActive = ForgerEmsKyraSdkFeatureFlags.IsSdkEnabledFromEnvironment();

        var hostRequest = new KyraHostRequest
        {
            Mode = options.Mode,
            UserPrompt = string.IsNullOrWhiteSpace(options.UserPrompt)
                ? KyraSdkDogfoodOptions.DefaultUserPrompt
                : options.UserPrompt.Trim(),
            GatewayBaseUrl = gatewayUrl,
            BearerToken = bearerToken,
            Privacy = privacy,
            HostApplicationId = "ForgerEMS",
            HostSessionId = KyraSdkDogfoodOptions.DogfoodFeatureId,
            HostApplicationVersion = options.HostApplicationVersion,
        };

        var host = CreateHostService(gatewayUrl);
        try
        {
            var response = await host.ProcessAsync(hostRequest, cancellationToken).ConfigureAwait(false);
            return BuildResult(privacy, gatewayUrl, bearerToken, sdkFlagActive, response);
        }
        finally
        {
            if (host is IDisposable disposable)
                disposable.Dispose();
        }
    }

    private static KyraSdkDogfoodResult BuildResult(
        KyraHostPrivacyOptions privacy,
        string? gatewayUrl,
        string? bearerToken,
        bool sdkFlagActive,
        KyraHostResponse response) =>
        new()
        {
            SdkFlagActive = sdkFlagActive,
            Succeeded = response.Succeeded,
            ErrorCode = response.ErrorCode,
            SafeMessage = response.SafeMessage,
            Mode = response.Mode,
            LocalInvoked = response.LocalInvoked,
            WorkerInvoked = response.WorkerInvoked,
            GatewayUrlConfigured = !string.IsNullOrWhiteSpace(gatewayUrl),
            GatewayTokenPresent = !string.IsNullOrWhiteSpace(bearerToken),
            AllowCloudContextSharing = privacy.AllowCloudContextSharing,
            AllowWorkerEnrichment = privacy.AllowWorkerEnrichment,
            ResponseTextPreview = TruncatePreview(response.Text),
        };

    private static IKyraHostService CreateHostService(string? gatewayUrl)
    {
        if (!ForgerEmsKyraSdkFeatureFlags.IsSdkEnabledFromEnvironment())
            return new KyraHostServiceNotWired();

        return string.IsNullOrWhiteSpace(gatewayUrl)
            ? KyraHostServiceFactory.Create()
            : KyraHostServiceFactory.Create(new KyraSdkHostServiceOptions { GatewayBaseUrl = gatewayUrl });
    }

    private static string? TruncatePreview(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        return trimmed.Length <= 240 ? trimmed : trimmed[..240] + "…";
    }
}
