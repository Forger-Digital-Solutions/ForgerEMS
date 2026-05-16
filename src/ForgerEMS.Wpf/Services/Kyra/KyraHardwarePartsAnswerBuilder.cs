#pragma warning disable CA1305 // Locale-sensitive calls; text is diagnostic/UI output
using System.Linq;
using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>Scan-grounded parts and upgrade explanations (no invented part numbers; live pricing routes via gateway).</summary>
public static class KyraHardwarePartsAnswerBuilder
{
    private const string NoScan =
        "I don’t have a current System Intelligence scan loaded yet. Run System Intelligence (Hardware X-Ray) and ask again — I’ll read NVMe/SATA, RAM type, and battery wear locally first.";

    public static bool TryBuild(string? prompt, SystemProfile? profile, CopilotSettings settings, out CopilotResponse response)
    {
        response = new CopilotResponse();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 420)
        {
            return false;
        }

        if (!IsHardwarePartsOrUpgradeQuestion(prompt))
        {
            return false;
        }

        var routedIntent = KyraIntentRouter.DetectIntent(prompt);
        var pl = prompt.ToLowerInvariant();
        if (routedIntent == KyraIntent.ResaleValue ||
            (routedIntent == KyraIntent.UpgradeAdvice &&
             (pl.Contains("selling", StringComparison.Ordinal) ||
              pl.Contains("sell ", StringComparison.Ordinal) ||
              pl.Contains("resale", StringComparison.Ordinal) ||
              pl.Contains("listing", StringComparison.Ordinal) ||
              pl.Contains("before listing", StringComparison.Ordinal))))
        {
            return false;
        }

        if (WantsLivePartPricing(prompt))
        {
            return false;
        }

        var playful = KyraPersonalityTone.UsePlayfulWording(settings.PersonalityProfile, prompt);
        var grounded = profile is not null;
        var body = grounded ? BuildAnswer(prompt.Trim(), profile!, playful) : NoScan;

        response = new CopilotResponse
        {
            Text = body.Trim(),
            UsedOnlineData = false,
            OnlineStatus = "Kyra Mode: Local hardware facts (System Intelligence scan).",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes = ["Kyra routing: hardware facts -> local System Intelligence"],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = KyraResponseComposer.KyraIdentityLabel,
            GroundedInSystemIntelligence = grounded,
            KyraTransparencySummary = grounded
                ? "Local hardware facts from the latest System Intelligence scan. No live marketplace pricing on this path — enable gateway research for current listings."
                : "No local scan snapshot was available for hardware facts.",
            ActionSuggestions = []
        };

