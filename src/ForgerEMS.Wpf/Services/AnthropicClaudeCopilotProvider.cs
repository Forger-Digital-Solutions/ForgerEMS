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

public sealed class AnthropicClaudeCopilotProvider : ICopilotProvider
{
    public string Id => "anthropic-claude";
    public string DisplayName => "Anthropic / Claude";
    public CopilotProviderType ProviderType => CopilotProviderType.AnthropicClaude;
    public string Category => "Online AI";
    public bool IsOnlineProvider => true;
    public bool IsPaidProvider => false;
    public bool EnabledByDefault => false;
    public string DefaultBaseUrl => "https://api.anthropic.com/v1";
    public string DefaultModelName => "claude-3-5-haiku-latest";
    public string DefaultApiKeyEnvironmentVariable => "ANTHROPIC_API_KEY";
    public string StatusText => "Adapter shell ready. Full Messages API implementation is intentionally deferred.";

    public bool IsConfigured(CopilotProviderConfiguration configuration)
    {
        return !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
    }

    public bool CanHandle(CopilotProviderRequest request) => false;

    public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = false,
            UserMessage = "Claude provider shell is present but full API calls are not enabled yet. Offline fallback is available.",
            DiagnosticMessage = "Anthropic Messages adapter pending."
        });
    }
}
