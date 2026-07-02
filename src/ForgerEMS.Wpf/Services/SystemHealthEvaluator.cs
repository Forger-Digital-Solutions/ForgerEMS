#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// ForgerEMS health heuristics; extract an IHealthEvaluator<T> interface for Kyra.Core.
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
using VentoyToolkitSetup.Wpf.Services.Compatibility;
using VentoyToolkitSetup.Wpf.Services.Intelligence;
using VentoyToolkitSetup.Wpf.Services.Kyra;
using VentoyToolkitSetup.Wpf.Services.KyraTools;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class SystemHealthEvaluator
{
    public static SystemHealthEvaluation Evaluate(SystemProfile? profile)
    {
        if (profile is null)
        {
            return new SystemHealthEvaluation
            {
                HealthScore = 0,
                ConfidenceScore = 0,
                DetectedIssues = ["No local device snapshot is available."]
            };
        }

        var score = 100;
        var confidence = 100;
        var issues = new List<string>();
        var categories = new List<SystemHealthCategoryScore>();

        ApplyStatusPenalty(profile.OverallStatus, "Overall scan status needs attention.", 4, 12);
        ApplyStatusPenalty(profile.DiskStatus, "Storage status needs attention.", 6, 24);
        ApplyStatusPenalty(profile.BatteryStatus, "Battery status needs attention.", 0, 8);
        ApplyStatusPenalty(profile.RamStatus, "Memory pressure was detected during the scan.", 5, 12);

        if (profile.RamTotalGb is > 0 and < 16)
        {
            score -= 12;
            issues.Add($"RAM is below the 16 GB resale/performance baseline ({profile.RamTotal}).");
        }

        foreach (var disk in profile.Disks)
        {
            if (!IsHealthyDisk(disk))
            {
                score -= 18;
                issues.Add($"Storage needs review: {disk.Name} reports health {disk.Health} / status {disk.Status}.");
            }

            if (disk.WearPercent is >= 80)
            {
                score -= 10;
                issues.Add($"Storage wear is elevated on {disk.Name}: {disk.WearPercent:0.#}%.");
            }

            if (disk.TemperatureC is >= 55)
            {
                score -= 8;
                issues.Add($"Storage temperature is high on {disk.Name}: {disk.TemperatureC:0.#} C.");
            }
        }

        foreach (var battery in profile.Batteries)
        {
            if (battery.WearPercent is >= 35)
            {
                score -= 10;
                issues.Add($"Battery wear is high at {battery.WearPercent:0.#}%.");
            }

            if (battery.CycleCount is >= 700)
            {
                score -= 6;
                issues.Add($"Battery cycle count is high ({battery.CycleCount}).");
            }
        }

        if (profile.ApipaAdapterCount > 0)
        {
            score -= 10;
            issues.Add("A physical internet-capable adapter has an APIPA address, which usually points to DHCP/network trouble.");
        }

        if (profile.MissingGatewayAdapterCount > 0)
        {
            score -= 8;
            issues.Add("A physical internet-capable adapter has no default gateway.");
        }

        // Under Wine compatibility mode, treat TPM/Secure-Boot "unknown" as a
        // host-limitation note instead of a confidence penalty. Real hardware
        // health is unchanged; we just stop pretending Windows ran a probe.
        var isCompatibility = WineProbeGate.IsWine;

        if (profile.TpmPresent == false && !isCompatibility)
        {
            score -= 8;
            issues.Add("TPM was not detected by Windows.");
        }
        else if (profile.TpmReady == false && IsConfirmedProblemStatus(profile.TpmStatus) && !isCompatibility)
        {
            score -= 4;
            issues.Add("TPM is present but Windows reports it is not ready.");
        }
        else if (profile.TpmPresent is null || profile.TpmReady is null || IsUnknownStatus(profile.TpmStatus))
        {
            if (isCompatibility)
            {
                issues.Add("TPM state not checked in Wine compatibility mode (Windows-only probe).");
            }
            else
            {
                confidence -= 8;
                issues.Add("TPM state is unknown; verify in BIOS/UEFI before treating it as disabled or missing.");
            }
        }

        if (profile.SecureBoot == false && !isCompatibility)
        {
            score -= 5;
            issues.Add("Secure Boot is disabled.");
        }
        else if (profile.SecureBoot is null || IsUnknownStatus(profile.SecureBootStatus))
        {
            if (isCompatibility)
            {
                issues.Add("Secure Boot state not checked in Wine compatibility mode (Windows-only probe).");
            }
            else
            {
                confidence -= 6;
                issues.Add("Secure Boot state is unknown; Windows did not expose enough firmware data to confirm it.");
            }
        }

        foreach (var problem in profile.ObviousProblems.Where(problem => !problem.Contains("No obvious", StringComparison.OrdinalIgnoreCase)).Take(8))
        {
            if (!issues.Any(issue => issue.Equals(problem, StringComparison.OrdinalIgnoreCase)))
            {
                score -= 4;
                issues.Add(problem);
            }
        }

        if (issues.Count == 0)
        {
            issues.Add("No obvious blocking problems detected locally.");
        }

        categories.Add(BuildPerformanceCategory(profile));
        categories.Add(BuildStorageCategory(profile));
        categories.Add(BuildBatteryCategory(profile));
        categories.Add(BuildSecurityCategory(profile));
        categories.Add(BuildNetworkCategory(profile));
        categories.Add(BuildUsbReadinessCategory());
        categories.Add(BuildFlipReadinessCategory(profile));
        categories.Add(new SystemHealthCategoryScore
        {
            Category = "Data confidence",
            Score = Math.Clamp(confidence, 0, 100),
            Status = confidence >= 85 ? "READY" : confidence >= 65 ? "WATCH" : "WARNING",
            Confidence = confidence >= 85 ? "High" : confidence >= 65 ? "Medium" : "Low",
            Reasons = confidence >= 85
                ? ["Core hardware and security fields were available."]
                : ["Some firmware/provider fields were unknown or not exposed."],
            RecommendedAction = confidence >= 85
                ? "Use the report normally."
                : "Verify unknown firmware/security fields in BIOS, vendor tools, or an elevated technician report."
        });

        return new SystemHealthEvaluation
        {
            HealthScore = Math.Clamp(score, 0, 100),
            ConfidenceScore = Math.Clamp(confidence, 0, 100),
            Categories = categories.ToArray(),
            DetectedIssues = issues.Take(10).ToArray()
        };

        void ApplyStatusPenalty(string status, string issue, int watchPenalty, int warningPenalty)
        {
            if (status.Equals("WARNING", StringComparison.OrdinalIgnoreCase))
            {
                score -= warningPenalty;
                issues.Add(issue);
            }
            else if (status.Equals("WATCH", StringComparison.OrdinalIgnoreCase))
            {
                score -= watchPenalty;
                issues.Add(issue);
            }
            else if (IsUnknownStatus(status))
            {
                confidence -= Math.Max(4, watchPenalty);
            }
        }
    }

    private static SystemHealthCategoryScore BuildPerformanceCategory(SystemProfile profile)
    {
        var reasons = new List<string>();
        var score = 86;
        if (profile.RamTotalGb is > 0 and < 16)
        {
            score -= 18;
            reasons.Add($"RAM is {profile.RamTotal}; 16 GB+ is the current resale/performance baseline.");
        }

        if (profile.Cpu.Contains("i7", StringComparison.OrdinalIgnoreCase) ||
            profile.Cpu.Contains("ryzen 7", StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
            reasons.Add("Performance-tier CPU detected.");
        }

        if (profile.Gpus.Any(g => IsDedicatedGpuName(g.Name)))
        {
            score += 5;
            reasons.Add("Dedicated GPU adds creator/resale upside.");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("CPU/RAM/GPU profile looks usable for general Windows work.");
        }

        return Category("Performance", score, reasons, "Use the detected CPU/RAM/GPU profile for pricing and upgrade decisions.");
    }

    private static SystemHealthCategoryScore BuildStorageCategory(SystemProfile profile)
    {
        var reasons = new List<string>();
        var score = profile.Disks.Count == 0 ? 78 : 90;
        if (profile.Disks.Count == 0)
        {
            reasons.Add("Physical disk details were not exposed; storage health needs verification.");
        }

        foreach (var disk in profile.Disks)
        {
            if (!IsHealthyDisk(disk))
            {
                score -= 25;
                reasons.Add($"{disk.Name} reports {disk.Health}/{disk.Status}.");
            }
            else if (disk.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                     disk.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                     disk.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                     disk.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"{disk.Name} is SSD/NVMe-class storage.");
            }
        }

        return Category("Storage", score, reasons, "Run vendor SMART tools before resale when health counters are unavailable.");
    }

    private static SystemHealthCategoryScore BuildBatteryCategory(SystemProfile profile)
    {
        if (profile.Batteries.Count == 0)
        {
            return Category("Battery", 82, ["No battery report was exposed; this may be normal for desktops or firmware-limited laptops."], "Run a battery report or vendor diagnostics if laptop runtime matters.", "Medium");
        }

        var reasons = new List<string>();
        var score = 88;
        foreach (var battery in profile.Batteries)
        {
            if (battery.WearPercent is >= 35)
            {
                score -= 25;
                reasons.Add($"Battery wear is {battery.WearPercent:0.#}%.");
            }
            else if (battery.WearPercent.HasValue)
            {
                reasons.Add($"Battery wear is {battery.WearPercent:0.#}%.");
            }
            else
            {
                reasons.Add("Firmware/Windows did not expose battery wear.");
            }
        }

        return Category("Battery", score, reasons, "Disclose wear when known; verify with powercfg/vendor diagnostics when not exposed.");
    }

    private static SystemHealthCategoryScore BuildSecurityCategory(SystemProfile profile)
    {
        var reasons = new List<string>();
        var score = 88;
        var confidence = "High";
        var isCompatibility = WineProbeGate.IsWine;

        if (isCompatibility)
        {
            // Wine has no TPM / Secure Boot surface — surface this as a host
            // limitation, not a "Low" confidence security finding.
            reasons.Add("TPM and Secure Boot not checked in Wine compatibility mode (Windows-only probes).");
            reasons.Add("Compatibility-limited under Linux/Wine — security category is informational only here.");
            return Category("Security", score, reasons, "Run ForgerEMS on native Windows to evaluate TPM and Secure Boot.", confidence);
        }

        if (profile.TpmPresent == false)
        {
            score -= 12;
            reasons.Add("TPM was not detected.");
        }
        else if (profile.TpmReady == false && IsConfirmedProblemStatus(profile.TpmStatus))
        {
            score -= 6;
            reasons.Add("TPM is present but not ready.");
        }
        else if (profile.TpmReady is null || IsUnknownStatus(profile.TpmStatus))
        {
            confidence = "Low";
            reasons.Add("TPM state was not exposed by Windows.");
        }

        if (profile.SecureBoot == false)
        {
            score -= 6;
            reasons.Add("Secure Boot is disabled.");
        }
        else if (profile.SecureBoot is null || IsUnknownStatus(profile.SecureBootStatus))
        {
            confidence = "Low";
            reasons.Add("Secure Boot state was not exposed by Windows.");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("TPM/Secure Boot data does not show a confirmed blocker.");
        }

        return Category("Security", score, reasons, "Verify unknown firmware states in BIOS/UEFI before listing Windows 11 readiness.", confidence);
    }

    private static SystemHealthCategoryScore BuildNetworkCategory(SystemProfile profile)
    {
        var reasons = new List<string>();
        var score = 90;
        if (profile.InternetCheck && profile.HasActivePhysicalInternetAdapter)
        {
            reasons.Add("A physical adapter has a route and internet connectivity passed.");
        }
        else if (profile.InternetCheck)
        {
            reasons.Add("Internet connectivity passed; adapter role data is limited.");
        }
        else
        {
            score -= 18;
            reasons.Add("Internet connectivity probe did not pass.");
        }

        if (profile.ApipaAdapterCount > 0)
        {
            score -= 12;
            reasons.Add("A physical adapter has APIPA addressing.");
        }

        if (profile.MissingGatewayAdapterCount > 0)
        {
            score -= 8;
            reasons.Add("A physical adapter has no gateway.");
        }

        return Category("Network", score, reasons, "Ignore host-only/virtual adapters unless they are the active route.");
    }

    private static SystemHealthCategoryScore BuildUsbReadinessCategory() =>
        Category("USB readiness", 85, ["USB target safety and benchmark readiness are reported in the USB Builder panel."], "Use USB Builder for target-specific safety, mapping, and benchmark evidence.", "Medium");

    private static SystemHealthCategoryScore BuildFlipReadinessCategory(SystemProfile profile)
    {
        var confidence = profile.FlipValue.ConfidenceScore switch
        {
            >= 0.7 => "High",
            >= 0.5 => "Medium",
            _ => "Low"
        };
        var reasons = new List<string>
        {
            profile.FlipValue.ProviderStatus.Contains("not configured", StringComparison.OrdinalIgnoreCase)
                ? "Offline heuristic estimate only; no live marketplace comps configured."
                : profile.FlipValue.ProviderStatus
        };
        reasons.AddRange(profile.FlipValue.ValueDrivers.Take(2));
        return Category("Flip/resale readiness", confidence == "Low" ? 68 : 78, reasons, "Add condition, charger, location, and manual/live comps before final pricing.", confidence);
    }

    private static SystemHealthCategoryScore Category(string name, int score, IReadOnlyList<string> reasons, string action, string confidence = "High")
    {
        var normalized = Math.Clamp(score, 0, 100);
        return new SystemHealthCategoryScore
        {
            Category = name,
            Score = normalized,
            Status = normalized >= 80 ? "READY" : normalized >= 60 ? "WATCH" : "WARNING",
            Confidence = confidence,
            Reasons = reasons,
            RecommendedAction = action
        };
    }

    private static bool IsUnknownStatus(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("NOTEXPOSED", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("NOT_EXPOSED", StringComparison.OrdinalIgnoreCase);

    private static bool IsConfirmedProblemStatus(string? status) =>
        status is not null &&
        (status.Equals("WARNING", StringComparison.OrdinalIgnoreCase) ||
         status.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase));

    private static bool IsDedicatedGpuName(string name) =>
        Regex.IsMatch(name ?? string.Empty, "nvidia|geforce|quadro|rtx|gtx|amd radeon|\\brx\\b|arc", RegexOptions.IgnoreCase) &&
        !Regex.IsMatch(name ?? string.Empty, "intel|uhd|iris", RegexOptions.IgnoreCase);

    private static bool IsHealthyDisk(SystemDiskProfile disk)
    {
        var healthy = string.IsNullOrWhiteSpace(disk.Health) ||
                      disk.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ||
                      disk.Health.Equals("OK", StringComparison.OrdinalIgnoreCase);
        var ready = disk.Status.Equals("READY", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(disk.Status);
        return healthy && ready;
    }
}
