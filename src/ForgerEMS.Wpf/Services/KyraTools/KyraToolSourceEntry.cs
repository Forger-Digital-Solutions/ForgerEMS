namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

public sealed class KyraToolSourceEntry
{
    public string Title { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string? Url { get; init; }

    public DateTimeOffset RetrievedAtUtc { get; init; }
}

public enum KyraLiveToolErrorKind
{
    None,
    NotConfigured,
    Disabled,
    Timeout,
    HttpError,
    ParseError,
    BadInput,
    Unknown
}
