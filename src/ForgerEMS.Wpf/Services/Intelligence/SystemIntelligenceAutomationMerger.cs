using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using VentoyToolkitSetup.Wpf.Services;

namespace VentoyToolkitSetup.Wpf.Services.Intelligence;

/// <summary>Merges automation metadata (health narrative, issues, recommendations) into system-intelligence-latest.json.</summary>
public static class SystemIntelligenceAutomationMerger
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static bool TryMerge(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return false;
        }

        try
        {
            var text = File.ReadAllText(reportPath);
            using var doc = JsonDocument.Parse(text);
            var profile = SystemProfileMapper.FromJson(doc.RootElement);
            var health = SystemHealthEvaluator.Evaluate(profile);
            var recs = RecommendationEngine.Generate(profile, health);
            var machineClass = MachineClassifier.Classify(profile);
            var sensorMatrix = SensorMatrixBuilder.Build(profile);
            sensorMatrix = ApplyUsbIntelligenceCoverage(reportPath, sensorMatrix);
            var deviceFit = new DeviceFitEngine().Evaluate(profile);

            var automation = BuildAutomationNode(doc.RootElement, profile, health, recs, deviceFit, machineClass, sensorMatrix);
            var root = JsonNode.Parse(text)?.AsObject();
            if (root is null)
            {
                return false;
            }

            root["deviceFit"] = JsonSerializer.SerializeToNode(deviceFit, SerializerOptions);
            root["machineClass"] = JsonSerializer.SerializeToNode(machineClass, SerializerOptions);
            root["sensorMatrix"] = JsonSerializer.SerializeToNode(sensorMatrix, SerializerOptions);
            root["forgerSensorStack"] = JsonSerializer.SerializeToNode(
                ForgerSensorStackState.Create(ResolveElevatedScanState(doc.RootElement)),
                SerializerOptions);
            root["forgerAutomation"] = JsonSerializer.SerializeToNode(automation, SerializerOptions);
            File.WriteAllText(reportPath, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));

            IntelligenceLogWriter.Append("system-intelligence.log", $"Automation metadata merged into {reportPath}");
            return true;
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("system-intelligence.log", $"Automation merge failed: {ex.Message}");
            return false;
        }
    }

    private static object BuildAutomationNode(
        JsonElement root,
        SystemProfile profile,
        SystemHealthEvaluation health,
        IReadOnlyList<string> recs,
        DeviceFitResult deviceFit,
        MachineClassResult machineClass,
        SensorMatrixResult sensorMatrix)
    {
        var issues = new List<object>();
        foreach (var issue in health.DetectedIssues.Where(i =>
                     !string.IsNullOrWhiteSpace(i) &&
                     !i.Contains("No obvious blocking", StringComparison.OrdinalIgnoreCase)))
        {
            var blocked = issue.Contains("Storage needs review", StringComparison.OrdinalIgnoreCase) ||
                          issue.Contains("blocking", StringComparison.OrdinalIgnoreCase);
            issues.Add(new
            {
                severity = blocked ? "Blocked" : "Warning",
                code = "system_health",
                message = issue,
                suggestedFix = PickFix(issue)
            });
        }

        var breakdown = BuildHealthBreakdown(health, profile, issues.Count);

        var norm = BuildNormalizedHardware(root, profile);

        var summary =
            $"Health {health.HealthScore}/100. " +
            $"Scan Confidence {health.ConfidenceScore}/100. " +
            $"{norm.CpuTier}. " +
            $"GPUs: {string.Join(", ", norm.GpuClasses)}. " +
            $"Boot volume: {norm.BootVolume}. " +
            $"Network: {norm.NetworkAdapterSummary}. " +
            $"Machine class: {machineClass.PrimaryClass}. " +
            $"Best use: {deviceFit.PrimaryFit}.";

        return new
        {
            schemaVersion = "1.0",
            mergedUtc = DateTimeOffset.UtcNow,
            summaryLine = summary,
            healthScore = health.HealthScore,
            healthScoreBreakdown = breakdown,
            deviceFitSummary = new
            {
                primaryFit = deviceFit.PrimaryFit,
                machineClass = deviceFit.MachineClass,
                confidence = deviceFit.Confidence,
                strongFits = deviceFit.StrongFits.Take(5).ToArray(),
                weakFits = deviceFit.WeakFits.Take(4).ToArray(),
                listingPositioning = deviceFit.ListingPositioning
            },
            machineClassSummary = new
            {
                primaryClass = machineClass.PrimaryClass,
                confidence = machineClass.Confidence,
                secondaryClasses = machineClass.SecondaryClasses.Take(3).ToArray(),
                note = machineClass.TechnicianNote
            },
            sensorCoverageSummary = new
            {
                confidence = sensorMatrix.Confidence,
                coverage = sensorMatrix.CoverageSummary,
                groups = sensorMatrix.Groups.Select(group => new
                {
                    group.Category,
                    group.KnownFields,
                    group.TotalFields,
                    group.Summary
                }).ToArray(),
                providers = sensorMatrix.SensorProviders.Select(provider => new
                {
                    provider.ProviderName,
                    provider.ProviderVersion,
                    provider.ProviderKind,
                    provider.IsEnabled,
                    provider.IsBundled,
                    provider.RequiresAdmin,
                    provider.IsReadOnly,
                    provider.TrustLevel,
                    provider.RuntimeMode,
                    provider.FailureReason
                }).ToArray(),
                deepSensorMode = sensorMatrix.DeepSensorMode,
                forgerSensorStack = sensorMatrix.ForgerSensorStack,
                note = sensorMatrix.DeepSensorModeNote
            },
            deepSensorMode = new
            {
                value = sensorMatrix.DeepSensorMode.Mode,
                source = sensorMatrix.DeepSensorMode.Source,
                enabled = sensorMatrix.DeepSensorMode.IsEnabled,
                providerActive = sensorMatrix.SensorProviders.Any(provider =>
                    provider.ProviderName.Contains("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) &&
                    provider.IsEnabled),
                providerBundled = sensorMatrix.SensorProviders.Any(provider =>
                    provider.ProviderName.Contains("LibreHardwareMonitor", StringComparison.OrdinalIgnoreCase) &&
                    provider.IsBundled),
                readOnly = true,
                noControlCapabilities = true,
                noticeText = "Deep Sensor Mode reads local hardware sensor data only while ForgerEMS is running or scanning. No sensor control or cloud service is used."
            },
            issues,
            recommendedActions = recs.ToArray(),
            normalizedHardware = norm
        };
    }

    private static string ResolveElevatedScanState(JsonElement root)
    {
        var scanMode = GetJsonString(root, "scanMode");
        if (!scanMode.Equals("Elevated", StringComparison.OrdinalIgnoreCase))
        {
            return "Recommended";
        }

        if (root.TryGetProperty("portPowerTelemetry", out var portPower) &&
            portPower.ValueKind == JsonValueKind.Object &&
            (HasNumber(portPower, "effectiveChargeRateWatts") ||
             HasNumber(portPower, "adapterWattageWatts") ||
             HasNumber(portPower, "adapterWattageClassWatts") ||
             HasNumber(portPower, "voltageVolts") ||
             HasNumber(portPower, "currentAmps")))
        {
            return "Complete";
        }

        return "Partial";
    }

    private static bool HasNumber(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.Number ||
               (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out _));
    }

    private static object[] BuildHealthBreakdown(SystemHealthEvaluation health, SystemProfile profile, int issueCount)
    {
        var rows = new List<object>
        {
            new
            {
                factor = "Overall scan status",
                points = 0,
                rationale = $"Scan overall status: {profile.OverallStatus}."
            },
            new
            {
                factor = "Active issues",
                points = issueCount,
                rationale = issueCount == 0
                    ? "No issues were promoted from the evaluator."
                    : $"{issueCount} issue row(s) were generated for Kyra and diagnostics."
            }
        };

        rows.AddRange(health.Categories.Select(category => new
        {
            factor = category.Category,
            points = category.Score,
            rationale = $"{category.Status}, confidence {category.Confidence}: {string.Join("; ", category.Reasons.Take(2))}"
        }));

        rows.Add(new
        {
            factor = "Final health score",
            points = health.HealthScore,
            rationale = "Composite 0-100 score from SystemHealthEvaluator. Unknown/not exposed data reduces confidence more than health."
        });

        return rows.ToArray();
    }

    private static ForgerNormalizedHardwareSummary BuildNormalizedHardware(JsonElement root, SystemProfile profile)
    {
        var tier = InferCpuTier(profile.Cpu);
        var gpuClasses = ExtractGpuClasses(root, profile);
        var boot = Environment.GetEnvironmentVariable("SystemDrive") ?? "UNKNOWN";
        var (phys, virt) = CountAdapterRoles(root);
        var security = SummarizeSecurity(root);
        var ram = SummarizeRam(root);

        return new ForgerNormalizedHardwareSummary
        {
            CpuTier = tier,
            GpuClasses = gpuClasses,
            BootVolume = boot,
            RamConfiguredVsRated = ram,
            NetworkAdapterSummary = $"{phys} physical / {virt} virtual adapters (active scan)",
            SecuritySummary = security
        };
    }

    private static string SummarizeRam(JsonElement root)
    {
        if (!root.TryGetProperty("summary", out var s))
        {
            return "unknown";
        }

        var cfg = GetJsonString(s, "ramConfiguredSpeedDisplay");
        var rated = GetJsonString(s, "ramModuleRatedSpeedDisplay");
        return $"configured: {cfg}; rated: {rated}";
    }

    private static string SummarizeSecurity(JsonElement root)
    {
        if (!root.TryGetProperty("security", out var sec))
        {
            return "Security details unavailable.";
        }

        var bit = "BitLocker: ";
        if (sec.TryGetProperty("bitLockerSummary", out var bl))
        {
            bit += GetJsonString(bl, "friendlyDisplayText");
        }
        else
        {
            bit += "not summarized";
        }

        _ = root.TryGetProperty("summary", out var summary);
        var tpm = summary.ValueKind != JsonValueKind.Undefined
            ? $"TPM: {GetJsonString(summary, "tpmInfo")}"
            : "TPM: unknown";
        var sb = summary.ValueKind != JsonValueKind.Undefined
            ? GetJsonString(summary, "secureBootInfo")
            : "unknown";

        return $"{tpm}; Secure Boot: {sb}; {bit}";
    }

    private static (int physical, int virtualAdapters) CountAdapterRoles(JsonElement root)
    {
        if (!root.TryGetProperty("network", out var net))
        {
            return (0, 0);
        }

        var explicitPhys = net.TryGetProperty("physicalAdapters", out var physicalAdapters) && physicalAdapters.ValueKind == JsonValueKind.Array
            ? physicalAdapters.GetArrayLength()
            : (int?)null;
        var explicitVirt = net.TryGetProperty("virtualAdapters", out var virtualAdapters) && virtualAdapters.ValueKind == JsonValueKind.Array
            ? virtualAdapters.GetArrayLength()
            : (int?)null;
        if (explicitPhys.HasValue && explicitVirt.HasValue)
        {
            return (explicitPhys.Value, explicitVirt.Value);
        }

        if (!net.TryGetProperty("adapters", out var adapters) || adapters.ValueKind != JsonValueKind.Array)
        {
            return (explicitPhys ?? 0, explicitVirt ?? 0);
        }

        var phys = 0;
        var virt = 0;
        foreach (var a in adapters.EnumerateArray())
        {
            var role = GetJsonString(a, "adapterRole");
            var name = GetJsonString(a, "name");
            var description = GetJsonString(a, "description");
            if (role.Contains("Virtual", StringComparison.OrdinalIgnoreCase) ||
                role.Contains("VPN", StringComparison.OrdinalIgnoreCase) ||
                role.Contains("Host-Only", StringComparison.OrdinalIgnoreCase) ||
                SystemIntelligenceFormatter.ShouldIgnoreAdapterForWarnings(name, description))
            {
                virt++;
            }
            else if (role.Contains("Physical", StringComparison.OrdinalIgnoreCase) ||
                     role.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) ||
                     role.Contains("ActivePhysical", StringComparison.OrdinalIgnoreCase))
            {
                phys++;
            }
            else
            {
                phys++;
            }
        }

        return (phys, virt);
    }

    private static string[] ExtractGpuClasses(JsonElement root, SystemProfile profile)
    {
        if (!root.TryGetProperty("summary", out var s) || !s.TryGetProperty("gpus", out var gpus) ||
            gpus.ValueKind != JsonValueKind.Array)
        {
            return profile.Gpus.Count == 0
                ? ["Unknown"]
                : profile.Gpus.Select(gpu => ClassifyGpuForSummary(gpu.Name, gpu.GpuKind)).ToArray();
        }

        var list = new List<string>();
        foreach (var g in gpus.EnumerateArray())
        {
            var t = GetJsonString(g, "type");
            var name = GetJsonString(g, "name");
            if (!string.IsNullOrWhiteSpace(t) && !string.Equals(t, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                list.Add($"{t}: {name}");
            }
            else if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                list.Add(ClassifyGpuForSummary(name, t));
            }
        }

        return list.Count > 0 ? list.ToArray() : ["Unknown"];
    }

    private static SensorMatrixResult ApplyUsbIntelligenceCoverage(string reportPath, SensorMatrixResult sensorMatrix)
    {
        var usbPath = Path.Combine(Path.GetDirectoryName(reportPath) ?? string.Empty, "usb-intelligence-latest.json");
        if (!File.Exists(usbPath))
        {
            return sensorMatrix;
        }

        try
        {
            using var usbDoc = JsonDocument.Parse(File.ReadAllText(usbPath));
            var usb = usbDoc.RootElement;
            var readings = BuildUsbReadings(usb);
            if (readings.Length == 0)
            {
                return sensorMatrix;
            }

            var known = readings.Count(r => !r.IsUnavailable);
            var group = new SensorGroup
            {
                Category = "USB",
                KnownFields = known,
                TotalFields = readings.Length,
                Summary = $"{known}/{readings.Length} fields known",
                Readings = readings
            };
            var groups = sensorMatrix.Groups
                .Where(g => !g.Category.Equals("USB", StringComparison.OrdinalIgnoreCase))
                .Concat([group])
                .ToArray();
            var totalKnown = groups.Sum(g => g.KnownFields);
            var total = groups.Sum(g => g.TotalFields);
            var confidenceRatio = total == 0 ? 0 : totalKnown / (double)total;
            return new SensorMatrixResult
            {
                Groups = groups,
                SensorProviders = sensorMatrix.SensorProviders,
                ForgerSensorStack = sensorMatrix.ForgerSensorStack,
                DeepSensorMode = sensorMatrix.DeepSensorMode,
                Confidence = confidenceRatio >= 0.7 ? "High" : confidenceRatio >= 0.45 ? "Medium" : "Low",
                DeepSensorModeNote = sensorMatrix.DeepSensorModeNote
            };
        }
        catch (Exception ex)
        {
            IntelligenceLogWriter.Append("system-intelligence.log", $"USB sensor coverage merge skipped: {ex.Message}");
            return sensorMatrix;
        }
    }

    private static SensorReading[] BuildUsbReadings(JsonElement usb)
    {
        var now = DateTimeOffset.UtcNow;
        var readings = new List<SensorReading>();
        if (usb.TryGetProperty("usbDiagnostics", out var diag) && diag.ValueKind == JsonValueKind.Object)
        {
            var mapped = GetJsonString(diag, "usbProfileKnownPortsCount");
            if (int.TryParse(mapped, out var mappedCount) && mappedCount > 0)
            {
                readings.Add(KnownUsb("USB mapped ports", mapped, "USB Intelligence profile", now, "Saved mapped port labels/profiles are available."));
            }

            var risk = GetJsonString(diag, "usbCurrentTargetRiskSummary");
            if (!string.IsNullOrWhiteSpace(risk) && !risk.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                readings.Add(KnownUsb("USB target risk", risk.TrimEnd('.'), "USB Intelligence diagnostics", now, "Current safe target risk is summarized by USB Builder."));
            }

            var best = GetJsonString(diag, "usbBestKnownPortSummary");
            if (!string.IsNullOrWhiteSpace(best) && !best.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                readings.Add(KnownUsb("Best measured port", best, "USB Builder benchmark/profile", now, "Best known write speed is based on ForgerEMS benchmark/profile data."));
            }

            if (diag.TryGetProperty("lastBenchmark", out var benchmark) &&
                benchmark.ValueKind == JsonValueKind.Object &&
                benchmark.TryGetProperty("succeeded", out var succeeded) &&
                succeeded.ValueKind == JsonValueKind.True)
            {
                readings.Add(KnownUsb("USB benchmark", GetJsonString(benchmark, "summaryLine"), "USB Builder benchmark", now, GetJsonString(benchmark, "benchmarkConfidence")));
            }
        }

        var topology = usb.TryGetProperty("topologyDiff", out var diff)
            ? GetJsonString(diff, "summaryLine")
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(topology) && !topology.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            readings.Add(KnownUsb("USB topology", topology, "USB Intelligence topology diff", now, "Topology status was available from the USB Intelligence report."));
        }

        return readings
            .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
    }

    private static SensorReading KnownUsb(string name, string value, string source, DateTimeOffset now, string note) => new()
    {
        Name = name,
        Category = "USB",
        Value = string.IsNullOrWhiteSpace(value) ? "Known" : value,
        Status = "Ready",
        Confidence = "Medium",
        Source = source,
        LastUpdatedUtc = now,
        TechnicianNote = note
    };

    private static string ClassifyGpuForSummary(string name, string type)
    {
        if (!string.IsNullOrWhiteSpace(type) && !type.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return $"{type}: {name}";
        }

        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Quadro", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("RTX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GTX", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase))
        {
            return $"Dedicated: {name}";
        }

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("UHD", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Iris", StringComparison.OrdinalIgnoreCase))
        {
            return $"Integrated: {name}";
        }

        return name;
    }

    private static string InferCpuTier(string cpu)
    {
        if (string.IsNullOrWhiteSpace(cpu))
        {
            return "CPU tier: unknown";
        }

        var u = cpu.ToUpperInvariant();
        if (u.Contains("RYZEN 9", StringComparison.Ordinal) || u.Contains("I9", StringComparison.Ordinal))
        {
            return "CPU tier: enthusiast / high";
        }

        if (u.Contains("RYZEN 7", StringComparison.Ordinal) || u.Contains("I7", StringComparison.Ordinal))
        {
            return "CPU tier: performance";
        }

        if (u.Contains("RYZEN 5", StringComparison.Ordinal) || u.Contains("I5", StringComparison.Ordinal))
        {
            return "CPU tier: mainstream";
        }

        if (u.Contains("RYZEN 3", StringComparison.Ordinal) || u.Contains("I3", StringComparison.Ordinal))
        {
            return "CPU tier: entry";
        }

        if (u.Contains("CELERON", StringComparison.Ordinal) || u.Contains("PENTIUM", StringComparison.Ordinal))
        {
            return "CPU tier: budget";
        }

        return "CPU tier: general desktop/mobile (manual review)";
    }

    private static string? PickFix(string issue)
    {
        if (issue.Contains("RAM", StringComparison.OrdinalIgnoreCase))
        {
            return "Plan a RAM upgrade to at least 16 GB if resale or heavy multitasking is the goal.";
        }

        if (issue.Contains("Storage", StringComparison.OrdinalIgnoreCase))
        {
            return "Back up data and test the drive with vendor tools; consider replacement if health is poor.";
        }

        if (issue.Contains("Battery", StringComparison.OrdinalIgnoreCase))
        {
            return "Calibrate battery reporting and plan replacement if wear is high.";
        }

        if (issue.Contains("TPM", StringComparison.OrdinalIgnoreCase) || issue.Contains("Secure Boot", StringComparison.OrdinalIgnoreCase))
        {
            return "Review firmware settings to enable TPM 2.0 and Secure Boot when Windows 11 readiness matters.";
        }

        if (issue.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("gateway", StringComparison.OrdinalIgnoreCase) ||
            issue.Contains("APIPA", StringComparison.OrdinalIgnoreCase))
        {
            return "Renew DHCP lease, verify router/cable, or disable conflicting virtual adapters.";
        }

        return "Review the System Intelligence recommendations list and rerun the scan after changes.";
    }

    private static string GetJsonString(JsonElement e, string name)
    {
        if (e.ValueKind == JsonValueKind.Undefined || !e.TryGetProperty(name, out var p))
        {
            return "unknown";
        }

        return p.ValueKind switch
        {
            JsonValueKind.String => p.GetString() ?? "unknown",
            JsonValueKind.Object when p.TryGetProperty("friendlyDisplayText", out var f) => f.GetString() ?? p.ToString(),
            _ => p.ToString()
        };
    }

    public sealed class ForgerNormalizedHardwareSummary
    {
        public string CpuTier { get; init; } = string.Empty;

        public string[] GpuClasses { get; init; } = Array.Empty<string>();

        public string BootVolume { get; init; } = string.Empty;

        public string RamConfiguredVsRated { get; init; } = string.Empty;

        public string NetworkAdapterSummary { get; init; } = string.Empty;

        public string SecuritySummary { get; init; } = string.Empty;
    }
}
