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

public sealed class LocalOfflineCopilotProvider : ICopilotProvider
{
    public string Id => "local-offline";
    public string DisplayName => "Local Offline Rules";
    public CopilotProviderType ProviderType => CopilotProviderType.LocalOffline;
    public string Category => "Offline fallback";
    public bool IsOnlineProvider => false;
    public bool IsPaidProvider => true;
    public bool EnabledByDefault => true;
    public string DefaultBaseUrl => string.Empty;
    public string DefaultModelName => "local-rules";
    public string DefaultApiKeyEnvironmentVariable => string.Empty;
    public string StatusText => "Always available. Uses local rules and local scan JSON only.";

    public bool IsConfigured(CopilotProviderConfiguration configuration) => true;

    public bool CanHandle(CopilotProviderRequest request) => true;

    public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var answer = LocalRulesCopilotEngine.GenerateReply(request.Prompt, request.Context);
        return Task.FromResult(new CopilotProviderResult
        {
            Succeeded = true,
            UsedOnlineData = false,
            UserMessage = answer,
            DiagnosticMessage = "Local offline answer."
        });
    }
}
