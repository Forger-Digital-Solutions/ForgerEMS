using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        Assert.Contains(context.Recommendations, item => item.Contains("16 GB RAM", StringComparison.OrdinalIgnoreCase));
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
        Assert.Contains("Local estimate only", response.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Pricing provider status", response.Text, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains(response.ProviderNotes, note => note.Contains("eBay Sold Listings", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("fallback", response.OnlineStatus, StringComparison.OrdinalIgnoreCase);
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
}
