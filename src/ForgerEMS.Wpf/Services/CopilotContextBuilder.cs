#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// FORGEREMS_KYRA_ADAPTER: ForgerEMS-specific coupling; stays in ForgerEMS.KyraAdapter.
// Reads ForgerEMS runtime JSON paths (USB, toolkit, diagnostics); hard host coupling.
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

public sealed class CopilotContextBuilder : ICopilotContextBuilder
{
    private static readonly JsonSerializerOptions _profileLoadOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly PricingEngine _pricingEngine = new();

    public CopilotContext Build(CopilotRequest request)
    {
        var settings = request.Settings ?? new CopilotSettings();
        var intent = KyraIntentRouter.DetectIntent(request.Prompt);
        var promptMode = DetectPromptMode(request.Prompt, intent);
        if (intent == KyraIntent.CodeAssist)
        {
            var codePrompt = string.Join(Environment.NewLine + Environment.NewLine, new[]
            {
                "You are Kyra, the ForgerEMS assistant. This turn is isolated code repair only.",
                "Do not continue prior diagnostic, lag, USB, resale, battery, TPM, Secure Boot, or System Intelligence topics.",
                "Return the corrected code and a short explanation. Do not use Markdown fences unless the caller explicitly asks for Markdown.",
                $"User question: {CopilotRedactor.Redact(request.Prompt, settings.RedactContextEnabled)}"
            });

            return new CopilotContext
            {
                UserQuestion = request.Prompt,
                ContextText = codePrompt.Length <= 8000 ? codePrompt : codePrompt[..8000],
                PromptMode = promptMode,
                Intent = KyraIntent.CodeAssist,
                PreviousIntent = KyraIntent.Unknown,
                SystemContext = new SystemContext(),
                PersonalityProfile = settings.PersonalityProfile
            };
        }

        // Always load the latest saved scan when present so Kyra facts/ledger stay accurate even if
        // "share System Intelligence with online providers" is disabled (that flag only trims prompt text).
        var profile = LoadSystemProfile(request.SystemIntelligenceReportPath);
        var systemContext = SystemContext.FromProfile(profile);
        var health = SystemHealthEvaluator.Evaluate(profile);
        var recommendations = RecommendationEngine.Generate(profile, health);
        var pricingEstimate = _pricingEngine.Estimate(profile, health);
        var parts = new List<string>
        {
            PromptTemplates.GetSystemPrompt(promptMode),
            $"User question: {CopilotRedactor.Redact(request.Prompt, settings.RedactContextEnabled)}",
            $"App version: {CopilotRedactor.Redact(request.AppVersion, settings.RedactContextEnabled)}"
        };

        if ((settings.KyraPersistentMemoryEnabled || settings.KyraLocalRepairMemoryEnabled) &&
            !string.IsNullOrWhiteSpace(request.KyraMemorySummaryForPrompt))
        {
            parts.Add(KyraSystemContextSanitizer.SanitizeForExternalProviders(request.KyraMemorySummaryForPrompt.Trim()));
        }

        if (settings.UseLatestSystemScanContext && profile is not null)
        {
            parts.Add(BuildSystemSummary(request.SystemIntelligenceReportPath, profile, health, recommendations, pricingEstimate, settings.RedactContextEnabled));
            var insight = KyraSystemAnalyzer.Analyze(profile, health, recommendations, pricingEstimate);
            parts.Add(CopilotRedactor.Redact(insight.ToPromptBlock(), settings.RedactContextEnabled));
        }

        parts.Add(BuildUsbSummary(request.SelectedUsbTarget, settings.RedactContextEnabled));
        parts.Add(BuildToolkitSummary(request.ToolkitHealthReportPath, settings.RedactContextEnabled, request.SelectedUsbTarget));
        parts.Add(BuildMachineProfileSummary(request.MachineProfilesPath, request.SystemIntelligenceReportPath, settings.RedactContextEnabled));
        if (!string.IsNullOrWhiteSpace(request.KyraSafeCrossSystemSummary))
        {
            parts.Add(
                "Cross-system summary (sanitized):" + Environment.NewLine +
                CopilotRedactor.Redact(request.KyraSafeCrossSystemSummary.Trim(), settings.RedactContextEnabled));
        }

        parts.Add(BuildLogSummary(request.RecentLogLines, settings.RedactContextEnabled));

        var contextText = string.Join(Environment.NewLine + Environment.NewLine, parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        if (settings.MaxContextCharacters > 0 && contextText.Length > settings.MaxContextCharacters)
        {
            contextText = contextText[..settings.MaxContextCharacters] + Environment.NewLine + "[context trimmed]";
        }

        return new CopilotContext
        {
            UserQuestion = request.Prompt,
            ContextText = contextText,
            PromptMode = promptMode,
            Intent = intent,
            SystemContext = systemContext,
            SystemProfile = profile,
            HealthEvaluation = health,
            Recommendations = recommendations,
            PricingEstimate = pricingEstimate,
            PersonalityProfile = settings.PersonalityProfile
        };
    }

    private static CopilotPromptMode DetectPromptMode(string prompt, KyraIntent intent)
    {
        var text = prompt.ToLowerInvariant();
        if (intent == KyraIntent.CodeAssist)
        {
            return CopilotPromptMode.Technician;
        }

        if (intent is KyraIntent.LiveOnlineQuestion or KyraIntent.Weather or KyraIntent.News or KyraIntent.CryptoPrice
            or KyraIntent.StockPrice or KyraIntent.Sports)
        {
            return CopilotPromptMode.CurrentLiveData;
        }

        if (intent == KyraIntent.ResaleValue)
        {
            return CopilotPromptMode.FlipResale;
        }

        if (intent == KyraIntent.UpgradeAdvice)
        {
            return CopilotPromptMode.Technician;
        }

        if (text.Contains("usb") || text.Contains("toolkit") || text.Contains("iso") || text.Contains("ventoy"))
        {
            return CopilotPromptMode.ToolkitBuilder;
        }

        if (text.Contains("repair") || text.Contains("fix") || text.Contains("diagnose") || text.Contains("step"))
        {
            return CopilotPromptMode.Technician;
        }

        if (intent is KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot or KyraIntent.OSRecommendation ||
            text.Contains("not showing") ||
            text.Contains("missing") ||
            text.Contains("os"))
        {
            return CopilotPromptMode.Troubleshooting;
        }

        return CopilotPromptMode.General;
    }

    private static SystemProfile? LoadSystemProfile(string reportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            return SystemProfileMapper.FromJson(document.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static string BuildSystemSummary(
        string reportPath,
        SystemProfile? profile,
        SystemHealthEvaluation health,
        IReadOnlyList<string> recommendations,
        PricingEstimate? pricingEstimate,
        bool redact)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return "System Intelligence: not available. Ask the user to run System Scan for better local context.";
        }

        if (profile is null)
        {
            return "System Intelligence: report could not be parsed. Ask the user to rerun System Scan.";
        }

        var gpuLine = profile.Gpus.Count == 0
            ? "Unknown GPU"
            : string.Join("; ", profile.Gpus.Select(gpu => $"{gpu.Name} ({gpu.GpuKind}) driver {gpu.DriverVersion}").Take(4));
        var storageLine = profile.Disks.Count == 0
            ? "No disk health counters available"
            : string.Join("; ", profile.Disks.Select(disk => $"{disk.Name} {disk.MediaType} {disk.Size} health {disk.Health} status {disk.Status} wear {FormatNullable(disk.WearPercent, "%")} temp {FormatNullable(disk.TemperatureC, " C")}").Take(4));
        var batteryLine = profile.Batteries.Count == 0
            ? "No battery detected"
            : string.Join("; ", profile.Batteries.Select(battery => $"{battery.Name} wear {FormatNullable(battery.WearPercent, "%")} cycles {FormatNullable(battery.CycleCount)} AC {FormatNullableBool(battery.AcConnected)} status {battery.Status}").Take(3));
        var machineClass = MachineClassifier.Classify(profile);
        var sensorMatrix = SensorMatrixBuilder.Build(profile);
        var deviceFit = new DeviceFitEngine().Evaluate(profile);

        var lines = new List<string>
        {
            "System Intelligence summary:",
            $"Model: {profile.Manufacturer} {profile.Model}",
            $"OS: {profile.OperatingSystem} build {profile.OsBuild}",
            $"CPU: {profile.Cpu}; cores {FormatNullable(profile.CpuCores)}; threads {FormatNullable(profile.CpuThreads)}",
            $"RAM: {profile.RamTotal} @ {profile.RamSpeed}; free slots {FormatNullable(profile.RamSlotsFree)}; upgrade path {profile.RamUpgradePath}",
            $"GPU: {gpuLine}",
            $"Storage: {storageLine}",
            $"Battery: {batteryLine}",
            $"Security: TPM present {FormatNullableBool(profile.TpmPresent)}, TPM ready {FormatNullableBool(profile.TpmReady)}, Secure Boot {FormatNullableBool(profile.SecureBoot)}",
            $"Network: {profile.NetworkStatus}; APIPA adapters {profile.ApipaAdapterCount}; missing gateway adapters {profile.MissingGatewayAdapterCount}; internet check {profile.InternetCheck}",
            $"Machine class: {machineClass.PrimaryClass}; confidence {machineClass.Confidence}; secondary {string.Join("; ", machineClass.SecondaryClasses.Take(3))}; note {machineClass.TechnicianNote}",
            $"Sensor matrix: {sensorMatrix.CoverageSummary}; confidence {sensorMatrix.Confidence}; missing sensors are availability limits, not hardware failures",
            $"Best use/device fit: {deviceFit.PrimaryFit}; confidence {deviceFit.Confidence}; strong fits {string.Join("; ", deviceFit.StrongFits.Take(5))}; weak fits {string.Join("; ", deviceFit.WeakFits.Take(4))}; examples {string.Join("; ", deviceFit.ExampleWorkloads.Take(5))}; listing angle {deviceFit.ListingPositioning}",
            $"Overall status: {profile.OverallStatus}",
            $"Health score: {health.HealthScore}/100",
            $"Detected issues: {string.Join("; ", health.DetectedIssues.Take(8))}",
            $"Recommendations: {string.Join("; ", recommendations.Take(8))}",
            pricingEstimate is null
                ? "Pricing Engine v0: not available"
                : $"Pricing Engine v0: ${pricingEstimate.LowEstimate:0} - ${pricingEstimate.HighEstimate:0}; confidence {pricingEstimate.ConfidenceScore:0.##}; action {FormatResaleAction(pricingEstimate.RecommendedAction)}; provider {pricingEstimate.ProviderName}; local estimate only {pricingEstimate.IsLocalEstimateOnly}",
            pricingEstimate is null
                ? string.Empty
                : $"Pricing assumptions: {string.Join("; ", pricingEstimate.Assumptions.Take(8))}",
            $"Flip estimate: {profile.FlipValue.EstimatedResaleRange} ({profile.FlipValue.EstimateType}; {profile.FlipValue.ProviderStatus}; confidence {FormatNullable(profile.FlipValue.ConfidenceScore)})",
            $"Value drivers: {string.Join("; ", profile.FlipValue.ValueDrivers.Take(5))}",
            $"Value reducers: {string.Join("; ", profile.FlipValue.ValueReducers.Take(5))}",
            $"Problems: {string.Join("; ", profile.ObviousProblems.Take(8))}"
        };

        return CopilotRedactor.Redact(string.Join(Environment.NewLine, lines), redact);
    }

    private static string FormatNullable(double? value, string suffix = "")
    {
        return value.HasValue ? $"{value.Value:0.#}{suffix}" : "UNKNOWN";
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "UNKNOWN";
    }

    private static string FormatNullableBool(bool? value)
    {
        return value.HasValue ? value.Value.ToString() : "UNKNOWN";
    }

    private static string FormatResaleAction(ResaleAction action)
    {
        return action switch
        {
            ResaleAction.SellNow => "sell now",
            ResaleAction.PartsOnly => "parts only",
            _ => "upgrade first"
        };
    }

    private static string BuildUsbSummary(UsbTargetInfo? target, bool redact)
    {
        if (target is null)
        {
            return "USB target: none selected.";
        }

        return CopilotRedactor.Redact(
            $"USB target: {target.RootPath} {target.LabelDisplay}; {target.DisplayTotalBytes}; {target.FileSystem}; {target.SelectionStatusText}; benchmark {target.BenchmarkStatusDisplay}; write {target.WriteSpeedDisplayNormalized}; read {target.ReadSpeedDisplayNormalized}; warning {target.SelectionWarningDisplay}",
            redact);
    }

    private static string BuildToolkitSummary(string reportPath, bool redact, UsbTargetInfo? usbTarget)
    {
        if (string.IsNullOrWhiteSpace(reportPath) || !File.Exists(reportPath))
        {
            return "Toolkit health: no latest toolkit-health report found.";
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(reportPath));
            var root = document.RootElement;
            var toolkitTargetRoot = GetJsonString(root, "targetRoot", string.Empty);
            var lines = new List<string>
            {
                $"Toolkit health verdict: {GetJsonString(root, "healthVerdict", "Unknown")}"
            };
            var missing = 0;
            var failed = 0;
            var updates = 0;
            var pending = 0;

            if (root.TryGetProperty("summary", out var summary))
            {
                _ = int.TryParse(GetJsonString(summary, "missingRequired", GetJsonString(summary, "missing", "0")), out missing);
                _ = int.TryParse(GetJsonString(summary, "failed", "0"), out failed);
                _ = int.TryParse(GetJsonString(summary, "updates", "0"), out updates);
                _ = int.TryParse(GetJsonString(summary, "verificationPending", "0"), out pending);
                lines.Add($"Toolkit summary: installed {GetJsonString(summary, "installed", "0")}; missing {missing}; failed {failed}; manual {GetJsonString(summary, "manual", "0")}");
            }

            var items = new List<ToolkitHealthItemView>();
            if (root.TryGetProperty("items", out var itemsElement) && itemsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsElement.EnumerateArray())
                {
                    var status = GetJsonString(item, "status", "UNKNOWN");
                    var type = GetJsonString(item, "type", "UNKNOWN");
                    items.Add(new ToolkitHealthItemView
                    {
                        Status = status,
                        Type = type,
                        OfficialUrl = GetJsonString(item, "officialUrl", GetJsonString(item, "url", string.Empty)),
                        DownloadStatus = GetJsonString(item, "downloadStatus", status),
                        ChecksumStatus = GetJsonString(item, "checksumStatus", GetJsonString(item, "verification", string.Empty))
                    });
                }
            }

            var linkSummary = ToolkitLinkVerificationSummaryForReadiness.TryLoadAligned(
                reportPath,
                toolkitTargetRoot,
                usbTarget?.RootPath ?? string.Empty);

            var readiness = ToolkitReadinessScorer.Evaluate(
                items,
                selectedTarget: null,
                ventoyStatusText: string.Empty,
                toolkitReportAvailable: true,
                toolkitLogAvailable: true,
                missingRequiredCount: missing,
                verificationFailedCount: failed,
                updatesAvailableCount: updates,
                verificationPendingCount: pending,
                omitLiveUsbVentoyContext: true,
                linkVerification: linkSummary);
            lines.Add($"Toolkit readiness: {readiness.LabelText} ({readiness.Score}/100)");
            if (readiness.Blockers.Count > 0)
            {
                lines.Add("Toolkit blockers: " + string.Join("; ", readiness.Blockers));
            }

            if (linkSummary is { HasRun: true })
            {
                lines.Add(
                    $"Toolkit link verification: verified metadata {linkSummary.VerifiedMetadataCount}; reachable {linkSummary.ReachableCount}; warnings {linkSummary.WarningCount}; broken {linkSummary.BrokenCount}; offline/timeouts {linkSummary.UnknownOfflineCount} (HEAD/ranged GET metadata only).");
            }
            else
            {
                lines.Add(
                    "Toolkit link verification: not available for this pairing yet — refresh toolkit health, pick the matching USB target, then run Verify Links in Toolkit Manager.");
            }

            var checksumRows = items.Count > 0
                ? items.Count(row =>
                    !string.IsNullOrWhiteSpace(row.ChecksumStatus) &&
                    !row.ChecksumStatus.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                : 0;
            lines.Add(checksumRows > 0
                ? $"Toolkit checksum coverage (report columns): {checksumRows}/{items.Count} rows include checksum/status hints."
                : "Toolkit checksum coverage (report columns): limited — refresh toolkit health for checksum columns.");

            return CopilotRedactor.Redact(string.Join(Environment.NewLine, lines), redact);
        }
        catch (Exception exception)
        {
            return $"Toolkit health: report could not be parsed ({exception.Message}).";
        }
    }

