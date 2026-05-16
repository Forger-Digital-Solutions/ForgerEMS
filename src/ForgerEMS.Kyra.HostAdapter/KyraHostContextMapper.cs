namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>Maps EMS device/report context into Kyra-safe host metadata (redacted; no raw logs/paths/secrets).</summary>
public static class KyraHostContextMapper
{
    private static readonly string[] ForbiddenMetadataKeys =
    [
        "memory", "memoryBody", "filePath", "localFile", "secret", "token", "apiKey", "password",
        "authorization", "bearer", "serial", "username", "supportBundle", "chatHistory",
    ];

    public static IReadOnlyDictionary<string, string>? MapSafeMetadata(KyraHostRequest request)
    {
        if (request is null)
            return null;

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(request.HostApplicationId))
            dict["hostApplicationId"] = RedactScalar(request.HostApplicationId);

        if (!string.IsNullOrWhiteSpace(request.HostSessionId))
            dict["hostSessionId"] = RedactScalar(request.HostSessionId);

        if (!string.IsNullOrWhiteSpace(request.HostApplicationVersion))
            dict["appVersion"] = RedactScalar(request.HostApplicationVersion);

        if (string.Equals(request.HostSessionId, KyraSdkDogfoodOptions.DogfoodFeatureId, StringComparison.OrdinalIgnoreCase))
            dict["feature"] = KyraSdkDogfoodOptions.DogfoodFeatureId;

        var summary = request.RedactedDeviceReportSummary;
        if (!string.IsNullOrWhiteSpace(summary) &&
            request.Privacy?.AllowCloudContextSharing == true)
        {
            dict["emsRedactedReportSummary"] = RedactSummary(summary);
        }

        return dict.Count == 0 ? null : dict;
    }

    internal static string RedactSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var text = value.Trim();
        if (text.Length > 512)
            text = text[..512] + "…";

        if (LooksLikePath(text) || ContainsForbiddenKey(text))
            return "[REDACTED_EMS_CONTEXT]";

        return text;
    }

    private static string RedactScalar(string value)
    {
        var text = value.Trim();
        return text.Length > 128 ? text[..128] : text;
    }

    private static bool ContainsForbiddenKey(string text) =>
        ForbiddenMetadataKeys.Any(key => text.Contains(key, StringComparison.OrdinalIgnoreCase));

    private static bool LooksLikePath(string text) =>
        text.Contains('\\') || text.Contains("C:", StringComparison.OrdinalIgnoreCase);
}

