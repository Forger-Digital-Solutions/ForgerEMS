using System.Globalization;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Constructs a <see cref="KyraFactsLedger"/> from the ForgerEMS host context types.</summary>
public static class KyraFactsLedgerFactory
{
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
