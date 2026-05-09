#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: No ForgerEMS-specific coupling; eligible for Kyra.Core in Phase 3.
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Configuration;
using VentoyToolkitSetup.Wpf.Infrastructure;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class StubCopilotProvider : ICopilotProvider
{
    public StubCopilotProvider(CopilotProviderType providerType, string id, string displayName, string category, string statusText)
    {
        ProviderType = providerType;
        Id = id;
        DisplayName = displayName;
        Category = category;
        StatusText = statusText;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public CopilotProviderType ProviderType { get; }
    public string Category { get; }
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => string.Empty;
    public string DefaultModelName => string.Empty;
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText { get; }

    public bool IsConfigured(CopilotProviderConfiguration configuration) => false;

    public bool CanHandle(CopilotProviderRequest request)
    {
        var prompt = request.Prompt.ToLowerInvariant();
        return ProviderType switch
        {
            CopilotProviderType.EbayPricing => prompt.Contains("worth") || prompt.Contains("price") || prompt.Contains("sell") || prompt.Contains("value"),
            CopilotProviderType.GitHubReleases => prompt.Contains("toolkit") || prompt.Contains("update") || prompt.Contains("release"),
            CopilotProviderType.ManufacturerSupport => prompt.Contains("driver") || prompt.Contains("bios") || prompt.Contains("manufacturer"),
            CopilotProviderType.MicrosoftDocs => prompt.Contains("windows") || prompt.Contains("tpm") || prompt.Contains("secure boot"),
            CopilotProviderType.LinuxReleaseInfo => prompt.Contains("ubuntu") || prompt.Contains("mint") || prompt.Contains("xubuntu") || prompt.Contains("linux"),
            _ => true
        };
    }

    public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = false,
            UserMessage = $"{DisplayName} is a provider shell and is not configured yet. Offline fallback is available.",
            DiagnosticMessage = StatusText
        });
    }
}
