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

    public string KeyStorageMode { get; set; } = "environment";

    public bool SavedKeyPresent { get; set; }

    public string SavedKeyStatus { get; set; } = string.Empty;

    public string LastTestResult { get; set; } = string.Empty;

    public bool IsPlaceholder { get; init; }

    public string ProviderStatusLabel { get; set; } = "Not configured";

    public string CredentialSourceText { get; set; } = string.Empty;

    public string DetailText =>
        $"{Category} | {ProviderStatusLabel} | {(IsPlaceholder ? "Placeholder/Future" : (IsPaidProvider ? "Paid/BYOK" : "Free/local"))} | {Status}";

    public string FriendlyStatus =>
        IsConfigured ? "Ready" :
        IsEnabled ? "Missing key or setup" :
        Id.Equals("local-offline", StringComparison.OrdinalIgnoreCase) ? "Local only" : "Disabled";

    public string SelectedModelDisplay =>
        string.IsNullOrWhiteSpace(ModelName) ? "Default model" : ModelName;

    public string CredentialBadgeText =>
        SavedKeyPresent ? "Protected key saved" :
        !string.IsNullOrWhiteSpace(MaskedApiKey) ? "Session key present" :
        string.IsNullOrWhiteSpace(CredentialSourceText) ? "No key saved" : CredentialSourceText;

    public string PrivacyNote =>
        Id.Equals("forgerems-gateway", StringComparison.OrdinalIgnoreCase)
            ? "ForgerEMS-managed gateway token; provider keys stay server-side."
            : Id.Equals("local-offline", StringComparison.OrdinalIgnoreCase) ||
              Id.Equals("ollama-local", StringComparison.OrdinalIgnoreCase) ||
              Id.Equals("lm-studio-local", StringComparison.OrdinalIgnoreCase)
                ? "Local/offline path. No cloud API key required."
                : "Your key is hidden; prompts use sanitized context according to Privacy settings.";
}
