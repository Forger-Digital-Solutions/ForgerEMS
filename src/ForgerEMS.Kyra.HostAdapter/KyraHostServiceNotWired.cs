namespace ForgerEMS.Kyra.HostAdapter;

/// <summary>Placeholder host service until Kyra.Sdk is wired behind FORGEREMS_KYRA_SDK_ENABLED.</summary>
public sealed class KyraHostServiceNotWired : IKyraHostService
{
    public Task<KyraHostResponse> ProcessAsync(KyraHostRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);

        var mode = request.Mode ?? KyraHostMode.LocalOnly;
        return Task.FromResult(new KyraHostResponse
        {
            Succeeded = false,
            Mode = mode,
            ErrorCode = "NotWired",
            SafeMessage =
                "Kyra SDK host adapter is not wired. " +
                ForgerEmsKyraSdkFeatureFlags.DisabledUiLabel,
            LocalInvoked = false,
            WorkerInvoked = false,
        });
    }
}
