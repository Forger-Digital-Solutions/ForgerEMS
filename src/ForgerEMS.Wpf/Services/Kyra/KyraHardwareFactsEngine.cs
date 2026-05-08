using System.Text.RegularExpressions;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Local-first hardware facts derived from System Intelligence (no gateway upload; use <see cref="BuildGatewayBands"/> for sanitized bands only).</summary>
public static class KyraHardwareFactsEngine
{
    public static KyraGatewayKnownLocalFactsDto BuildGatewayBands(SystemProfile profile)
    {
        var wear = PrimaryBatteryWear(profile);
        var wearBand = wear switch
        {
            null => "unknown",
            >= 35 => "high",
            >= 20 => "elevated",
            _ => "low"
        };

        var bus = PrimaryStorageBusKind(profile);
        var busBand = bus switch
        {
            KyraStorageBusKind.Nvme => "NVMe",
            KyraStorageBusKind.Sata => "SATA",
            KyraStorageBusKind.Usb => "USB",
            KyraStorageBusKind.Raid => "RAID",
            KyraStorageBusKind.Unknown => "unknown",
            _ => "unknown"
        };

        var mem = MemoryTypeLabel(profile);
        var memBand = string.IsNullOrWhiteSpace(mem) || mem.Equals("RAM", StringComparison.OrdinalIgnoreCase)
            ? "unknown"
            : mem;

        var ramGb = profile.RamTotalGb;
        var ramBand = ramGb switch
        {
            null => "unknown",
            >= 32 => "32gb_plus",
            >= 16 => "16_31gb",
            _ => "under_16gb"
        };

        return new KyraGatewayKnownLocalFactsDto
        {
            StorageBusBand = busBand,
            MemoryTypeBand = memBand,
            BatteryWearBand = wearBand,
            RamTotalGbBand = ramBand
        };
    }


    public static KyraStorageBusKind PrimaryStorageBusKind(SystemProfile profile)
    {
        var disk = profile.Disks.Count > 0 ? profile.Disks[0] : null;
        if (disk is null)
        {
            return KyraStorageBusKind.Unknown;
        }

        return ClassifyBus(disk.InterfaceType, disk.Name, disk.MediaType);
    }

    public static KyraStorageBusKind ClassifyBus(string interfaceType, string diskName, string mediaType)
    {
        var bus = interfaceType.Trim();
        var name = diskName;
        var media = mediaType;
        if (ContainsLoose(bus, "NVMe") || ContainsLoose(bus, "NVME") || ContainsLoose(name, "NVMe"))
        {
            return KyraStorageBusKind.Nvme;
        }

        if (ContainsLoose(bus, "SATA") || ContainsLoose(bus, "ATA"))
        {
            return KyraStorageBusKind.Sata;
        }

        if (ContainsLoose(bus, "USB"))
        {
            return KyraStorageBusKind.Usb;
        }

        if (ContainsLoose(bus, "RAID"))
        {
            return KyraStorageBusKind.Raid;
        }

        if (ContainsLoose(media, "SSD") && (ContainsLoose(name, "NVMe") || ContainsLoose(name, "M.2")))
        {
            return KyraStorageBusKind.Nvme;
        }

        return string.IsNullOrWhiteSpace(bus) ? KyraStorageBusKind.Unknown : KyraStorageBusKind.Unknown;
    }

    public static string MemoryTypeLabel(SystemProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.MemoryTypeSummary))
        {
            return profile.MemoryTypeSummary.Trim();
        }

        var ram = profile.RamTotal ?? string.Empty;
        var match = Regex.Match(ram, @"\b(DDR\d|LPDDR\dX?)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : string.Empty;
    }

    public static bool IsLikelyLaptop(SystemProfile profile)
    {
        if (profile.Batteries.Count > 0)
        {
            return true;
        }

        var mc = MachineClassifier.Classify(profile).PrimaryClass;
        return mc.Contains("Laptop", StringComparison.OrdinalIgnoreCase) ||
               mc.Contains("Mobile Workstation", StringComparison.OrdinalIgnoreCase) ||
               mc.Contains("Surface", StringComparison.OrdinalIgnoreCase);
    }

    public static double? PrimaryBatteryWear(SystemProfile profile) =>
        profile.Batteries.Select(b => b.WearPercent).FirstOrDefault(w => w is not null);

    public static bool StorageLooksHealthyNvmeSsd(SystemProfile profile)
    {
        foreach (var d in profile.Disks)
        {
            var bus = ClassifyBus(d.InterfaceType, d.Name, d.MediaType);
            var ssd = d.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                      d.MediaType.Contains("Solid", StringComparison.OrdinalIgnoreCase);
            var healthy = d.Health.Contains("Healthy", StringComparison.OrdinalIgnoreCase) ||
                          d.Health.Contains("OK", StringComparison.OrdinalIgnoreCase);
            if (bus == KyraStorageBusKind.Nvme && ssd && healthy)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsLoose(string haystack, string needle) =>
        !string.IsNullOrWhiteSpace(haystack) &&
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}

public enum KyraStorageBusKind
{
    Unknown = 0,
    Nvme,
    Sata,
    Usb,
    Raid
}