    private static string BuildMachineProfileSummary(string profilesPath, string systemReportPath, bool redact)
    {
        if (string.IsNullOrWhiteSpace(profilesPath) || !File.Exists(profilesPath) || string.IsNullOrWhiteSpace(systemReportPath) || !File.Exists(systemReportPath))
        {
            return "Machine profile: no previous profile.";
        }

        try
        {
            using var reportDoc = JsonDocument.Parse(File.ReadAllText(systemReportPath));
            var summary = reportDoc.RootElement.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
            var manufacturer = summary.ValueKind == JsonValueKind.Object ? GetJsonString(summary, "manufacturer", "Unknown") : "Unknown";
            var model = summary.ValueKind == JsonValueKind.Object ? GetJsonString(summary, "model", "Unknown") : "Unknown";
            var machineLabel = summary.ValueKind == JsonValueKind.Object ? GetJsonString(summary, "computerName", Environment.MachineName) : Environment.MachineName;
            var os = summary.ValueKind == JsonValueKind.Object ? GetJsonString(summary, "os", Environment.OSVersion.VersionString) : Environment.OSVersion.VersionString;
            var hash = MachineProfileStore.ComputeMachineIdentityHash(machineLabel, manufacturer, model, os);
            var store = JsonSerializer.Deserialize<List<MachineProfileSnapshot>>(
                File.ReadAllText(profilesPath),
                _profileLoadOptions) ?? [];
            var entries = store
                .Where(item => item.MachineIdentityHash.Equals(hash, StringComparison.Ordinal))
                .OrderByDescending(item => item.LastScanUtc)
                .Take(2)
                .ToArray();
            if (entries.Length == 0)
            {
                return "Machine profile: no previous profile.";
            }

            var latest = entries[0];
            var lines = new List<string>
            {
                entries.Length > 1 ? "Machine profile: previous scan available." : "Machine profile: saved locally.",
                $"Profile last scan: {latest.LastScanUtc.LocalDateTime:g}",
                $"Profile last health: {latest.HealthScore}/100",
                latest.ToolkitReadinessScore.HasValue
                    ? $"Profile last toolkit readiness: {latest.ToolkitReadinessLabel} ({latest.ToolkitReadinessScore}/100)"
                    : $"Profile last toolkit readiness: {latest.ToolkitReadinessLabel}"
            };
            if (entries.Length > 1)
            {
                var previous = entries[1];
                var healthDelta = latest.HealthScore - previous.HealthScore;
                var toolkitSegment = latest.ToolkitReadinessScore.HasValue && previous.ToolkitReadinessScore.HasValue
                    ? $"toolkit {(latest.ToolkitReadinessScore.Value - previous.ToolkitReadinessScore.Value >= 0 ? "+" : string.Empty)}{latest.ToolkitReadinessScore.Value - previous.ToolkitReadinessScore.Value}"
                    : "toolkit delta not recorded on both snapshots";
                lines.Add(
                    $"Profile change since previous: health {(healthDelta >= 0 ? "+" : string.Empty)}{healthDelta}; {toolkitSegment}.");
            }

            return CopilotRedactor.Redact(string.Join(Environment.NewLine, lines), redact);
        }
        catch
        {
            return "Machine profile: unavailable (profile data parse error).";
        }
    }

    private static string BuildLogSummary(IReadOnlyList<string> logs, bool redact)
    {
        var safeLines = logs
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .TakeLast(12)
            .Select(line => CopilotRedactor.Redact(line, redact))
            .ToArray();

        return safeLines.Length == 0
            ? "Recent logs: none supplied."
            : "Recent safe log snippets:" + Environment.NewLine + string.Join(Environment.NewLine, safeLines);
    }

    private static string GetJsonString(JsonElement element, string propertyName, string fallback)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return fallback;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString() ?? fallback,
            JsonValueKind.Number => property.ToString(),
            JsonValueKind.True => "True",
            JsonValueKind.False => "False",
            _ => fallback
        };
    }
}
