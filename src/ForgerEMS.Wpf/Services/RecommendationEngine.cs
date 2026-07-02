#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// ForgerEMS USB/pricing recommendation engine; no equivalent in Kyra.Core.
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

public sealed class RecommendationEngine
{
    public static IReadOnlyList<string> Generate(SystemProfile? profile, SystemHealthEvaluation evaluation)
    {
        if (profile is null)
        {
            return ["Add a local device snapshot so Kyra can use local hardware facts."];
        }

        var recommendations = new List<string>();
        AddRange(profile.ReportRecommendations);

        if (profile.RamTotalGb is > 0 and < 16)
        {
            Add("Upgrade to at least 16 GB RAM before selling or for smoother Windows 11 use.");
        }

        if (profile.Disks.Count == 0 || profile.DiskStatus.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
        {
            Add("Run elevated SMART/storage diagnostics before pricing or diagnosing lag.");
        }
        else if (profile.Disks.Any(disk => !disk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) && !disk.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase)))
        {
            Add("Replace slow or unknown storage with a known-good SSD before resale when practical.");
        }

        if (profile.Disks.Any(disk => !disk.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) && !disk.Health.Equals("OK", StringComparison.OrdinalIgnoreCase)))
        {
            Add("Replace questionable storage or list the machine as parts/repair.");
        }

        if (profile.Batteries.Any(battery => battery.WearPercent is >= 35))
        {
            Add("Replace the battery before sale or disclose battery wear clearly.");
        }

        if (profile.ApipaAdapterCount > 0 || profile.MissingGatewayAdapterCount > 0)
        {
            Add("Fix physical-adapter DHCP or gateway issues before relying on updates, downloads, or online pricing. Host-only/virtual adapters can usually be ignored when real internet works.");
        }

        if (profile.TpmPresent == false || (profile.TpmReady == false && !profile.TpmStatus.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)) || profile.SecureBoot == false)
        {
            Add("Confirm TPM and Secure Boot state before presenting this as Windows 11-ready.");
        }
        else if (profile.TpmReady is null || profile.SecureBoot is null)
        {
            Add("Verify TPM/Secure Boot in BIOS or vendor tools; Windows did not expose enough data to treat unknown as disabled.");
        }

        AddRange(profile.FlipValue.SuggestedUpgradeRecommendations);

        if (evaluation.HealthScore < 55)
        {
            Add("Treat this as repair-first or parts/repair until the highest severity scan issues are resolved.");
        }

        return recommendations.Count == 0
            ? ["No urgent upgrade is required from the local scan; clean, update, verify drivers, and photograph condition before listing."]
            : recommendations.Take(10).ToArray();

        void Add(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !recommendations.Any(item => item.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                recommendations.Add(value);
            }
        }

        void AddRange(IEnumerable<string> values)
        {
            foreach (var value in values)
            {
                Add(value);
            }
        }
    }
}
