#pragma warning disable CA1822 // DI-related code; instance methods called via interface references
// KYRA_CORE_CANDIDATE: rule engine framework is generic.
// FORGEREMS_KYRA_ADAPTER: individual rule content references ForgerEMS product concepts (USB builder, toolkit, scan modes).
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

public sealed class LocalRulesCopilotEngine
{
    public static string GenerateReply(string prompt, CopilotContext context)
    {
        var normalizedPrompt = prompt.Trim();
        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return "I can help with resale value, upgrades, lag, OS choice, USB toolkit picks, or warnings from the latest scan. Ask it like you would in a normal chat and I will keep it practical.";
        }

        if (KyraSafetyGuard.TryBuildRefusal(normalizedPrompt, out var refusal))
        {
            return refusal;
        }

        if (TryBuildCasualConversationalReply(normalizedPrompt, context, out var casualReply))
        {
            return casualReply;
        }

        if (TryBuildCalculatorAnswer(normalizedPrompt, out var calculatorAnswer))
        {
            return calculatorAnswer;
        }

        if (IsEmbeddedWslDiagnosticsStabilityQuestion(normalizedPrompt))
        {
            return BuildEmbeddedWslDiagnosticsStabilityAnswer();
        }

        if (IsKyraBetaOperatorApiKeyQuestion(normalizedPrompt))
        {
            return BuildBetaOperatorApiConfigurationAnswer();
        }

        if (KyraFollowUpClassifier.LooksLikeRepairContinuation(
                normalizedPrompt,
                context.PreviousIntent,
                context.ConversationMeta?.LastKyraResponseListedIssues == true) &&
            context.SystemProfile is not null)
        {
            return BuildDiagnosticRepairFollowUpAnswer(context);
        }

        if (ContainsAny(normalizedPrompt, "what did you just say", "repeat that", "summarize that"))
        {
            return BuildMemoryRecallAnswer(context);
        }

        if (ContainsAny(normalizedPrompt, "explain that simpler", "simpler", "plain english"))
        {
            return BuildSimplerAnswer(context);
        }

        if (ContainsAny(normalizedPrompt, "give me the commands", "commands", "powershell"))
        {
            return BuildSafeCommandsAnswer(context);
        }

        if (TechnicianWorkflowPresetCatalog.TryBuildKyraWorkflowAnswer(normalizedPrompt, out var workflowAnswer))
        {
            return workflowAnswer;
        }

