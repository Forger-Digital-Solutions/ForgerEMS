using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VentoyToolkitSetup.Wpf.Models;
using VentoyToolkitSetup.Wpf.Services;

namespace ForgerEMS.Wpf.Tests;

public sealed class CopilotServiceTests
{
    [Fact]
    public void ContextBuilderRedactsSecretsUserPathsAndSerials()
    {
        var builder = new CopilotContextBuilder();

        var context = builder.Build(new CopilotRequest
        {
            Prompt = "api_key=sk-test123456789012 serial ABC12345",
            AppVersion = @"C:\Users\Daddy_FDS\Desktop\ForgerEMS",
            RecentLogLines =
            [
                @"token=very-secret-value loaded from C:\Users\Daddy_FDS\AppData\Local\thing.txt"
            ],
            Settings = new CopilotSettings
            {
                RedactContextEnabled = true
            }
        });

        Assert.DoesNotContain("Daddy_FDS", context.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("very-secret-value", context.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sk-test123456789012", context.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ABC12345", context.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[redacted]", context.ContextText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RedactorRemovesPrivateIpUsernameSerialAndPaths()
    {
        var redacted = CopilotRedactor.Redact(@"username=Daddy_FDS serial ABC12345 IP 192.168.1.44 C:\Users\Daddy_FDS\AppData\Local\thing.txt", enabled: true);

        Assert.DoesNotContain("Daddy_FDS", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ABC12345", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("192.168.1.44", redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[private ip redacted]", redacted, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextBuilderMapsSystemProfileHealthAndRecommendations()
    {
        var reportPath = WriteTempSystemReport();
        var builder = new CopilotContextBuilder();

        var context = builder.Build(new CopilotRequest
        {
            Prompt = "Why is my PC slow?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.NotNull(context.SystemProfile);
        Assert.NotNull(context.HealthEvaluation);
        Assert.Equal("Dell", context.SystemProfile!.Manufacturer);
        Assert.Equal("Latitude 5400", context.SystemProfile.Model);
        Assert.True(context.HealthEvaluation!.HealthScore < 100);
        Assert.Contains("Health score:", context.ContextText);
        Assert.Contains("Detected issues:", context.ContextText);
        Assert.Contains("Recommendations:", context.ContextText);
        Assert.NotNull(context.PricingEstimate);
        Assert.Contains("Pricing Engine v0:", context.ContextText);
        Assert.Contains(context.Recommendations, item => item.Contains("16 GB RAM", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(KyraIntent.PerformanceLag, context.Intent);
        Assert.Equal(8, context.SystemContext.RAM);
        Assert.Contains("Intel", context.SystemContext.CPU, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KyraIntentRouterClassifiesCommonUserQuestions()
    {
        Assert.Equal(KyraIntent.PerformanceLag, KyraIntentRouter.DetectIntent("My laptop is lagging when I open apps"));
        Assert.Equal(KyraIntent.UpgradeAdvice, KyraIntentRouter.DetectIntent("What should I upgrade?"));
        Assert.Equal(KyraIntent.UpgradeAdvice, KyraIntentRouter.DetectIntent("What laptop should I upgrade to from this one?"));
        Assert.Equal(KyraIntent.ResaleValue, KyraIntentRouter.DetectIntent("What is this worth if I sell it?"));
        Assert.Equal(KyraIntent.AppFreezing, KyraIntentRouter.DetectIntent("Prime Video keeps freezing"));
        Assert.Equal(KyraIntent.SlowBoot, KyraIntentRouter.DetectIntent("startup slow after login"));
        Assert.Equal(KyraIntent.GPUQuestion, KyraIntentRouter.DetectIntent("what about the GPU?"));
        Assert.Equal(KyraIntent.GeneralTechQuestion, KyraIntentRouter.DetectIntent("Can you explain this?"));
    }

    [Fact]
    public void PricingEngineLocalHeuristicReturnsRangeActionAndAssumptions()
    {
        var reportPath = WriteTempSystemReport();
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(reportPath));
        var profile = SystemProfileMapper.FromJson(document.RootElement);
        var health = SystemHealthEvaluator.Evaluate(profile);
        var estimate = new PricingEngine().Estimate(profile, health);

        Assert.NotNull(estimate);
        Assert.True(estimate!.LowEstimate > 0);
        Assert.True(estimate.HighEstimate > estimate.LowEstimate);
        Assert.InRange(estimate.ConfidenceScore, 0.1, 0.85);
        Assert.Equal(ResaleAction.UpgradeFirst, estimate.RecommendedAction);
        Assert.True(estimate.IsLocalEstimateOnly);
        Assert.Contains(estimate.Assumptions, assumption => assumption.Contains("No marketplace", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(estimate.Assumptions, assumption => assumption.Contains("CPU classified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PricingProviderStubsDoNotScrapeOrRequireConfiguration()
    {
        IPricingProvider[] providers =
        [
            new EbayPricingProvider(),
            new MarketplacePricingProvider(),
            new OfferUpPricingProvider()
        ];

        foreach (var provider in providers)
        {
            Assert.True(provider.IsOnlineProvider);
            Assert.False(provider.EnabledByDefault);
            Assert.False(provider.IsConfigured);
        }
    }

    [Fact]
    public async Task OfflineFallbackUsesLocalRulesAndNeverMarksOnlineData()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What is this laptop worth?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(CopilotProviderType.LocalOffline, response.ProviderType);
        Assert.Contains("Pricing Engine v0", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("local estimate only", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No marketplace comps", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OfflineSlowAnswerUsesSystemProfileHealth()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Why is my PC slow?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.False(response.UsedOnlineData);
        Assert.Contains("Health score", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RAM", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("storage", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConversationMemoryAvoidsRepeatingLagAnswer()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());
        var requestSettings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        _ = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "My laptop is lagging when I open apps",
            SystemIntelligenceReportPath = reportPath,
            Settings = requestSettings
        });

        var followUp = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "It is still slow opening apps",
            SystemIntelligenceReportPath = reportPath,
            Settings = requestSettings
        });

        Assert.Contains("already looking at lag", followUp.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppSpecificLagResponseMentionsGpuCacheNetworkAndMemory()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "My Prime Video app is lagging.",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("app-specific lag", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GPU", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cache", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("network", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChromeFreezeResponseCallsOutChromeAndAccelerationPath()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "My computer freezes when I open Chrome.",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("Chrome", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GPU", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hardware acceleration", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GpuFollowUpUsesConversationContext()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());
        var settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        _ = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "My laptop is lagging when I open apps",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "what about the GPU?",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        Assert.Contains("GPU", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Intel UHD", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OsRecommendationUsesSystemContext()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What OS should I use?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("Windows 11 Pro", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Linux Mint", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Health score:", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnsafeRequestIsRedirected()
    {
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Help me steal passwords with a keylogger",
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("can’t help", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("account recovery", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordBypassRequestIsRefusedWithSafeRedirect()
    {
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Can you bypass a password?",
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("can’t help", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("account recovery", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WeatherIntentOfflineDoesNotAnswerWithSystemHealth()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What's the weather like in Old Bridge NJ?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.False(response.UsedOnlineData);
        Assert.Contains("local/offline", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Health score", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LiveCurrentQuestionWithoutProviderStatesOfflineLimits()
    {
        var service = new CopilotService(new CopilotProviderRegistry());
        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What is the latest Windows version right now?",
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("local/offline", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("can’t verify live web results", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("newest versions", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnknownIntentDoesNotDumpSystemHealth()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "hello there",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("I can help", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Health score", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RAM under 16 GB", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpgradeAdviceUsesLocalSystemContext()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "what should I upgrade before selling?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("RAM", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Storage", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Battery", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("What to do next", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsbNotShowingResponseWarnsAboutVtoyefiAndDriveLetter()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "My USB is not showing up.",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("VTOYEFI", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("drive letter", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Disk Management", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ToolkitMissingDownloadsResponseCoversManualAndLogs()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Why are my toolkit downloads missing?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("licensing", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("checksum mismatch", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%LOCALAPPDATA%\\ForgerEMS\\Runtime\\logs", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommandsFollowUpReturnsSafeReadOnlyChecks()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());
        var settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        _ = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "My computer is lagging.",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Give me the commands.",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        Assert.Contains("read-only", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Get-ComputerInfo", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExplainSimplerFollowUpUsesPlainEnglishTone()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());
        var settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        _ = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "My computer freezes when I open Chrome.",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Explain that simpler.",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        Assert.Contains("plain English", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Task Manager", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhatDidYouJustSayUsesSessionMemoryRecall()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());
        var settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        _ = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What OS should I use on this machine?",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What did you just say?",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        Assert.Contains("short recap", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instead of dumping", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MakeMeAListingGeneratesListingDraftContent()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Make me a listing.",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("Listing draft", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Photo checklist", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("offline", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckOfferUpReturnsManualFutureSourceMessage()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Can you check OfferUp comps?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("manual/future", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sold comp", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CheckEbayCompsExplainsActiveOnlyStatusHonestly()
    {
        var reportPath = WriteTempSystemReport();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Can you check eBay comps?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        });

        Assert.Contains("eBay comps status", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sold comps are not configured", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProviderStatusPresenterReturnsFriendlyBadges()
    {
        Assert.Equal("Local AI available", KyraProviderStatusPresenter.GetProviderBadge(CopilotMode.HybridAuto, localOllamaEnabled: true, localLmStudioEnabled: false, openAiConfigured: false, anyOnlineConfigured: false));
        Assert.Equal("Local AI available", KyraProviderStatusPresenter.GetProviderBadge(CopilotMode.HybridAuto, localOllamaEnabled: false, localLmStudioEnabled: true, openAiConfigured: false, anyOnlineConfigured: false));
        Assert.Equal("API providers ready", KyraProviderStatusPresenter.GetProviderBadge(CopilotMode.HybridAuto, localOllamaEnabled: false, localLmStudioEnabled: false, openAiConfigured: true, anyOnlineConfigured: true));
        Assert.Equal(
            "API providers ready · Local AI available",
            KyraProviderStatusPresenter.GetProviderBadge(CopilotMode.HybridAuto, localOllamaEnabled: true, localLmStudioEnabled: false, openAiConfigured: true, anyOnlineConfigured: true));
        Assert.Equal("Online Not Configured", KyraProviderStatusPresenter.GetProviderBadge(CopilotMode.HybridAuto, localOllamaEnabled: false, localLmStudioEnabled: false, openAiConfigured: false, anyOnlineConfigured: false));
        Assert.Equal("Offline Ready", KyraProviderStatusPresenter.GetProviderBadge(CopilotMode.OfflineOnly, localOllamaEnabled: false, localLmStudioEnabled: false, openAiConfigured: false, anyOnlineConfigured: false));
        Assert.Equal("Ask Before Sending", KyraProviderStatusPresenter.GetPrivacyBadge(CopilotMode.AskFirst));
    }

    [Fact]
    public async Task ProviderPoolUsesConfiguredFreeProviderBeforeLocalFallback()
    {
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), new FakeSuccessProvider());
        var service = new CopilotService(registry);
        var settings = new CopilotSettings
        {
            Mode = CopilotMode.FreeApiPool,
            EnableFreeProviderPool = true,
            EnableByokProviders = false
        };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            ApiKeyEnvironmentVariable = "FAKE_FREE_KEY"
        };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "General question", Settings = settings });

        Assert.True(response.UsedOnlineData);
        Assert.Contains("Provider: Fake Free", response.OnlineStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HiRoutesToConfiguredFreeProvider()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Hi", Settings = settings });

        Assert.True(response.UsedOnlineData);
        Assert.Equal(KyraResponseSource.Groq, response.ResponseSource);
        Assert.Contains("Answered by", response.SourceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HiKyraRoutesToConfiguredFreeProvider()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Hi Kyra", Settings = settings });

        Assert.True(response.UsedOnlineData);
        Assert.Equal(1, online.CallCount);
        Assert.Contains("normal chat ->", string.Join('|', response.ProviderNotes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhyIsKyraOfflineStaysLocalWithoutOnlineProvider()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Why is Kyra offline?", Settings = settings });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(0, online.CallCount);
        Assert.Contains("local tool intent ->", string.Join('|', response.ProviderNotes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneralChatPrefersCloudFreePoolOverLocalOllamaWhenBothConfigured()
    {
        var cloud = new FakeSuccessProvider("gemini-free", "Gemini", CopilotProviderType.GeminiApi);
        var localAi = new FakeLocalAiSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), localAi, cloud);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["gemini-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_GEMINI_KEY" };
        settings.Providers["ollama-local"] = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            BaseUrl = "http://localhost:11434",
            ModelName = "llama3.2"
        };
        Environment.SetEnvironmentVariable("FAKE_GEMINI_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Hey Kyra", Settings = settings });

        Assert.True(response.UsedOnlineData);
        Assert.Equal(KyraResponseSource.Gemini, response.ResponseSource);
        Assert.Equal(0, localAi.CallCount);
    }

    [Fact]
    public async Task GenericTechQuestionRoutesToFreeProvider()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.HybridAuto, EnableFreeProviderPool = true, PreferLocalForDiagnostics = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Compare Windows and Ubuntu for a beginner.", Settings = settings });

        Assert.True(response.UsedOnlineData);
        Assert.Contains("Provider: Fake Free", response.OnlineStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsbQuestionStaysLocalEvenWhenProviderConfigured()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.HybridAuto, EnableFreeProviderPool = true, PreferLocalForDiagnostics = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Why is my USB not showing?", Settings = settings });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(CopilotProviderType.LocalOffline, response.ProviderType);
        Assert.Equal(0, online.CallCount);
    }

    [Fact]
    public async Task UpgradeBeforeSellingStaysLocalWhenOnlineContextSharingOff()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings
        {
            Mode = CopilotMode.HybridAuto,
            EnableFreeProviderPool = true,
            PreferLocalForDiagnostics = true,
            AllowOnlineSystemContextSharing = false
        };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "What should I upgrade before selling?", Settings = settings });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(0, online.CallCount);
        Assert.Contains("System Intelligence", response.SourceLabel, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("machine-specific with online context sharing OFF", string.Join('|', response.ProviderNotes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpgradeBeforeSellingUsesOnlineWhenContextSharingOn()
    {
        var online = new FakeCapturingProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings
        {
            Mode = CopilotMode.HybridAuto,
            EnableFreeProviderPool = true,
            PreferLocalForDiagnostics = true,
            AllowOnlineSystemContextSharing = true
        };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");
        var reportPath = WriteTempSystemReport();

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What should I upgrade before selling?",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        Assert.True(response.UsedOnlineData);
        Assert.Equal(1, online.CallCount);
        Assert.Contains("Latitude 5400", online.LastContext!.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\", online.LastContext.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sanitized System Intelligence context", response.SourceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ComputerLagStaysLocalWhenContextSharingOff()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings
        {
            Mode = CopilotMode.HybridAuto,
            EnableFreeProviderPool = true,
            PreferLocalForDiagnostics = true,
            AllowOnlineSystemContextSharing = false
        };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Why is my computer lagging?", Settings = settings });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(0, online.CallCount);
    }

    [Fact]
    public async Task MachineSpecificQuestionWithoutScanPromptsForSystemIntelligence()
    {
        var registry = new CopilotProviderRegistry();
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What should I upgrade before selling?",
            SystemIntelligenceReportPath = Path.Combine(Path.GetTempPath(), "nonexistent-scan-" + Guid.NewGuid().ToString("N") + ".json"),
            Settings = settings
        });

        Assert.Contains("System Intelligence scan", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResaleQuestionDoesNotAskForSpecsWhenScanExists()
    {
        var reportPath = WriteTempSystemReport();
        var registry = new CopilotProviderRegistry();
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What should I upgrade before selling this laptop?",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        Assert.Contains("RAM:", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("what model", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("specs do you have", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpgradeFromThisMachineUsesBaselineWhenScanExists()
    {
        var reportPath = WriteTempSystemReport();
        var registry = new CopilotProviderRegistry();
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly };

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What laptop should I upgrade to from this one?",
            SystemIntelligenceReportPath = reportPath,
            Settings = settings
        });

        Assert.Contains("Latitude 5400", response.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SanitizedProviderSummaryIncludesModelWithoutRawLogMarkers()
    {
        var reportPath = WriteTempSystemReport();
        var builder = new CopilotContextBuilder();
        var context = builder.Build(new CopilotRequest
        {
            Prompt = "What should I upgrade?",
            SystemIntelligenceReportPath = reportPath,
            Settings = new CopilotSettings()
        });

        var summary = KyraPrivacyGate.BuildSanitizedProviderSummary(context);

        Assert.Contains("Sanitized System Intelligence", summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Latitude 5400", summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Recent safe log", summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderPoolFallsBackWhenFreeProviderFails()
    {
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), new FakeRateLimitProvider());
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-rate"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Explain wifi channels", Settings = settings });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(CopilotProviderType.LocalOffline, response.ProviderType);
    }

    [Fact]
    public async Task ProviderFailureFallsBackToNextHealthyProvider()
    {
        var fallback = new FakeSuccessProvider(id: "fake-second", displayName: "Fake Second", providerType: CopilotProviderType.OpenRouterFree);
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), new FakeRateLimitProvider(), fallback);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-rate"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        settings.Providers["fake-second"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Explain this", Settings = settings });

        Assert.True(response.UsedOnlineData);
        Assert.Equal(CopilotProviderType.OpenRouterFree, response.ProviderType);
    }

    [Fact]
    public async Task AllProvidersFailUsesLocalFallbackLabel()
    {
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), new FakeRateLimitProvider());
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-rate"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Help me think through this", Settings = settings });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(CopilotProviderType.LocalOffline, response.ProviderType);
        Assert.Contains("Local Kyra", response.SourceLabel, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiPaidProviderSkippedWhenByokDisabled()
    {
        var paid = new FakePaidProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), paid);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true, EnableByokProviders = false };
        settings.Providers["fake-paid"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_PAID_KEY" };
        Environment.SetEnvironmentVariable("FAKE_PAID_KEY", "paid-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Help me", Settings = settings });

        Assert.Equal(CopilotProviderType.LocalOffline, response.ProviderType);
        Assert.Equal(0, paid.CallCount);
    }

    [Fact]
    public void CloudflareProviderRequiresAccountIdAndToken()
    {
        var provider = new OpenAiStyleCopilotProvider("cloudflare-workers-ai", "Cloudflare Workers AI", CopilotProviderType.CloudflareWorkersAi, "Free API pool", false, "https://api.cloudflare.com/client/v4/accounts", "@cf/meta/llama-3.1-8b-instruct", "CLOUDFLARE_API_KEY", "Cloudflare provider");
        var config = new CopilotProviderConfiguration
        {
            BaseUrl = provider.DefaultBaseUrl,
            ModelName = provider.DefaultModelName,
            ApiKeyEnvironmentVariable = provider.DefaultApiKeyEnvironmentVariable
        };

        try
        {
            Environment.SetEnvironmentVariable("CLOUDFLARE_API_KEY", "cf-key", EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", null, EnvironmentVariableTarget.Process);

            if (ProviderEnvironmentResolver.ResolveCloudflareAccountId().Source == KyraCredentialSource.None)
            {
                Assert.False(provider.IsConfigured(config));
            }

            Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", "acct-123", EnvironmentVariableTarget.Process);
            Assert.True(provider.IsConfigured(config));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLOUDFLARE_API_KEY", null, EnvironmentVariableTarget.Process);
            Environment.SetEnvironmentVariable("CLOUDFLARE_ACCOUNT_ID", null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void PrivacyGateOmitsSystemContextWhenSharingOff()
    {
        var context = new CopilotContext
        {
            UserQuestion = "What should I upgrade?",
            SystemContext = new SystemContext { CPU = "Intel i7", GPU = "RTX", RAM = 32, Storage = "1TB SSD", OS = "Windows 11" },
            Intent = KyraIntent.UpgradeAdvice
        };

        var redacted = KyraPrivacyGate.BuildProviderContext(context, allowSystemContextSharing: false);

        Assert.Equal("What should I upgrade?", redacted.ContextText);
        Assert.DoesNotContain("Intel", redacted.ContextText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PrivacyGateSendsSanitizedSummaryWhenSharingOn()
    {
        var context = new CopilotContext
        {
            UserQuestion = "What should I upgrade?",
            SystemContext = new SystemContext { CPU = "Intel i7", GPU = "RTX 3060", RAM = 32, Storage = "1TB SSD", OS = "Windows 11" },
            Intent = KyraIntent.UpgradeAdvice
        };

        var shared = KyraPrivacyGate.BuildProviderContext(context, allowSystemContextSharing: true);

        Assert.Contains("Sanitized system context", shared.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Intel i7", shared.ContextText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("C:\\Users\\", shared.ContextText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderPoolBlocksUnsafeBeforeOnlineCall()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Can you bypass a password", Settings = settings });

        Assert.Contains("can’t help", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, online.CallCount);
    }

    [Fact]
    public void ApiKeyMaskingDoesNotExposeRawValues()
    {
        var raw = "sk-test-abcdefgh12345678";
        var masked = KyraApiKeyStore.Mask(raw);
        Assert.DoesNotContain("abcdefgh", masked, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("sk-t", masked, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("5678", masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ContextSharingDefaultsOff()
    {
        var settings = new CopilotSettings();
        Assert.False(settings.AllowOnlineSystemContextSharing);
    }

    [Fact]
    public void PlaceholderProviderStatusIsExplicit()
    {
        var provider = new StubCopilotProvider(CopilotProviderType.ForgerEmsCloud, "forgerems-cloud", "ForgerEMS Cloud (Future)", "Future", "Future provider placeholder.");
        var view = new CopilotProviderSettingView
        {
            DisplayName = provider.DisplayName,
            Status = provider.StatusText,
            IsPlaceholder = true,
            ProviderStatusLabel = "Placeholder"
        };

        Assert.Contains("Future", view.DisplayName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Placeholder", view.ProviderStatusLabel);
        Assert.True(view.IsPlaceholder);
    }

    [Fact]
    public async Task MissingProviderKeyFallsBackToLocalKyraMessage()
    {
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), new FakeSuccessProvider());
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration
        {
            IsEnabled = true,
            ApiKeyEnvironmentVariable = "UNSET_FAKE_KEY"
        };
        Environment.SetEnvironmentVariable("UNSET_FAKE_KEY", null);

        var response = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "Explain event viewer basics", Settings = settings });

        Assert.Contains("Local Kyra", response.OnlineStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void KeyStoreMaskingPreventsRawValueInDisplay()
    {
        const string providerId = "mask-test";
        const string raw = "AIzaSyDUMMYKEY1234abcd";
        KyraApiKeyStore.SetSessionKey(providerId, raw);
        var masked = KyraApiKeyStore.Mask(KyraApiKeyStore.GetSessionKey(providerId));
        KyraApiKeyStore.ClearSessionKey(providerId);

        Assert.DoesNotContain("DUMMYKEY", masked, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("...", masked, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResponseCacheAvoidsDuplicateProviderCall()
    {
        var online = new FakeSuccessProvider();
        var registry = new FakeProviderRegistry(new LocalOfflineCopilotProvider(), online);
        var service = new CopilotService(registry);
        var settings = new CopilotSettings { Mode = CopilotMode.FreeApiPool, EnableFreeProviderPool = true };
        settings.Providers["fake-free"] = new CopilotProviderConfiguration { IsEnabled = true, ApiKeyEnvironmentVariable = "FAKE_FREE_KEY" };
        Environment.SetEnvironmentVariable("FAKE_FREE_KEY", "fake-key");

        _ = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "How to clean temp files safely?", Settings = settings });
        _ = await service.GenerateReplyAsync(new CopilotRequest { Prompt = "How to clean temp files safely?", Settings = settings });

        Assert.Equal(1, online.CallCount);
    }

    [Fact]
    public async Task OnlineProviderFailureFallsBackToOfflineWithoutCrash()
    {
        var service = new CopilotService(new CopilotProviderRegistry());
        var settings = new CopilotSettings
        {
            Mode = CopilotMode.OnlineAssisted,
            OfflineFallbackEnabled = true
        };
        settings.Providers["ebay-sold-listings"] = new CopilotProviderConfiguration { IsEnabled = true };

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "What is this laptop worth?",
            Settings = settings
        });

        Assert.False(response.UsedOnlineData);
        Assert.Equal(CopilotProviderType.LocalOffline, response.ProviderType);
        Assert.Contains(response.ProviderNotes, note => note.Contains("machine-specific with online context sharing OFF", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("Offline Local", response.OnlineStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CancelledRequestReturnsStoppedMessage()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var service = new CopilotService(new CopilotProviderRegistry());

        var response = await service.GenerateReplyAsync(new CopilotRequest
        {
            Prompt = "Why is my computer lagging?",
            Settings = new CopilotSettings { Mode = CopilotMode.OfflineOnly }
        }, cancellation.Token);

        Assert.Contains("stopped", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Stopped", response.OnlineStatus);
    }

    [Fact]
    public void SettingsStoreSavesLoadsAndAppliesProviderDefaults()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "ForgerEMS-CopilotTests", Guid.NewGuid().ToString("N"), "copilot-settings.json");
        var registry = new CopilotProviderRegistry();
        var store = new CopilotSettingsStore(tempPath, registry);

        store.Save(new CopilotSettings
        {
            Mode = CopilotMode.HybridAuto,
            TimeoutSeconds = 9,
            MaxContextCharacters = 1234,
            UseLatestSystemScanContext = false
        });

        var loaded = store.Load();

        Assert.Equal(CopilotMode.HybridAuto, loaded.Mode);
        Assert.Equal(9, loaded.TimeoutSeconds);
        Assert.Equal(1234, loaded.MaxContextCharacters);
        Assert.False(loaded.UseLatestSystemScanContext);
        Assert.True(loaded.Providers.ContainsKey("local-offline"));
        Assert.Equal("http://localhost:11434", loaded.Providers["ollama-local"].BaseUrl);
    }

    private static string WriteTempSystemReport()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ForgerEMS-CopilotTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "system-intelligence-latest.json");
        File.WriteAllText(path, """
            {
              "overallStatus": "Needs attention",
              "diskStatus": "Unknown",
              "batteryStatus": "Fair",
              "summary": {
                "manufacturer": "Dell",
                "model": "Latitude 5400",
                "os": "Windows 11 Pro",
                "osBuild": "22631",
                "cpu": "Intel Core i5",
                "cpuCores": 4,
                "cpuLogicalProcessors": 8,
                "ramTotal": "8 GB",
                "ramSpeed": "2666 MT/s",
                "ramSlotsFree": 1,
                "ramUpgradePath": "1 free RAM slot(s) detected; upgrade may be possible.",
                "ramStatus": "WATCH",
                "tpmPresent": true,
                "tpmReady": true,
                "secureBoot": true,
                "gpus": [
                  { "name": "Intel UHD Graphics", "driverVersion": "31.0.101.2125" }
                ],
                "gpuStatus": "Intel UHD"
              },
              "disks": [
                {
                  "name": "KINGSTON SATA SSD",
                  "mediaType": "SSD",
                  "size": "256 GB",
                  "health": "Healthy",
                  "status": "READY",
                  "temperatureC": 42,
                  "wearPercent": 12
                }
              ],
              "diskStatus": "READY",
              "batteries": [
                {
                  "name": "Primary Battery",
                  "estimatedChargeRemaining": 77,
                  "wearPercent": 41,
                  "cycleCount": 812,
                  "acConnected": true,
                  "status": "WATCH"
                }
              ],
              "batteryStatus": "WATCH",
              "network": {
                "status": "READY",
                "internetCheck": true,
                "adapters": [
                  { "apipaDetected": false, "gatewayPresent": true }
                ]
              },
              "flipValue": {
                "estimatedResaleRange": "$120-$180",
                "recommendedListPrice": "$180",
                "quickSalePrice": "$110",
                "partsRepairPrice": "$55",
                "confidenceScore": 0.68,
                "estimateType": "local estimate only",
                "providerStatus": "Pricing provider not configured",
                "valueReducers": [
                  "Less than 16 GB RAM reduces resale appeal.",
                  "High battery wear affects laptop resale value."
                ],
                "suggestedUpgradeRecommendations": [
                  "Upgrade to at least 16 GB RAM before selling if the platform supports it."
                ]
              },
              "obviousProblems": [
                "RAM under 16 GB",
                "Battery health is reduced: 41% wear."
              ],
              "recommendations": [
                "Battery wear is high at 41%. Plan a battery replacement if runtime matters."
              ]
            }
            """);
        return path;
    }

    private sealed class FakeProviderRegistry(params ICopilotProvider[] providers) : ICopilotProviderRegistry
    {
        public IReadOnlyList<ICopilotProvider> Providers { get; } = providers;
        public ICopilotProvider? FindById(string id) => Providers.FirstOrDefault(item => item.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        public ICopilotProvider? FindByType(CopilotProviderType providerType) => Providers.FirstOrDefault(item => item.ProviderType == providerType);
    }

    private sealed class FakeSuccessProvider : ICopilotProvider
    {
        private readonly string _id;
        private readonly string _displayName;
        private readonly CopilotProviderType _providerType;

        public FakeSuccessProvider(
            string id = "fake-free",
            string displayName = "Fake Free",
            CopilotProviderType providerType = CopilotProviderType.GroqApi)
        {
            _id = id;
            _displayName = displayName;
            _providerType = providerType;
        }

        public int CallCount { get; private set; }
        public string Id => _id;
        public string DisplayName => _displayName;
        public CopilotProviderType ProviderType => _providerType;
        public string Category => "Free API pool";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "https://example.test";
        public string DefaultModelName => "fake-model";
        public string DefaultApiKeyEnvironmentVariable => "FAKE_FREE_KEY";
        public string StatusText => "fake";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CopilotProviderResult { Succeeded = true, UsedOnlineData = true, UserMessage = "online result" });
        }
    }

    private sealed class FakeCapturingProvider : ICopilotProvider
    {
        public int CallCount { get; private set; }
        public CopilotContext? LastContext { get; private set; }
        public string Id => "fake-free";
        public string DisplayName => "Fake Free";
        public CopilotProviderType ProviderType => CopilotProviderType.GroqApi;
        public string Category => "Free API pool";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "https://example.test";
        public string DefaultModelName => "fake-model";
        public string DefaultApiKeyEnvironmentVariable => "FAKE_FREE_KEY";
        public string StatusText => "fake";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => !string.IsNullOrWhiteSpace(KyraApiKeyStore.ResolveApiKey(Id, configuration));
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastContext = request.Context;
            return Task.FromResult(new CopilotProviderResult { Succeeded = true, UsedOnlineData = true, UserMessage = "online result" });
        }
    }

    private sealed class FakeLocalAiSuccessProvider : ICopilotProvider
    {
        public int CallCount { get; private set; }

        public string Id => "ollama-local";
        public string DisplayName => "Ollama Local Model";
        public CopilotProviderType ProviderType => CopilotProviderType.OllamaLocal;
        public string Category => "Offline/local AI";
        public bool IsOnlineProvider => false;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "http://localhost:11434";
        public string DefaultModelName => "llama3.2";
        public string DefaultApiKeyEnvironmentVariable => string.Empty;
        public string StatusText => "fake ollama";
        public bool IsConfigured(CopilotProviderConfiguration configuration) =>
            !string.IsNullOrWhiteSpace(configuration.BaseUrl) && !string.IsNullOrWhiteSpace(configuration.ModelName);

        public bool CanHandle(CopilotProviderRequest request) => true;

        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CopilotProviderResult { Succeeded = true, UsedOnlineData = false, UserMessage = "local ai" });
        }
    }

    private sealed class FakeRateLimitProvider : ICopilotProvider
    {
        public string Id => "fake-rate";
        public string DisplayName => "Fake Rate Limited";
        public CopilotProviderType ProviderType => CopilotProviderType.OpenRouterFree;
        public string Category => "Free API pool";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => false;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "https://example.test";
        public string DefaultModelName => "fake-model";
        public string DefaultApiKeyEnvironmentVariable => "FAKE_FREE_KEY";
        public string StatusText => "fake";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => true;
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new CopilotProviderResult
            {
                Succeeded = false,
                IsTransientFailure = true,
                FailureReason = KyraProviderFailureReason.RateLimited,
                UserMessage = "rate limited"
            });
        }
    }

    private sealed class FakePaidProvider : ICopilotProvider
    {
        public int CallCount { get; private set; }
        public string Id => "fake-paid";
        public string DisplayName => "Fake Paid";
        public CopilotProviderType ProviderType => CopilotProviderType.OpenAICompatible;
        public string Category => "BYOK";
        public bool IsOnlineProvider => true;
        public bool IsPaidProvider => true;
        public bool EnabledByDefault => false;
        public string DefaultBaseUrl => "https://example.test";
        public string DefaultModelName => "paid-model";
        public string DefaultApiKeyEnvironmentVariable => "FAKE_PAID_KEY";
        public string StatusText => "fake";
        public bool IsConfigured(CopilotProviderConfiguration configuration) => true;
        public bool CanHandle(CopilotProviderRequest request) => true;
        public Task<CopilotProviderResult> GenerateAsync(CopilotProviderRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CopilotProviderResult { Succeeded = true, UsedOnlineData = true, UserMessage = "paid result" });
        }
    }
}
