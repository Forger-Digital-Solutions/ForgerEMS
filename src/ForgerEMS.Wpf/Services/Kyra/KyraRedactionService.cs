using VentoyToolkitSetup.Wpf.Infrastructure;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Central redaction for Kyra persistence and provider-bound text.</summary>
public static class KyraRedactionService
{
    public static string RedactForPersistence(string? text, bool enabled = true) =>
        CopilotRedactor.Redact(text ?? string.Empty, enabled);

    public static string RedactForProviders(string? text, bool enabled = true) =>
        CopilotRedactor.Redact(text ?? string.Empty, enabled);
}