        return context.Intent switch
        {
            KyraIntent.PerformanceLag => BuildTroubleshootingAnswer(normalizedPrompt, context),
            KyraIntent.AppFreezing => BuildAppFreezingAnswer(normalizedPrompt, context),
            KyraIntent.SlowBoot => BuildSlowBootAnswer(context),
            KyraIntent.UpgradeAdvice => BuildUpgradeAnswer(context),
            KyraIntent.ResaleValue => BuildValueAnswer(normalizedPrompt, context),
            KyraIntent.USBBuilderHelp => BuildUsbBuilderAnswer(context),
            KyraIntent.ToolkitManagerHelp => BuildToolkitManagerAnswer(context),
            KyraIntent.SystemHealthSummary => BuildSystemAnswer(context),
            KyraIntent.DriverIssue => BuildDriverAnswer(context),
            KyraIntent.StorageIssue => BuildStorageAnswer(context),
            KyraIntent.MemoryIssue => BuildMemoryAnswer(context),
            KyraIntent.GPUQuestion => BuildGpuAnswer(context),
            KyraIntent.OSRecommendation => BuildOsAnswer(context),
            KyraIntent.ForgerEMSQuestion => BuildForgerEmsAnswer(context),
            KyraIntent.LiveOnlineQuestion => BuildLiveDataAnswer(context),
            KyraIntent.Weather or KyraIntent.News or KyraIntent.CryptoPrice or KyraIntent.StockPrice or KyraIntent.Sports => BuildLiveDataAnswer(context),
            KyraIntent.CodeAssist => BuildCodeAssistAnswer(normalizedPrompt, context),
            _ => context.PromptMode switch
            {
                CopilotPromptMode.CurrentLiveData => BuildLiveDataAnswer(context),
                CopilotPromptMode.FlipResale => BuildValueAnswer(normalizedPrompt, context),
                CopilotPromptMode.ToolkitBuilder => BuildToolkitAnswer(context.ContextText),
                CopilotPromptMode.Technician => BuildTechnicianAnswer(context.ContextText),
                CopilotPromptMode.Troubleshooting => BuildTroubleshootingAnswer(normalizedPrompt, context),
                _ => BuildGeneralAnswer(context)
            }
        };
    }

    private static string BuildLiveDataAnswer(CopilotContext context)
    {
        if (context.Intent == KyraIntent.Weather)
        {
            return $"""
                Direct answer:
                {KyraLiveToolRouter.LiveToolsUnavailableMessage}

                What you can do:
                1. Check https://www.weather.gov/ (National Weather Service) or your favorite weather site in a browser for current conditions.
                2. If your operator enabled Open-Meteo or OpenWeather in Kyra Advanced live tools, use a /weather command from the Kyra slash menu after setting a default location.

                If you use an online language model here, it still does not automatically gain real radar or live forecasts—only configured tools do.
                """;
        }

        return $"""
            Direct answer:
            {KyraLiveToolRouter.LiveToolsUnavailableMessage}

            What I can still do:
            Help with this PC, USB builds, diagnostics, resale prep, and OS recommendations using local scan context.

            Next step:
            If you need verified live figures, enable the relevant live tool in Kyra Advanced (operator configuration) or check a trusted website directly.
            """;
    }

    private static bool TryBuildCasualConversationalReply(string prompt, CopilotContext context, out string answer)
    {
        answer = string.Empty;
        var l = prompt.ToLowerInvariant();
        if (l.Contains("battery", StringComparison.Ordinal) ||
            l.Contains("nvme", StringComparison.Ordinal) ||
            l.Contains("sata", StringComparison.Ordinal) ||
            l.Contains("ddr", StringComparison.Ordinal) ||
            l.Contains("upgrade", StringComparison.Ordinal) ||
            (l.Contains("ram", StringComparison.Ordinal) && l.Contains("gb", StringComparison.Ordinal)))
        {
            return false;
        }

        var playful = KyraPersonalityTone.UsePlayfulWording(context.PersonalityProfile, prompt);

        if (l.Contains("normal conversation", StringComparison.Ordinal) ||
            (l.Contains("just chat", StringComparison.Ordinal) && l.Length < 80) ||
            (l.Contains("small talk", StringComparison.Ordinal) && l.Length < 80) ||
            (l.Contains("just talk", StringComparison.Ordinal) && l.Length < 80))
        {
            answer = KyraPersonalityTone.NormalConversationRelaxLine(playful);
            return true;
        }

        if ((l.Contains("how are you", StringComparison.Ordinal) ||
             l.Contains("how's it going", StringComparison.Ordinal) ||
             l.Contains("hows it going", StringComparison.Ordinal) ||
             l.Contains("what's up", StringComparison.Ordinal) ||
             l.Contains("whats up", StringComparison.Ordinal) ||
             l.Contains("good morning", StringComparison.Ordinal) ||
             l.Contains("good evening", StringComparison.Ordinal)) &&
            l.Length < 96)
        {
            answer = playful
                ? "Doing good 😄 Powered on, caffeinated, and ready — we can chat, or dive into this machine whenever you want."
                : "Doing well — here when you need tech help or a quick chat.";
            return true;
        }

        var trimmed = prompt.Trim();
        if ((Regex.IsMatch(trimmed, @"^(hi|hey|yo|sup)\s*!?\s*$", RegexOptions.IgnoreCase) ||
             Regex.IsMatch(trimmed, @"^hello\s*!?\s*$", RegexOptions.IgnoreCase) ||
             Regex.IsMatch(trimmed, @"^(hi|hey)\s+there\s*!?\s*$", RegexOptions.IgnoreCase)) &&
            !l.Contains("slow", StringComparison.Ordinal) &&
            !l.Contains("fix", StringComparison.Ordinal))
        {
            answer = KyraPersonalityTone.CasualGreetingLine(playful);
            return true;
        }

        if (l.Contains("frustrated", StringComparison.Ordinal) ||
            l.Contains("ridiculous", StringComparison.Ordinal) ||
            l.Contains("this sucks", StringComparison.Ordinal) ||
            l.Contains("so annoyed", StringComparison.Ordinal))
        {
            answer = KyraPersonalityTone.FrustrationAckLine(playful);
            return true;
        }

        if (l.Contains("be serious", StringComparison.Ordinal) ||
            l.Contains("less cute", StringComparison.Ordinal) ||
            l.Contains("normal mode", StringComparison.Ordinal))
        {
            answer = playful
                ? "Roger — I’ll dial the sparkle down. Tell me what you need next (chat or tech)."
                : "Switching to a more neutral tone. What do you want to tackle next?";
            return true;
        }

        if (l.Contains("be cute", StringComparison.Ordinal) ||
            l.Contains("kyra mode", StringComparison.Ordinal) ||
            l.Contains("silly mode", StringComparison.Ordinal))
        {
            answer = "Okay okay — bubbly CyberViking mode engaged (still accurate, still safe). What’s up?";
            return true;
        }

        return false;
    }

    private static bool TryBuildCalculatorAnswer(string prompt, out string answer)
    {
        answer = string.Empty;
        return KyraSimpleMathEvaluator.TryEvaluate(prompt, out answer, out _);
    }

    private static bool IsKyraBetaOperatorApiKeyQuestion(string prompt)
    {
        var t = prompt.ToLowerInvariant();
        if (!t.Contains("api key", StringComparison.OrdinalIgnoreCase) &&
            !t.Contains("apikey", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ContainsAny(
            t,
            "how do i add",
            "where do i add",
            "how to add",
            "enter my api",
            "paste my key",
            "configure my key",
            "set up my api",
            "add my key",
            "add an api key");
    }

    private static string BuildBetaOperatorApiConfigurationAnswer()
    {
        return """
            Direct answer:
            During beta, ForgerEMS expects online models to be configured by the developer or operator (environment variables or shipped configuration). You do not need to paste your own API key to use Kyra.

            Offline Kyra:
            Works with no keys—local rules and your System Intelligence scan context stay on this PC.

            Online Kyra:
            Only activates when a provider is already configured for this install. If you are a tester, ask your operator what is enabled; do not send API keys in screenshots, logs, or support email.

            Optional:
            Advanced Kyra documentation for operators: docs/KYRA_PROVIDER_ENVIRONMENT_SETUP.md
            """;
    }

    private static string BuildDiagnosticRepairFollowUpAnswer(CopilotContext context)
    {
        var profile = context.SystemProfile!;
        var tpmLine = profile.TpmReady == true
            ? "TPM looks ready in the scan—if Windows still complains, re-run System Intelligence after a BIOS update."
            : "TPM / security readiness: open Windows Security → Device security → Processor/TPM/Secure Boot status. If TPM is off or not ready, reboot into firmware (BIOS/UEFI) and enable TPM/PTT/fTPM, then save and exit.";

        var apipa = profile.ApipaAdapterCount > 0
            ? $"Virtual / APIPA-style adapters detected ({profile.ApipaAdapterCount}). If a VirtualBox Host-Only adapter shows APIPA (169.254.x.x), it is often harmless when your real Wi‑Fi/Ethernet has a normal DHCP address. If the web still works, you can ignore it or disable unused virtual adapters. If the web fails, focus on the physical adapter: renew DHCP, confirm gateway/DNS, and ignore virtual NICs unless they are default routes."
            : "Networking: if you still see odd adapters, check ipconfig /all and compare the active internet adapter vs virtual adapters.";

        var storage = string.Join("; ",
            profile.Disks.Select(disk => $"{disk.MediaType} health {disk.Health} status {disk.Status}").Take(3));
        if (string.IsNullOrWhiteSpace(storage))
        {
            storage = "storage health unknown from the scan";
        }

        var net = profile.MissingGatewayAdapterCount > 0
            ? $"Some adapters are missing a default gateway ({profile.MissingGatewayAdapterCount}). Release/renew on the physical adapter, confirm router DHCP, and verify DNS."
            : "Gateway looks present on scanned adapters—if browsing fails, still test the physical NIC first and compare against virtual adapters.";

        var recap = context.ConversationHistory.LastOrDefault(m => m.Role.Equals("Kyra", StringComparison.OrdinalIgnoreCase))?.Text.Trim();
        var recapLine = string.IsNullOrWhiteSpace(recap)
            ? string.Empty
            : $"Recap I am continuing from: {recap.ReplaceLineEndings(" ")}";

        return $"""
            Direct answer:
            Here is a practical repair pass for the issues we were discussing on this machine.

            TPM / Secure Boot:
            {tpmLine}

            Virtual adapters / APIPA:
            {apipa}

            Storage:
            {storage}
            Steps: run System Intelligence storage checks again after changes, use vendor/CrystalDisk-style SMART tools if health is unknown, and back up important data before any repair or wipe.

            Network / DHCP:
            {net}
            Commands (read-only): ipconfig /all, then ipconfig /release and /renew on the active adapter if DHCP looks stuck.

            {recapLine}
            """;
    }

    private static string BuildCodeAssistAnswer(string prompt, CopilotContext context)
    {
        if (TryBuildTinyCSharpAddFix(prompt, out var fixedAnswer))
        {
            return fixedAnswer;
        }

        var hint = KyraCodeSnippetDetector.GuessLanguageHint(prompt);
        return $"""
            I can help with that {hint} snippet. I’m not executing anything here; this is read-only guidance.

            What often goes wrong:
            Unbalanced braces/parentheses/brackets, missing semicolons where the language requires them, bad string escaping, JSON trailing commas, YAML indentation, or XAML namespace typos.

            Fixed snippet:
            I need a smaller pasted snippet or an online provider to safely rewrite the whole thing. Paste the exact compiler/runtime error and the smallest block that reproduces it, and I’ll return a corrected version with the key change called out.
            """;
    }

    private static bool TryBuildTinyCSharpAddFix(string prompt, out string answer)
    {
        answer = string.Empty;
        var normalized = prompt.ReplaceLineEndings("\n");
        if (!normalized.Contains("public int Add", StringComparison.OrdinalIgnoreCase) ||
            !normalized.Contains("return a - b;", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        answer = """
            The bug is that the method subtracts instead of adding.

            public int Add(int a, int b)
            {
                return a + b;
            }
            """;
        return true;
    }

    private static string BuildMachineSpecificScanRequiredResponse(SystemContext systemContext)
    {
        return $"""
            Short answer:
            I need a System Intelligence scan before I can give machine-specific advice.

            What I can see without a scan:
            {DescribeSystemContext(systemContext)}

            What to do next:
            Run System Intelligence from this app, then ask again.
            """;
    }

    private static string BuildValueAnswer(string prompt, CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var health = context.HealthEvaluation?.HealthScore ?? 0;
        var probe = new WindowsHardwareReader().Read(profile);
        var resaleProfile = new DeviceResaleProfile
        {
            Identity = probe.Identity,
            RawSystemProfile = profile,
            Condition = new ResaleConditionProfile
            {
                CosmeticCondition = "Unknown (ask seller/operator)",
                ScreenCondition = "Unknown (ask seller/operator)",
                KeyboardTrackpadCondition = "Unknown (ask seller/operator)",
                HingeCondition = "Unknown (ask seller/operator)",
                ChargerIncluded = true,
                BatteryHoldsCharge = true,
                WindowsActivated = true,
                FreshInstallCompleted = false,
                CleanedOrRepasted = false,
                MissingScrewsOrDamage = false
            }
        };
        var estimator = new OfflineResaleEstimator();
        var listingEstimate = estimator.Estimate(resaleProfile);
        var listingDraft = estimator.GenerateListingDraft(resaleProfile, listingEstimate);
        var wantsListing = prompt.Contains("listing", StringComparison.OrdinalIgnoreCase) || prompt.Contains("make me", StringComparison.OrdinalIgnoreCase);
        var asksEbay = prompt.Contains("ebay", StringComparison.OrdinalIgnoreCase) || prompt.Contains("comps", StringComparison.OrdinalIgnoreCase);
        var asksOfferUp = prompt.Contains("offerup", StringComparison.OrdinalIgnoreCase);
        var asksFacebook = prompt.Contains("facebook", StringComparison.OrdinalIgnoreCase) || prompt.Contains("marketplace", StringComparison.OrdinalIgnoreCase);
        var salePosture = health < 55
            ? "repair-first or parts/repair until the scan issues are fixed"
            : "worth preparing for resale if condition/photos/charger/activation check out";
        var pricing = context.PricingEstimate;
        var conversationalEstimate = EstimateDeviceValue(context.SystemContext);
        if (pricing is not null)
        {
            var pricedAnswer = $"""
                Short answer:
                This looks like a {FormatResaleAction(pricing.RecommendedAction)} situation. Pricing Engine v0 says ${pricing.LowEstimate:0} - ${pricing.HighEstimate:0}, local estimate only.

                What I found:
                {conversationalEstimate}
                Health score: {health}/100.
                Confidence: {pricing.ConfidenceScore:0.##}.
                No marketplace comps, scraping, or API prices were used.

                What to do next:
                {FormatNumbered(context.Recommendations.Take(5), "Clean it, update it, verify drivers, and photograph the condition.")}

                Technical details:
                {JoinOrFallback(pricing.Assumptions.Take(5), "Local hardware facts only.")}
                Confidence detail: {listingEstimate.ConfidenceReason}
                {GetMarketplaceStatusLine(asksEbay, asksOfferUp, asksFacebook)}
                """;
            if (!wantsListing)
            {
                return pricedAnswer;
            }

            return pricedAnswer + Environment.NewLine + $"""
                
                Listing draft:
                Title: {listingDraft.Title}
                Short description: {listingDraft.ShortDescription}
                Recommended list: ${listingEstimate.FairListingPrice:0}; quick-sale: ${listingEstimate.QuickSalePrice:0}; min acceptable: ${listingEstimate.MinimumAcceptablePrice:0}.
                Photo checklist: {string.Join("; ", listingDraft.PhotoChecklist)}
                """;
        }

        var baseAnswer = $"""
            Short answer:
            This is {salePosture}. Local estimate only: {profile.FlipValue.EstimatedResaleRange}.

            What I found:
            {conversationalEstimate}
            Recommended list: {profile.FlipValue.RecommendedListPrice}.
            Quick-sale: {profile.FlipValue.QuickSalePrice}.
            Parts/repair floor: {profile.FlipValue.PartsRepairPrice}.
            Confidence: {FormatConfidence(profile.FlipValue.ConfidenceScore)}.

            What to do next:
            {FormatNumbered(context.Recommendations.Take(5), "Clean it, update it, verify drivers, and photograph condition.")}

            Technical details:
            Pricing provider status: {profile.FlipValue.ProviderStatus}.
            Value reducers: {JoinOrFallback(profile.FlipValue.ValueReducers, "nothing obvious from the local scan")}.
            """;
        if (!wantsListing)
        {
            return baseAnswer + Environment.NewLine + GetMarketplaceStatusLine(asksEbay, asksOfferUp, asksFacebook);
        }

        return baseAnswer + Environment.NewLine + $"""
            
            Listing draft:
            Title: {listingDraft.Title}
            Short description: {listingDraft.ShortDescription}
            Recommended list: ${listingEstimate.FairListingPrice:0}; quick-sale: ${listingEstimate.QuickSalePrice:0}; min acceptable: ${listingEstimate.MinimumAcceptablePrice:0}.
            Photo checklist: {string.Join("; ", listingDraft.PhotoChecklist)}
            """;
    }

    private static string GetMarketplaceStatusLine(bool asksEbay, bool asksOfferUp, bool asksFacebook)
    {
        if (asksOfferUp || asksFacebook)
        {
            return "Marketplace status: OfferUp/Facebook are manual/future sources only in this beta. I can estimate offline or use manual comparables.";
        }

        if (asksEbay)
        {
            return "eBay comps status: Active eBay comps can be used only when official API config is present. Sold comps are not configured in this beta.";
        }

        return "Marketplace status: Offline estimate only by default. Facebook/OfferUp are manual/future sources only in this beta.";
    }

    private static string BuildUpgradeAnswer(CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Short answer:
            I’d fix buyer-confidence issues first, then upgrade only where it changes the feel or resale value.

            What I found:
            Device: {profile.Manufacturer} {profile.Model}.
            Health score: {context.HealthEvaluation?.HealthScore ?? 0}/100.
            RAM: {profile.RamTotal}; upgrade path: {profile.RamUpgradePath}.
            Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.Name} {disk.MediaType}, health {disk.Health}"), "storage health unknown")}.
            Battery: {JoinOrFallback(profile.Batteries.Select(battery => $"wear {FormatNullable(battery.WearPercent, "%")}, cycles {FormatNullable(battery.CycleCount)}"), "no battery detected")}.

            What to do next:
            {FormatNumbered(context.Recommendations.Take(6), "No urgent hardware upgrade found locally. Clean it, update it, and verify drivers before listing.")}

            Technical details:
            If this is for resale, don’t overspend. Prioritize required-for-sale fixes first, low-cost confidence upgrades second, and optional upgrades last.
            """;
    }

    private static string BuildSystemAnswer(CopilotContext context)
    {
        var profile = context.SystemProfile;
        if (profile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var q = context.UserQuestion;
        if (ContainsAny(q, "scanned this machine before", "have we scanned", "previous scan"))
        {
            var profileLine = FindLine(context.ContextText, "Machine profile:");
            var saved = FindLine(context.ContextText, "Profile last scan:");
            return string.IsNullOrWhiteSpace(profileLine)
                ? "I do not see a previous local machine profile yet. Run System Intelligence again after profile save to compare scans."
                : $"{profileLine} {saved}".Trim();
        }

        if (ContainsAny(q, "what changed since last scan", "changed since last scan", "since last scan"))
        {
            var delta = FindLine(context.ContextText, "Profile change since previous:");
            return string.IsNullOrWhiteSpace(delta)
                ? "I do not have enough previous local profile data to compare this scan yet."
                : delta;
        }

        return $"""
            Short answer:
            This machine looks like a {profile.Manufacturer} {profile.Model} with a local health score of {context.HealthEvaluation?.HealthScore ?? 0}/100.

            What I found:
            CPU: {profile.Cpu}.
            RAM: {profile.RamTotal}.
            GPU: {JoinOrFallback(profile.Gpus.Select(gpu => gpu.Name), "GPU unknown")}.
            Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.MediaType} health {disk.Health}"), "storage health unknown")}.

            What to do next:
            {FormatNumbered(context.Recommendations.Take(5), "Review any warnings, update drivers, and rerun the scan after fixes.")}
            """;
    }

    private static string BuildAppFreezingAnswer(string prompt, CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var appName = ResolveAppName(prompt);
        return $"""
            Quick read:
            Yeah, app-specific lag usually points to GPU acceleration, app cache, network, or storage/RAM pressure before I blame the whole computer.

            What I'm seeing:
            {DescribeSystemContext(context.SystemContext)}
            {SummarizeHealth(context)}

            What to try first:
            1. Restart {appName}, then test the same video/app again.
            2. Update the app and GPU driver.
            3. Turn hardware acceleration off/on for the app or browser and retest.
            4. Check Task Manager while it lags: CPU, memory, disk, and GPU video decode.
            5. If it is streaming, test another network or browser to separate app lag from internet lag.

            Next step:
            If you tell me whether it only happens in {appName} or everywhere, I can narrow it down fast.
            """;
    }

    private static string BuildSlowBootAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            Slow boot is usually startup apps, Windows update cleanup, storage health, or a driver/service hanging during login.

            What I'm seeing:
            {DescribeSystemContext(context.SystemContext)}
            {SummarizeHealth(context)}

            What to try first:
            1. Open Task Manager > Startup apps and disable anything nonessential.
            2. Check storage health before chasing Windows tweaks.
            3. Run Windows Update once, reboot twice, then retest boot time.
            4. If boot is still slow, check Event Viewer > Diagnostics-Performance > Operational.
            """;
    }

    private static string BuildUsbBuilderAnswer(CopilotContext context)
    {
        if (context.UserQuestion.Contains("No likely USB targets were detected", StringComparison.OrdinalIgnoreCase))
        {
            return """
                Short answer:
                ForgerEMS did not find a safe removable USB target to show in USB Builder.

                What that usually means:
                No flash drive is plugged in, Windows has not mounted it yet, the drive has no usable data partition or drive letter, or the only detected drives look like internal/system disks that ForgerEMS intentionally blocks.

                What to try:
                1. Plug in the USB stick and wait a few seconds.
                2. Click refresh targets in USB Builder.
                3. Check Disk Management for a missing drive letter or uninitialized removable disk.
                4. Use a normal removable USB data partition, not C:, not an internal disk, and not the tiny EFI/VTOYEFI boot slice.
                """;
        }

        var localUsb = KyraUsbAnswerBuilder.TryBuildAnswer(context.UserQuestion);
        var baseline = $"""
            Short answer:
            Pick the large removable data partition in USB Builder — not the tiny EFI/VTOYEFI boot slice.

            Likely cause:
            Windows often shows multiple volumes per stick; the small partition is boot metadata only.

            Next steps:
            1. Unplug/replug, wait for mount, refresh targets.
            2. Choose the largest safe removable data volume.
            3. Avoid system disks and the VTOYEFI/EFI partition.
            4. If the drive is missing, open Disk Management and look for a missing drive letter or an uninitialized disk.
            5. Run USB Benchmark when you want measured speeds for Kyra/USB Intelligence.

            What I'm seeing locally:
            {FindLine(context.ContextText, "USB target:")}

            Ventoy note:
            Toolkit files belong on the big data partition; EFI/VTOYEFI is boot-only.
            """;

        if (string.IsNullOrWhiteSpace(localUsb))
        {
            return baseline;
        }

        return $"""
            {localUsb.Trim()}

            —

            {baseline.Trim()}
            """;
    }

    private static string BuildToolkitManagerAnswer(CopilotContext context)
    {
        var readiness = FindLine(context.ContextText, "Toolkit readiness:");
        var blockers = FindLine(context.ContextText, "Toolkit blockers:");
        var verifyLine = FindLine(context.ContextText, "Toolkit link verification:");
        var checksumLine = FindLine(context.ContextText, "Toolkit checksum coverage");

        var q = context.UserQuestion;
        if (ContainsAny(q, "toolkit links", "links good", "links bad", "broken link", "official url", "link verification"))
        {
            if (string.IsNullOrWhiteSpace(verifyLine) || verifyLine.Contains("not available", StringComparison.OrdinalIgnoreCase))
            {
                return """
                    Short answer:
                    Toolkit link verification has not been captured for the current toolkit-health + USB pairing yet.

                    Next steps:
                    1. Refresh toolkit health for your USB target.
                    2. Run Verify Links in Toolkit Manager (safe HTTP metadata HEAD/ranged GET — downloads/archives are not executed).

                    What I'm seeing now:
                    """ + Environment.NewLine + readiness + Environment.NewLine + blockers;
            }

            return $"""
                Short answer:
                {verifyLine}

                Checksum guidance:
                {(string.IsNullOrWhiteSpace(checksumLine) ? "Use toolkit checksum columns plus Refresh Health — HTTP metadata cannot prove payload hashes." : checksumLine)}

                Caveat:
                Metadata probes confirm hosts responded; they do not execute installers or replace toolkit checksum verification.

                Related readiness context:
                {readiness}
                {blockers}
                """;
        }

        if (ContainsAny(q, "checksum coverage", "checksum hints", "hash coverage", "have checksum"))
        {
            return string.IsNullOrWhiteSpace(checksumLine)
                ? """
                    Short answer:
                    Refresh toolkit health first so checksum columns populate, then compare against vendor-published hashes.

                    Note:
                    Kyra cannot infer hashes from HTTP metadata alone—Verify Links only checks transport-level signals.
                    """
                : $"""
                    Short answer:
                    {checksumLine}

                    Note:
                    Verified checksums still require the toolkit health scan + managed verification—not link metadata alone.
                    """;
        }

        if (ContainsAny(context.UserQuestion, "last toolkit readiness", "what was my last toolkit readiness"))
        {
            var previousReadiness = FindLine(context.ContextText, "Profile last toolkit readiness:");
            return string.IsNullOrWhiteSpace(previousReadiness)
                ? "I do not have a saved previous toolkit readiness for this machine yet."
                : previousReadiness;
        }

        return $"""
            Short answer:
            Toolkit readiness combines missing tools, checksum health, managed download freshness, USB safety context, Ventoy readiness, and report/log coverage.

            Likely cause:
            Managed Missing usually means a download/verify/path issue. Manual Required means licensing, vendor gating, or verification limits block auto-download.

            Next steps:
            1. Read the Status column: Managed ready, Managed missing, Manual required, or Verification issues.
            2. For Manual Required, use vendor links or your own media, place files where the manifest expects, then Refresh Health.
            3. For Managed Missing, retry downloads with the intended USB selected; checksum mismatch or moved files are common causes.
            4. Re-run Toolkit Manager health after changes.
            5. Check logs: `%LOCALAPPDATA%\ForgerEMS\Runtime\logs` and `%LOCALAPPDATA%\ForgerEMS\Runtime\reports`.

            What I'm seeing:
            {FindLine(context.ContextText, "Toolkit health")}
            {readiness}
            {blockers}
            """;
    }

    private static string BuildDriverAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            Driver problems usually show up as lag, missing devices, bad GPU switching, weak Wi-Fi, or sleep/display weirdness.

            What I'm seeing:
            GPU: {context.SystemContext.GPU}.
            OS: {context.SystemContext.OS}.

            What to try first:
            1. Install chipset/platform drivers first.
            2. Then install GPU, network, audio, and storage drivers.
            3. Prefer the manufacturer support page for laptops and OEM workstations.
            4. If Hybrid/Online Kyra is configured later, I can help look up the exact support page without sending serial numbers.
            """;
    }

    private static string BuildStorageAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            Storage is one of the first things I check because a weak SSD/HDD can make a good machine feel awful.

            What I'm seeing:
            {context.SystemContext.Storage}

            What to try first:
            1. Confirm SMART/health status.
            2. Check free space.
            3. Watch Disk usage in Task Manager during lag.
            4. If health or wear is unknown, verify with a vendor tool before selling.
            """;
    }

    private static string BuildMemoryAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var ram = context.SystemContext.RAM > 0 ? $"{context.SystemContext.RAM} GB" : "unknown";
        return $"""
            Quick read:
            RAM matters most when apps pile up, browsers are heavy, or Windows starts paging to disk.

            What I'm seeing:
            Installed RAM: {ram}.

            What to try first:
            1. If this is under 16 GB, upgrade RAM before judging the whole laptop.
            2. If it is already 16 GB, check actual memory pressure in Task Manager.
            3. If it is 32 GB or more, RAM probably is not the first bottleneck unless a specific app is leaking memory.
            """;
    }

    private static string BuildGpuAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        return $"""
            Quick read:
            The GPU matters for video playback, external displays, CAD/light gaming, and apps that need acceleration. For normal resale, it helps, but battery/storage/RAM condition still matters too.

            What I'm seeing:
            GPU: {context.SystemContext.GPU}.
            Dedicated GPU detected: {(context.SystemContext.HasDedicatedGpu ? "yes" : "not obvious from the local scan")}.

            What to try first:
            1. Make sure the GPU driver is installed cleanly.
            2. For app lag, test hardware acceleration on and off.
            3. For NVIDIA/AMD systems, check Windows Graphics settings and the vendor control panel.
            """;
    }

    private static string BuildOsAnswer(CopilotContext context)
    {
        if (context.SystemProfile is null)
        {
            return BuildMachineSpecificScanRequiredResponse(context.SystemContext);
        }

        var ram = context.SystemContext.RAM;
        var recommendation = ram is > 0 and < 8
            ? "Linux Mint XFCE or Xubuntu will probably feel better than Windows 11 on low RAM."
            : "Windows 11 Pro is usually best for resale/business use if TPM, Secure Boot, drivers, and activation are clean.";
        var device = context.SystemContext.Device.Trim();
        var deviceLead = string.IsNullOrWhiteSpace(device) || device.Equals("Unknown device", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : $"Based on this {device}: ";

        return $"""
            Quick read:
            {deviceLead}{recommendation}

            What I'm seeing:
            {DescribeSystemContext(context.SystemContext)}

            What I would choose:
            1. Resale/business: Windows 11 Pro if supported and activated.
            2. Older/low-spec daily use: Linux Mint XFCE or Xubuntu.
            3. Diagnostics/recovery: ForgerEMS USB toolkit with Windows and Linux rescue tools.

            One note:
            I would not sell an unsupported OS install as the primary setup.
            """;
    }

    private static string BuildKyraWindowsEnvConfigurationAnswer()
    {
        return """
            ForgerEMS on Windows reads Kyra gateway settings from **User** or **Machine** environment variables (operator-managed). Use **User** scope unless IT requires machine-wide values.

            PowerShell (User scope) examples — replace placeholder URLs with your operator gateway endpoint; do not use real secrets in screenshots or chat:

            [Environment]::SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_URL", "https://your-gateway.example/v1/", "User")
            [Environment]::SetEnvironmentVariable("FORGEREMS_KYRA_GATEWAY_ENABLED", "true", "User")
            [Environment]::SetEnvironmentVariable("FORGEREMS_KYRA_RESEARCH_ENABLED", "true", "User")

            After changing variables, **fully restart ForgerEMS** so the process picks them up.

            Safety: **Do not paste API keys, tokens, or beta credentials into Kyra chat, logs, or support email.** Set secrets only via environment variables or your operator’s secure channel.

            Linux/macOS shells are only relevant if you are configuring a non-Windows build; this desktop app is Windows-first.
            """;
    }

    private static string BuildForgerEmsAnswer(CopilotContext context)
    {
        var q = context.UserQuestion.Trim().ToLowerInvariant();
        if (KyraPromptIsolation.LooksLikeKyraWindowsEnvConfigurationQuestion(context.UserQuestion))
        {
            return BuildKyraWindowsEnvConfigurationAnswer();
        }

        if (IsNewestForgerEmsReleaseQuestion(q))
        {
            var appLine = FindContextLine(context.ContextText, "App version:");
            if (!string.IsNullOrWhiteSpace(appLine) &&
                !appLine.Contains("unknown", StringComparison.OrdinalIgnoreCase) &&
                appLine.Length < 400)
            {
                return $"""
                    Short answer:
                    This running install reports **{appLine.Trim()}** from local Kyra context (the build on this PC).

                    What that is not:
                    That is not a live crawl of GitHub or the web — it is whatever version string the app supplied when this chat was built.

                    Next steps:
                    1. Compare with **Help → About** in the app if you want a second on-screen confirmation.
                    2. For release notes, use the documentation bundled with your build or your distributor’s page. I will not invent a “latest public” version number if the app did not provide one.
                    """;
            }

            return $"""
                Short answer:
                I do not have a verified “newest ForgerEMS on the internet” number for this session from local updater metadata alone.

                What I will not do:
                Guess a GitHub tag, download count, or release date — that would be fabricated without your configured update channel.

                Next steps:
                1. Use the in-app **update check** (if enabled for this install) or your operator’s release bundle.
                2. Ask Kyra again after a scan or update run if the app starts embedding a fresh **App version:** line in context.
                """;
        }

        if (ContainsAny(
                q,
                "before beta",
                "human testing",
                "beta testing",
                "release checklist",
                "missing before",
                "ready for beta",
                "beta readiness",
                "what should i test before beta",
                "what's missing before"))
        {
            return """
                Short answer:
                Beta testing is mostly “does install, USB Builder, USB Intelligence, System Intelligence, Toolkit Manager, Diagnostics, and Kyra behave on real PCs?”

                Likely gaps:
                Unsigned builds can trigger SmartScreen, some WMI fields may be blank, and USB topology is best-effort without benchmarks.

                Next steps:
                1. Run System Scan, USB Benchmark, Toolkit Refresh, and Diagnostics; skim logs under %LOCALAPPDATA%\ForgerEMS\logs\.
                2. Follow docs/BETA_TESTER_QUICKSTART.md and the versioned docs/BETA_HUMAN_TESTING_CHECKLIST_v*.md that matches your build from the repo or release bundle.
                3. Note anything confusing with screenshots + log snippets (no secrets).
                """;
        }

        if (ContainsAny(
                q,
                "smartscreen",
                "smart screen",
                "windows protected your pc",
                "unrecognized app",
                "publisher unknown",
                "defender smartscreen"))
        {
            return """
                Short answer:
                SmartScreen warnings are normal for unsigned or low-reputation installers; ForgerEMS does not bypass Windows security.

                Likely cause:
                The file is new to Microsoft’s reputation database or not code-signed yet.

                Next steps:
                1. Prefer the ZIP + START_HERE.bat flow from the GitHub release and verify checksums.
                2. Only continue if you trust the source; use “More info” / “Run anyway” only on your own judgment.
                3. If IT policy blocks it, ask your admin—there is no supported silent bypass.
                """;
        }

        return """
            Short answer:
            ForgerEMS helps you prep, inspect, and build repair USBs with guided scans and Kyra.

            What Kyra can do here:
            1. Explain System Intelligence summaries (no raw serials in chat context).
            2. Suggest USB Builder + USB Intelligence steps (benchmark + mapping).
            3. Clarify Toolkit Manager statuses (Managed vs Manual Required).
            4. Point to Diagnostics for a read-only health checklist.

            Next step:
            Run System Intelligence and USB Benchmark, then ask a specific question so I stay concise.
            """;
    }

    private static bool IsNewestForgerEmsReleaseQuestion(string q)
    {
        if (!q.Contains("forgerems", StringComparison.OrdinalIgnoreCase) &&
            !q.Contains("forger ems", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ContainsAny(q, "newest", "latest", "current version", "what version") &&
               ContainsAny(q, "release", "version", "update");
    }

    private static string FindContextLine(string contextText, string prefix)
    {
        if (string.IsNullOrWhiteSpace(contextText))
        {
            return string.Empty;
        }

        return contextText
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string BuildMemoryRecallAnswer(CopilotContext context)
    {
        var last = context.ConversationHistory.LastOrDefault(message => message.Role.Equals("Kyra", StringComparison.OrdinalIgnoreCase));
        if (last is null)
        {
            return "I don’t have a previous answer in this chat yet. Ask me what you want to diagnose and I’ll keep it practical.";
        }

        return $"""
            Quick read:
            Here’s the short recap: {last.Text}

            Shorter version:
            I’m trying to narrow the issue to the most likely cause instead of dumping every scan detail at you.
            """;
    }

    private static string BuildSimplerAnswer(CopilotContext context)
    {
        return $"""
            Quick read:
            In plain English: I’m looking for the part of the machine that makes everything else wait.

            What that usually means:
            If apps open slowly, check RAM, storage, startup apps, drivers, and heat first. Those are the common “this feels laggy” causes.

            What I’d do:
            Start with Task Manager while the problem is happening. If CPU, memory, disk, or GPU spikes hard, that tells us where to look next.
            """;
    }

    private static string BuildSafeCommandsAnswer(CopilotContext context)
    {
        return """
            Quick read:
            I can give safe read-only checks. I’m avoiding wipe/repair commands unless you explicitly ask for an owner-authorized repair plan.

            Safe PowerShell checks:
            1. Get-ComputerInfo | Select-Object WindowsProductName, WindowsVersion, OsBuildNumber
            2. Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, Size
            3. Get-CimInstance Win32_PhysicalMemory | Select-Object Capacity, Speed, Manufacturer, PartNumber
            4. Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion

            Next step:
            Paste the output here if you want me to interpret it.
            """;
    }

    private static string ResolveAppName(string prompt)
    {
        if (prompt.Contains("prime", StringComparison.OrdinalIgnoreCase))
        {
            return "Prime Video";
        }

        if (prompt.Contains("chrome", StringComparison.OrdinalIgnoreCase))
        {
            return "Chrome";
        }

        if (prompt.Contains("edge", StringComparison.OrdinalIgnoreCase))
        {
            return "Edge";
        }

        return "that app";
    }

    private static string BuildToolkitAnswer(string contextText)
    {
        var usb = FindLine(contextText, "USB target:");
        var toolkit = FindLine(contextText, "Toolkit health");
        return $"I would use the largest safe USB data partition and ignore EFI/system partitions. {usb} {toolkit} For a solid technician kit, keep Ventoy plus Windows installer media, Linux Mint or Ubuntu, Rescuezilla or Clonezilla, MemTest, and storage tools where licensing allows it.";
    }

    private static string BuildTechnicianAnswer(string contextText)
    {
        var problems = FindLine(contextText, "Problems:");
        return $"Start with the safe checks first. {problems} I would do this: 1. Check power, storage health, RAM pressure, network state, and drivers. 2. Reproduce the issue once. 3. Back up customer data before repairs. 4. Do not format, wipe, reinstall, or run destructive commands unless the user clearly confirms it.";
    }

    private static string BuildTroubleshootingAnswer(string prompt, CopilotContext context)
    {
        if (prompt.Contains("slow", StringComparison.OrdinalIgnoreCase) || prompt.Contains("lag", StringComparison.OrdinalIgnoreCase))
        {
            var profile = context.SystemProfile;
            var facts = profile is null
                ? "I need a System Intelligence scan before I can give machine-specific advice."
                : $"Health score: {context.HealthEvaluation?.HealthScore ?? 0}/100. RAM: {profile.RamTotal}. Storage: {JoinOrFallback(profile.Disks.Select(disk => $"{disk.MediaType} health {disk.Health} status {disk.Status}"), "storage health unknown")}. Battery: {profile.BatteryStatus}.";
            var memoryHint = context.PreviousIntent is KyraIntent.PerformanceLag or KyraIntent.AppFreezing or KyraIntent.SlowBoot
                ? "Since we were already looking at lag, I’ll keep this focused instead of repeating the whole scan."
                : "Yeah, that kind of lag usually lines up with a bottleneck during app launch.";
            return $"""
                Short answer:
                {memoryHint}

                What I found:
                {facts}
                Detected issues: {JoinOrFallback(context.HealthEvaluation?.DetectedIssues.Take(5) ?? Array.Empty<string>(), "no obvious blocking issue found locally")}.

                What to do next:
                {FormatNumbered(context.Recommendations.Take(5), "Check Task Manager, SMART health, Windows Update activity, thermals, and driver status.")}

                Technical details:
                I’m using the local System Intelligence scan only, so I’m not sending your device details anywhere.
                """;
        }

        if (prompt.Contains("usb", StringComparison.OrdinalIgnoreCase))
        {
            return "First check whether Windows mounted the main data partition, not the small VTOYEFI partition. " + FindLine(context.ContextText, "USB target:") + " Replug the USB, wait a few seconds for mount, confirm it in Disk Management, then use refresh only if auto-detect does not update.";
        }

        if (prompt.Contains("os", StringComparison.OrdinalIgnoreCase))
        {
            return "For resale or business use, I would usually choose Windows 11 Pro when the CPU, TPM, Secure Boot, RAM, and SSD are all solid. For older or lower-spec systems, Linux Mint XFCE, Xubuntu, or Ubuntu can feel much better. I would not sell an unsupported OS install as the main setup.";
        }

        return """
            Short answer:
            I can help, but I need the symptom or goal first.

            What I can do:
            Device diagnostics, lag troubleshooting, USB builder help, resale prep, OS recommendations, and explaining local warnings.

            What to do next:
            Ask something like “why is this slow?”, “what should I upgrade?”, or “build me a repair USB.”
            """;
    }

    private static string BuildGeneralAnswer(CopilotContext context)
    {
        var q = context.UserQuestion.Trim();
        var lower = q.ToLowerInvariant();
        if (context.Intent == KyraIntent.GeneralTechQuestion &&
            context.SystemProfile is not null &&
            KyraIntentRouter.PromptReferencesThisMachine(lower) &&
            (lower.Contains("upgrade", StringComparison.OrdinalIgnoreCase) || lower.Contains("laptop", StringComparison.OrdinalIgnoreCase) || lower.Contains("replace", StringComparison.OrdinalIgnoreCase)))
        {
            var device = context.SystemContext.Device.Trim();
            return $"""
                Short answer:
                Based on this {device}, I can compare upgrade paths, but I need how you use it (gaming, business, school) and a rough budget.

                What I already know from System Intelligence:
                {DescribeSystemContext(context.SystemContext)}

                What to do next:
                Reply with budget + priority (battery vs performance vs quiet/fan noise), and whether you want new or used.
                """;
        }

        return """
            Short answer:
            I can help like a technician, a resale advisor, or a USB toolkit helper.

            What I can’t do offline:
            Live weather, current web research, marketplace comps, or driver page lookups need an online provider.

            What to do next:
            Tell me what you want to fix, build, sell, or understand. If it’s about this PC, I’ll use the local scan without dumping raw logs at you.
            """;
    }

    private static string EstimateDeviceValue(SystemContext context)
    {
        var low = 120;
        var high = 220;

        if (context.RAM >= 32)
        {
            low += 130;
            high += 180;
        }
        else if (context.RAM >= 16)
        {
            low += 70;
            high += 110;
        }
        else if (context.RAM > 0 && context.RAM < 8)
        {
            low -= 40;
            high -= 60;
        }

        if (context.CPU.Contains("i7", StringComparison.OrdinalIgnoreCase) ||
            context.CPU.Contains("Ryzen 7", StringComparison.OrdinalIgnoreCase))
        {
            low += 90;
            high += 140;
        }
        else if (context.CPU.Contains("i5", StringComparison.OrdinalIgnoreCase) ||
                 context.CPU.Contains("Ryzen 5", StringComparison.OrdinalIgnoreCase))
        {
            low += 45;
            high += 80;
        }

        if (context.HasDedicatedGpu)
        {
            low += 80;
            high += 160;
        }

        low = Math.Max(60, low);
        high = Math.Max(low + 80, high);
        return $"Based on the local specs I can see, a rough offline range is around ${low:0}-${high:0}. Treat that as a starting point, not a real marketplace comp.";
    }

    private static string DescribeSystemContext(SystemContext context)
    {
        var ram = context.RAM > 0 ? $"{context.RAM} GB RAM" : "RAM unknown";
        return $"{context.Device}; {context.CPU}; {ram}; {context.GPU}; {context.Storage}; {context.OS}";
    }

    private static string SummarizeHealth(CopilotContext context)
    {
        if (context.HealthEvaluation is null)
        {
            return "I do not have a full health score loaded yet.";
        }

        var issues = JoinOrFallback(context.HealthEvaluation.DetectedIssues.Take(3), "no major issue called out by the local scan");
        return $"Health score: {context.HealthEvaluation.HealthScore}/100. Main signals: {issues}.";
    }

    private static string FindLine(string text, string prefix)
    {
        return text
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
    }

    private static string JoinOrFallback(IEnumerable<string> values, string fallback)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToArray();
        return items.Length == 0 ? fallback : string.Join("; ", items);
    }

    private static string FormatNumbered(IEnumerable<string> values, string fallback)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).Take(6).ToArray();
        if (items.Length == 0)
        {
            return "1. " + fallback;
        }

        return string.Join(Environment.NewLine, items.Select((item, index) => $"{index + 1}. {item}"));
    }

    private static string FormatNullable(double? value, string suffix = "")
    {
        return value.HasValue ? $"{value.Value:0.#}{suffix}" : "UNKNOWN";
    }

    private static string FormatNullable(int? value)
    {
        return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "UNKNOWN";
    }

    private static string FormatConfidence(double? value)
    {
        return value.HasValue ? $"{value.Value:0.##}" : "UNKNOWN";
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

    private static bool IsEmbeddedWslDiagnosticsStabilityQuestion(string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        if (!lower.Contains("wsl", StringComparison.Ordinal))
        {
            return false;
        }

        var mentionsForger = lower.Contains("forger", StringComparison.Ordinal) ||
            lower.Contains("diagnostic", StringComparison.Ordinal) ||
            lower.Contains("embedded", StringComparison.Ordinal);

        var mentionsProblem =
            lower.Contains("crash", StringComparison.Ordinal) ||
            lower.Contains("crashes", StringComparison.Ordinal) ||
            lower.Contains("freeze", StringComparison.Ordinal) ||
            lower.Contains("hang", StringComparison.Ordinal) ||
            (lower.Contains("why", StringComparison.Ordinal) && lower.Contains("inside", StringComparison.Ordinal));

        return mentionsProblem && mentionsForger;
    }

    private static string BuildEmbeddedWslDiagnosticsStabilityAnswer()
    {
        return """
            Direct answer:
            Embedded WSL terminal hosting is experimental. Use the external WSL Terminal button or Windows Sandbox/VM for beta stability.

            Why it can feel unstable:
            Running WSL output capture inside the main WPF window couples your distro lifecycle to the UI thread and graphics stack. That is convenient for demos but brittle during beta.

            What to do instead:
            1. Use Open WSL Terminal in Diagnostics so Windows Terminal or wsl.exe opens outside the app window.
            2. Use Windows Sandbox or a full VM when you are unsure about installers or scripts.
            3. Do not run risky downloads or unknown scripts inside the main ForgerEMS window—treat the host app as your control plane, not a lab VM.
            """;
    }

    private static bool ContainsAny(string text, params string[] terms)
    {
        return terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
