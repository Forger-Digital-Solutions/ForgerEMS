using System;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

public static class SystemIntelligenceFormatter
{
    public static string FriendlyUnknown(string? value, string reason)
    {
        return string.IsNullOrWhiteSpace(value) || value.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            ? reason
            : value;
    }

    public static string FormatRamSpeedSummary(string installed, string configuredSpeed, string ratedSpeed, string slots)
    {
        return $"{FriendlyUnknown(installed, "Installed RAM not reported")}; configured {FriendlyUnknown(configuredSpeed, "Configured speed not reported")}; rated {FriendlyUnknown(ratedSpeed, "Module rated speed not reported")}; {FriendlyUnknown(slots, "Slot count not reported")}";
    }

    public static string FormatBatteryWear(double? wearPercent, bool designCapacityReported, bool fullChargeCapacityReported)
    {
        if (wearPercent.HasValue)
        {
            return $"{wearPercent.Value:0.#}%";
        }

        if (!designCapacityReported)
        {
            return "Wear unavailable - design capacity not reported";
        }

        return fullChargeCapacityReported
            ? "Wear unavailable"
            : "Wear unavailable - full charge capacity not reported";
    }

    public static bool ShouldIgnoreAdapterForWarnings(string? name, string? description)
    {
        var combined = $"{name} {description}";
        return Regex.IsMatch(
            combined,
            "virtual|hyper-v|virtualbox|vmware|vpn|tap|wintun|wireguard|tailscale|zerotier|loopback|host-only|bluetooth",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static string FormatTpmFriendly(bool? present, bool? enabled, bool? activated, bool? ready)
    {
        if (present == false)
        {
            return "TPM not detected";
        }

        if (ready == true)
        {
            return "TPM ready for Windows 11";
        }

        if (present == true && enabled == false)
        {
            return "TPM disabled in firmware";
        }

        if (present == true && activated == false)
        {
            return "TPM present but not ready";
        }

        return present == true
            ? "TPM present but not ready"
            : "TPM status unavailable";
    }
}
