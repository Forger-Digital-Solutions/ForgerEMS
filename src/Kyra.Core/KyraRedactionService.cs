namespace Kyra.Core;

/// <summary>Central redaction for Kyra persistence and provider-bound text.</summary>
public static class KyraRedactionService
{
    /// <summary>Redacts <paramref name="text"/> before writing to disk (logs, memory files). Semantically distinct from <see cref="RedactForProviders"/> even though the current implementation is identical.</summary>
    public static string RedactForPersistence(string? text, bool enabled = true) =>
        CopilotRedactor.Redact(text ?? string.Empty, enabled);

    /// <summary>Redacts <paramref name="text"/> before injecting into an online provider prompt. Semantically distinct from <see cref="RedactForPersistence"/> even though the current implementation is identical.</summary>
    public static string RedactForProviders(string? text, bool enabled = true) =>
        CopilotRedactor.Redact(text ?? string.Empty, enabled);
}
