using System.Net;

namespace VentoyToolkitSetup.Wpf.Services;

public enum SafetyCheckSeverity
{
    CleanOrLowConcern,
    UnknownManualReview,
    Suspicious,
    KnownSafeSecurityTestFixture,
    SimulatedMalwareTestFixture,
    SimulatedPhishingTestFixture,
    Quarantined,
    BlockedByExternalSecurity,
    DownloadFailed,
    RejectedByPolicy,
    InvalidInput,
    LocalFileNotFound,
    LocalFileReadBlocked,
    HashComputed,
    SignatureChecked,
    NotExecutable,
    ExecutableMetadataOnly
}

public enum QuarantineOutcome
{
    InvalidInput,
    RejectedByPolicy,
    DownloadFailed,
    BlockedByExternalSecurity,
    Quarantined
}

public sealed class SafetyCheckResult
{
    public SafetyCheckSeverity Severity { get; init; } = SafetyCheckSeverity.UnknownManualReview;

    public IReadOnlyList<SafetyCheckSeverity> States { get; init; } = [];

    public string Verdict { get; init; } = "Manual review still required.";

    public IReadOnlyList<string> Evidence { get; init; } = [];
}

public sealed class KnownSecurityTestFixture
{
    public static KnownSecurityTestFixture None { get; } = new();

    public bool IsKnown { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public SafetyCheckSeverity PrimarySeverity { get; init; } = SafetyCheckSeverity.UnknownManualReview;

    public IReadOnlyList<SafetyCheckSeverity> Classifications { get; init; } = [];
}

public sealed class UrlSafetyAnalysisOptions
{
    public const int DefaultMaxRedirects = 5;
    public const int DefaultMaxHtmlPreviewBytes = 32 * 1024;
    public const int MinHtmlPreviewBytes = 16 * 1024;
    public const int MaximumHtmlPreviewBytes = 64 * 1024;

    public int MaxRedirects { get; init; } = DefaultMaxRedirects;

    public int MaxHtmlPreviewBytes { get; init; } = DefaultMaxHtmlPreviewBytes;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
}

public sealed class UrlSafetyAnalysisResult
{
    public SafetyCheckSeverity Severity { get; init; } = SafetyCheckSeverity.UnknownManualReview;

    public LinkSafetyBand Band { get; init; } = LinkSafetyBand.Unknown;

    public IReadOnlyList<SafetyCheckSeverity> States { get; init; } = [];

    public string Verdict { get; init; } = "Manual review still required.";

    public string? OriginalUrl { get; init; }

    public string? FinalUrl { get; init; }

    public bool IsHttps { get; init; }

    public HttpStatusCode? StatusCode { get; init; }

    public string? ContentType { get; init; }

    public long? ContentLength { get; init; }

    public string? ContentDispositionFileName { get; init; }

    public string? SuspiciousExtension { get; init; }

    public bool HeadAttempted { get; init; }

    public bool GetFallbackAttempted { get; init; }

    public bool HtmlPreviewRead { get; init; }

    public int HtmlPreviewBytesRead { get; init; }

    public bool AnalyzeSavedPayload { get; init; }

    public KnownSecurityTestFixture Fixture { get; init; } = KnownSecurityTestFixture.None;

    public IReadOnlyList<string> Evidence { get; init; } = [];
}

public sealed class QuarantineDownloadOptions
{
    public const long DefaultMaxDownloadBytes = 50L * 1024L * 1024L;

    public string? QuarantineRoot { get; init; }

    public long MaxDownloadBytes { get; init; } = DefaultMaxDownloadBytes;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    public int MaxRedirects { get; init; } = UrlSafetyAnalysisOptions.DefaultMaxRedirects;

    public Func<string, CancellationToken, Task>? AfterPayloadWriteAsync { get; init; }
}

public sealed class QuarantineDownloadResult
{
    public QuarantineOutcome Outcome { get; init; } = QuarantineOutcome.DownloadFailed;

    public IReadOnlyList<SafetyCheckSeverity> States { get; init; } = [];

    public string Verdict { get; init; } = "Download failed.";

    public string? OriginalUrl { get; init; }

    public string? FinalUrl { get; init; }

    public DateTimeOffset UtcTimestamp { get; init; } = DateTimeOffset.UtcNow;

    public int? HttpStatus { get; init; }

    public Dictionary<string, string> HeadersSummary { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ContentType { get; init; }

    public long? ContentLength { get; init; }

    public long BytesAttempted { get; init; }

    public long BytesWritten { get; init; }

    public string? Sha256Hex { get; init; }

    public KnownSecurityTestFixture Fixture { get; init; } = KnownSecurityTestFixture.None;

    public string? QuarantineRoot { get; init; }

    public string? DownloadDirectory { get; init; }

    public string? PayloadPath { get; init; }

    public string? MetadataPath { get; init; }

    public string? ErrorCategory { get; init; }

    public string? ErrorMessage { get; init; }

    public bool FinalFileExists { get; init; }

    public bool ExternalSecurityLikelyIntercepted { get; init; }

    public bool MarkOfTheWebWritten { get; init; }
}
