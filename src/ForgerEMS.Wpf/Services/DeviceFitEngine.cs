using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace VentoyToolkitSetup.Wpf.Services;

public sealed class DeviceFitReason
{
    public string Text { get; init; } = string.Empty;

    public string Evidence { get; init; } = string.Empty;
}

public sealed class DeviceFitScore
{
    public string Category { get; init; } = string.Empty;

    public int Score { get; init; }

    public string Label { get; init; } = "Unknown";

    public string Confidence { get; init; } = "Medium";

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> LimitingFactors { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExampleWorkloads { get; init; } = Array.Empty<string>();
}

public sealed class DeviceFitProfile
{
    public string Name { get; init; } = string.Empty;

    public int Score { get; init; }

    public string Confidence { get; init; } = "Medium";
}

public sealed class DeviceFitResult
{
    public string PrimaryFit { get; init; } = "Unknown / needs scan";

    public string MachineClass { get; init; } = "Unknown / Mixed";

    public string Confidence { get; init; } = "Low";

    public IReadOnlyList<DeviceFitProfile> SecondaryProfiles { get; init; } = Array.Empty<DeviceFitProfile>();

    public IReadOnlyList<string> StrongFits { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> WeakFits { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> ExampleWorkloads { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> UpgradeFirstAdvice { get; init; } = Array.Empty<string>();

    public string ListingPositioning { get; init; } = string.Empty;

    public IReadOnlyList<DeviceFitScore> Scores { get; init; } = Array.Empty<DeviceFitScore>();

    public IReadOnlyList<DeviceFitReason> Reasons { get; init; } = Array.Empty<DeviceFitReason>();

    public string SummaryLine =>
        $"{PrimaryFit} ({Confidence} confidence). Listing angle: {ListingPositioning}";
}

public sealed class DeviceFitEngine
{
    public DeviceFitResult Evaluate(SystemProfile? profile)
    {
        if (profile is null)
        {
            return new DeviceFitResult
            {
                PrimaryFit = "Unknown / run System Intelligence",
                Confidence = "Low",
                WeakFits = ["Machine-specific use guidance needs a System Intelligence scan."],
                UpgradeFirstAdvice = ["Run System Intelligence before recommending workloads, games, or resale positioning."],
                ListingPositioning = "Do not market until specs are scanned."
            };
        }

        var machineClass = MachineClassifier.Classify(profile);
        var signals = DeviceFitSignals.FromProfile(profile);
        var scores = BuildScores(profile, signals);
        var primary = PickPrimaryFit(scores, signals, machineClass);
        var confidence = CalculateConfidence(profile, signals);
        var strong = BuildStrongFits(scores, signals);
        var weak = BuildWeakFits(scores, signals);
        var examples = BuildExamples(scores, signals);
        var upgrades = BuildUpgradeAdvice(profile, signals);
        var reasons = BuildReasons(profile, signals);

        return new DeviceFitResult
        {
            PrimaryFit = primary,
            MachineClass = machineClass.PrimaryClass,
            Confidence = confidence,
            SecondaryProfiles = scores
                .Where(score => score.Score >= 68 && !primary.Contains(score.Category, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(score => score.Score)
                .Take(4)
                .Select(score => new DeviceFitProfile
                {
                    Name = score.Category,
                    Score = score.Score,
                    Confidence = score.Confidence
                })
                .ToArray(),
            StrongFits = strong,
            WeakFits = weak,
            ExampleWorkloads = examples,
            UpgradeFirstAdvice = upgrades,
            ListingPositioning = BuildListingPositioning(primary, signals),
            Scores = scores,
            Reasons = reasons
        };
    }

    public static string FormatCard(DeviceFitResult result)
    {
        return
            $"Primary fit: {result.PrimaryFit}{Environment.NewLine}" +
            $"Machine class: {result.MachineClass}{Environment.NewLine}" +
            $"Confidence: {result.Confidence}{Environment.NewLine}" +
            $"Strong fits: {Join(result.StrongFits.Take(3), "needs scan")}{Environment.NewLine}" +
            $"Watch-outs: {Join(result.WeakFits.Take(2), "none obvious")}{Environment.NewLine}" +
            $"Examples: {Join(result.ExampleWorkloads.Take(3), "no examples available")}{Environment.NewLine}" +
            $"Upgrade/listing advice: {Join(result.UpgradeFirstAdvice.Take(2), "clean install, update drivers, verify condition")}{Environment.NewLine}" +
            $"Listing angle: {result.ListingPositioning}";
    }

    private static DeviceFitScore[] BuildScores(SystemProfile profile, DeviceFitSignals s)
    {
        var scores = new List<DeviceFitScore>
        {
            Score("Office / school / general use", 55 + s.CpuScore / 4 + s.RamScore / 4 + s.StorageScore / 5, s, ["Office, school, browser, email, video calls"], []),
            Score("Web / streaming", 60 + s.CpuScore / 5 + s.RamScore / 5 + s.StorageScore / 6, s, ["YouTube, streaming, browser tabs, cloud apps"], []),
            Score("Software development", 30 + s.CpuScore / 2 + s.RamScore / 3 + s.StorageScore / 4, s, ["Visual Studio Code, Git, WSL, Docker-lite, scripting, diagnostics"], BuildDevLimits(s)),
            Score("Technician / repair / diagnostics", 40 + s.CpuScore / 3 + s.RamScore / 4 + s.StorageScore / 4 + (profile.OperatingSystem.Contains("Pro", StringComparison.OrdinalIgnoreCase) ? 8 : 0), s, ["ForgerEMS, Ventoy prep, SMART tools, WSL, remote support, light VMs"], []),
            Score("Light gaming", 35 + s.CpuScore / 4 + s.GpuScore / 2 + s.RamScore / 6, s, ["Roblox, Minecraft, Terraria, Stardew Valley, older esports, emulation, indie games"], []),
            Score("Medium gaming", 15 + s.CpuScore / 4 + s.GpuScore / 2 + s.RamScore / 8, s, ["Fortnite Performance Mode, GTA V, Skyrim, Rocket League, Valorant, older AAA titles"], BuildGamingLimits(s)),
            Score("Heavy gaming", s.GpuScore / 2 + s.CpuScore / 5 + s.RamScore / 10, s, ["Modern AAA, high settings, ray tracing, VR"], BuildHeavyGamingLimits(s)),
            Score("Content creation", 25 + s.CpuScore / 3 + s.RamScore / 4 + s.GpuScore / 4 + s.StorageScore / 5, s, ["Photoshop-style edits, light Premiere/DaVinci timelines, OBS, Canva, batch media work"], BuildCreationLimits(s)),
            Score("CAD / workstation", 20 + s.CpuScore / 3 + s.RamScore / 4 + s.WorkstationGpuBonus + s.StorageScore / 6, s, ["AutoCAD/SolidWorks-light, 2D/3D review, workstation apps, model viewing"], BuildWorkstationLimits(s)),
            Score("AI / local model testing", 10 + s.CpuScore / 5 + s.RamScore / 3 + s.GpuScore / 4, s, ["Small CPU models, embeddings tests, light CUDA experiments if supported"], BuildAiLimits(s)),
            Score("Homelab / server / NAS", 25 + s.CpuScore / 4 + s.RamScore / 3 + s.StorageScore / 5, s, ["Proxmox/Hyper-V lab, Docker services, diagnostics server, light NAS workflows"], BuildHomelabLimits(s)),
            Score("Linux compatibility", 62 + (s.HasNvidia ? -6 : 4) + s.StorageScore / 8, s, ["Ubuntu/Fedora/Debian live USB, WSL, repair Linux workflows"], s.HasNvidia ? ["NVIDIA drivers may need extra setup on Linux."] : []),
            Score("Windows 11 readiness", 55 + (profile.TpmReady == true ? 15 : 0) + (profile.SecureBoot == true ? 10 : 0) + (s.CpuGeneration >= 8 ? 15 : 0), s, ["Windows 11 Pro, BitLocker readiness checks, UEFI security verification"], BuildWindowsLimits(profile, s)),
            Score("Travel / battery use", 50 + Math.Min(20, s.BatteryScore) + (s.IsLaptop ? 8 : -20), s, ["Mobile work, classes, field repair, travel sessions"], BuildBatteryLimits(s)),
            Score("Resale / flipping", 30 + s.CpuScore / 4 + s.RamScore / 4 + s.GpuScore / 5 + s.StorageScore / 5 + (s.HasPremiumBrand ? 8 : 0), s, ["Local resale listing, refurb flip, mobile workstation positioning"], [])
        };

        return scores
            .Select(score => NormalizeScore(score, Math.Clamp(score.Score, 0, 100)))
            .ToArray();
    }

    private static DeviceFitScore Score(string category, int score, DeviceFitSignals signals, IReadOnlyList<string> examples, IReadOnlyList<string> limits)
    {
        var reasons = new List<string>();
        if (signals.CpuScore >= 70) reasons.Add("Strong CPU tier.");
        if (signals.RamGb >= 32) reasons.Add("32 GB+ RAM helps multitasking and pro workflows.");
        else if (signals.RamGb >= 16) reasons.Add("16 GB RAM meets a practical baseline.");
        if (signals.HasFastStorage) reasons.Add("SSD/NVMe storage improves responsiveness.");
        if (signals.HasDedicatedGpu) reasons.Add("Dedicated GPU expands creator/gaming/workstation fit.");
        if (category.Contains("Travel", StringComparison.OrdinalIgnoreCase) && signals.BatteryUnknown)
        {
            reasons.Add("Battery wear/runtime data was not exposed (verification item, not confirmed battery failure).");
        }

        return new DeviceFitScore
        {
            Category = category,
            Score = score,
            Label = LabelForScore(score),
            Confidence = ConfidenceFor(signals, category),
            Reasons = reasons.Count == 0 ? ["Limited detected hardware evidence."] : reasons,
            LimitingFactors = limits,
            ExampleWorkloads = examples
        };
    }

    private static DeviceFitScore NormalizeScore(DeviceFitScore score, int normalized) => new()
    {
        Category = score.Category,
        Score = normalized,
        Label = LabelForScore(normalized),
        Confidence = score.Confidence,
        Reasons = score.Reasons,
        LimitingFactors = score.LimitingFactors,
        ExampleWorkloads = score.ExampleWorkloads
    };

    private static string PickPrimaryFit(IReadOnlyList<DeviceFitScore> scores, DeviceFitSignals s, MachineClassResult machineClass)
    {
        if (machineClass.PrimaryClass.Equals("Mobile Workstation", StringComparison.OrdinalIgnoreCase) &&
            s.CpuScore >= 62 &&
            s.RamGb >= 16)
        {
            return "Developer / Creator Workstation + Light Gaming";
        }

        if (machineClass.PrimaryClass.Equals("Business Laptop", StringComparison.OrdinalIgnoreCase) &&
            scores.First(x => x.Category == "Software development").Score >= 70)
        {
            return "Business / developer productivity laptop";
        }

        if (s.HasWorkstationGpu && s.RamGb >= 24 && s.CpuScore >= 65)
        {
            return "Developer / Creator Workstation + Light Gaming";
        }

        if (s.HasGamingGpu && scores.First(x => x.Category == "Medium gaming").Score >= 72)
        {
            return machineClass.PrimaryClass.Equals("Gaming Laptop", StringComparison.OrdinalIgnoreCase)
                ? "Gaming laptop + creator side workloads"
                : "Gaming / Creator Laptop";
        }

        if (scores.First(x => x.Category == "Software development").Score >= 74)
        {
            return "Developer / Technician Workstation";
        }

        if (s.CpuScore < 45 || s.RamGb < 12)
        {
            return "Office / School / Budget Productivity";
        }

        return scores.OrderByDescending(score => score.Score).First().Category;
    }

    private static string CalculateConfidence(SystemProfile profile, DeviceFitSignals s)
    {
        var confidence = 100;
        if (profile.CpuCores is null || profile.CpuThreads is null) confidence -= 10;
        if (profile.RamTotalGb is null or <= 0) confidence -= 12;
        if (profile.Gpus.Count == 0) confidence -= 12;
        if (profile.Disks.Count == 0) confidence -= 10;
        if (s.BatteryUnknown) confidence -= 8;
        return confidence >= 78 ? "High" : confidence >= 55 ? "Medium" : "Low";
    }

    private static string[] BuildStrongFits(IReadOnlyList<DeviceFitScore> scores, DeviceFitSignals s)
    {
        var strong = scores
            .Where(score => score.Score >= 72)
            .OrderByDescending(score => score.Score)
            .Select(score => score.Category)
            .Take(6)
            .ToList();
        if (s.HasWorkstationGpu && !strong.Any(item => item.Contains("CAD", StringComparison.OrdinalIgnoreCase)))
        {
            strong.Add("CAD / workstation tasks");
        }

        return strong.ToArray();
    }

    private static string[] BuildWeakFits(IReadOnlyList<DeviceFitScore> scores, DeviceFitSignals s)
    {
        var weak = scores
            .Where(score => score.Score < 55)
            .OrderBy(score => score.Score)
            .Select(score => score.Category)
            .Take(4)
            .ToList();
        if (s.BatteryUnknown)
        {
            weak.Add("Long unplugged sessions are lower confidence until battery wear/runtime is verified");
        }

        return weak.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] BuildExamples(IReadOnlyList<DeviceFitScore> scores, DeviceFitSignals s)
    {
        var examples = scores
            .Where(score => score.Score >= 65)
            .OrderByDescending(score => score.Score)
            .SelectMany(score => score.ExampleWorkloads.Take(2))
            .Take(8)
            .ToList();
        if (s.HasDedicatedGpu)
        {
            examples.Add("Light/medium gaming and older AAA titles, depending on thermals and drivers");
        }

        return examples.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string[] BuildUpgradeAdvice(SystemProfile profile, DeviceFitSignals s)
    {
        var advice = new List<string>();
        if (s.RamGb is > 0 and < 16) advice.Add("Upgrade to at least 16 GB RAM before resale or development workloads.");
        if (!s.HasFastStorage) advice.Add("Install/verify SSD or NVMe storage before listing.");
        if (profile.Disks.Any(d => d.WearPercent is >= 80 || !IsHealthyDisk(d))) advice.Add("Replace or disclose questionable storage.");
        if (profile.Batteries.Any(b => b.WearPercent is >= 35)) advice.Add("Replace or clearly disclose high battery wear.");
        if (s.BatteryUnknown && s.IsLaptop) advice.Add("Run battery report/vendor diagnostics before advertising runtime.");
        if (advice.Count == 0) advice.Add("Clean install/update drivers, verify thermals, photograph condition, and include charger details.");
        return advice.ToArray();
    }

    private static DeviceFitReason[] BuildReasons(SystemProfile profile, DeviceFitSignals s)
    {
        var reasons = new List<DeviceFitReason>
        {
            new() { Text = $"{profile.CpuCores?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"}-core / {profile.CpuThreads?.ToString(CultureInfo.InvariantCulture) ?? "Unknown"}-thread CPU signal", Evidence = profile.Cpu },
            new() { Text = $"{profile.RamTotal} RAM", Evidence = "SystemProfile.RamTotal" }
        };
        if (s.HasFastStorage) reasons.Add(new DeviceFitReason { Text = "Fast SSD/NVMe storage detected.", Evidence = string.Join("; ", profile.Disks.Select(d => $"{d.Name} {d.MediaType}").Take(2)) });
        if (s.HasDedicatedGpu) reasons.Add(new DeviceFitReason { Text = "Dedicated GPU detected.", Evidence = string.Join("; ", profile.Gpus.Select(g => g.Name).Take(2)) });
        if (profile.OperatingSystem.Contains("Windows 11", StringComparison.OrdinalIgnoreCase)) reasons.Add(new DeviceFitReason { Text = "Windows 11 detected.", Evidence = profile.OperatingSystem });
        if (s.BatteryUnknown) reasons.Add(new DeviceFitReason { Text = "Battery wear/runtime confidence is lower because wear data was not exposed.", Evidence = "Battery wear/cycle fields unavailable" });
        return reasons.ToArray();
    }

    private static string BuildListingPositioning(string primary, DeviceFitSignals s)
    {
        if (primary.Contains("Gaming", StringComparison.OrdinalIgnoreCase) && s.HasGamingGpu)
        {
            return "Market as an entry/mid gaming laptop; include tested games/settings if possible.";
        }

        if (primary.Contains("Workstation", StringComparison.OrdinalIgnoreCase) || s.HasWorkstationGpu)
        {
            return "Market as a mobile workstation/dev laptop, not primarily as a gaming laptop.";
        }

        if (primary.Contains("Office", StringComparison.OrdinalIgnoreCase))
        {
            return "Market as a budget school/office laptop; emphasize SSD, clean Windows install, and verified battery if available.";
        }

        return "Market around the strongest verified fit and disclose unknown battery/security fields honestly.";
    }

    private static string[] BuildDevLimits(DeviceFitSignals s) => s.RamGb < 16
        ? ["RAM below 16 GB limits heavier IDEs, containers, and VMs."]
        : [];

    private static string[] BuildGamingLimits(DeviceFitSignals s) => !s.HasDedicatedGpu
        ? ["No dedicated GPU detected; keep expectations to light games."]
        : s.GpuScore < 70
            ? ["Dedicated GPU is better suited for older/light games than new high-settings AAA."]
            : [];

    private static string[] BuildHeavyGamingLimits(DeviceFitSignals s) => s.GpuScore < 80
        ? ["Modern AAA/high settings/ray tracing/VR need a stronger gaming GPU and thermal headroom."]
        : [];

    private static string[] BuildCreationLimits(DeviceFitSignals s) => s.RamGb < 32
        ? ["32 GB+ RAM is preferred for heavier media timelines."]
        : [];

    private static string[] BuildWorkstationLimits(DeviceFitSignals s) => !s.HasWorkstationGpu
        ? ["No workstation-class GPU was detected."]
        : [];

    private static string[] BuildAiLimits(DeviceFitSignals s) => s.GpuScore < 80
        ? ["Heavy local AI needs more GPU VRAM/compute; treat support as light testing unless benchmarked."]
        : [];

    private static string[] BuildHomelabLimits(DeviceFitSignals s) => s.RamGb < 32
        ? ["More RAM helps VM-heavy homelab workloads."]
        : [];

    private static string[] BuildWindowsLimits(SystemProfile profile, DeviceFitSignals s)
    {
        var limits = new List<string>();
        if (profile.TpmReady is null) limits.Add("TPM state was not exposed; verify before Windows 11 readiness claims.");
        if (profile.SecureBoot is null) limits.Add("Secure Boot state was not exposed; verify in BIOS/UEFI.");
        if (s.CpuGeneration is > 0 and < 8) limits.Add("CPU generation may limit official Windows 11 support.");
        return limits.ToArray();
    }

    private static string[] BuildBatteryLimits(DeviceFitSignals s)
    {
        if (s.BatteryUnknown && s.IsLaptop)
        {
            return ["Battery wear data unavailable; unplugged-session confidence is lower until verified."];
        }

        if (s.BatteryScore < 45)
        {
            return ["Battery wear/cycles reduce travel confidence."];
        }

        return [];
    }

    private static string LabelForScore(int score) => score switch
    {
        >= 85 => "Excellent",
        >= 70 => "Good",
        >= 55 => "Fair",
        >= 35 => "Weak",
        _ => "Not Recommended"
    };

    private static string ConfidenceFor(DeviceFitSignals signals, string category)
    {
        if (category.Contains("Travel", StringComparison.OrdinalIgnoreCase) && signals.BatteryUnknown)
        {
            return "Low";
        }

        if (category.Contains("Gaming", StringComparison.OrdinalIgnoreCase) && signals.GpuVramUnknown)
        {
            return "Medium";
        }

        return signals.CoreFactsConfidence;
    }

    private static bool IsHealthyDisk(SystemDiskProfile disk) =>
        disk.Health.Equals("Healthy", StringComparison.OrdinalIgnoreCase) ||
        disk.Health.Equals("OK", StringComparison.OrdinalIgnoreCase) ||
        string.IsNullOrWhiteSpace(disk.Health);

    private static string Join(IEnumerable<string> values, string fallback)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return items.Length == 0 ? fallback : string.Join("; ", items);
    }

    private sealed class DeviceFitSignals
    {
        public int CpuScore { get; init; }
        public int CpuGeneration { get; init; }
        public int RamScore { get; init; }
        public double RamGb { get; init; }
        public int GpuScore { get; init; }
        public int WorkstationGpuBonus { get; init; }
        public int StorageScore { get; init; }
        public int BatteryScore { get; init; }
        public bool HasDedicatedGpu { get; init; }
        public bool HasGamingGpu { get; init; }
        public bool HasWorkstationGpu { get; init; }
        public bool HasNvidia { get; init; }
        public bool GpuVramUnknown { get; init; }
        public bool HasFastStorage { get; init; }
        public bool BatteryUnknown { get; init; }
        public bool IsLaptop { get; init; }
        public bool HasPremiumBrand { get; init; }
        public string CoreFactsConfidence { get; init; } = "Medium";

        public static DeviceFitSignals FromProfile(SystemProfile profile)
        {
            var cpuScore = ScoreCpu(profile);
            var ramGb = profile.RamTotalGb ?? 0;
            var ramScore = ramGb switch
            {
                >= 64 => 100,
                >= 32 => 90,
                >= 16 => 72,
                >= 8 => 48,
                > 0 => 25,
                _ => 35
            };

            var gpuText = string.Join(" ", profile.Gpus.Select(gpu => $"{gpu.Name} {gpu.GpuKind}"));
            var hasNvidia = gpuText.Contains("nvidia", StringComparison.OrdinalIgnoreCase) ||
                            gpuText.Contains("quadro", StringComparison.OrdinalIgnoreCase) ||
                            gpuText.Contains("rtx", StringComparison.OrdinalIgnoreCase) ||
                            gpuText.Contains("gtx", StringComparison.OrdinalIgnoreCase);
            var hasGamingGpu = Regex.IsMatch(gpuText, "(rtx|gtx|radeon\\s+rx|geforce)", RegexOptions.IgnoreCase);
            var hasWorkstationGpu = Regex.IsMatch(gpuText, "(quadro|rtx\\s+a\\d|firepro|radeon\\s+pro)", RegexOptions.IgnoreCase);
            var hasDedicated = hasGamingGpu || hasWorkstationGpu || Regex.IsMatch(gpuText, "(dedicated|discrete|nvidia|amd radeon|arc)", RegexOptions.IgnoreCase);
            var gpuScore = ScoreGpu(gpuText, hasDedicated);
            var hasFastStorage = profile.Disks.Any(d =>
                d.MediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                d.MediaType.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ||
                d.Name.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                d.Name.Contains("NVMe", StringComparison.OrdinalIgnoreCase));
            var storageScore = hasFastStorage ? 88 : profile.Disks.Count > 0 ? 48 : 40;
            var batteryWearKnown = profile.Batteries.Any(b => b.WearPercent.HasValue || b.CycleCount.HasValue);
            var worstWear = profile.Batteries.Where(b => b.WearPercent.HasValue).Select(b => b.WearPercent!.Value).DefaultIfEmpty(0).Max();
            var batteryScore = profile.Batteries.Count == 0 ? 60 : worstWear switch
            {
                >= 50 => 25,
                >= 35 => 42,
                > 0 => 70,
                _ => 58
            };
            var isLaptop = profile.Batteries.Count > 0 ||
                           Regex.IsMatch($"{profile.Manufacturer} {profile.Model}", "(laptop|notebook|precision|latitude|thinkpad|elitebook|surface)", RegexOptions.IgnoreCase);
            var knownCoreFacts = 0;
            if (!string.IsNullOrWhiteSpace(profile.Cpu) && !profile.Cpu.Contains("Unknown", StringComparison.OrdinalIgnoreCase)) knownCoreFacts++;
            if (ramGb > 0) knownCoreFacts++;
            if (profile.Gpus.Count > 0) knownCoreFacts++;
            if (profile.Disks.Count > 0) knownCoreFacts++;

            return new DeviceFitSignals
            {
                CpuScore = cpuScore.score,
                CpuGeneration = cpuScore.generation,
                RamScore = ramScore,
                RamGb = ramGb,
                GpuScore = gpuScore,
                WorkstationGpuBonus = hasWorkstationGpu ? 34 : 0,
                StorageScore = storageScore,
                BatteryScore = batteryScore,
                HasDedicatedGpu = hasDedicated,
                HasGamingGpu = hasGamingGpu,
                HasWorkstationGpu = hasWorkstationGpu,
                HasNvidia = hasNvidia,
                GpuVramUnknown = hasDedicated,
                HasFastStorage = hasFastStorage,
                BatteryUnknown = profile.Batteries.Count > 0 && !batteryWearKnown,
                IsLaptop = isLaptop,
                HasPremiumBrand = Regex.IsMatch($"{profile.Manufacturer} {profile.Model}", "(dell|precision|latitude|thinkpad|elitebook|macbook|surface|xps)", RegexOptions.IgnoreCase),
                CoreFactsConfidence = knownCoreFacts >= 4 ? "High" : knownCoreFacts >= 2 ? "Medium" : "Low"
            };
        }

        private static (int score, int generation) ScoreCpu(SystemProfile profile)
        {
            var cpu = profile.Cpu ?? string.Empty;
            var generation = InferCpuGeneration(cpu);
            var score = 42;
            if (Regex.IsMatch(cpu, "(i9|ryzen\\s*9|xeon|ultra\\s*9)", RegexOptions.IgnoreCase)) score = 90;
            else if (Regex.IsMatch(cpu, "(i7|ryzen\\s*7|ultra\\s*7)", RegexOptions.IgnoreCase)) score = 76;
            else if (Regex.IsMatch(cpu, "(i5|ryzen\\s*5|ultra\\s*5)", RegexOptions.IgnoreCase)) score = 62;
            else if (Regex.IsMatch(cpu, "(i3|ryzen\\s*3)", RegexOptions.IgnoreCase)) score = 44;
            else if (Regex.IsMatch(cpu, "(celeron|pentium|athlon)", RegexOptions.IgnoreCase)) score = 24;

            if (Regex.IsMatch(cpu, "(\\bH\\b|\\d{4,5}H\\b|HX\\b)", RegexOptions.IgnoreCase)) score += 8;
            if (profile.CpuCores is >= 8) score += 8;
            else if (profile.CpuCores is >= 6) score += 5;
            else if (profile.CpuCores is <= 2) score -= 12;
            if (generation is > 0 and < 8) score -= 10;
            else if (generation >= 12) score += 6;
            return (Math.Clamp(score, 0, 100), generation);
        }

        private static int ScoreGpu(string gpuText, bool hasDedicated)
        {
            if (Regex.IsMatch(gpuText, "(rtx\\s*40|rtx\\s*30|rx\\s*7|rx\\s*6)", RegexOptions.IgnoreCase)) return 88;
            if (Regex.IsMatch(gpuText, "(rtx\\s*20|gtx\\s*16|quadro\\s+t2000|quadro\\s+p|radeon\\s+rx|arc)", RegexOptions.IgnoreCase)) return 70;
            if (hasDedicated) return 62;
            if (Regex.IsMatch(gpuText, "(iris|vega|uhd|integrated)", RegexOptions.IgnoreCase)) return 35;
            return 28;
        }

        private static int InferCpuGeneration(string cpu)
        {
            var match = Regex.Match(cpu, @"i[3579][- ](?<gen>\d{4,5})", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var digits = match.Groups["gen"].Value;
                if (digits.Length >= 5 && int.TryParse(digits[..2], out var gen2)) return gen2;
                if (digits.Length >= 4 && int.TryParse(digits[..1], out var gen1)) return gen1;
            }

            match = Regex.Match(cpu, @"ryzen\s+[3579]\s+(?<gen>\d{4})", RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups["gen"].Value[..1], out var ryzenGen))
            {
                return ryzenGen + 6;
            }

            return 0;
        }
    }
}
