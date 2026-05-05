using System;
using System.Linq;
using System.Text;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Kyra;

/// <summary>
/// Deterministic, scan-grounded answers for short hardware identity questions (no provider invention).
/// </summary>
public static class KyraLocalSpecAnswerBuilder
{
    private const string NoScanBody =
        "I do not have a current System Intelligence scan yet. Run System Intelligence first.";

    public static bool TryBuildLocalSpecAnswer(string? prompt, SystemProfile? profile, out CopilotResponse response)
    {
        response = new CopilotResponse();
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 200)
        {
            return false;
        }

        if (!IsHardwareSpecQuestion(prompt))
        {
            return false;
        }

        var grounded = profile is not null;
        var body = grounded ? BuildAnswerFromProfile(prompt.Trim(), profile!) : NoScanBody;
        var footer = grounded
            ? "_Kyra · grounded in latest System Intelligence scan_"
            : "_Kyra · no current scan available_";
        var text = body.TrimEnd() + Environment.NewLine + Environment.NewLine + footer;

        response = new CopilotResponse
        {
            Text = text,
            UsedOnlineData = false,
            OnlineStatus = "Kyra Mode: Local hardware facts (System Intelligence scan).",
            ProviderType = CopilotProviderType.LocalOffline,
            ProviderNotes = ["Kyra routing: local-first deterministic hardware spec answer."],
            ResponseSource = KyraResponseSource.LocalKyra,
            SourceLabel = KyraResponseComposer.KyraIdentityLabel,
            GroundedInSystemIntelligence = grounded,
            ActionSuggestions = []
        };

