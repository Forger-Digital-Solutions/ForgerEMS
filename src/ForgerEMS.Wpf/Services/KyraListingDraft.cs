using System.Linq;
using System.Text;

namespace VentoyToolkitSetup.Wpf.Services;

/// <summary>Safe marketplace listing copy — no serials, keys, paths, or exact addresses.</summary>
public static class KyraListingDraft
{
    public enum ListingChannel
    {
        Facebook,
        OfferUp,
        Ebay
    }

    public static string Build(SystemProfile? profile, ListingChannel channel, SystemHealthEvaluation? health)
    {
        if (profile is null)
        {
            return "Run System Intelligence first — I need sanitized specs before drafting a listing.";
        }

        var title = channel switch
        {
            ListingChannel.Ebay => $"Laptop — {profile.Manufacturer} {profile.Model} — see details",
            ListingChannel.OfferUp => $"{profile.Manufacturer} {profile.Model} laptop",
            _ => $"{profile.Manufacturer} {profile.Model} — refreshed, honest condition"
        };

        var sb = new StringBuilder();
        sb.AppendLine($"Suggested title: {title}");
        sb.AppendLine();
        sb.AppendLine("Bullets (safe — verify before posting):");
        sb.AppendLine($"- CPU: {profile.Cpu}");
        sb.AppendLine($"- RAM: {profile.RamTotal}");
        sb.AppendLine($"- Storage: {SummarizeDisks(profile)}");
        sb.AppendLine($"- Display/GPU: {SummarizeGpu(profile)}");
        sb.AppendLine($"- OS: {profile.OperatingSystem} (build {profile.OsBuild})");
        if (health is not null)
        {
            sb.AppendLine($"- Reported health score from local scan: {health.HealthScore}/100 (informational only)");
        }

        sb.AppendLine();
        sb.AppendLine("Honesty notes:");
        sb.AppendLine("- No serial numbers, Windows keys, recovery keys, or personal accounts included.");
        sb.AppendLine("- Mention wear items (battery, storage health) if the buyer asks; local scan can justify claims.");
        sb.AppendLine("- This is draft text only; marketplace accuracy and compliance are on the seller.");

        if (channel == ListingChannel.Ebay)
        {
            sb.AppendLine();
            sb.AppendLine("eBay: use their condition categories and shipping calculator; avoid exaggerated “mint” if battery/storage are weak.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string SummarizeDisks(SystemProfile profile)
    {
        if (profile.Disks.Count == 0)
        {
            return "unknown";
        }

        return string.Join("; ",
            profile.Disks.Take(3).Select(d => $"{d.MediaType} {d.Size} health {d.Health}"));
    }

    private static string SummarizeGpu(SystemProfile profile)
    {
        if (profile.Gpus.Count == 0)
        {
            return "unknown";
        }

        return string.Join(" + ", profile.Gpus.Take(2).Select(g => g.Name));
    }
}
