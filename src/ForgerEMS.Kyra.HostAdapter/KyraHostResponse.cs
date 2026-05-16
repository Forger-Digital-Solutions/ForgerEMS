namespace ForgerEMS.Kyra.HostAdapter;

public sealed class KyraHostResponse
{
    public bool Succeeded { get; init; }

    public KyraHostMode Mode { get; init; }

    public string? Text { get; init; }

    public string? ErrorCode { get; init; }

    public string? SafeMessage { get; init; }

    public bool LocalInvoked { get; init; }

    public bool WorkerInvoked { get; init; }

    public bool WorkerSkippedForPrivacy { get; init; }
}
