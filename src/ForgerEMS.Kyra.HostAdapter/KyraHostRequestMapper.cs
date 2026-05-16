using Kyra.Sdk;

namespace ForgerEMS.Kyra.HostAdapter;

internal static class KyraHostRequestMapper
{
    public static KyraSdkRequest ToSdkRequest(KyraHostRequest request)
    {
        var privacy = request.Privacy ?? KyraHostPrivacyOptions.SafeDefaults;
        var mode = ResolveSdkMode(request);

        return new KyraSdkRequest
        {
            Mode = mode,
            UserPrompt = request.UserPrompt?.Trim(),
            GatewayBaseUrl = string.IsNullOrWhiteSpace(request.GatewayBaseUrl) ? null : request.GatewayBaseUrl.Trim(),
            BearerToken = string.IsNullOrWhiteSpace(request.BearerToken) ? null : request.BearerToken,
            Privacy = new KyraSdkPrivacyOptions
            {
                AllowCloudContextSharing = privacy.AllowCloudContextSharing,
                AllowWorkerEnrichment = privacy.AllowWorkerEnrichment,
                RedactedCloudContextSummary = privacy.RedactedCloudContextSummary,
            },
            HostContext = ToSdkHostContext(request),
        };
    }

    public static KyraHostResponse ToHostResponse(KyraSdkResponse response) =>
        new()
        {
            Succeeded = response.Succeeded,
            Mode = ToHostMode(response.Mode),
            Text = response.Text,
            ErrorCode = response.ErrorCode,
            SafeMessage = response.SafeMessage,
            LocalInvoked = response.LocalInvoked,
            WorkerInvoked = response.WorkerInvoked,
            WorkerSkippedForPrivacy = response.WorkerSkippedForPrivacy,
        };

    private static KyraHostMode ToHostMode(KyraSdkMode mode) =>
        mode switch
        {
            KyraSdkMode.LocalOnly => KyraHostMode.LocalOnly,
            KyraSdkMode.WorkerOnly => KyraHostMode.WorkerOnly,
            KyraSdkMode.Combined => KyraHostMode.Combined,
            _ => KyraHostMode.LocalOnly,
        };

    public static KyraHostResponse NotConfigured(KyraHostMode mode, string safeMessage) =>
        new()
        {
            Succeeded = false,
            Mode = mode,
            ErrorCode = "NotConfigured",
            SafeMessage = safeMessage,
            LocalInvoked = false,
            WorkerInvoked = false,
        };

    private static KyraSdkMode ResolveSdkMode(KyraHostRequest request)
    {
        var mode = request.Mode ?? KyraHostMode.LocalOnly;
        return mode switch
        {
            KyraHostMode.LocalOnly => KyraSdkMode.LocalOnly,
            KyraHostMode.WorkerOnly => KyraSdkMode.WorkerOnly,
            KyraHostMode.Combined => KyraSdkMode.Combined,
            _ => KyraSdkMode.LocalOnly,
        };
    }

    private static KyraSdkHostContext? ToSdkHostContext(KyraHostRequest request)
    {
        var safeMetadata = KyraHostContextMapper.MapSafeMetadata(request);
        var appId = string.IsNullOrWhiteSpace(request.HostApplicationId) ? "ForgerEMS" : request.HostApplicationId.Trim();
        var sessionId = request.HostSessionId?.Trim();

        if (safeMetadata is null && string.IsNullOrWhiteSpace(sessionId))
        {
            return new KyraSdkHostContext { HostApplicationId = appId };
        }

        return new KyraSdkHostContext
        {
            HostApplicationId = appId,
            HostSessionId = sessionId,
            SafeMetadata = safeMetadata,
        };
    }
}