        return true;
    }

    /// <summary>Live gateway research should handle current pricing / broad marketplace lookup (never invent offline).</summary>
    public static bool PromptRequestsLivePartPricing(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return false;
        }

        var l = prompt.ToLowerInvariant();
        var partCue = l.Contains("battery", StringComparison.Ordinal) ||
                      l.Contains("ssd", StringComparison.Ordinal) ||
                      l.Contains("nvme", StringComparison.Ordinal) ||
                      l.Contains("ram", StringComparison.Ordinal) ||
                      l.Contains("memory", StringComparison.Ordinal) ||
                      l.Contains("charger", StringComparison.Ordinal) ||
                      l.Contains("adapter", StringComparison.Ordinal);
        if (!partCue)
        {
            return false;
        }

        return l.Contains("cheapest", StringComparison.Ordinal) ||
               l.Contains("best deal", StringComparison.Ordinal) ||
               l.Contains("lowest price", StringComparison.Ordinal) ||
               l.Contains("where to buy", StringComparison.Ordinal) ||
               l.Contains("where can i buy", StringComparison.Ordinal) ||
               l.Contains("should i buy", StringComparison.Ordinal) ||
               l.Contains("what should i buy", StringComparison.Ordinal) ||
               l.Contains("which should i buy", StringComparison.Ordinal) ||
               l.Contains("compatible", StringComparison.Ordinal) ||
               (l.Contains("price", StringComparison.Ordinal) && !l.Contains("price range", StringComparison.Ordinal));
    }

    private static bool WantsLivePartPricing(string prompt) => PromptRequestsLivePartPricing(prompt);

    private static bool IsHardwarePartsOrUpgradeQuestion(string prompt)
    {
        var l = prompt.ToLowerInvariant();
        var storageBus =
            l.Contains("nvme", StringComparison.Ordinal) ||
            l.Contains("sata", StringComparison.Ordinal) ||
            l.Contains("m.2", StringComparison.Ordinal) ||
            ((l.Contains("drive", StringComparison.Ordinal) || l.Contains("disk", StringComparison.Ordinal) ||
              l.Contains("storage", StringComparison.Ordinal)) &&
             (l.Contains("nvme", StringComparison.Ordinal) || l.Contains("sata", StringComparison.Ordinal) ||
              l.Contains("what kind", StringComparison.Ordinal) || l.Contains("which interface", StringComparison.Ordinal) ||
              l.Contains("interface", StringComparison.Ordinal)));

        var ramType =
            l.Contains("ddr", StringComparison.Ordinal) ||
            l.Contains("lpddr", StringComparison.Ordinal) ||
            l.Contains("sodimm", StringComparison.Ordinal) ||
            (l.Contains("ram", StringComparison.Ordinal) &&
             (l.Contains("type", StringComparison.Ordinal) || l.Contains("what kind", StringComparison.Ordinal) ||
              l.Contains("upgrade", StringComparison.Ordinal))) ||
            l.Contains("memory type", StringComparison.Ordinal);

        var battery =
            l.Contains("battery", StringComparison.Ordinal) &&
            (l.Contains("replace", StringComparison.Ordinal) || l.Contains("replacement", StringComparison.Ordinal) ||
             l.Contains("part", StringComparison.Ordinal) || l.Contains("which", StringComparison.Ordinal) ||
             l.Contains("what", StringComparison.Ordinal) || l.Contains("number", StringComparison.Ordinal) ||
             l.Contains("need", StringComparison.Ordinal));

        var upgrade =
            l.Contains("upgrade first", StringComparison.Ordinal) ||
            (l.Contains("what should i upgrade", StringComparison.Ordinal)) ||
            (l.Contains("should i upgrade", StringComparison.Ordinal) && l.Contains("first", StringComparison.Ordinal));

        var sell =
            l.Contains("before selling", StringComparison.Ordinal) ||
            (l.Contains("replace before", StringComparison.Ordinal) && l.Contains("sell", StringComparison.Ordinal)) ||
            (l.Contains("selling this", StringComparison.Ordinal) && l.Contains("replace", StringComparison.Ordinal));

        var ramUpgrade =
            l.Contains("can i upgrade ram", StringComparison.Ordinal) ||
            l.Contains("upgrade ram", StringComparison.Ordinal) ||
            (l.Contains("more ram", StringComparison.Ordinal) && l.Contains("add", StringComparison.Ordinal));

        var ssdFit =
            l.Contains("what ssd", StringComparison.Ordinal) && l.Contains("fit", StringComparison.Ordinal) ||
            l.Contains("ssd fits", StringComparison.Ordinal);

        var chargers =
            (l.Contains("charger", StringComparison.Ordinal) || l.Contains("power brick", StringComparison.Ordinal) ||
             l.Contains("power adapter", StringComparison.Ordinal) || l.Contains("ac adapter", StringComparison.Ordinal)) &&
            (l.Contains("fit", StringComparison.Ordinal) || l.Contains("which", StringComparison.Ordinal) ||
             l.Contains("what", StringComparison.Ordinal) || l.Contains("need", StringComparison.Ordinal));

        var docks =
            l.Contains("dock", StringComparison.Ordinal) &&
            (l.Contains("fit", StringComparison.Ordinal) || l.Contains("compatible", StringComparison.Ordinal) ||
             l.Contains("which", StringComparison.Ordinal) || l.Contains("what", StringComparison.Ordinal));

        var drivers =
            l.Contains("official driver", StringComparison.Ordinal) || l.Contains("oem driver", StringComparison.Ordinal) ||
            l.Contains("drivers for this", StringComparison.Ordinal) ||
            (l.Contains("driver", StringComparison.Ordinal) && l.Contains("support", StringComparison.Ordinal) &&
             (l.Contains("download", StringComparison.Ordinal) || l.Contains("where", StringComparison.Ordinal)));

        return storageBus || ramType || battery || upgrade || sell || ramUpgrade || ssdFit || chargers || docks || drivers;
    }

    private static string BuildAnswer(string prompt, SystemProfile profile, bool playful)
    {
        var l = prompt.ToLowerInvariant();
        var open = playful ? "Tiny upgrade goblin check-in 😄 " : string.Empty;
        var sb = new StringBuilder();
        if (!string.IsNullOrEmpty(open))
        {
            sb.Append(open.TrimEnd()).AppendLine();
            sb.AppendLine();
        }

        if (l.Contains("official driver", StringComparison.Ordinal) || l.Contains("oem driver", StringComparison.Ordinal) ||
            l.Contains("drivers for this", StringComparison.Ordinal) ||
            (l.Contains("driver", StringComparison.Ordinal) && l.Contains("support", StringComparison.Ordinal)))
        {
            AppendOfficialDrivers(sb, profile);
        }
        else if ((l.Contains("charger", StringComparison.Ordinal) || l.Contains("power adapter", StringComparison.Ordinal) ||
                  l.Contains("ac adapter", StringComparison.Ordinal)) &&
                 (l.Contains("fit", StringComparison.Ordinal) || l.Contains("need", StringComparison.Ordinal) ||
                  l.Contains("which", StringComparison.Ordinal)))
        {
            AppendChargerDock(sb, profile, playful, dock: false);
        }
        else if (l.Contains("dock", StringComparison.Ordinal))
        {
            AppendChargerDock(sb, profile, playful, dock: true);
        }
        else if (l.Contains("battery", StringComparison.Ordinal) &&
            (l.Contains("replace", StringComparison.Ordinal) || l.Contains("part", StringComparison.Ordinal) ||
             l.Contains("what", StringComparison.Ordinal) || l.Contains("which", StringComparison.Ordinal) ||
             l.Contains("need", StringComparison.Ordinal)))
        {
            AppendBattery(sb, profile, playful);
        }
        else if (l.Contains("ddr", StringComparison.Ordinal) || l.Contains("lpddr", StringComparison.Ordinal) ||
                 l.Contains("memory type", StringComparison.Ordinal) ||
                 (l.Contains("ram", StringComparison.Ordinal) && l.Contains("type", StringComparison.Ordinal)))
        {
            AppendRamType(sb, profile);
        }
        else if (l.Contains("upgrade first", StringComparison.Ordinal) ||
                 l.Contains("what should i upgrade", StringComparison.Ordinal))
        {
            AppendUpgradeFirst(sb, profile);
        }
        else if (l.Contains("before selling", StringComparison.Ordinal) ||
                 (l.Contains("sell", StringComparison.Ordinal) && l.Contains("replace", StringComparison.Ordinal)))
        {
            AppendBeforeSelling(sb, profile);
        }
        else if (l.Contains("can i upgrade ram", StringComparison.Ordinal) ||
                 l.Contains("upgrade ram", StringComparison.Ordinal) ||
                 l.Contains("more ram", StringComparison.Ordinal))
        {
            AppendRamUpgrade(sb, profile);
        }
        else if (l.Contains("ssd", StringComparison.Ordinal) && l.Contains("fit", StringComparison.Ordinal))
        {
            AppendSsdFit(sb, profile);
        }
        else
        {
            AppendStorageBus(sb, profile);
        }

        sb.AppendLine();
        sb.AppendLine("Confirm before buying: match any part against your service manual, the physical battery label, or the OEM compatibility matrix — not just marketplace titles.");
        return sb.ToString().Trim();
    }

    private static void AppendStorageBus(StringBuilder sb, SystemProfile profile)
    {
        if (profile.Disks.Count == 0)
        {
            sb.AppendLine("What I know: the last scan didn’t list physical disks.");
            sb.AppendLine("Best next move: re-run System Intelligence from an elevated PowerShell session if this looks wrong.");
            return;
        }

        sb.AppendLine("What I know (local scan):");
        foreach (var d in profile.Disks.Take(4))
        {
            var bus = KyraHardwareFactsEngine.ClassifyBus(d.InterfaceType, d.Name, d.MediaType);
            var busTxt = bus switch
            {
                KyraStorageBusKind.Nvme => "NVMe",
                KyraStorageBusKind.Sata => "SATA",
                KyraStorageBusKind.Usb => "USB",
                KyraStorageBusKind.Raid => "RAID",
                _ => string.IsNullOrWhiteSpace(d.InterfaceType) ? "unknown bus (not exposed)" : d.InterfaceType
            };
            sb.Append("• ")
                .Append(d.Name)
                .Append(": ")
                .Append(busTxt)
                .Append(" bus, media ")
                .Append(d.MediaType)
                .Append(", ")
                .Append(d.Size)
                .Append(", health ")
                .Append(d.Health)
                .AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine("What I’m inferring: NVMe vs SATA is based on Windows storage bus reporting plus name/media hints — if bus reads empty, confidence is lower.");
        sb.AppendLine("Confidence: high when BusType is explicit; medium when inferred from naming; low when missing.");
    }

    private static void AppendRamType(StringBuilder sb, SystemProfile profile)
    {
        var label = KyraHardwareFactsEngine.MemoryTypeLabel(profile);
        sb.AppendLine("What I know (local scan):");
        sb.AppendLine($"• Total RAM: {profile.RamTotal}");
        sb.AppendLine($"• Configured speed summary: {profile.RamSpeed}");
        sb.AppendLine($"• Slots / upgrade hint: {profile.RamSlotsFree?.ToString() ?? "?"} free (if exposed), {profile.RamUpgradePath}");

        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(label) && !label.Equals("RAM", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine($"What I’m inferring: SMBIOS memory type maps to **{label}** in this scan.");
            sb.AppendLine("Confidence: medium-high when SMBIOS exposes a DDR/LPDDR family.");
        }
        else
        {
            sb.AppendLine("What I’m inferring: Windows/SMBIOS didn’t expose a clear DDR generation in the scan JSON.");
            sb.AppendLine("What I need: a fresh System Intelligence run, or live research against your exact CPU/platform if you need certainty.");
            sb.AppendLine("Confidence: low for DDR generation right now.");
        }
    }

    private static void AppendBattery(StringBuilder sb, SystemProfile profile, bool playful)
    {
        if (profile.Batteries.Count == 0)
        {
            sb.AppendLine("What I know: this scan doesn’t show a battery — common on desktops or if WMI didn’t expose one.");
            return;
        }

        var b = profile.Batteries[0];
        sb.AppendLine("What I know (local scan):");
        sb.AppendLine($"• Machine: {profile.Manufacturer} {profile.Model}");
        sb.AppendLine($"• Battery name (as reported): {b.Name}");
        if (!string.IsNullOrWhiteSpace(b.DesignCapacityDisplay))
        {
            sb.AppendLine($"• Design capacity: {b.DesignCapacityDisplay}");
        }

        if (!string.IsNullOrWhiteSpace(b.FullChargeCapacityDisplay))
        {
            sb.AppendLine($"• Full-charge capacity: {b.FullChargeCapacityDisplay}");
        }

        if (b.WearPercent is { } w)
        {
            sb.AppendLine($"• Wear estimate: {w:0.#}%");
        }
        else
        {
            sb.AppendLine("• Wear estimate: not exposed by firmware/Windows in this scan");
        }

        if (b.CycleCount is { } c)
        {
            sb.AppendLine($"• Cycles: {c}");
        }

        sb.AppendLine();
        sb.AppendLine("Exact OEM battery part number: **not in the scan JSON** — I won’t pretend it is.");
        sb.AppendLine("What I’m inferring: likely compatible candidates usually come from the OEM parts store, the service manual, or the label on the pack — mark anything else as *candidate* until verified.");
        if (playful)
        {
            sb.AppendLine("Repair gremlin note: third-party listings love keyword-stuffing “compatible” — trust the manual more than the title.");
        }

        sb.AppendLine();
        sb.AppendLine(
            "What I need live research for: curated candidate SKUs, regional availability, and **current** price bands (only after you enable gateway research — I won’t invent cheapest listings offline).");
    }

    private static void AppendUpgradeFirst(StringBuilder sb, SystemProfile profile)
    {
        sb.AppendLine("What I know: local health signals from the scan.");
        sb.AppendLine(KyraUpgradePathEngine.BuildUpgradeFirstSummary(profile, null));
        sb.AppendLine();
        sb.AppendLine("Confidence: medium — based on scan exposure limits; verify thermals and workloads that matter to you.");
    }

    private static void AppendBeforeSelling(StringBuilder sb, SystemProfile profile)
    {
        sb.AppendLine("What I know: resale checklist grounded in the scan.");
        sb.AppendLine(KyraUpgradePathEngine.BuildBeforeSellingSummary(profile));
    }

    private static void AppendRamUpgrade(StringBuilder sb, SystemProfile profile)
    {
        var gb = profile.RamTotalGb ?? 0;
        sb.AppendLine("What I know (local scan):");
        sb.AppendLine($"• Installed: {profile.RamTotal}");
        sb.AppendLine($"• Type hint: {KyraHardwareFactsEngine.MemoryTypeLabel(profile)}");
        sb.AppendLine($"• Slots free (if exposed): {profile.RamSlotsFree?.ToString() ?? "unknown"}");
        sb.AppendLine(profile.RamUpgradePath);
        sb.AppendLine();
        if (gb >= 32)
        {
            sb.AppendLine(
                "What I’m inferring: you’re already at high capacity — only upgrade if the board supports more and you actually need it.");
        }
        else
        {
            sb.AppendLine("What I’m inferring: more RAM can help if you’re memory-bound; confirm max supported memory for this exact model family.");
        }

        sb.AppendLine("Confidence: medium for “can I add more” without the OEM max-RAM matrix.");
    }

    private static void AppendSsdFit(StringBuilder sb, SystemProfile profile)
    {
        AppendStorageBus(sb, profile);
        sb.AppendLine();
        sb.AppendLine(
            "What I need: your chassis manual (NVMe-only vs SATA bay), and whether you’re replacing the boot drive or adding secondary storage.");
    }

    private static void AppendOfficialDrivers(StringBuilder sb, SystemProfile profile)
    {
        sb.AppendLine("Direct answer:");
        sb.AppendLine(
            $"Use your OEM’s **official support** page for **{profile.Manufacturer} {profile.Model}** — I won’t fabricate download URLs or driver SKUs.");
        sb.AppendLine();
        sb.AppendLine("What I know (local scan):");
        sb.AppendLine($"• System: {profile.OperatingSystem} ({profile.OsBuild})");
        sb.AppendLine("• GPU line(s): " + (profile.Gpus.Count == 0
            ? "not listed in summary"
            : string.Join("; ", profile.Gpus.Select(g => g.Name).Take(3))));
        sb.AppendLine();
        sb.AppendLine(
            "What I’m inferring: start with chipset/platform + storage/NIC stacks from the OEM, then discrete GPU vendor (NVIDIA/AMD/Intel) if applicable.");
        sb.AppendLine("What I need live research for: whether a specific optional component has a separate driver package (docks, fingerprint, etc.).");
        sb.AppendLine("Confidence: high on “use OEM support”; medium on exact driver order until you match the OS build on the OEM site.");
    }

    private static void AppendChargerDock(StringBuilder sb, SystemProfile profile, bool playful, bool dock)
    {
        var mc = MachineClassifier.Classify(profile).PrimaryClass;
        sb.AppendLine("What I know (local scan):");
        sb.AppendLine($"• Machine: {profile.Manufacturer} {profile.Model}");
        sb.AppendLine($"• Class hint: {mc}");
        sb.AppendLine();
        if (dock)
        {
            sb.AppendLine(
                "What I’m inferring: dock compatibility is **model- and generation-specific** (Thunderbolt vs USB-C DP-alt mode, wattage passthrough).");
            sb.AppendLine(
                "What I need: your exact SKU/family and port types — then check the OEM dock compatibility matrix or live research for **likely compatible candidates** (not exact unless the manual lists a part).");
        }
        else
        {
            sb.AppendLine(
                "What I’m inferring: laptops need the **wattage and barrel/USB-C PD profile** the OEM specified — wrong wattage can throttle or fail to charge.");
            sb.AppendLine(
                "What I need live research for: part numbers for the factory adapter; I’ll only treat those as exact when they match OEM docs.");
        }

        if (playful)
        {
            sb.AppendLine("Repair gremlin note: marketplace “compatible” chargers are where laptops meet spicy electricity — verify watts and tip/USB-PD.");
        }

        sb.AppendLine("Confidence: medium until we match OEM wattage / Thunderbolt generation.");
    }
}
