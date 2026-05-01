using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

public interface IKyraTool
{
    string Name { get; }

    string Description { get; }

    KyraToolSurfaceCategory SurfaceCategory { get; }

    bool CanHandle(KyraIntent intent, string prompt);

    Task<KyraToolResult> ExecuteAsync(KyraToolExecutionRequest request, CancellationToken cancellationToken);

    /// <summary>UI/provider status; must not include secrets or API key values.</summary>
    KyraToolOperationalStatus GetOperationalStatus(CopilotSettings settings, KyraToolHostFacts facts);
}

public sealed class KyraToolExecutionRequest
{
    public KyraIntent Intent { get; init; }

    public string Prompt { get; init; } = string.Empty;

    public CopilotContext Context { get; init; } = new();

    public CopilotSettings Settings { get; init; } = new();
}

public sealed class KyraToolResult
{
    public bool AugmentsProviderPrompt { get; init; }

    /// <summary>Appended to provider context when <see cref="AugmentsProviderPrompt"/> is true.</summary>
    public string ProviderAugmentation { get; init; } = string.Empty;
}