        return true;
    }

    private static bool IsHardwareSpecQuestion(string prompt)
    {
        var t = prompt.Trim();
        return ContainsLoose(t, "what pc are we") ||
               (ContainsLoose(t, "what pc") && ContainsLoose(t, "work")) ||
               ContainsLoose(t, "what computer") ||
               ContainsLoose(t, "what are my specs") ||
               ContainsLoose(t, "what are the specs") ||
               (ContainsLoose(t, "my specs") && !ContainsLoose(t, "resale")) ||
               ContainsLoose(t, "what cpu") ||
               ContainsLoose(t, "which cpu") ||
               ContainsLoose(t, "cpu do i have") ||
               ContainsLoose(t, "what gpu") ||
               ContainsLoose(t, "which gpu") ||
               ContainsLoose(t, "what graphics") ||
               ContainsLoose(t, "what kind of machine") ||
               ContainsLoose(t, "machine class") ||
               ContainsLoose(t, "business class") ||
               ContainsLoose(t, "consumer") ||
               ContainsLoose(t, "what sensors") ||
               ContainsLoose(t, "sensors missing") ||
               ContainsLoose(t, "fan speed") ||
               ContainsLoose(t, "overheating") ||
               ContainsLoose(t, "how much ram") ||
               ContainsLoose(t, "what storage") ||
               ContainsLoose(t, "storage is in this") ||
               ContainsLoose(t, "drives in this") ||
               ContainsLoose(t, "best for") ||
               ContainsLoose(t, "best use") ||
               ContainsLoose(t, "device fit") ||
               ContainsLoose(t, "good for coding") ||
               ContainsLoose(t, "run games") ||
               ContainsLoose(t, "gaming laptop") ||
               ContainsLoose(t, "sell this as");
    }

    private static bool ContainsLoose(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static string BuildAnswerFromProfile(string prompt, SystemProfile p)
    {
        var t = prompt.Trim();
        if (IsCpuQuestion(t))
        {
            return FormatCpu(p);
        }

        if (IsGpuQuestion(t))
        {
            return FormatGpu(p);
        }

        if (IsRamQuestion(t))
        {
            return FormatRam(p);
        }

        if (IsStorageQuestion(t))
        {
            return FormatStorage(p);
        }

        if (IsDeviceFitQuestion(t))
        {
            return FormatDeviceFit(p);
        }

        if (IsMachineClassQuestion(t))
        {
            return FormatMachineClass(p);
        }

        if (IsSensorQuestion(t))
        {
            return FormatSensorMatrix(p, t);
        }

        if (IsPcQuestion(t))
        {
            return FormatPc(p);
        }

        return FormatSpecs(p);
    }

    private static bool IsCpuQuestion(string t) =>
        ContainsLoose(t, "cpu") || ContainsLoose(t, "processor");

    private static bool IsGpuQuestion(string t) =>
        ContainsLoose(t, "gpu") || ContainsLoose(t, "graphics");

    private static bool IsRamQuestion(string t) => ContainsLoose(t, "ram");

    private static bool IsStorageQuestion(string t) =>
        ContainsLoose(t, "storage") || ContainsLoose(t, "drive") || ContainsLoose(t, "disk");

    private static bool IsPcQuestion(string t) =>
        ContainsLoose(t, "what pc") || ContainsLoose(t, "computer") || ContainsLoose(t, "laptop");

    private static bool IsMachineClassQuestion(string t) =>
        ContainsLoose(t, "what kind of machine") ||
        ContainsLoose(t, "machine class") ||
        ContainsLoose(t, "business class") ||
        ContainsLoose(t, "consumer") ||
        ContainsLoose(t, "gaming laptop");

    private static bool IsSensorQuestion(string t) =>
        ContainsLoose(t, "what sensors") ||
        ContainsLoose(t, "sensors missing") ||
        ContainsLoose(t, "fan speed") ||
        ContainsLoose(t, "overheating") ||
        ContainsLoose(t, "temperature");

    private static bool IsDeviceFitQuestion(string t) =>
        ContainsLoose(t, "best for") ||
        ContainsLoose(t, "best use") ||
        ContainsLoose(t, "device fit") ||
        ContainsLoose(t, "good for coding") ||
        ContainsLoose(t, "run games") ||
        ContainsLoose(t, "gaming laptop") ||
        ContainsLoose(t, "sell this as");

    private static string FormatCpu(SystemProfile p)
    {
        var cores = p.CpuCores is { } c ? $"{c}P/" : string.Empty;
        var threads = p.CpuThreads is { } th ? $"{th}T" : "threads unknown";
        return $"CPU (from System Intelligence): {p.Cpu} ({cores}{threads}).";
    }

    private static string FormatGpu(SystemProfile p)
    {
        if (p.Gpus.Count == 0)
        {
            return "GPU (from System Intelligence): no discrete/integrated GPU rows were captured in the last scan.";
        }

        var lines = p.Gpus.Select(g => $"• {g.Name} ({g.GpuKind})").ToArray();
        return "GPU (from System Intelligence):" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string FormatRam(SystemProfile p)
    {
        var gb = p.RamTotalGb is { } g
            ? FormattableString.Invariant($"{g:0.#} GB")
            : p.RamTotal;
        return $"RAM (from System Intelligence): {gb} total. Reported speed summary: {p.RamSpeed}.";
    }

    private static string FormatStorage(SystemProfile p)
    {
        if (p.Disks.Count == 0)
        {
            return "Storage (from System Intelligence): no physical disks were listed in the last scan JSON.";
        }

        var lines = p.Disks.Select(d =>
                $"• {d.Name}: {d.Size}, {d.MediaType}, health {d.Health} ({d.Status})")
            .ToArray();
        return "Storage (from System Intelligence):" + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string FormatPc(SystemProfile p)
    {
        var machineClass = MachineClassifier.Classify(p);
        return $"This PC (from System Intelligence): {p.Manufacturer} {p.Model}, running {p.OperatingSystem}. Machine class: {machineClass.PrimaryClass} ({machineClass.Confidence} confidence).";
    }

    private static string FormatDeviceFit(SystemProfile p)
    {
        var fit = new DeviceFitEngine().Evaluate(p);
        return "Best use / device fit (from System Intelligence):" + Environment.NewLine +
               DeviceFitEngine.FormatCard(fit) + Environment.NewLine +
               "Inference note: this fit is inferred from scanned CPU/RAM/GPU/storage/battery signals. Gaming, thermals, and runtime are confidence-limited unless benchmarked.";
    }

    private static string FormatMachineClass(SystemProfile p)
    {
        var machineClass = MachineClassifier.Classify(p);
        var signals = machineClass.Signals.Take(4).Select(signal => $"- {signal.Name}: {signal.Value}");
        return
            $"Machine class (from System Intelligence): {machineClass.PrimaryClass} ({machineClass.Confidence} confidence)." + Environment.NewLine +
            $"Secondary: {string.Join("; ", machineClass.SecondaryClasses.DefaultIfEmpty("none"))}" + Environment.NewLine +
            machineClass.TechnicianNote + Environment.NewLine +
            string.Join(Environment.NewLine, signals);
    }

    private static string FormatSensorMatrix(SystemProfile p, string prompt)
    {
        var sensors = SensorMatrixBuilder.Build(p);
        var missingQuery = sensors.Groups
            .SelectMany(group => group.Readings)
            .Where(reading => reading.IsUnavailable);
        if (ContainsLoose(prompt, "fan"))
        {
            missingQuery = missingQuery
                .OrderByDescending(reading => reading.Name.Contains("fan", StringComparison.OrdinalIgnoreCase));
        }

        var missing = missingQuery
            .Take(6)
            .Select(reading => $"- {reading.Name}: {reading.UnavailableReason}. {reading.TechnicianNote}");
        return
            "Sensor / Hardware X-Ray coverage (from System Intelligence):" + Environment.NewLine +
            sensors.CoverageSummary + Environment.NewLine +
            $"Confidence: {sensors.Confidence}" + Environment.NewLine +
            "Unavailable sensors are exposure limits, not hardware failures." + Environment.NewLine +
            "Status guide: Unknown = lower confidence; NotExposed = firmware/driver/permission limit; Inferred = derived from another signal; Failure = only with explicit warning/critical evidence." + Environment.NewLine +
            string.Join(Environment.NewLine, missing);
    }

    private static string FormatSpecs(SystemProfile p)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Specs (from System Intelligence, latest scan):");
        sb.AppendLine($"- System: {p.Manufacturer} {p.Model}");
        sb.AppendLine($"- OS: {p.OperatingSystem} ({p.OsBuild})");
        sb.AppendLine($"- CPU: {p.Cpu}");
        sb.AppendLine($"- RAM: {p.RamTotal} ({p.RamSpeed})");
        if (p.Gpus.Count > 0)
        {
            sb.AppendLine("- GPU(s):");
            foreach (var g in p.Gpus)
            {
                sb.Append("  • ").Append(g.Name).Append(" (").Append(g.GpuKind).AppendLine(")");
            }
        }
        else
        {
            sb.AppendLine("- GPU(s): not listed in scan JSON");
        }

        if (p.Disks.Count > 0)
        {
            sb.AppendLine("- Storage:");
            foreach (var d in p.Disks.Take(6))
            {
                sb.Append("  • ")
                    .Append(d.Name)
                    .Append(": ")
                    .Append(d.Size)
                    .Append(", ")
                    .Append(d.MediaType)
                    .AppendLine();
            }
        }
        else
        {
            sb.AppendLine("- Storage: not listed in scan JSON");
        }

        return sb.ToString().TrimEnd();
    }
}
