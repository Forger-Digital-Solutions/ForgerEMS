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

public interface ICopilotProvider
{
    string Id { get; }

    string DisplayName { get; }

    CopilotProviderType ProviderType { get; }

    string Category { get; }

    bool IsOnlineProvider { get; }

    bool IsPaidProvider { get; }

    bool EnabledByDefault { get; }

    string DefaultBaseUrl { get; }

    string DefaultModelName { get; }

    string DefaultApiKeyEnvironmentVariable { get; }

    string StatusText { get; }

    bool IsConfigured(CopilotProviderConfiguration configuration);

    bool CanHandle(CopilotProviderRequest request);

    Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken);
}

public interface IKyraProvider : ICopilotProvider
{
    KyraProviderKind Kind { get; }
}

public sealed class KyraProviderPool
{
    private readonly IReadOnlyList<ICopilotProvider> _providers;

    public KyraProviderPool(IReadOnlyList<ICopilotProvider> providers)
    {
        _providers = providers;
    }

    public IReadOnlyList<ICopilotProvider> GetPrioritizedProviders(CopilotSettings settings)
    {
        var index = KyraProviderPriority.DefaultOrder
            .Select((id, order) => new { id, order })
            .ToDictionary(item => item.id, item => item.order, StringComparer.OrdinalIgnoreCase);
        return _providers
            .OrderBy(provider => index.TryGetValue(provider.Id, out var order) ? order : int.MaxValue)
            .ThenBy(provider => provider.IsPaidProvider ? 1 : 0)
            .ToArray();
    }
}

public interface ICopilotProviderRegistry
{
    IReadOnlyList<ICopilotProvider> Providers { get; }

    ICopilotProvider? FindById(string id);

    ICopilotProvider? FindByType(CopilotProviderType providerType);
}

public interface ICopilotContextBuilder
{
    CopilotContext Build(CopilotRequest request);
}

public interface ICopilotSettingsStore
{
    CopilotSettings Load();

    void Save(CopilotSettings settings);
}

public interface ICopilotService
{
    Task<CopilotResponse> GenerateReplyAsync(CopilotRequest request, CancellationToken cancellationToken = default);

    void ClearMemory();
}
