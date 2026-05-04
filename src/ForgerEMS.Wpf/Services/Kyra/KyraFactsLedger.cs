using System.Globalization;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Sources for stable facts Kyra treats as authoritative over provider prose.</summary>
public enum KyraFactSource
{
    SystemIntelligence,
    UsbBuilder,
    ToolkitManager,
    UpdateSystem,
    UserMessage,
    KyraLocalAnalysis,
    ProviderEnhancement
}

/// <summary>Trust ordering: higher numeric value = lower authority vs local ForgerEMS facts.</summary>
public enum KyraTrustLevel
{
    ForgerEMSLocalFact = 1,
    UserProvidedFact = 2,
    KyraDerivedLocalAnalysis = 3,
    ProviderSuggestion = 4,
    GeneralKnowledge = 5
}

/// <summary>Compact ledger of machine facts extracted from local context (never from API text).</summary>
public sealed class KyraFactsLedger
{
    public bool HasSystemIntelligenceProfile { get; init; }

    public string DeviceSummary { get; init; } = string.Empty;

    public string CpuSummary { get; init; } = string.Empty;

    public string GpuSummary { get; init; } = string.Empty;

    public string RamSummary { get; init; } = string.Empty;

    public string StorageSummary { get; init; } = string.Empty;

    public string OsSummary { get; init; } = string.Empty;

    public string UsbHeadline { get; init; } = string.Empty;

    public string ToolkitHeadline { get; init; } = string.Empty;

    public int? HealthScore { get; init; }

    /// <summary>True when we have enough structured hardware facts to reject “I can’t see your PC” API answers.</summary>
    public bool HasTrustedLocalHardwareFacts =>
        HasSystemIntelligenceProfile ||
        (!string.IsNullOrWhiteSpace(CpuSummary) && !CpuSummary.Contains("Unknown", StringComparison.OrdinalIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(DeviceSummary) && !DeviceSummary.Contains("Unknown device", StringComparison.OrdinalIgnoreCase));

    public static KyraFactsLedger FromCopilotContext(CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is not null)
        {
            var gpu = profile.Gpus.Count == 0
                ? "Unknown GPU"
                : string.Join("; ", profile.Gpus.Select(g => g.Name).Take(2));
            var storage = profile.Disks.Count == 0
                ? "Storage unknown"
                : string.Join("; ", profile.Disks.Select(d => $"{d.MediaType} {d.Size}").Take(2));

            return new KyraFactsLedger
            {
                HasSystemIntelligenceProfile = true,
                DeviceSummary = $"{profile.Manufacturer} {profile.Model}".Trim(),
                CpuSummary = profile.Cpu,
                GpuSummary = gpu,
                RamSummary = profile.RamTotal,
                StorageSummary = storage,
                OsSummary = $"{profile.OperatingSystem} ({profile.OsBuild})",
                UsbHeadline = SummarizeUsb(context),
                ToolkitHeadline = SummarizeToolkit(context),
                HealthScore = context.HealthEvaluation?.HealthScore
            };
        }

        var sc = context.SystemContext;
        return new KyraFactsLedger
        {
            HasSystemIntelligenceProfile = false,
            DeviceSummary = sc.Device,
            CpuSummary = sc.CPU,
            GpuSummary = sc.GPU,
            RamSummary = sc.RAM > 0 ? $"{sc.RAM.ToString(CultureInfo.InvariantCulture)} GB" : string.Empty,
            StorageSummary = sc.Storage,
            OsSummary = sc.OS,
            UsbHeadline = SummarizeUsb(context),
            ToolkitHeadline = SummarizeToolkit(context),
            HealthScore = context.HealthEvaluation?.HealthScore
        };
    }

    public string ToPromptSummaryBlock()
    {
        if (!HasTrustedLocalHardwareFacts)
        {
            return "Facts ledger: no full System Intelligence profile — treat hardware specifics as uncertain until a scan exists.";
        }

        var lines = new List<string>
        {
            "Facts ledger (ForgerEMS local — authoritative over any model guess):",
            $"Device: {DeviceSummary}",
            $"CPU: {CpuSummary}",
            $"GPU: {GpuSummary}",
            $"RAM: {RamSummary}",
            $"Storage: {StorageSummary}",
            $"OS: {OsSummary}",
            HealthScore is { } hs ? $"Health score: {hs}/100" : "Health score: (not loaded)"
        };

        if (!string.IsNullOrWhiteSpace(UsbHeadline))
        {
            lines.Add($"USB: {UsbHeadline}");
        }

        if (!string.IsNullOrWhiteSpace(ToolkitHeadline))
        {
            lines.Add($"Toolkit: {ToolkitHeadline}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string SummarizeUsb(CopilotContext context)
    {
        var u = context.UserQuestion;
        if (string.IsNullOrWhiteSpace(u))
        {
            return string.Empty;
        }

        return u.Contains("usb", StringComparison.OrdinalIgnoreCase) ? "USB topic referenced in question." : string.Empty;
    }

    private static string SummarizeToolkit(CopilotContext context)
    {
        var u = context.UserQuestion;
        if (string.IsNullOrWhiteSpace(u))
        {
            return string.Empty;
        }

        return u.Contains("toolkit", StringComparison.OrdinalIgnoreCase) ? "Toolkit topic referenced." : string.Empty;
    }
}
