namespace VentoyToolkitSetup.Wpf.Models;

public sealed class CopilotProviderSettingView
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public bool IsEnabled { get; set; }

    public bool IsConfigured { get; set; }

    public bool IsPaidProvider { get; init; }

    public string BaseUrl { get; set; } = string.Empty;

    public string ModelName { get; set; } = string.Empty;

    public string ApiKeyEnvironmentVariable { get; set; } = string.Empty;

    public string SessionApiKey { get; set; } = string.Empty;

    public string MaskedApiKey { get; set; } = string.Empty;

    public bool IsPlaceholder { get; init; }

    public string ProviderStatusLabel { get; set; } = "Not configured";

    public string CredentialSourceText { get; set; } = string.Empty;

    public string DetailText =>
        $"{Category} | {ProviderStatusLabel} | {(IsPlaceholder ? "Placeholder/Future" : (IsPaidProvider ? "Paid/BYOK" : "Free/local"))} | {Status}";
}
