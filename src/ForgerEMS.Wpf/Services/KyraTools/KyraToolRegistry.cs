using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.KyraTools;

public sealed class KyraToolRegistry
{
    private readonly IReadOnlyList<IKyraTool> _tools;

    public KyraToolRegistry()
    {
        _tools =
        [
            new SystemContextKyraTool(),
            new ToolkitHealthKyraTool(),
            new DiagnosticsKyraTool(),
            new WeatherKyraTool(),
            new NewsKyraTool(),
            new CryptoPriceKyraTool(),
            new StockPriceKyraTool(),
            new SportsKyraTool(),
            new WebSearchKyraTool(),
            new MarketplaceKyraTool(),
            new CodeAssistKyraTool()
        ];
    }

    public IReadOnlyList<IKyraTool> Tools => _tools;

    public static string FormatStatusLabel(KyraToolOperationalStatus status) =>
        status switch
        {
            KyraToolOperationalStatus.Ready => "Ready",
            KyraToolOperationalStatus.NotConfigured => "Not configured",
            KyraToolOperationalStatus.Disabled => "Disabled",
            KyraToolOperationalStatus.Failed => "Failed",
            KyraToolOperationalStatus.MissingScan => "Missing scan",
            KyraToolOperationalStatus.Available => "Available",
            _ => "Unknown"
        };

    public static string FormatCategoryLabel(KyraToolSurfaceCategory category) =>
        category switch
        {
            KyraToolSurfaceCategory.LiveData => "Live data",
            KyraToolSurfaceCategory.LocalContext => "Local context",
            KyraToolSurfaceCategory.CodeAssist => "Code assist",
            KyraToolSurfaceCategory.Marketplace => "Marketplace",
            _ => "Other"
        };

    /// <summary>True when at least one live/marketplace tool is actually configured (not stub).</summary>
    public bool HasConfiguredLiveDataCapability(CopilotSettings settings, KyraToolHostFacts facts)
    {
        foreach (var tool in _tools)
        {
            if (tool.SurfaceCategory is not (KyraToolSurfaceCategory.LiveData or KyraToolSurfaceCategory.Marketplace))
            {
                continue;
            }

            var st = tool.GetOperationalStatus(settings, facts);
            if (st is KyraToolOperationalStatus.Ready or KyraToolOperationalStatus.Available)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Rows for Advanced panel grid (no API keys or secrets).</summary>
    public IReadOnlyList<KyraToolStatusRowView> BuildStatusGridRows(CopilotSettings settings, KyraToolHostFacts facts)
    {
        var list = new List<KyraToolStatusRowView>();
        foreach (var tool in _tools.OrderBy(t => t.SurfaceCategory).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var st = tool.GetOperationalStatus(settings, facts);
            list.Add(new KyraToolStatusRowView
            {
                ToolName = tool.Name,
                Category = FormatCategoryLabel(tool.SurfaceCategory),
                Status = FormatStatusLabel(st),
                Provider = DescribeProviderColumn(tool, st),
                LastChecked = "—",
                Notes = BuildNotes(tool, st)
            });
        }

        return list;
    }

    /// <summary>Multi-line block for /provider (and host snapshot).</summary>
    public string BuildProviderToolDetailText(CopilotSettings settings, KyraToolHostFacts facts, bool verboseLiveLogs)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Verbose beta logs: {(verboseLiveLogs ? "on" : "off")}");
        sb.AppendLine();
        foreach (var tool in _tools.OrderBy(t => t.SurfaceCategory).ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var st = tool.GetOperationalStatus(settings, facts);
            sb.AppendLine($"- **{tool.Name}** ({FormatCategoryLabel(tool.SurfaceCategory)}): {FormatStatusLabel(st)}");
        }

        sb.AppendLine();
        sb.AppendLine("Live weather, news, and market tools stay “not configured” until real APIs are wired — Kyra will not invent prices or headlines.");
        return sb.ToString().TrimEnd();
    }

    public string BuildStatusSummary() =>
        "See /provider or Kyra Advanced → Tools for per-tool status. Stubs report “not configured” until APIs are added.";

    /// <summary>Builds a single augmentation block for API providers (disclaimers + stub status).</summary>
    public async Task<string?> BuildAugmentationAsync(
        KyraToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        foreach (var tool in _tools)
        {
            if (!tool.CanHandle(request.Intent, request.Prompt))
            {
                continue;
            }

            var result = await tool.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (result.AugmentsProviderPrompt && !string.IsNullOrWhiteSpace(result.ProviderAugmentation))
            {
                sb.AppendLine(result.ProviderAugmentation.Trim());
                sb.AppendLine();
            }
        }

        var text = sb.ToString().Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string DescribeProviderColumn(IKyraTool tool, KyraToolOperationalStatus st)
    {
        if (st == KyraToolOperationalStatus.NotConfigured)
        {
            return "—";
        }

        if (tool.SurfaceCategory is KyraToolSurfaceCategory.LiveData or KyraToolSurfaceCategory.Marketplace)
        {
            return st == KyraToolOperationalStatus.Ready ? "Configured provider" : "ForgerEMS (local)";
        }

        return "ForgerEMS (local)";
    }

    private static string BuildNotes(IKyraTool tool, KyraToolOperationalStatus st)
    {
        if (st == KyraToolOperationalStatus.MissingScan)
        {
            return tool.Name.Contains("Toolkit", StringComparison.OrdinalIgnoreCase)
                ? "Run Toolkit Manager scan to populate health context."
                : "Run System Intelligence scan for machine-specific context.";
        }

        if (st == KyraToolOperationalStatus.NotConfigured &&
            tool.SurfaceCategory is KyraToolSurfaceCategory.LiveData or KyraToolSurfaceCategory.Marketplace)
        {
            return "No live API wired in this build; Kyra answers honestly when data is unavailable.";
        }

        if (tool.SurfaceCategory == KyraToolSurfaceCategory.CodeAssist)
        {
            return "Routes pasted snippets to code-assist prompts; never executes code.";
        }

        return tool.Description;
    }
}
