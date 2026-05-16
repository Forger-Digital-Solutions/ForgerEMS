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

public enum IntelligenceFieldStatus
{
    Ready,
    Watch,
    Warning,
    Critical,
    Unknown,
    NotExposed,
    Inferred
}

public enum IntelligenceConfidence
{
    High,
    Medium,
    Low
}

public sealed class IntelligenceEvidenceField
{
    public string Value { get; init; } = "Unknown";

    public IntelligenceFieldStatus Status { get; init; } = IntelligenceFieldStatus.Unknown;

    public IntelligenceConfidence Confidence { get; init; } = IntelligenceConfidence.Low;

    public string Evidence { get; init; } = string.Empty;

    public string TechnicianNote { get; init; } = string.Empty;

    public bool IsConfirmedFailure =>
        Status is IntelligenceFieldStatus.Warning or IntelligenceFieldStatus.Critical;

    public bool IsUnavailable =>
        Status is IntelligenceFieldStatus.Unknown or IntelligenceFieldStatus.NotExposed;
}

public static class SystemIntelligenceReportRedactor
{
    public static string RedactSupportReport(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = HardwarePrivacyRedactor.Redact(value);
        redacted = Regex.Replace(redacted, "(?i)(product\\s*key|digital\\s*license\\s*key)\\s*[:=]\\s*[^\\r\\n;]+", "$1=[redacted]");
        redacted = Regex.Replace(redacted, "(?i)(service\\s*tag|serial\\s*number|serial)\\s*[:=]\\s*[^\\r\\n;]+", "$1=[redacted]");
        redacted = Regex.Replace(redacted, "(?i)(\\[REDACTED_(?:SERIAL|LICENSE)\\])\\s*[:=]\\s*[^\\r\\n;]+", "$1=[redacted]");
        return redacted;
    }
}
